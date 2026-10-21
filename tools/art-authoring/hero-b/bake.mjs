// Offline authoring only. Runtime consumes the resulting single-quad atlas.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const outputDirectory = path.resolve(root, process.argv[2] || 'artifacts/grounded-motion/hero-new');
fs.mkdirSync(outputDirectory, { recursive: true });
const sourcePath = 'unity/Assets/Game/Editor/Authoring/HeroB/hero-b-cutout-original.png';
const rigPath = process.argv[3] || 'tools/art-authoring/hero-b/rig.json';
const rig = JSON.parse(fs.readFileSync(path.resolve(root, rigPath)));
const sourceBytes = fs.readFileSync(path.join(root, sourcePath));
const source = decodeRgbaPng(sourceBytes);
const swordPath = 'unity/Assets/Game/Editor/Authoring/HeroB/hero-b-sword-clean.png';
const swordBytes = fs.readFileSync(path.join(root, swordPath));
const sword = decodeRgbaPng(swordBytes);
const W = 512, H = 512, columns = 4, count = rig.poses.length;
const atlasW = W * columns, atlasH = H * Math.ceil(count / columns);
const atlas = Buffer.alloc(atlasW * atlasH * 4);
const layerNames = ['back', 'far', 'near', 'front'];
const layerAtlases = layerNames.map(() => Buffer.alloc(atlas.length));
function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) { const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6; const rows = Buffer.alloc((width * 4 + 1) * height); for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4); return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]); }
const rad = degrees => degrees * Math.PI / 180;
function transform(parent, offset, angle) { const c = Math.cos(parent.angle), s = Math.sin(parent.angle); return { x: parent.x + c * offset[0] - s * offset[1], y: parent.y + s * offset[0] + c * offset[1], angle: parent.angle + rad(angle) }; }
function composite(target, width, height, sprite, joint) {
  const image = sprite.source === 'cleanSword' ? sword : source;
  const [sx, sy, sw, sh] = sprite.rect, [ax, ay] = sprite.anchor;
  const angle = joint.angle + rad(sprite.angle || 0), c = Math.cos(angle), s = Math.sin(angle), scale = sprite.scale, scaleX=sprite.scaleX??scale, scaleY=sprite.scaleY??scale;
  const corners = [[sx,sy],[sx+sw,sy],[sx,sy+sh],[sx+sw,sy+sh]].map(([x,y]) => [joint.x + (x-ax)*c*scaleX-(y-ay)*s*scaleY, joint.y+(x-ax)*s*scaleX+(y-ay)*c*scaleY]);
  const minX = Math.max(4, Math.floor(Math.min(...corners.map(p=>p[0])))), maxX = Math.min(width-4, Math.ceil(Math.max(...corners.map(p=>p[0]))));
  const minY = Math.max(4, Math.floor(Math.min(...corners.map(p=>p[1])))), maxY = Math.min(height-4, Math.ceil(Math.max(...corners.map(p=>p[1]))));
  for (let y=minY;y<maxY;y++) for(let x=minX;x<maxX;x++) {
    const dx=x+.5-joint.x, dy=y+.5-joint.y;
    const u=ax+(dx*c+dy*s)/scaleX, v=ay+(-dx*s+dy*c)/scaleY;
    if(u<sx||v<sy||u>=sx+sw-1||v>=sy+sh-1)continue;
    const ix=Math.floor(u),iy=Math.floor(v),fx=u-ix,fy=v-iy;
    let a=0,r=0,g=0,b=0;
    for (let yy=0;yy<2;yy++) for(let xx=0;xx<2;xx++) { const i=((iy+yy)*image.width+ix+xx)*4, weight=(xx?fx:1-fx)*(yy?fy:1-fy), alpha=image.pixels[i+3]/255*weight; a+=alpha;r+=image.pixels[i]*alpha;g+=image.pixels[i+1]*alpha;b+=image.pixels[i+2]*alpha; }
    if(a<1/255)continue;
    const i=(y*width+x)*4, old=target[i+3]/255, out=a+old*(1-a);
    target[i]=Math.round((r+target[i]*old*(1-a))/out);target[i+1]=Math.round((g+target[i+1]*old*(1-a))/out);target[i+2]=Math.round((b+target[i+2]*old*(1-a))/out);target[i+3]=Math.round(out*255);
  }
}
const frames = [];
const previewW = count*128, previewH = 160, preview = Buffer.alloc(previewW*previewH*4);
for (const [index, pose] of rig.poses.entries()) {
  const joints = { root: { ...rig.root, angle: 0 } };
  for (const bone of rig.bones) joints[bone.id] = transform(joints[bone.parent], bone.offset, (bone.angle||0)+(pose.rotations[bone.id]||0));
  // Feet stay on the authored ground line: solve hip-to-foot two-bone chains, not whole-body bob.
  for (const side of ['far','near']) {
    const hip=joints[side+'Hip'], authored=pose.feet[side], foot=[...authored];
    // Calibrate each boot's opaque sole, not the body's height, onto the stance/swing line.
    for(let iteration=0;iteration<12;iteration++) {
      const dx=foot[0]-hip.x,dy=foot[1]-hip.y;
      const {upper,lower}=rig.legLengths,d=Math.min(upper+lower-.01,Math.hypot(dx,dy));
      const offset=Math.acos(Math.max(-1,Math.min(1,(upper*upper+d*d-lower*lower)/(2*upper*d))));
      const angle=Math.atan2(dy,dx)-offset;
      hip.angle=angle-Math.PI/2;
      const knee={x:hip.x+Math.cos(angle)*upper,y:hip.y+Math.sin(angle)*upper,angle:0};
      knee.angle=Math.atan2(foot[1]-knee.y,foot[0]-knee.x)-Math.PI/2;
      joints[side+'Knee']=knee;
      const boot=Buffer.alloc(W*H*4);
      composite(boot,W,H,rig.sprites.find(sprite=>sprite.bone===side+'Knee'),knee);
      let bottom=0;for(let y=0;y<H;y++)for(let x=0;x<W;x++)if(boot[(y*W+x)*4+3]>127)bottom=Math.max(bottom,y);
      let left=W,right=0;
      for(let y=bottom-1;y<=bottom;y++)for(let x=0;x<W;x++)if(boot[(y*W+x)*4+3]>127){left=Math.min(left,x);right=Math.max(right,x);}
      const soleX=(left+right)/2;
      if(Math.abs(bottom-authored[1])<=1&&Math.abs(soleX-authored[0])<=1)break;
      foot[0]+=authored[0]-soleX;
      foot[1]+=authored[1]-bottom;
    }
  }
  const pixels=Buffer.alloc(W*H*4);
  const layers=layerNames.map(()=>Buffer.alloc(W*H*4));
  for(const [spriteIndex,sprite] of rig.sprites.entries()) {
    composite(pixels,W,H,sprite,joints[sprite.bone]);
    const layer=sprite.bone==='farHip'||sprite.bone==='farKnee'?1:sprite.bone==='nearHip'||sprite.bone==='nearKnee'?2:spriteIndex<3?0:3;
    composite(layers[layer],W,H,sprite,joints[sprite.bone]);
  }
  let opaque=0;for(let i=3;i<pixels.length;i+=4)if(pixels[i])opaque++;
  if(opaque<5000)throw new Error('Empty or implausibly small pose '+pose.id);
  const ox=index%columns*W,oy=Math.floor(index/columns)*H;
  for(let y=0;y<H;y++)pixels.copy(atlas,((oy+y)*atlasW+ox)*4,y*W*4,(y+1)*W*4);
  for(let layer=0;layer<4;layer++)for(let y=0;y<H;y++)layers[layer].copy(layerAtlases[layer],((oy+y)*atlasW+ox)*4,y*W*4,(y+1)*W*4);
  // Actual small-size review sheet: each 512px frame shown at 128px, without repainting.
  for(let y=0;y<128;y++)for(let x=0;x<128;x++){const i=((y*4)*W+x*4)*4,o=((y+16)*previewW+index*128+x)*4;pixels.copy(preview,o,i,i+4);}
  const soles={};
  for(const [side,layer] of [['far',1],['near',2]]) {
    let bottom=0,left=W,right=0;
    for(let y=0;y<H;y++)for(let x=0;x<W;x++)if(layers[layer][(y*W+x)*4+3]>127)bottom=Math.max(bottom,y);
    for(let y=bottom-1;y<=bottom;y++)for(let x=0;x<W;x++)if(layers[layer][(y*W+x)*4+3]>127){left=Math.min(left,x);right=Math.max(right,x);}
    soles[side]={x:(left+right)/2,y:bottom};
  }
  frames.push({id:pose.id,x:ox,y:oy,width:W,height:H,pivot:{x:rig.root.x/W,y:1-420/H},phase:pose.phase??0,stance:pose.stance??{near:false,far:false},feet:pose.feet,soles,joints});
}
const output=path.join(outputDirectory,'fallow-hero-b.png');
fs.writeFileSync(output,png(atlasW,atlasH,atlas));
fs.writeFileSync(path.join(outputDirectory,'actual-size-preview.png'),png(previewW,previewH,preview));
// Four 1024-square layers in one 2048-square texture. Area-average from 512px authoring cells.
const packedW=atlasW,packedH=atlasH,packed=Buffer.alloc(packedW*packedH*4);
for(let layer=0;layer<4;layer++)for(let y=0;y<atlasH/2;y++)for(let x=0;x<atlasW/2;x++) {
  const target=(((layer>>1)*atlasH/2+y)*packedW+(layer%2)*atlasW/2+x)*4;
  let alpha=0,r=0,g=0,b=0;
  for(let yy=0;yy<2;yy++)for(let xx=0;xx<2;xx++){const i=((y*2+yy)*atlasW+x*2+xx)*4,a=layerAtlases[layer][i+3]/255;alpha+=a;r+=layerAtlases[layer][i]*a;g+=layerAtlases[layer][i+1]*a;b+=layerAtlases[layer][i+2]*a;}
  if(alpha>0){packed[target]=Math.round(r/alpha);packed[target+1]=Math.round(g/alpha);packed[target+2]=Math.round(b/alpha);packed[target+3]=Math.round(alpha*255/4);}
}
fs.writeFileSync(path.join(outputDirectory,'fallow-hero-b-layers.png'),png(packedW,packedH,packed));
// Runtime pins the actual downsampled sole pixel centers, not ideal IK targets.
for(const [index,frame] of frames.entries()) {
  frame.renderSoles={};
  for(const [side,sheetX,sheetY] of [['far',atlasW/2,0],['near',0,atlasH/2]]) {
    const ox=sheetX+index%4*256,oy=sheetY+Math.floor(index/4)*256;
    let bottom=0,left=256,right=0;
    for(let y=0;y<256;y++)for(let x=0;x<256;x++)if(packed[((oy+y)*packedW+ox+x)*4+3]>127)bottom=Math.max(bottom,y);
    for(let y=bottom-1;y<=bottom;y++)for(let x=0;x<256;x++)if(packed[((oy+y)*packedW+ox+x)*4+3]>127){left=Math.min(left,x);right=Math.max(right,x);}
    frame.renderSoles[side]={x:left+right+1,y:bottom*2+1};
  }
}
const mask=Buffer.alloc(256*256*4);
for(let y=0;y<256;y++)for(let x=0;x<256;x++){const i=(y*256+x)*4,w=Math.round(255*Math.max(0,Math.min(1,(y*2-rig.gait.hipWarpStartY)/(rig.gait.soleWarpFullY-rig.gait.hipWarpStartY))));mask[i]=mask[i+1]=w;mask[i+3]=255;}
fs.writeFileSync(path.join(outputDirectory,'fallow-hero-b-contact-mask.png'),png(256,256,mask));
const metadata={schemaVersion:2,source:sourcePath,sourceSha256:createHash('sha256').update(sourceBytes).digest('hex'),swordSource:swordPath,swordSha256:createHash('sha256').update(swordBytes).digest('hex'),atlas:'fallow-hero-b.png',layeredAtlas:'fallow-hero-b-layers.png',mask:'fallow-hero-b-contact-mask.png',layers:layerNames,width:atlasW,height:atlasH,padding:4,gait:rig.gait,frameCoordinateOrigin:'top-left',pivotCoordinateOrigin:'bottom-left',runtime:'one quad, four layered samples with continuous sole correction',frames};
fs.writeFileSync(path.join(outputDirectory,'baked-frames.json'),JSON.stringify(metadata,null,2)+'\n');
const role=(name,indices)=>({id:`hero.preview-b.${name}`,atlas:'feel-hero-b',frames:indices.map(index=>({x:index%4*512+4,y:Math.floor(index/4)*512+4,width:504,height:504})),pivot:{x:(rig.root.x-4)/504,y:88/504},worldSize:{x:.785,y:.785},frameMs:80,tween:'none'});
fs.writeFileSync(path.join(outputDirectory,'manifest-fragment.json'),JSON.stringify({atlas:{id:'feel-hero-b',path:'Assets/Art/fallow-hero-b.png',width:atlasW,height:atlasH,padding:4,license:'feel-hero-b'},roles:[role('walk',Array.from({length:12},(_,i)=>i)),role('idle',[12]),role('attack',[13]),role('hit',[14])]},null,2)+'\n');
const layeredRole=(name,indices)=>({id:`hero.preview-b.${name}`,atlas:'feel-hero-b-layered',frames:indices.map(index=>({x:index%4*256+2,y:Math.floor(index/4)*256+2,width:252,height:252})),pivot:{x:(rig.root.x/2-2)/252,y:44/252},worldSize:{x:.785,y:.785},frameMs:80,tween:'none'});
fs.writeFileSync(path.join(outputDirectory,'manifest-layered-fragment.json'),JSON.stringify({atlas:{id:'feel-hero-b-layered',path:'Assets/Art/fallow-hero-b-layers.png',width:packedW,height:packedH,padding:2,license:'feel-hero-b'},roles:[layeredRole('walk',Array.from({length:12},(_,i)=>i)),layeredRole('idle',[12]),layeredRole('attack',[13]),layeredRole('hit',[14])]},null,2)+'\n');
console.log(`Baked ${count} hierarchical poses and layered contact atlas to ${outputDirectory}.`);
