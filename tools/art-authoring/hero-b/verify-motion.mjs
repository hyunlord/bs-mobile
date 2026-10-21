import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const directory = process.argv[2] || 'artifacts/m1-246/hero-motion-b';
const runtime = JSON.parse(fs.readFileSync(path.join(directory, 'motion-runtime.json')));
const metadata = JSON.parse(fs.readFileSync(path.join(directory, 'baked-frames.json')));
const image = decodeRgbaPng(fs.readFileSync(path.join(directory, metadata.atlas)));
const packed = decodeRgbaPng(fs.readFileSync(path.join(directory, metadata.layeredAtlas)));
assert.equal(runtime.frames.length, 16);
assert.equal(runtime.gaitFrames.length, 13);
assert.equal(runtime.jointNames.length, 15);
assert.deepEqual([packed.width, packed.height], [2048,2048]);
const sword = runtime.jointNames.indexOf('sword');
for (const [index, frame] of metadata.frames.entries()) {
  const exported = runtime.frames[index];
  assert.equal(frame.id, exported.id);
  assert.equal(exported.joints.length, runtime.jointNames.length);
  for (const [jointIndex, name] of runtime.jointNames.entries()) {
    const expected = frame.joints[name], actual = exported.joints[jointIndex];
    assert.deepEqual(actual, { x: expected.x, y: expected.y, angleRadians: expected.angle }, `exact b/c pose ${frame.id}/${name}`);
  }
  assert.deepEqual(frame.joints.root, {x:300,y:272,angle:0});
  for (let y=0;y<512;y++) for(let x=0;x<512;x++) {
    if (x<8||y<8||x>=504||y>=504) assert.equal(image.pixels[((frame.y+y)*image.width+frame.x+x)*4+3],0,`${frame.id} has clipped perimeter`);
  }
}
for (const clip of runtime.clips) {
  const frames = runtime.frames.slice(clip.firstFrame,clip.firstFrame+clip.frameCount);
  assert.ok(new Set(frames.map(frame=>frame.joints[sword].angleRadians)).size>=3,`${clip.id} distinct sword articulation`);
  assert.deepEqual(frames[0].joints,frames.at(-1).joints,`${clip.id} returns to idle upper pose`);
}
console.log(JSON.stringify({exactSharedJointSamples:runtime.frames.length*runtime.jointNames.length,actionFrames:runtime.frames.length,unchangedGaitFrames:runtime.gaitFrames.length,rootMotion:false,transparentPerimeter:8,runtimeVisualAcceptance:'pending native comparison'},null,2));
