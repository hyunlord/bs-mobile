import fs from 'node:fs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const input = process.argv[2] ?? 'artifacts/chat-march/source';
const output = process.argv[3] ?? 'unity/Assets/Resources/ChatMarchUI';
const source = JSON.parse(fs.readFileSync('tools/art-authoring/chat-march-ui/sources.json', 'utf8'));
const manifest = JSON.parse(fs.readFileSync(output + '/chat-march.json', 'utf8'));
const selected = source.assets.filter(asset => !asset.excluded);
assert.equal(manifest.assets.length, selected.length);
assert.equal(new Set(manifest.assets.map(asset => asset.id)).size, selected.length);
assert.equal(manifest.assets.filter(asset => asset.atlas === 'chat').length, 2);
const atlases = new Map(manifest.atlases.map(entry => {
  const image = decodeRgbaPng(fs.readFileSync(`${output}/${entry.name}.png`));
  assert.equal(image.width, entry.width); assert.equal(image.height, entry.height);
  return [entry.name, image];
}));
for (const asset of source.assets) {
  const entry = manifest.assets.find(candidate => candidate.id === asset.id);
  if (asset.excluded) { assert.equal(entry, undefined); continue; }
  assert.ok(entry, asset.id);
  const bytes = fs.readFileSync(`${input}/${asset.file}`);
  assert.equal(createHash('sha256').update(bytes).digest('hex'), asset.sha256);
  assert.equal(entry.sha256, asset.sha256);
  assert.deepEqual(entry.border, asset.border);
  const image = decodeRgbaPng(bytes), atlas = atlases.get(entry.atlas);
  const [x, y, width, height] = entry.rect;
  assert.equal(width, image.width); assert.equal(height, image.height);
  assert.ok(x >= 4 && y >= 4 && x + width + 4 <= atlas.width && y + height + 4 <= atlas.height);
  for (let row = 0; row < height; row++) {
    assert.deepEqual(atlas.pixels.subarray(((y + row) * atlas.width + x) * 4, ((y + row) * atlas.width + x + width) * 4),
      image.pixels.subarray(row * width * 4, (row + 1) * width * 4), `${asset.id} row ${row}`);
  }
}
console.log(`PASS: ${selected.length} approved sprites retain exact source RGBA; 2 excluded sources absent; chat atlas isolated.`);
