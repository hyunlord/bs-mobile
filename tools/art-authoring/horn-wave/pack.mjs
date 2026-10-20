// Offline authoring only. Packs Astra's muster horn sound wave (Release muster-horn-wave-20261020: one 1024x1024 painted frame
// each for level 1 and the evolved horn) into its own wave-1a atlas without resampling. Each picture is cropped to its nonzero
// alpha box (a lossless crop: only fully transparent pixels go) and keeps the picture's centre as its pivot. The record keeps
// the visible outer radius (alpha > 8) in source pixels: the game sizes the wave so that edge lands on the Core radius.
// Usage: node tools/art-authoring/horn-wave/pack.mjs <release tag> <release dir>
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [release, releaseDir] = process.argv.slice(2);
assert.ok(release && releaseDir, 'usage: pack.mjs <release tag> <release dir>');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const draftFile = path.join(releaseDir, 'horn-manifest-draft.json');
const draft = read(draftFile);
const atlasId = 'wave1a-horn-wave', atlasPath = 'Assets/Art/wave1a-horn-wave.png', license = 'generated-wave1a-horn-wave', padding = 4;
const roleOf = { lv1: 'fx.muster_horn.wave.lv1', evolved: 'fx.muster_horn.wave.evolved' };

const cells = draft.roles.map(role => {
  const bytes = fs.readFileSync(path.join(releaseDir, role.file));
  const image = decodeRgbaPng(bytes);
  assert.deepEqual([image.width, image.height], [role.rect[2], role.rect[3]], role.file);
  assert.deepEqual(role.pivot, [.5, .5], role.file);
  const W = image.width, H = image.height, cx = W / 2, cy = H / 2;
  let [l, t, r, b] = [W, H, 0, 0], outer = 0, maximumAlpha = 0;
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const a = image.pixels[(y * W + x) * 4 + 3];
    if (a === 0) continue;
    l = Math.min(l, x); t = Math.min(t, y); r = Math.max(r, x + 1); b = Math.max(b, y + 1); maximumAlpha = Math.max(maximumAlpha, a);
    if (a > 8) outer = Math.max(outer, Math.hypot(x + .5 - cx, y + .5 - cy));
  }
  const w = r - l, h = b - t, cell = Buffer.alloc(w * h * 4);
  for (let y = 0; y < h; y++) image.pixels.copy(cell, y * w * 4, ((t + y) * W + l) * 4, ((t + y) * W + l + w) * 4);
  return { stage: role.proposedVariant, id: roleOf[role.proposedVariant], file: role.file, sheetSha256: sha(bytes), crop: { left: l, top: t, width: w, height: h },
    pivot: { x: +((cx - l) / w).toFixed(6), y: +(1 - (cy - t) / h).toFixed(6) }, outerRadiusPx: +outer.toFixed(2), maximumAlpha, cell, cellRgbaSha256: sha(cell) };
});
assert.deepEqual(cells.map(c => c.stage).sort(), ['evolved', 'lv1']);

const atlasW = padding + cells.reduce((sum, c) => sum + c.crop.width + padding, 0), atlasH = padding * 2 + Math.max(...cells.map(c => c.crop.height));
assert.ok(atlasW <= 2048 && atlasH <= 2048, `atlas ${atlasW}x${atlasH} exceeds 2048`);
const atlas = Buffer.alloc(atlasW * atlasH * 4);
let x = padding;
for (const c of cells) {
  c.frame = { x, y: padding, width: c.crop.width, height: c.crop.height };
  for (let y = 0; y < c.crop.height; y++) c.cell.copy(atlas, ((padding + y) * atlasW + x) * 4, y * c.crop.width * 4, (y + 1) * c.crop.width * 4);
  x += c.crop.width + padding;
}
function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(atlasW, atlasH, atlas);
fs.writeFileSync(path.join(root, 'unity', atlasPath), atlasBytes);

// worldSize is the cell in units of the visible outer radius: drawn at worldSize x R, the painted edge lands on radius R.
const manifestFile = path.join(root, 'unity/Assets/Art/wave1a-manifest.json');
const manifest = read(manifestFile);
manifest.atlases = manifest.atlases.filter(a => a.id !== atlasId);
manifest.atlases.push({ id: atlasId, path: atlasPath, width: atlasW, height: atlasH, padding, license });
manifest.licenses = manifest.licenses.filter(l => l.id !== license);
manifest.licenses.push({ id: license,
  origin: 'Generated with built-in image_gen by the Astra art pipeline for the muster horn sound wave (Release ' + release + '; level 1 and evolved, matte gouache pressure bands); cropped to the nonzero alpha box, not resampled',
  source: 'https://openai.com/policies/terms-of-use/', prompt: 'docs/licenses/prompts/muster-horn-wave-prompt.md',
  commercialUse: 'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.' });
manifest.roles = manifest.roles.filter(r => !Object.values(roleOf).includes(r.id));
const after = manifest.roles.findIndex(r => r.id === 'attack.core.muster_horn.wave');
manifest.roles.splice(after + 1, 0, ...cells.map(c => ({ id: c.id, atlas: atlasId, frames: [c.frame], pivot: c.pivot,
  worldSize: { x: +(c.crop.width / c.outerRadiusPx).toFixed(6), y: +(c.crop.height / c.outerRadiusPx).toFixed(6) }, frameMs: 1000, tween: 'none' })));
fs.writeFileSync(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
fs.mkdirSync(path.join(root, 'docs/licenses/prompts'), { recursive: true });
fs.copyFileSync(path.join(releaseDir, 'PROMPTS.md'), path.join(root, 'docs/licenses/prompts/muster-horn-wave-prompt.md'));
fs.writeFileSync(path.join(root, 'tools/art-authoring/horn-wave/packed.json'), JSON.stringify({ schemaVersion: 1, release,
  draftManifestSha256: sha(fs.readFileSync(draftFile)), atlas: path.join('unity', atlasPath), atlasSha256: sha(atlasBytes), width: atlasW, height: atlasH,
  roles: cells.map(({ cell, ...c }) => c) }, null, 2) + '\n');
console.log(JSON.stringify({ atlas: [atlasW, atlasH], atlasSha256: sha(atlasBytes), roles: cells.map(c => ({ id: c.id, crop: c.crop, pivot: c.pivot, outerRadiusPx: c.outerRadiusPx, maximumAlpha: c.maximumAlpha })) }));
