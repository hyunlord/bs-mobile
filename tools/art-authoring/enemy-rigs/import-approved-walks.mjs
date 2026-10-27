// #399: append approved walks without resampling or replacing any historical atlas pixels.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const spec = JSON.parse(fs.readFileSync(new URL('./approved-walks.json', import.meta.url)));
const art = path.join(root, 'unity/Assets/Art');
const atlasPath = path.join(art, 'fallow-enemy-rigs.png');
const manifestPath = path.join(art, 'wave1a-manifest.json');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const frames = index => Array.from({ length: 16 }, (_, frame) => ({
  x: (frame % 8) * spec.cell + spec.padding,
  y: spec.baseHeight + (index * 2 + Math.floor(frame / 8)) * spec.cell + spec.padding,
  width: spec.cell - spec.padding * 2, height: spec.cell - spec.padding * 2,
}));
const expectedRole = (walk, index) => ({ id: walk.id, atlas: 'fallow-enemy-rigs', frames: frames(index),
  pivot: spec.pivot, worldSize: walk.worldSize, frameMs: walk.frameMs ?? spec.frameMs, tween: spec.tween });

function checkBase(atlas) {
  assert.equal(atlas.width, spec.width);
  assert.ok([spec.baseHeight, spec.previousHeight, spec.height].includes(atlas.height));
  assert.equal(hash(atlas.pixels.subarray(0, spec.width * spec.baseHeight * 4)), spec.basePixelsSha256,
    'Historical atlas pixels changed; do not silently overwrite newer artwork');
  if (atlas.height >= spec.previousHeight) assert.equal(hash(atlas.pixels.subarray(0, spec.width * spec.previousHeight * 4)),
    spec.previousPixelsSha256, 'Previously approved shield/ram pixels changed');
}

function check(atlas, manifest) {
  checkBase(atlas);
  assert.equal(atlas.height, spec.height);
  const definition = manifest.atlases.find(a => a.id === 'fallow-enemy-rigs');
  assert.equal(definition.width, spec.width); assert.equal(definition.height, spec.height);
  for (const [index, walk] of spec.walks.entries()) {
    if (walk.source) assert.equal(hash(fs.readFileSync(path.join(root, walk.source))), walk.sourceSha256);
    assert.deepEqual(manifest.roles.find(r => r.id === walk.id), expectedRole(walk, index));
    const reconstructed = Buffer.alloc(4096 * 256 * 4);
    for (let frame = 0; frame < 16; frame++) for (let y = 0; y < 256; y++) {
      const from = ((spec.baseHeight + index * 512 + Math.floor(frame / 8) * 256 + y) * spec.width + (frame % 8) * 256) * 4;
      const columns = walk.sourceColumns ?? 16;
      const to = ((Math.floor(frame / columns) * 256 + y) * columns * 256 + (frame % columns) * 256) * 4;
      atlas.pixels.copy(reconstructed, to, from, from + 256 * 4);
    }
    assert.equal(hash(reconstructed), walk.pixelsSha256, `${walk.id} exact approved RGBA including transparent pixels`);
    for (const f of frames(index)) {
      let opaque = 0;
      for (let y = -4; y < 252; y++) for (let x = -4; x < 252; x++) {
        const alpha = atlas.pixels[((f.y + y) * atlas.width + f.x + x) * 4 + 3];
        if (x < 0 || y < 0 || x >= 248 || y >= 248) assert.equal(alpha, 0, `${walk.id} padding`);
        else opaque += alpha > 0 ? 1 : 0;
      }
      assert.ok(opaque > 0, `${walk.id} nonempty frame`);
    }
  }
}

const args = process.argv.slice(2);
const original = decodeRgbaPng(fs.readFileSync(atlasPath));
const manifest = JSON.parse(fs.readFileSync(manifestPath));
if (args.length === 1 && args[0] === '--check') check(original, manifest);
else if (args.length === 2 && args[0] === '--source') {
  checkBase(original);
  const pixels = Buffer.alloc(spec.width * spec.height * 4);
  original.pixels.copy(pixels, 0, 0, spec.width * spec.baseHeight * 4);
  for (const [index, walk] of spec.walks.entries()) {
    const bytes = fs.readFileSync(path.join(args[1], walk.file));
    assert.equal(hash(bytes), walk.sha256, `${walk.file} approved Release bytes`);
    const source = decodeRgbaPng(bytes);
    const columns = walk.sourceColumns ?? 16;
    assert.equal(source.width, columns * 256); assert.equal(source.height, 16 / columns * 256);
    for (let frame = 0; frame < 16; frame++) for (let y = 0; y < spec.cell; y++) {
      const from = ((Math.floor(frame / columns) * spec.cell + y) * source.width + (frame % columns) * spec.cell) * 4;
      const to = ((spec.baseHeight + index * 512 + Math.floor(frame / 8) * 256 + y) * spec.width + (frame % 8) * 256) * 4;
      source.pixels.copy(pixels, to, from, from + spec.cell * 4);
    }
    const at = manifest.roles.findIndex(r => r.id === walk.id);
    assert.ok(at >= 0, 'Existing walk role required');
    manifest.roles[at] = expectedRole(walk, index);
  }
  manifest.atlases.find(a => a.id === 'fallow-enemy-rigs').height = spec.height;
  const atlas = { width: spec.width, height: spec.height, pixels };
  check(atlas, manifest);
  fs.writeFileSync(atlasPath, encodeRgbaPng(atlas));
  fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
} else throw new Error('Usage: node tools/art-authoring/enemy-rigs/import-approved-walks.mjs --check | --source DIR');
const fpPath = path.join(art, 'first-playable-manifest.json');
const stone = spec.firstPlayable;
const stonePath = path.join(art, stone.file);
const fp = JSON.parse(fs.readFileSync(fpPath));
if (args[0] === '--source') {
  const bytes = fs.readFileSync(path.join(args[1], stone.file));
  assert.equal(hash(bytes), stone.sha256, 'Approved stone breaker Release bytes');
  const source = decodeRgbaPng(bytes);
  assert.equal(source.width, 4096); assert.equal(source.height, 256);
  const pixels = Buffer.alloc(stone.atlas.width * stone.atlas.height * 4);
  for (const [frame, rect] of stone.role.frames.entries()) for (let y = 0; y < 256; y++) {
    const from = (y * source.width + frame * 256) * 4;
    source.pixels.copy(pixels, ((rect.y + y) * stone.atlas.width + rect.x) * 4, from, from + 256 * 4);
  }
  fs.writeFileSync(stonePath, encodeRgbaPng({ width: stone.atlas.width, height: stone.atlas.height, pixels }));
  const at = fp.roles.findIndex(r => r.id === stone.role.id);
  assert.ok(at >= 0, 'Existing first-playable walk role required');
  fp.roles[at] = stone.role;
  const atlasIndex = fp.atlases.findIndex(a => a.id === stone.atlas.id);
  if (atlasIndex < 0) fp.atlases.push(stone.atlas); else fp.atlases[atlasIndex] = stone.atlas;
  fs.writeFileSync(fpPath, JSON.stringify(fp, null, 2) + '\n');
}
const stoneImage = decodeRgbaPng(fs.readFileSync(stonePath));
assert.equal(stoneImage.width, stone.atlas.width); assert.equal(stoneImage.height, stone.atlas.height);
const stoneOriginal = Buffer.alloc(4096 * 256 * 4);
for (const [frame, rect] of stone.role.frames.entries()) for (let y = 0; y < 256; y++) {
  const from = ((rect.y + y) * stoneImage.width + rect.x) * 4;
  stoneImage.pixels.copy(stoneOriginal, (y * 4096 + frame * 256) * 4, from, from + 256 * 4);
}
assert.equal(hash(stoneOriginal), stone.pixelsSha256, 'Every approved full-cell RGBA pixel preserved');
assert.deepEqual(fp.roles.find(r => r.id === stone.role.id), stone.role);
assert.deepEqual(fp.atlases.find(a => a.id === stone.atlas.id), stone.atlas);
console.log('PASS: four approved 16-frame walks, historical/shared RGBA preserved, shared atlas 2048×3840; padded stone atlas 2080×520, full 256px cells preserved');
