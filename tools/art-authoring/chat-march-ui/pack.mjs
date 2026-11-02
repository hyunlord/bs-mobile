import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.dirname(fileURLToPath(import.meta.url));
const source = JSON.parse(fs.readFileSync(path.join(root, 'sources.json'), 'utf8'));
export function buildChatMarchAtlases(input) {
  const manifest = { release: source.release, atlases: [], assets: [] };
  const pngs = new Map();
  for (const name of ['chat', 'march']) {
    const selected = source.assets.filter(asset => !asset.excluded && asset.id.startsWith('chat-') === (name === 'chat'));
    const images = selected.map(asset => {
      const bytes = fs.readFileSync(path.join(input, asset.file));
      assert.equal(createHash('sha256').update(bytes).digest('hex'), asset.sha256, asset.file);
      const image = decodeRgbaPng(bytes);
      assert.equal(image.width, asset.width); assert.equal(image.height, asset.height);
      assert.equal(asset.border.length, 4);
      assert.ok(asset.border.every(value => Number.isInteger(value) && value >= 0));
      assert.ok(asset.border[0] + asset.border[2] < image.width);
      assert.ok(asset.border[1] + asset.border[3] < image.height);
      return image;
    });
    const result = pack(images, name === 'chat' ? 128 : 2048, 4);
    pngs.set(name, encodeRgbaPng(result.atlas));
    manifest.atlases.push({ name, width: result.atlas.width, height: result.atlas.height });
    manifest.assets.push(...selected.map((asset, index) => ({ ...asset, atlas: name, rect: result.rects[index] })));
  }
  return { pngs, manifest };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const input = process.argv[2] ?? 'artifacts/chat-march/source';
  const output = process.argv[3] ?? 'unity/Assets/Resources/ChatMarchUI';
  const result = buildChatMarchAtlases(input);
  fs.mkdirSync(output, { recursive: true });
  for (const [name, png] of result.pngs) fs.writeFileSync(path.join(output, name + '.png'), png);
  fs.writeFileSync(path.join(output, 'chat-march.json'), JSON.stringify(result.manifest, null, 2) + '\n');
  console.log(JSON.stringify({ sprites: result.manifest.assets.length, atlases: result.manifest.atlases }));
}
