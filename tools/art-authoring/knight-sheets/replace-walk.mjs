// Offline authoring only. Replaces the 16 walk cells of hero-knight.png with a newer approved 16-frame walk bake, in the cells
// packed-frames.json already records for the walk (#317 layout), without resampling. Every pixel outside those cells, both art
// manifests and the walk's frameMs stay unchanged; the check fails otherwise.
// Usage: node tools/art-authoring/knight-sheets/replace-walk.mjs <release tag> <status> <sheet.png> <walk manifest draft.json>
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [release, status, sheetFile, draftFile] = process.argv.slice(2);
assert.ok(release && status && sheetFile && draftFile, 'usage: replace-walk.mjs <release tag> <status> <sheet.png> <walk manifest draft.json>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const recordPath = path.join(root, 'tools/art-authoring/knight-sheets/packed-frames.json');
const record = read(recordPath);
const clip = record.clips.find(c => c.role === 'walk');
const [cellW, cellH] = record.cell;

const draft = read(draftFile).walk;
const sheetBytes = fs.readFileSync(sheetFile);
assert.equal(sha(sheetBytes), draft.atlasSha256, 'sheet differs from the delivery');
assert.equal(draft.frames, clip.frames.length, 'frame count');
assert.deepEqual(draft.cell, record.cell, 'cell size');
assert.ok(Math.abs(draft.pivot[1] - record.pivotUnityNormalized[1]) < 1e-6 && draft.pivot[0] === record.pivotUnityNormalized[0], 'pivot');
const sheet = decodeRgbaPng(sheetBytes);

const atlasPath = path.join(root, record.atlas);
const before = decodeRgbaPng(fs.readFileSync(atlasPath));
assert.deepEqual([before.width, before.height], [record.width, record.height]);
const atlas = Buffer.from(before.pixels);
const inWalkCell = new Uint8Array(record.width * record.height);
const frames = clip.frames.map((f, i) => {
  const [sx, sy, w, h] = draft.frameRectsTopLeft[i];
  assert.deepEqual([w, h], [cellW, cellH]);
  const cell = Buffer.alloc(cellW * cellH * 4);
  for (let r = 0; r < cellH; r++) {
    sheet.pixels.copy(cell, r * cellW * 4, ((sy + r) * sheet.width + sx) * 4, ((sy + r) * sheet.width + sx + cellW) * 4);
    cell.copy(atlas, ((f.y + r) * record.width + f.x) * 4, r * cellW * 4, (r + 1) * cellW * 4);
    inWalkCell.fill(1, (f.y + r) * record.width + f.x, (f.y + r) * record.width + f.x + cellW);
  }
  return { ...f, decodedRgbaSha256: sha(cell) };
});
let outside = 0;
for (let p = 0; p < inWalkCell.length; p++) if (!inWalkCell[p] && atlas.readUInt32BE(p * 4) !== before.pixels.readUInt32BE(p * 4)) outside++;
assert.equal(outside, 0, 'pixels outside the walk cells changed');

// The manifests already point the walk role at these cells with the chosen frameMs; nothing there changes.
for (const file of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const role = read(path.join(root, file)).roles.find(r => r.id === 'hero.core.frontier_knight.walk');
  assert.deepEqual(role.frames, clip.frames.map(f => ({ x: f.x, y: f.y, width: f.width, height: f.height })), file);
  assert.equal(role.frameMs, clip.frameMs, file);
}

function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(record.width, record.height, atlas);
fs.writeFileSync(atlasPath, atlasBytes);

const sheetName = path.basename(sheetFile);
Object.assign(clip, { clip: sheetName.replace(/\.png$/, ''), status, sheet: sheetName, sheetSha256: draft.atlasSha256, frames });
record.releases.walk = release;
record.walkSheetSha256 = draft.atlasSha256;
record.atlasSha256 = sha(atlasBytes);
fs.writeFileSync(recordPath, JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ release, atlasSha256: record.atlasSha256, frames: frames.length, outsideChanged: outside }));
