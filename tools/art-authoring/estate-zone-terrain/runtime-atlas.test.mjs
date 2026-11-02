import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
const read = path => JSON.parse(readFileSync(path, 'utf8'));
const root = 'unity/Assets/Resources/EstateArt/';
const manifest = read(root + 'estate-zone-art.json');
const registries = ['source-assets', 'field-source-assets', 'dense-source-assets'].flatMap(name => read(`tools/art-authoring/estate-zone-terrain/${name}.json`).assets);

test('runtime atlas only uses exact approved 1x source identities and source pivots', () => {
  assert.equal(manifest.version, 'estate-zone-art-v1');
  assert.equal(manifest.sprites.length, 128);
  const names = new Set();
  for (const item of manifest.sprites) {
    assert(!names.has(item.id)); names.add(item.id);
    const source = registries.find(a => a.file === item.source);
    assert(source); assert.equal(source.sha256, item.sha256);
    assert(source.scale === undefined || source.scale === 1);
    assert.deepEqual(item.rect.slice(2), source.canvas);
    assert.deepEqual(item.pivot, [source.pivotTopLeft[0] / source.canvas[0], 1 - source.pivotTopLeft[1] / source.canvas[1]]);
    assert.equal(item.atlas, 'zone-terrain');
    assert(item.rect[0] + item.rect[2] <= manifest.width);
    assert(item.rect[1] + item.rect[3] <= manifest.height);
  }
});

test('river r2 chunks preserve native origins, widths and dark pairs; no river r3', () => {
  const river = read('unity/Assets/Game/App/Resources/EstateZoneRiver.json');
  assert.equal(river.chunks.length, 12);
  for (const c of river.chunks) {
    const lit = manifest.sprites.find(s => s.id === c.lit);
    const dark = manifest.sprites.find(s => s.id === c.dark);
    assert(lit && dark);
    const source = registries.find(s => s.file === lit.source);
    assert.equal(source.metadata, 'dense-river-r2-placement.json');
    assert.deepEqual([c.x, c.y], source.nativeOrigin);
    assert.deepEqual([c.width, c.height], source.nativeSize);
  }
  assert(!manifest.sprites.some(s => s.source.includes('river-r3')));
});

test('runtime texture identity and mobile memory delta match committed data', () => {
  const b = readFileSync(root + 'estate-zone-terrain.png');
  const record = read('docs/metrics/texture-memory-deltas/estate-zone-terrain-495.json').records[0];
  assert.equal(createHash('sha256').update(b).digest('hex'), record.after.identity.sha256);
  assert.equal(b.readUInt32BE(16), manifest.width); assert.equal(b.readUInt32BE(20), manifest.height);
  assert.equal(record.after.formats.android, 'ASTC4'); assert.equal(record.after.formats.ios, 'ASTC4');
  assert.equal(record.after.formats.mac, 'RGBA32');
});

test('every packed RGBA crop is identical to approved source pixels after padding extrusion', async () => {
  const { decodeRgbaPng } = await import('../../wave1a-art.mjs');
  const atlas=decodeRgbaPng(readFileSync(root+'estate-zone-terrain.png'));
  for(const item of manifest.sprites) {
    const [x,y,w,h]=item.rect;
    const hash=createHash('sha256');
    for(let row=0;row<h;row++) hash.update(atlas.pixels.subarray(((y+row)*atlas.width+x)*4,((y+row)*atlas.width+x+w)*4));
    assert.equal(hash.digest('hex'),item.rgbaSha256,item.id);
  }
});

test('river chunks with off-map canvas centres retain visible land occupancy', () => {
  const river=read('unity/Assets/Game/App/Resources/EstateZoneRiver.json');
  for(const chunk of river.chunks) {
    assert(Array.isArray(chunk.occupiedZones));
    assert.deepEqual(chunk.occupiedZones,[...new Set(chunk.occupiedZones)].sort());
  }
  for(const suffix of ['3-3','0-4','1-4']) {
    const chunk=river.chunks.find(c=>c.lit.includes(suffix));
    assert(chunk && chunk.occupiedZones.length>0,suffix);
  }
});
