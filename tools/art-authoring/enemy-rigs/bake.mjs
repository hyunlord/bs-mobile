// Offline articulated cutout bake. Player uses one quad and cached UVs, no skeleton or authoring code.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {deflateSync} from 'node:zlib';
import {createHash} from 'node:crypto';
import {decodeRgbaPng} from '../../wave1a-art.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..');
const out=path.resolve(root,process.argv[2]||'artifacts/grounded-motion/enemy-new');
fs.mkdirSync(out,{recursive:true});
const spec=JSON.parse(fs.readFileSync(path.join(root,'tools/art-authoring/enemy-rigs/rigs.json')));
const {canvas:C,frameSize:S,padding:P,columns}=spec;
const count=spec.rigs.length*spec.poses.length, AW=columns*S,AH=Math.ceil(count/columns)*S;
const atlas=Buffer.alloc(AW*AH*4),preview=Buffer.alloc(S*11*S*6*4);
const rad=d=>d*Math.PI/180;
function crc(bytes){let c=0xffffffff;for(const v of bytes){c^=v;for(let i=0;i<8;i++)c=(c>>>1)^((c&1)?0xedb88320:0);}return(c^0xffffffff)>>>0;}
function chunk(type,bytes){const size=Buffer.alloc(4),sum=Buffer.alloc(4),data=Buffer.concat([Buffer.from(type),bytes]);size.writeUInt32BE(bytes.length);sum.writeUInt32BE(crc(data));return Buffer.concat([size,data,sum]);}
function png(w,h,p){const header=Buffer.alloc(13);header.writeUInt32BE(w);header.writeUInt32BE(h,4);header[8]=8;header[9]=6;const rows=Buffer.alloc((w*4+1)*h);for(let y=0;y<h;y++)p.copy(rows,y*(w*4+1)+1,y*w*4,(y+1)*w*4);return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('IDAT',deflateSync(rows)),chunk('IEND',Buffer.alloc(0))]);}
function bounds(p,w,h){let l=w,t=h,r=-1,b=-1,n=0;for(let y=0;y<h;y++)for(let x=0;x<w;x++)if(p[(y*w+x)*4+3]>16){l=Math.min(l,x);r=Math.max(r,x);t=Math.min(t,y);b=Math.max(b,y);n++;}return{x:l,y:t,width:r-l+1,height:b-t+1,pixels:n};}
function transform(parent,offset,degrees){const c=Math.cos(parent.angle),s=Math.sin(parent.angle);return{x:parent.x+c*offset[0]-s*offset[1],y:parent.y+s*offset[0]+c*offset[1],angle:parent.angle+rad(degrees)};}
function composite(target,image,sprite,joint,scaleY=1){
const[sx,sy,sw,sh]=sprite.rect,[ax,ay]=sprite.anchor,a=joint.angle+rad(sprite.angle||0),c=Math.cos(a),s=Math.sin(a),scale=sprite.scale,ys=scale*scaleY;
const corners=[[sx,sy],[sx+sw,sy],[sx,sy+sh],[sx+sw,sy+sh]].map(([x,y])=>[joint.x+(x-ax)*c*scale-(y-ay)*s*ys,joint.y+(x-ax)*s*scale+(y-ay)*c*ys]);
const x0=Math.max(0,Math.floor(Math.min(...corners.map(p=>p[0])))),x1=Math.min(C,Math.ceil(Math.max(...corners.map(p=>p[0]))));
const y0=Math.max(0,Math.floor(Math.min(...corners.map(p=>p[1])))),y1=Math.min(C,Math.ceil(Math.max(...corners.map(p=>p[1]))));
for(let y=y0;y<y1;y++)for(let x=x0;x<x1;x++){
const dx=x+.5-joint.x,dy=y+.5-joint.y,u=ax+(dx*c+dy*s)/scale,v=ay+(-dx*s+dy*c)/ys;
if(u<sx||v<sy||u>=sx+sw-1||v>=sy+sh-1)continue;
const ix=Math.floor(u),iy=Math.floor(v),fx=u-ix,fy=v-iy;let a=0,r=0,g=0,b=0;
for(let yy=0;yy<2;yy++)for(let xx=0;xx<2;xx++){const i=((iy+yy)*image.width+ix+xx)*4,weight=(xx?fx:1-fx)*(yy?fy:1-fy),alpha=image.pixels[i+3]/255*weight;a+=alpha;r+=image.pixels[i]*alpha;g+=image.pixels[i+1]*alpha;b+=image.pixels[i+2]*alpha;}
if(a<1/255)continue;const i=(y*C+x)*4,old=target[i+3]/255,total=a+old*(1-a);target[i]=Math.round((r+target[i]*old*(1-a))/total);target[i+1]=Math.round((g+target[i+1]*old*(1-a))/total);target[i+2]=Math.round((b+target[i+2]*old*(1-a))/total);target[i+3]=Math.round(total*255);
}}
function pose(r,index){const walk=index<8,tell=index===8||index===9,p=walk?index/8*Math.PI*2:0,step=Math.sin(p),rot={},ys={};
for(const b of r.bones){let angle=0;const name=b.id;
if(r.family==='human'||r.family==='charger'){
const amplitude=r.family==='charger'?31:25;
if(name==='nearThigh')angle=walk?step*amplitude:0;
if(name==='farThigh')angle=walk?-step*amplitude:0;
if(name==='nearShin')angle=walk?-Math.max(0,-step)*32:0;
if(name==='farShin')angle=walk?-Math.max(0,step)*32:0;
if(name==='nearUpper')angle=walk?-step*18:tell?-35:0;
if(name==='farUpper')angle=walk?step*18:tell?20:0;
if(name==='nearLower')angle=tell?-30:walk?-8+Math.max(0,step)*9:0;
if(name==='farLower')angle=tell?-18:walk?-8+Math.max(0,-step)*9:0;
if(name==='cape')angle=walk?Math.sin(p-.7)*4:tell?5:0;
if(name==='head')angle=tell?(r.family==='charger'?(index===8?15:22):-8):0;
if(name==='body'&&r.family==='charger')angle=tell?27:20;
if(name==='spear'&&tell)angle=index===8?-15:-25;
if(name==='shield'&&tell)angle=index===8?-10:-18;
}else if(r.family==='tripod'||r.family==='quadruped'){
if('gaitPhase'in b)angle=walk?(Math.sin(p+.17+b.gaitPhase*Math.PI)+.18*Math.sin(p*2))*(r.family==='tripod'?15:20):tell?(b.gaitPhase?8:-8):0;
if(name==='head')angle=tell?(index===8?12:22):walk?Math.sin(p)*2:0;
}else{
if(name.includes('Wing')||name.startsWith('wing')){const left=name.endsWith('L');angle=(left?-1:1)*(walk?Math.sin(p)*35:tell?35:12);ys[name]=walk?.65+.35*Math.cos(p):tell?.5:1;}
if(name==='abdomen')angle=tell?(index===8?-14:-24):walk?Math.sin(p)*5:0;
if(name.startsWith('leg'))angle=walk?Math.sin(p+b.gaitPhase*Math.PI)*9:tell?12:0;
if(name.startsWith('antenna'))angle=walk?Math.sin(p)*4:tell?-10:0;
}
rot[name]=angle;
}
return{rot,ys};}
function downsample(p){const target=Buffer.alloc(S*S*4);for(let y=0;y<S;y++)for(let x=0;x<S;x++){let a=0,r=0,g=0,b=0;for(let dy=0;dy<2;dy++)for(let dx=0;dx<2;dx++){const i=(((y*2+dy)*C)+x*2+dx)*4,alpha=p[i+3]/255;a+=alpha;r+=p[i]*alpha;g+=p[i+1]*alpha;b+=p[i+2]*alpha;}if(a){const i=(y*S+x)*4;target[i]=Math.round(r/a);target[i+1]=Math.round(g/a);target[i+2]=Math.round(b/a);target[i+3]=Math.round(a*255/4);}}return target;}
function outline(p){const result=Buffer.from(p);for(let y=1;y<S-1;y++)for(let x=1;x<S-1;x++){const i=(y*S+x)*4;let near=0;for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++)near=Math.max(near,p[((y+dy)*S+x+dx)*4+3]);const a=p[i+3]/255,edge=near/255*.85*(1-a),total=a+edge;if(edge>0){for(let c=0;c<3;c++)result[i+c]=Math.round((p[i+c]*a+[25,25,28][c]*edge)/total);result[i+3]=Math.round(total*255);}}return result;}
function isolate(image,rect){
const [rx,ry,rw,rh]=rect,pixels=Buffer.from(image.pixels),seen=new Uint8Array(rw*rh),queue=new Int32Array(rw*rh);let largest=[];
for(let y=0;y<rh;y++)for(let x=0;x<rw;x++){
const local=y*rw+x;if(seen[local]||image.pixels[((ry+y)*image.width+rx+x)*4+3]<8)continue;
let read=0,write=1;queue[0]=local;seen[local]=1;
while(read<write){const n=queue[read++],px=n%rw,py=Math.floor(n/rw);for(const [dx,dy]of [[-1,0],[1,0],[0,-1],[0,1]]){const nx=px+dx,ny=py+dy;if(nx<0||ny<0||nx>=rw||ny>=rh)continue;const next=ny*rw+nx;if(!seen[next]&&image.pixels[((ry+ny)*image.width+rx+nx)*4+3]>=8){seen[next]=1;queue[write++]=next;}}}
if(write>largest.length)largest=Array.from(queue.subarray(0,write));
}
const keep=new Uint8Array(rw*rh);for(const i of largest){const x=i%rw,y=Math.floor(i/rw);for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++)if(x+dx>=0&&y+dy>=0&&x+dx<rw&&y+dy<rh)keep[(y+dy)*rw+x+dx]=1;}
for(let y=0;y<rh;y++)for(let x=0;x<rw;x++)if(!keep[y*rw+x])pixels[((ry+y)*image.width+rx+x)*4+3]=0;
return{width:image.width,height:image.height,pixels};
}
const records=[],roles=[];
for(const [ri,r]of spec.rigs.entries()){
const bytes=fs.readFileSync(path.join(root,r.source)),image=decodeRgbaPng(bytes),cutouts=r.sprites.map(sprite=>isolate(image,sprite.rect));const frames=[];
for(let fi=0;fi<11;fi++){
const {rot,ys}=pose(r,fi),joints={root:{x:r.root[0],y:r.root[1],angle:0}},pixels=Buffer.alloc(C*C*4);
for(const b of r.bones)joints[b.id]=transform(joints[b.parent],b.offset,b.angle+rot[b.id]);
if((r.family==='human'||r.family==='charger')&&fi<8){
for(const [side,offset]of [['near',0],['far',Math.PI]]){
const hip=joints[side+'Thigh'],phase=fi/8*Math.PI*2+offset,upper=side==='near'?59:61,lower=r.family==='charger'?88:83;
const stride=r.family==='charger'?30:23,lift=r.family==='charger'?16:10;
const foot={x:hip.x-Math.sin(phase)*stride,y:r.ground[1]-(side==='far'?5:0)-Math.max(0,Math.cos(phase))*lift};
const dx=foot.x-hip.x,dy=foot.y-hip.y,d=Math.min(upper+lower-.01,Math.hypot(dx,dy)),bend=Math.acos(Math.max(-1,Math.min(1,(upper*upper+d*d-lower*lower)/(2*upper*d))));
const angle=Math.atan2(dy,dx)-bend;hip.angle=angle-Math.PI/2;
const knee={x:hip.x+Math.cos(angle)*upper,y:hip.y+Math.sin(angle)*upper,angle:0};knee.angle=Math.atan2(foot.y-knee.y,foot.x-knee.x)-Math.PI/2;joints[side+'Shin']=knee;
}
}
for(const [si,sprite]of r.sprites.entries())composite(pixels,cutouts[si],sprite,joints[sprite.bone],ys[sprite.bone]||1);
const rendered=outline(downsample(pixels)),bound=bounds(rendered,S,S),index=ri*11+fi,ox=index%columns*S,oy=Math.floor(index/columns)*S;
for(let y=0;y<S;y++){rendered.copy(atlas,((oy+y)*AW+ox)*4,y*S*4,(y+1)*S*4);rendered.copy(preview,((ri*S+y)*S*11+fi*S)*4,y*S*4,(y+1)*S*4);}
frames.push({pose:spec.poses[fi],rect:{x:ox+P,y:oy+P,width:S-2*P,height:S-2*P},bounds:bound,root:joints.root,joints,sha256:createHash('sha256').update(rendered).digest('hex')});
}
const idle=frames[10],visibleHeight=idle.bounds.height,pivot={x:(r.ground[0]/2-P)/(S-2*P),y:1-(r.ground[1]/2-P)/(S-2*P)};
const size=r.height*(S-2*P)/visibleHeight;
for(const [state,indices]of[['walk',[0,1,2,3,4,5,6,7]],['tell',[8,9]],['idle',[10]]])roles.push({id:`enemy.rig.${r.slug}.${state}`,atlas:'fallow-enemy-rigs',frames:indices.map(i=>frames[i].rect),pivot,worldSize:{x:size,y:size},frameMs:state==='walk'?(r.family==='tripod'?55:r.family==='wingbeat'?40:80):state==='tell'?140:120,tween:'none'});
records.push({slug:r.slug,source:r.source,sourceSha256:createHash('sha256').update(bytes).digest('hex'),family:r.family,frames});
}
fs.writeFileSync(path.join(out,'fallow-enemy-rigs.png'),png(AW,AH,atlas));
fs.writeFileSync(path.join(out,'contact-sheet.png'),png(S*11,S*6,preview));
fs.writeFileSync(path.join(out,'baked-frames.json'),JSON.stringify({schemaVersion:1,atlas:{width:AW,height:AH,cell:S,padding:P},rigs:records},null,2)+'\n');
fs.writeFileSync(path.join(out,'manifest-fragment.json'),JSON.stringify({licenses:[{id:'fallow-enemy-rigs',origin:'OpenAI image_gen separated-parts drawings and project offline rig authoring',source:'tools/art-authoring/enemy-rigs/generation-provenance.json',prompt:'tools/art-authoring/enemy-rigs/generation-provenance.json',commercialUse:'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.'}],atlases:[{id:'fallow-enemy-rigs',path:'Assets/Art/fallow-enemy-rigs.png',width:AW,height:AH,padding:P,license:'fallow-enemy-rigs'}],roles},null,2)+'\n');
console.log(`Baked ${records.length} rigs / ${count} frames to ${out} (${AW}x${AH}); one runtime sprite quad per actor.`);
