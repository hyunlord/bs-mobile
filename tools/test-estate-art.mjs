import assert from 'node:assert/strict';
import test from 'node:test';
import { decodeRgbaPng } from './wave1a-art.mjs';
import { build, check, downsample, encodeRgbaPng, opaqueWidth, pack } from './estate-art.mjs';

const image = (width, height, fill) => ({ width, height, pixels: Buffer.from(Array.from({ length: width * height }, (_, i) => fill(i % width, Math.floor(i / width))).flat()) });

test('PNG encoding round-trips through the repository decoder', () => {
  const source = image(5, 3, (x, y) => [x * 40, y * 80, 7, x === 4 ? 0 : 255]);
  assert.deepEqual(decodeRgbaPng(encodeRgbaPng(source)).pixels, source.pixels);
});

test('downsampling averages premultiplied colour so transparent pixels do not darken edges', () => {
  const source = image(2, 1, x => x === 0 ? [200, 100, 50, 255] : [0, 0, 0, 0]);
  const out = downsample(source, 1, 1);
  assert.deepEqual([...out.pixels], [200, 100, 50, 128]);
});

test('packing is deterministic and keeps sprites inside the atlas with padding', () => {
  const sprites = [image(30, 20, () => [1, 2, 3, 255]), image(50, 10, () => [4, 5, 6, 255]), image(40, 40, () => [7, 8, 9, 255])];
  const a = pack(sprites, 64, 2), b = pack(sprites, 64, 2);
  assert.deepEqual(a.rects, b.rects);
  for (const [x, y, w, h] of a.rects) assert.ok(x >= 2 && y >= 2 && x + w + 2 <= 64 && y + h + 2 <= a.atlas.height);
});

test('a replaced source file only changes its own sprite and the manifest hashes', () => {
  const sources = { 'a.png': encodeRgbaPng(image(8, 8, () => [10, 20, 30, 255])), 'b.png': encodeRgbaPng(image(8, 8, () => [40, 50, 60, 255])),
    'body.png': encodeRgbaPng(image(4, 4, () => [1, 1, 1, 255])), 'shadow.png': encodeRgbaPng(image(4, 4, () => [0, 0, 0, 128])),
    'manifest.json': Buffer.from(JSON.stringify({ records: [] })) };
  const spec = { version: 'estate-art-r1', note: '', atlasWidth: 64, padding: 2, buildings: { release: 'r', body: 'body.png', shadow: 'shadow.png', shadowScale: 0.5, manifest: 'manifest.json' },
    assets: ['a', 'b'].map(id => ({ id, release: 'r', file: id + '.png', atlas: 'decor', maxSize: 8, pivot: [0.5, 0], displayWidth: 10, layer: 'standing' })) };
  const first = JSON.parse(build(spec, f => sources[f])['estate-art.json']);
  sources['b.png'] = encodeRgbaPng(image(8, 8, () => [90, 90, 90, 255]));
  const second = JSON.parse(build(spec, f => sources[f])['estate-art.json']);
  assert.deepEqual(second.sprites.map(s => s.rect), first.sprites.map(s => s.rect), 'same names and sizes keep the layout');
  assert.equal(second.sprites[0].source.sha256, first.sprites[0].source.sha256);
  assert.notEqual(second.sprites[1].source.sha256, first.sprites[1].source.sha256);
});

test('the body width is the opaque width inside the cell, ignoring near-transparent pixels like assembly.js trim()', () => {
  const cell = image(10, 2, (x, y) => [0, 0, 0, x === 0 ? 8 : x >= 3 && x <= 6 ? 255 : x === 8 && y === 1 ? 9 : 0]);
  assert.equal(opaqueWidth(cell, [0, 0, 10, 2]), 6, 'columns 3..8; alpha 8 at column 0 does not count');
  assert.equal(opaqueWidth(cell, [3, 0, 4, 2]), 4);
});

test('the committed estate art matches its manifest', () => {
  const manifest = check();
  assert.equal(manifest.version, 'estate-art-r1');
  assert.ok(manifest.sprites.length >= 50);
  assert.equal(manifest.buildings.records.find(r => r.id === 'house-s1').bodyWidth, 90, "Astra's trimmed house body (assembly.js bodyWidths)");
});
