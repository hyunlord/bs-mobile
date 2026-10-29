import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

// Approved one-resolution sprites only. River r2 remains in the existing terrain path.
const base = 'tools/art-authoring/estate-zone-terrain';
const output = 'unity/Assets/Resources/EstateArt';
const inputs = [
  ['source-assets', 'png', a => a.projection === 'isometric-sprite'],
  ['field-source-assets', 'field-r3-png', a => a.scale === 1],
  ['dense-source-assets', 'dense-png', a => a.scale === 1 && (a.variantId.startsWith('prop-') || a.variantId.includes('mist') || a.projection === 'registered-chunk')],
];
const selected = inputs.flatMap(([registry, directory, accept]) => JSON.parse(readFileSync(`${base}/${registry}.json`)).assets.filter(accept).map(a => ({ ...a, directory })));
const images = selected.map(a => {
  const bytes = readFileSync(resolve('artifacts/estate-zone-terrain', a.directory, a.file));
  if (createHash('sha256').update(bytes).digest('hex') !== a.sha256) throw new Error(`Source identity mismatch: ${a.file}`);
  const image = decodeRgbaPng(bytes);
  if (image.width !== a.canvas[0] || image.height !== a.canvas[1]) throw new Error(`Source dimensions: ${a.file}`);
  return image;
});
const packed = pack(images, 2048, 4);
if (packed.atlas.height > 4096) throw new Error('Runtime atlas exceeds bounded 2048x4096 budget');
const sprites = selected.map((a, i) => ({ id: `zone-${a.variantId}-${a.state ?? 'dark'}`, atlas: 'zone-terrain', layer: 'standing', rect: packed.rects[i], pivot: [a.pivotTopLeft[0] / a.canvas[0], 1 - a.pivotTopLeft[1] / a.canvas[1]], displayWidth: a.canvas[0], opaqueHeight: a.canvas[1], source: a.file, sha256: a.sha256 }));
if (new Set(sprites.map(a => a.id)).size !== sprites.length) throw new Error('Duplicate runtime sprite');
writeFileSync(`${output}/estate-zone-terrain.png`, encodeRgbaPng(packed.atlas));
writeFileSync(`${output}/estate-zone-art.json`, JSON.stringify({ version: 'estate-zone-art-v1', release: 'estate-r8-terrain-488-20261028', width: packed.atlas.width, height: packed.atlas.height, sprites }, null, 2) + '\n');
const river = selected.filter(a => a.projection === 'registered-chunk' && a.state === 'lit').map(a => ({ lit: 'zone-' + a.variantId + '-lit', dark: 'zone-' + a.variantId.replace(/-lit$/, '-dark') + '-dark', x: a.nativeOrigin[0], y: a.nativeOrigin[1], width: a.nativeSize[0], height: a.nativeSize[1] }));
writeFileSync('unity/Assets/Game/App/Resources/EstateZoneRiver.json', JSON.stringify({ chunks: river }, null, 2) + '\n');
console.log(JSON.stringify({ sprites: sprites.length, width: packed.atlas.width, height: packed.atlas.height, rgbaBytes: packed.atlas.pixels.length }));
