import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const directory = process.argv[2] || 'artifacts/grounded-motion/hero-new';
const metadata = JSON.parse(fs.readFileSync(path.join(directory,'baked-frames.json')));
const image = decodeRgbaPng(fs.readFileSync(path.join(directory,metadata.atlas)));
const packed = decodeRgbaPng(fs.readFileSync(path.join(directory,metadata.layeredAtlas)));
const mask = decodeRgbaPng(fs.readFileSync(path.join(directory,metadata.mask)));
assert.equal(metadata.frames.length,15);
assert.equal(packed.width,2048);assert.equal(packed.height,2048);
assert.equal(mask.width,256);assert.equal(mask.height,256);
const stanceErrors=[];
for (const frame of metadata.frames) {
  for(let y=0;y<512;y++)for(let x=0;x<512;x++){
    const alpha=image.pixels[((frame.y+y)*image.width+frame.x+x)*4+3];
    if(x<8||y<8||x>=504||y>=504)assert.equal(alpha,0,`${frame.id}: perimeter`);
  }
  assert.deepEqual(frame.joints.root,{x:280,y:272,angle:0});
  if(frame.id.startsWith('walk'))for(const side of ['far','near']) {
    if(!frame.stance[side])continue;
    const sole=frame.soles[side],target=frame.feet[side];
    const error=Math.hypot(sole.x-target[0],sole.y-target[1]);
    assert.ok(error<=1.5,`${frame.id} ${side}: actual opaque sole differs from authoring target ${error}px`);
    assert.equal(mask.pixels[(Math.floor(sole.y/2)*256+128)*4],255,'stance sole must receive full continuous correction');
    stanceErrors.push(error);
  }
}
const walk=metadata.frames.slice(0,12);
assert.equal(walk.filter(f=>f.stance.near).length,6);
assert.equal(walk.filter(f=>f.stance.far).length,6);
assert.ok(new Set(walk.map(f=>f.joints.nearKnee.angle)).size>=10,'twelve distinct articulations');
for(const side of ['near','far']){
  const stance=walk.filter(f=>f.stance[side]);
  assert.ok(stance[0].soles[side].x-stance[5].soles[side].x>=104,'readable 100px+ stance travel');
  for(let i=1;i<stance.length;i++)assert.ok(stance[i].soles[side].x<stance[i-1].soles[side].x,'stance travels continuously backward');
}
assert.equal(mask.pixels[(100*256+128)*4],0,'hip region is not translated');
console.log(JSON.stringify({frames:15,walkFrames:12,layeredTexture:[packed.width,packed.height],transparentPerimeter:8,fixedRoot:true,stanceSamples:stanceErrors.length,maxAuthoredContactErrorPixels:Math.max(...stanceErrors),runtimeFootSlip:'not measured by this authoring check'},null,2));
