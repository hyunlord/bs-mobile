import fs from 'node:fs';
import assert from 'node:assert/strict';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const metadata = JSON.parse(fs.readFileSync('tools/art-authoring/hero-b/baked-frames.json'));
const image = decodeRgbaPng(fs.readFileSync(metadata.atlas));
assert.equal(metadata.frames.length, 11);
const bottoms=[];
for (const frame of metadata.frames) {
  let bottom=0;
  for(let y=0;y<512;y++)for(let x=0;x<512;x++){
    const alpha=image.pixels[((frame.y+y)*image.width+frame.x+x)*4+3];
    if(x<8||y<8||x>=504||y>=504)assert.equal(alpha,0,`${frame.id}: perimeter`);
    if(alpha>127)bottom=Math.max(bottom,y);
  }
  assert.deepEqual(frame.joints.root,{x:280,y:272,angle:0});
  if(frame.id.startsWith('walk')){
    assert.equal(Math.max(frame.feet.near[1],frame.feet.far[1]),420);
    bottoms.push(bottom);
  }
}
assert.ok(new Set(metadata.frames.slice(0,8).map(f=>f.joints.nearKnee.angle)).size>=6,'distinct leg articulation');
assert.ok(new Set(metadata.frames.slice(0,8).map(f=>f.joints.nearElbow.angle)).size>=5,'distinct arm articulation');
// Rotated boot silhouette can shift its last antialiased pixel; stance contact cannot bob a whole limb.
assert.ok(Math.max(...bottoms)-Math.min(...bottoms)<=6,'planted boot pixel edge instability');
console.log(JSON.stringify({frames:11,transparentPerimeter:8,fixedRoot:true,walkGroundAnchorY:420,walkOpaqueBottoms:bottoms,referenceGroundLine:'foot target, not whole-body translation'},null,2));
