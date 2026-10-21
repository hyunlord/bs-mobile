// Offline authoring only. Packs Astra's approved Soldier B r3 walk (Release soldier-b-rank-rig-r3-20261019, 16 frames, 320x240
// cells) into the free bottom-left band of crops-people.png without resampling, and binds it to person.militia and person.guard in
// both art manifests (#331). Each frame is cut to the union of the opaque bounds of all 16 cells, so every frame shares one pivot.
// Usage: node tools/art-authoring/soldier-b/pack.mjs <release dir>  (needs the sheet, its manifest draft and candidate-02.json)
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [releaseDir] = process.argv.slice(2).map(p => path.resolve(p));
assert.ok(releaseDir, 'usage: pack.mjs <release dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));

const release = 'soldier-b-rank-rig-r3-20261019';
const draftFile = 'soldier-b-r3-walk-manifest-draft.json';
const draft = read(path.join(releaseDir, draftFile));
assert.match(draft.userApproval, /^APPROVED/);
const sheetBytes = fs.readFileSync(path.join(releaseDir, draft.atlasFile));
assert.equal(sha(sheetBytes), draft.atlasSha256, 'sheet differs from the delivery');
const sheet = decodeRgbaPng(sheetBytes);
const [cellW, cellH] = draft.cellSize;
assert.equal(draft.frameCount, 16);
const cells = draft.frames.map(f => {
  const [sx, sy, w, h] = f.atlasRectTopLeft;
  assert.deepEqual([w, h], [cellW, cellH]);
  const pixels = Buffer.alloc(w * h * 4);
  for (let r = 0; r < h; r++) sheet.pixels.copy(pixels, r * w * 4, ((sy + r) * sheet.width + sx) * 4, ((sy + r) * sheet.width + sx + w) * 4);
  assert.equal(sha(pixels), f.cellRgbaSha256, `cell ${f.index} differs from the delivery`);
  return pixels;
});
const alpha = (cell, x, y) => cell[(y * cellW + x) * 4 + 3];

// Union of every pixel with any alpha across the 16 cells: one crop rectangle and one pivot for the whole clip.
let [left, top, right, bottom] = [cellW, cellH, 0, 0];
for (const cell of cells)
  for (let y = 0; y < cellH; y++) for (let x = 0; x < cellW; x++)
    if (alpha(cell, x, y) > 0) { left = Math.min(left, x); top = Math.min(top, y); right = Math.max(right, x + 1); bottom = Math.max(bottom, y + 1); }
const cropW = right - left, cropH = bottom - top;
const [pivotLeft, pivotTop] = draft.pivotTopLeft;
const pivot = { x: +((pivotLeft - left) / cropW).toFixed(6), y: +((bottom - pivotTop) / cropH).toFixed(6) };

// Standing frame for idle/work/muster/return: the narrowest boot spread in the 10 px above the foot baseline (the spear butt stays
// higher). Frames 4 and 12 tie at 23 px; the tie goes to the flatter sole, the most opaque pixels on the baseline row.
const band = 10, baseline = pivotTop;
const spread = cells.map((cell, index) => {
  let min = cellW, max = -1, contact = 0;
  for (let y = baseline - band; y <= baseline; y++) for (let x = 0; x < cellW; x++) if (alpha(cell, x, y) > 128) { min = Math.min(min, x); max = Math.max(max, x); }
  for (let x = 0; x < cellW; x++) if (alpha(cell, x, baseline) > 128) contact++;
  return { index, bootSpreadPx: max - min + 1, baselineContactPx: contact };
});
const standing = [...spread].sort((a, b) => a.bootSpreadPx - b.bootSpreadPx || b.baselineContactPx - a.baselineContactPx || a.index - b.index)[0];

// Display scale: 92% of the hero's pixel scale (user decision, #331). The hero walk shows a 240 px cell 1.081423 units tall.
const heroUnitsPerPixel = 1.081423 / 240, scale = 0.92;
const unitsPerPixel = heroUnitsPerPixel * scale;
const worldSize = { x: +(cropW * unitsPerPixel).toFixed(6), y: +(cropH * unitsPerPixel).toFixed(6) };

// Placement: the old militia and guard art (and alpha<=1 residue) in the bottom-left of crops-people is cleared; two rows of eight
// frames, 2 px apart, start 2 px below the poison-sickle crops' padding ring and end left of the returning person's ring.
const atlasPath = 'unity/Assets/Art/crops-people.png';
const atlasImage = decodeRgbaPng(fs.readFileSync(path.join(root, atlasPath)));
const atlasW = atlasImage.width, padding = 2;
const atlas = Buffer.from(atlasImage.pixels);
const cleared = [[0, 912, 690, 1254], [79, 951, 276, 1206], [394, 903, 583, 1218]];
for (const [x0, y0, x1, y1] of cleared) for (let y = y0; y < y1; y++) atlas.fill(0, (y * atlasW + x0) * 4, (y * atlasW + x1) * 4);
const origin = { x: padding, y: 912 }, gap = 2, columns = 8;
const frames = cells.map((cell, index) => {
  const x = origin.x + (index % columns) * (cropW + gap), y = origin.y + Math.floor(index / columns) * (cropH + gap);
  assert.ok(x + cropW + padding <= 690 && y + cropH + padding <= atlasImage.height, 'frame leaves the cleared band');
  for (let r = 0; r < cropH; r++) cell.copy(atlas, ((y + r) * atlasW + x) * 4, ((top + r) * cellW + left) * 4, ((top + r) * cellW + right) * 4);
  return { index, x, y, width: cropW, height: cropH, sourceCellRgbaSha256: draft.frames[index].cellRgbaSha256 };
});

function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(atlasW, atlasImage.height, atlas);
fs.writeFileSync(path.join(root, atlasPath), atlasBytes);

// militia and guard keep separate roles (same art for now, so a later tint can tell them apart).
const rect = f => ({ x: f.x, y: f.y, width: f.width, height: f.height });
const role = (id, list) => ({ id, atlas: 'crops-people', frames: list.map(rect), pivot, worldSize, frameMs: draft.frameMs, tween: 'none' });
const roles = [];
for (const kind of ['militia', 'guard'])
  for (const state of ['idle', 'move', 'work', 'muster', 'return'])
    roles.push(role(`person.${kind}.${state}`, state === 'move' ? frames : [frames[standing.index]]));
const licenseOrigin = 'Generated with built-in image_gen for this project; Soldier B walk (person.militia/guard) rigged in Unity and baked by Astra (Release soldier-b-rank-rig-r3-20261019), packed without resampling';
for (const file of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const m = read(path.join(root, file));
  const license = m.licenses.find(l => l.id === 'generated-crops-people');
  license.origin = licenseOrigin;
  for (const replacement of roles) {
    const at = m.roles.findIndex(r => r.id === replacement.id);
    assert.ok(at >= 0, `${file} lacks ${replacement.id}`);
    m.roles[at] = replacement;
  }
  fs.writeFileSync(path.join(root, file), JSON.stringify(m, null, 2) + '\n');
}
const record = {
  schemaVersion: 1, release, draftManifestSha256: sha(fs.readFileSync(path.join(releaseDir, draftFile))), sheetSha256: draft.atlasSha256,
  atlas: atlasPath, atlasSha256: sha(atlasBytes), cleared, crop: { left, top, width: cropW, height: cropH }, pivot, baselineY: pivotTop,
  unitsPerPixel: +unitsPerPixel.toFixed(9), heroScale: scale, worldSize, frameMs: draft.frameMs,
  // Root travel of one full walk cycle in source pixels; the View plays the walk by distance, one cycle per this travel.
  cyclePixels: read(path.join(releaseDir, 'candidate-02.json')).stride,
  standingFrame: standing.index, standingSelection: spread, frames
};
fs.writeFileSync(path.join(root, 'tools/art-authoring/soldier-b/packed-frames.json'), JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ crop: record.crop, pivot, worldSize, standing: standing.index, roles: roles.length }));
