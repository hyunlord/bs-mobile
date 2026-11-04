import fs from 'node:fs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const input = process.argv[2] ?? 'artifacts/world-map/source';
const output = process.argv[3] ?? 'unity/Assets/Resources/WorldMapArt';
const source = JSON.parse(fs.readFileSync('tools/art-authoring/world-map/sources.json', 'utf8'));
const manifest = JSON.parse(fs.readFileSync(output + '/world-map.json', 'utf8'));
assert.equal(source.assets.length, manifest.assets.length);
const atlases = new Map(manifest.atlases.map(a => [a.name, decodeRgbaPng(fs.readFileSync(output + '/' + a.name + '.png'))]));
for (const entry of source.assets) {
  const registered = manifest.assets.find(a => a.id === entry.id);
  assert.ok(registered, entry.id);
  for (const key of Object.keys(entry)) assert.deepEqual(registered[key], entry[key]);
  const bytes = fs.readFileSync(input + '/' + entry.file);
  assert.equal(createHash('sha256').update(bytes).digest('hex'), entry.sha256);
  const original = decodeRgbaPng(bytes), atlas = atlases.get(registered.atlas);
  const [x, y, width, height] = registered.rect;
  assert.equal(width, original.width); assert.equal(height, original.height);
  for (let row = 0; row < height; row++) {
    assert.deepEqual(atlas.pixels.subarray(((y + row) * atlas.width + x) * 4, ((y + row) * atlas.width + x + width) * 4),
      original.pixels.subarray(row * width * 4, (row + 1) * width * 4), entry.id + ' pixel row ' + row);
  }
}
console.log(`PASS: ${source.assets.length} approved sources retain identical RGBA pixels and metadata in registered atlas rectangles.`);
