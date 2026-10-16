// Offline authoring only. Packs Astra's knight game sheets (Release knight-game-sheets-r4-20261016, approved r3 art, plus
// the idle/hit review candidates from knight-game-sheets-r4-additions-review-20261016) into one atlas without resampling,
// and registers the same hero roles in the first-playable and wave-1a manifests so no profile lacks a hero role (#268).
// Usage: node tools/art-authoring/knight-sheets/pack.mjs <approved release dir> <additions release dir>
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [approvedDir, additionsDir] = process.argv.slice(2).map(p => path.resolve(p));
assert.ok(approvedDir && additionsDir, 'usage: pack.mjs <approved release dir> <additions release dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));

// One row per clip, cells left to right as delivered. Status: approved r3 art or a review candidate used temporarily.
const clips = [
  { clip: 'hero-a-walk', role: 'walk', status: 'approved-r3' },
  { clip: 'hero-a-slash', role: 'action.slash', status: 'approved-r3' },
  { clip: 'hero-a-aim', role: 'action.aim', status: 'approved-r3' },
  { clip: 'hero-a-cast', role: 'action.cast', status: 'approved-r3' },
  { clip: 'hero-a-blow', role: 'action.blow', status: 'approved-r3' },
  { clip: 'hero-a-throw', role: 'action.throw', status: 'approved-r3' },
  { clip: 'hero-a-moving-keys', role: 'key', status: 'approved-r3', keys: ['slash', 'aim', 'cast', 'blow', 'throw'] },
  { clip: 'hero-a-idle', role: 'idle', status: 'review-candidate-temporary', additions: true },
  { clip: 'hero-a-hit', role: 'hit', status: 'review-candidate-temporary', additions: true },
];
const approved = read(path.join(approvedDir, 'manifest.json'));
const additions = read(path.join(additionsDir, 'additions-manifest.json'));
const [cellW, cellH] = approved.cellSizePx, padding = 2, columns = 8;
for (const m of [approved, additions]) {
  assert.deepEqual(m.cellSizePx, [cellW, cellH]);
  assert.deepEqual(m.pivotUnityNormalized, approved.pivotUnityNormalized);
  assert.equal(m.bodyHeightReferencePx, approved.bodyHeightReferencePx);
}
const atlasW = padding * 2 + columns * cellW, atlasH = padding * 2 + clips.length * cellH;
const atlas = Buffer.alloc(atlasW * atlasH * 4);
const packed = [];
clips.forEach((entry, row) => {
  const source = (entry.additions ? additions : approved).clips.find(c => c.name === entry.clip);
  assert.ok(source, entry.clip);
  const dir = entry.additions ? additionsDir : approvedDir;
  const sheetBytes = fs.readFileSync(path.join(dir, source.sheet));
  const sheet = decodeRgbaPng(sheetBytes);
  assert.ok(source.frameCount <= columns && source.frames.length === source.frameCount, entry.clip);
  const frames = source.frames.map((f, column) => {
    const [sx, sy, w, h] = f.sheetRectTopLeftPx;
    assert.deepEqual([w, h], [cellW, cellH]);
    const x = padding + column * cellW, y = padding + row * cellH;
    for (let r = 0; r < cellH; r++) sheet.pixels.copy(atlas, ((y + r) * atlasW + x) * 4, ((sy + r) * sheet.width + sx) * 4, ((sy + r) * sheet.width + sx + cellW) * 4);
    // The delivered SHA covers the approved 240x200 source inside the cell; check it from the packed atlas.
    const [ox, oy, ow, oh] = f.sourceRectWithinCellTopLeftPx;
    const region = Buffer.alloc(ow * oh * 4);
    for (let r = 0; r < oh; r++) atlas.copy(region, r * ow * 4, ((y + oy + r) * atlasW + x + ox) * 4, ((y + oy + r) * atlasW + x + ox + ow) * 4);
    assert.equal(sha(region), f.decodedRgbaSha256, `${entry.clip}[${column}] differs from the delivered pixels`);
    return { x, y, width: cellW, height: cellH, frameTicks60: f.frameTicks60, decodedRgbaSha256: f.decodedRgbaSha256 };
  });
  packed.push({ clip: entry.clip, role: entry.role, status: entry.status, keys: entry.keys, sheet: source.sheet, sheetSha256: sha(sheetBytes), frames });
});

function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(atlasW, atlasH, atlas);
const atlasPath = 'unity/Assets/Art/hero-knight.png';
fs.writeFileSync(path.join(root, atlasPath), atlasBytes);

// World size: the delivered 115 px body reference matches the current knight's figure (0.6 world units for its 242 px
// frame, figure 209 px tall), so a 320x240 cell is 240/115 of that figure.
const figure = 0.6 * 209 / 242;
const worldSize = { x: +(figure * cellW / approved.bodyHeightReferencePx).toFixed(6), y: +(figure * cellH / approved.bodyHeightReferencePx).toFixed(6) };
const pivot = { x: approved.pivotUnityNormalized[0], y: approved.pivotUnityNormalized[1] };
const ms = ticks => Math.round(ticks * 1000 / 60);
const prefix = 'hero.core.frontier_knight.';
const roles = [];
for (const p of packed) {
  const rect = f => ({ x: f.x, y: f.y, width: f.width, height: f.height });
  if (p.keys) p.keys.forEach((key, i) => roles.push({ id: `${prefix}key.${key}`, atlas: 'hero-knight', frames: [rect(p.frames[i])], pivot, worldSize, frameMs: ms(p.frames[i].frameTicks60), tween: 'none' }));
  else roles.push({ id: prefix + p.role, atlas: 'hero-knight', frames: p.frames.map(rect), pivot, worldSize, frameMs: ms(p.frames[0].frameTicks60), tween: 'none' });
  if (p.role === 'hit') roles.push({ id: prefix + 'death', atlas: 'hero-knight', frames: [rect(p.frames[p.frames.length - 1])], pivot, worldSize, frameMs: ms(p.frames[0].frameTicks60), tween: 'none' });
}
const license = {
  id: 'generated-knight-sheets', origin: 'Generated with built-in image_gen for this project (Character Forge sheet pipeline, BiRefNet matting); packed without resampling',
  source: 'https://openai.com/policies/terms-of-use/', prompt: 'docs/licenses/prompts/knight-sheets-r4-prompt.txt',
  commercialUse: 'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.'
};
const atlasEntry = { id: 'hero-knight', path: 'Assets/Art/hero-knight.png', width: atlasW, height: atlasH, padding, license: license.id };
for (const file of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const m = read(path.join(root, file));
  m.licenses = m.licenses.filter(l => l.id !== license.id).concat([license]);
  m.atlases = m.atlases.filter(a => a.id !== atlasEntry.id).concat([atlasEntry]);
  const ids = new Set(roles.map(r => r.id));
  const at = m.roles.findIndex(r => r.id.startsWith(prefix));
  m.roles = m.roles.filter(r => !ids.has(r.id));
  m.roles.splice(at < 0 ? m.roles.length : Math.min(at, m.roles.length), 0, ...roles);
  fs.writeFileSync(path.join(root, file), JSON.stringify(m, null, 2) + '\n');
}
const record = {
  schemaVersion: 1,
  releases: { approved: 'knight-game-sheets-r4-20261016', additions: 'knight-game-sheets-r4-additions-review-20261016' },
  approvedManifestSha256: sha(fs.readFileSync(path.join(approvedDir, 'manifest.json'))),
  additionsManifestSha256: sha(fs.readFileSync(path.join(additionsDir, 'additions-manifest.json'))),
  atlas: atlasPath, atlasSha256: sha(atlasBytes), width: atlasW, height: atlasH, padding, cell: [cellW, cellH],
  pivotUnityNormalized: approved.pivotUnityNormalized, bodyHeightReferencePx: approved.bodyHeightReferencePx, worldSize,
  movingKeyHoldTicks60: approved.movingAttackContract.movingKeyHoldTicks60,
  clips: packed
};
fs.writeFileSync(path.join(root, 'tools/art-authoring/knight-sheets/packed-frames.json'), JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ atlas: atlasPath, width: atlasW, height: atlasH, frames: packed.reduce((n, p) => n + p.frames.length, 0), roles: roles.length, worldSize }));
