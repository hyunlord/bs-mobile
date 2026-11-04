import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
export function buildWorldMapAtlases(input) {
  const source = JSON.parse(fs.readFileSync(path.join(directory, 'sources.json'), 'utf8'));
  const manifest = { version: source.version, atlases: [], assets: [] };
  const pngs = new Map();
  assert.equal(new Set(source.assets.map(asset => asset.id)).size, source.assets.length);
  for (const group of ['terrain', 'objects']) {
    const selected = source.assets.filter(asset => asset.group === group);
    const images = selected.map(asset => {
      const bytes = fs.readFileSync(path.join(input, asset.file));
      assert.equal(createHash('sha256').update(bytes).digest('hex'), asset.sha256, asset.file);
      const image = decodeRgbaPng(bytes);
      assert.equal(image.width, asset.width); assert.equal(image.height, asset.height);
      assert.equal(asset.pivot.length, 2);
      assert.ok(asset.pivot.every(value => Number.isFinite(value) && value >= 0 && value <= 1));
      assert.deepEqual(asset.border, [0, 0, 0, 0]);
      return image;
    });
    const result = pack(images, 2048, 4);
    assert.ok(result.atlas.height <= 4096, 'Atlas must not be downscaled by the importer.');
    pngs.set(group, encodeRgbaPng(result.atlas));
    manifest.atlases.push({ name: group, width: result.atlas.width, height: result.atlas.height });
    manifest.assets.push(...selected.map((asset, index) => ({ ...asset, atlas: group, rect: result.rects[index] })));
  }
  return { pngs, manifest };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const result = buildWorldMapAtlases(process.argv[2] ?? 'artifacts/world-map/source');
  const output = process.argv[3] ?? 'unity/Assets/Resources/WorldMapArt';
  fs.mkdirSync(output, { recursive: true });
  for (const [name, bytes] of result.pngs) fs.writeFileSync(path.join(output, name + '.png'), bytes);
  fs.writeFileSync(path.join(output, 'world-map.json'), JSON.stringify(result.manifest, null, 2) + '\n');
  console.log(JSON.stringify({ sprites: result.manifest.assets.length, atlases: result.manifest.atlases }));
}
