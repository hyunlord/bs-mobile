import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const source = readFileSync('data/opt-in/estate/terrain-layout-r8.json');
const approved = JSON.parse(source);
const runtime = JSON.parse(readFileSync('unity/Assets/Game/App/Resources/EstateAuthoredLayout.json'));
const atlas = decodeRgbaPng(readFileSync('unity/Assets/Resources/EstateArt/estate-authored-terrain.png'));
test('all approved protected placements retain every field, without review-only or regenerated data', () => {
  assert.equal(runtime.sourceSha256, createHash('sha256').update(source).digest('hex'));
  assert.deepEqual(runtime.instances, approved.instances);
  assert.equal(runtime.instances.length, 2055);
  assert.deepEqual(runtime.zones, approved.zones);
  assert.equal(runtime.reviewOnly, undefined);
  assert(!runtime.instances.some(i => i.category === 'building'));
});
test('every runtime crop retains the approved source RGBA and anchor', () => {
  for (const a of runtime.assets) {
    const original = approved.assets.find(x => x.id === a.id);
    assert.deepEqual(a.anchor, original.anchor); assert.deepEqual(a.size, original.size); assert.deepEqual(a.source, original.source);
    const [x,y,w,h] = a.rect, rgba = Buffer.alloc(w*h*4);
    for(let row=0; row<h;row++) atlas.pixels.copy(rgba,row*w*4,((y+row)*atlas.width+x)*4,((y+row)*atlas.width+x+w)*4);
    assert.equal(createHash('sha256').update(rgba).digest('hex'), a.rgbaSha256);
  }
  const grass = runtime.assets.find(a => a.id === 'ground-grass-r4-512');
  assert.equal(grass.source.sha256, '525ea7100690e958802a41315b8caa5a69c47ce613e3a1a7a51b28a1ce5c465e');
});
