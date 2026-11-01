import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { extrudeGutter } from './river-gutter.mjs';
const source = readFileSync('data/opt-in/estate/terrain-layout-r8.json');
const layout = JSON.parse(source);
const used = new Set([...layout.instances.map(i => i.assetId), ...layout.zones.map(z => z.groundAssetId), layout.background.assetId]);
const assets = layout.assets.filter(a => used.has(a.id));
const images = assets.map(a => {
  const bytes = readFileSync(`artifacts/terrain-layout-approved/cache/${a.source.sha256}.png`);
  if (createHash('sha256').update(bytes).digest('hex') !== a.source.sha256) throw Error(a.id);
  const image = decodeRgbaPng(bytes);
  if (image.width !== a.size[0] || image.height !== a.size[1]) throw Error(`Size ${a.id}`);
  return image;
});
const packed = pack(images, 2048, 4);
if (packed.atlas.height > 4096) throw Error('Atlas exceeds 2048x4096');
assets.forEach((a,i) => extrudeGutter(packed.atlas, packed.rects[i]));
const output = { ...layout, sourceSha256: createHash('sha256').update(source).digest('hex'),
  assets: assets.map((a,i) => ({ ...a, rect: packed.rects[i], rgbaSha256: createHash('sha256').update(images[i].pixels).digest('hex') })) };
delete output.reviewOnly;
delete output.reservations;
writeFileSync('unity/Assets/Resources/EstateArt/estate-authored-terrain.png', encodeRgbaPng(packed.atlas));
writeFileSync('unity/Assets/Game/App/Resources/EstateAuthoredLayout.json', JSON.stringify(output) + '\n');
console.log(JSON.stringify({ assets:assets.length, instances:layout.instances.length, width:packed.atlas.width, height:packed.atlas.height, rgbaBytes:packed.atlas.pixels.length }));
