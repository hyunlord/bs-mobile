// Offline authoring only. Packs Astra's ally marker B+ (Release ally-soldier-marker-bplus-20261022: a 1.5x helmet band and a blue
// plume drawn on Soldier B walk frame 0, as separate 320x240 layers) into crops-people.png as one small cell per walk frame (#352).
// The Soldier B frames stay as they are; the View draws the marker cell over the soldier's own frame.
// Each frame's helmet is found by an integer shift of frame 0's helmet (no resampling); the band keeps the frame's own alpha.
// Usage: node tools/art-authoring/ally-marker/pack.mjs <release dir>  (needs the band and plume PNGs and the manifest draft)
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

const release = 'ally-soldier-marker-bplus-20261022';
const delivery = {
  band: ['ally-marker-Bplus-band-cell0.png', '8230a8f708bbc1261aac54da7c051591c5affb1d014f0559f7e0291ba0df29c9'],
  plume: ['ally-marker-Bplus-plume-cell0.png', 'd3474373cd838d342de6d8bcb7819a7511c15d5f0dbd2dcb6188c6a2c2cc03f1'],
  draft: ['ally-marker-Bplus-manifest-draft.json', 'c221f88aee0c1d322e50c1e88f25da8795ece9dcb3d2be7f189bfc8a1a844461'],
};
const bytes = {};
for (const [key, [file, hash]] of Object.entries(delivery)) {
  bytes[key] = fs.readFileSync(path.join(releaseDir, file));
  assert.equal(sha(bytes[key]), hash, `${file} differs from the delivery`);
}
const draft = JSON.parse(bytes.draft);
assert.deepEqual(draft.cell, [320, 240]);
assert.equal(draft.sourceFrame, 0);
const band = decodeRgbaPng(bytes.band), plume = decodeRgbaPng(bytes.plume);

// Soldier B frames as packed by tools/art-authoring/soldier-b/pack.mjs (#331): 83x135 crops of the 320x240 cell.
const soldier = read(path.join(root, 'tools/art-authoring/soldier-b/packed-frames.json'));
const atlasPath = 'unity/Assets/Art/crops-people.png';
const atlasImage = decodeRgbaPng(fs.readFileSync(path.join(root, atlasPath)));
assert.equal(sha(fs.readFileSync(path.join(root, atlasPath))), soldier.atlasSha256, 'crops-people.png differs from the Soldier B pack');
const atlasW = atlasImage.width;
const { left, top, width: cropW, height: cropH } = soldier.crop;
const frames = soldier.frames.map(f => {
  const pixels = Buffer.alloc(cropW * cropH * 4);
  for (let r = 0; r < cropH; r++) atlasImage.pixels.copy(pixels, r * cropW * 4, ((f.y + r) * atlasW + f.x) * 4, ((f.y + r) * atlasW + f.x + cropW) * 4);
  return pixels;
});
const at = (pixels, w, x, y, c) => pixels[(y * w + x) * 4 + c];
// The release layers in crop coordinates.
const layer = image => (x, y, c) => at(image.pixels, image.width, x + left, y + top, c);
const bandAt = layer(band), plumeAt = layer(plume);

// Helmet patch of frame 0 (dome and brim): rows 2-17, columns 30-63 of the crop. Each frame's shift minimises the squared
// difference of premultiplied colour plus alpha over the patch, searched over +-5 px.
const patch = { x0: 30, x1: 64, y0: 2, y1: 18 }, reach = 5;
const difference = (frame, dx, dy) => {
  let sum = 0;
  for (let y = patch.y0; y < patch.y1; y++) for (let x = patch.x0; x < patch.x1; x++) {
    const sx = x + dx, sy = y + dy;
    const a0 = at(frames[0], cropW, x, y, 3), a1 = sx >= 0 && sy >= 0 && sx < cropW && sy < cropH ? at(frame, cropW, sx, sy, 3) : 0;
    for (let c = 0; c < 3; c++) {
      const v1 = a1 ? at(frame, cropW, sx, sy, c) * a1 / 255 : 0;
      sum += (at(frames[0], cropW, x, y, c) * a0 / 255 - v1) ** 2;
    }
    sum += (a0 - a1) ** 2;
  }
  return sum;
};
// Helmet outline after the shift: the dome's top row and its left and right edges 3 rows below it (alpha >= 128).
const dome = (frame, dx, dy) => {
  const opaque = (x, y) => x >= 0 && y >= 0 && x < cropW && y < cropH && at(frame, cropW, x, y, 3) >= 128;
  let topRow = -1;
  for (let y = patch.y0 - reach + dy; y < patch.y1 + dy && topRow < 0; y++) for (let x = patch.x0 + dx; x < patch.x1 + dx; x++) if (opaque(x, y)) { topRow = y; break; }
  let l = -1, r = -1;
  for (let x = patch.x0 + dx; x < patch.x1 + dx; x++) if (opaque(x, topRow + 3)) { if (l < 0) l = x; r = x; }
  return { top: topRow - dy, left: l - dx, right: r - dx };
};
const reference = dome(frames[0], 0, 0);
const tracks = frames.map((frame, index) => {
  let best = null;
  for (let dy = -reach; dy <= reach; dy++) for (let dx = -reach; dx <= reach; dx++) {
    const score = difference(frame, dx, dy);
    if (!best || score < best.score) best = { dx, dy, score };
  }
  const d = dome(frame, best.dx, best.dy);
  const residual = { top: d.top - reference.top, left: d.left - reference.left, right: d.right - reference.right };
  assert.ok(Object.values(residual).every(v => Math.abs(v) <= 1), `frame ${index}: helmet outline moves more than 1 px after the shift`);
  return { index, dx: best.dx, dy: best.dy, outlineResidualPx: residual };
});

// Marker layer per frame: the band takes the frame's own alpha (it only recolours the helmet), the plume goes over it.
const markers = tracks.map(({ dx, dy }, index) => {
  const out = Buffer.alloc(cropW * cropH * 4);
  for (let y = 0; y < cropH; y++) for (let x = 0; x < cropW; x++) {
    const sx = x - dx, sy = y - dy;
    if (sx < 0 || sy < 0 || sx >= cropW || sy >= cropH) continue;
    const frameAlpha = at(frames[index], cropW, x, y, 3) / 255;
    const ba = Math.round(bandAt(sx, sy, 3) * frameAlpha) / 255, pa = plumeAt(sx, sy, 3) / 255;
    const a = pa + ba * (1 - pa);
    if (a <= 0) continue;
    const o = (y * cropW + x) * 4;
    for (let c = 0; c < 3; c++) out[o + c] = Math.round((plumeAt(sx, sy, c) * pa + bandAt(sx, sy, c) * ba * (1 - pa)) / a);
    out[o + 3] = Math.round(a * 255);
  }
  return out;
});
// One window for every frame: the union of the marker pixels.
let [wx0, wy0, wx1, wy1] = [cropW, cropH, 0, 0];
for (const m of markers) for (let y = 0; y < cropH; y++) for (let x = 0; x < cropW; x++)
  if (m[(y * cropW + x) * 4 + 3] > 0) { wx0 = Math.min(wx0, x); wy0 = Math.min(wy0, y); wx1 = Math.max(wx1, x + 1); wy1 = Math.max(wy1, y + 1); }
const cellW = wx1 - wx0, cellH = wy1 - wy0;

// Placement: one row of 16 cells, 2 px apart, in the free strip at the top of crops-people (x 50, y 2); the strip and its 2 px
// padding ring must be empty and outside every role.
const padding = 2, origin = { x: 50, y: 2 }, gap = 2;
const atlas = Buffer.from(atlasImage.pixels);
const stripRight = origin.x + 16 * (cellW + gap) - gap;
const rolesInAtlas = ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']
  .flatMap(file => read(path.join(root, file)).roles.filter(r => r.atlas === 'crops-people' && r.id !== 'person.ally-marker.move'));
for (const role of rolesInAtlas) for (const f of role.frames)
  assert.ok(f.x - padding >= stripRight + padding || f.x + f.width + padding <= origin.x - padding || f.y - padding >= origin.y + cellH + padding || f.y + f.height + padding <= origin.y - padding,
    `marker strip overlaps ${role.id}`);
for (let y = origin.y - padding; y < origin.y + cellH + padding; y++) for (let x = origin.x - padding; x < stripRight + padding; x++)
  assert.ok(y < 0 || atlas[(y * atlasW + x) * 4 + 3] === 0, `marker strip is not empty at ${x},${y}`);
const cells = markers.map((marker, index) => {
  const x = origin.x + index * (cellW + gap), y = origin.y;
  for (let r = 0; r < cellH; r++) marker.copy(atlas, ((y + r) * atlasW + x) * 4, ((wy0 + r) * cropW + wx0) * 4, ((wy0 + r) * cropW + wx1) * 4);
  return { index, x, y, width: cellW, height: cellH };
});

function crc(data) { let c = 0xffffffff; for (const v of data) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(data.length); const body = Buffer.concat([Buffer.from(type), data]); sum.writeUInt32BE(crc(body)); return Buffer.concat([size, body, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(atlasW, atlasImage.height, atlas);
fs.writeFileSync(path.join(root, atlasPath), atlasBytes);

// The marker cell's bottom-left corner relative to the soldier's foot pivot, in source pixels (x right, y up). The View places
// the cell there in the soldier's own pose, scale and facing (GameVisualTokens.AllyMarkerOffsetPixels).
const pivotPx = { x: soldier.pivot.x * cropW, y: soldier.pivot.y * cropH };
const offsetPixels = { x: +(wx0 - pivotPx.x).toFixed(6), y: +((cropH - wy1) - pivotPx.y).toFixed(6) };
const worldSize = { x: +(cellW * soldier.unitsPerPixel).toFixed(6), y: +(cellH * soldier.unitsPerPixel).toFixed(6) };
const role = { id: 'person.ally-marker.move', atlas: 'crops-people', frames: cells.map(({ x, y, width, height }) => ({ x, y, width, height })),
  pivot: { x: 0, y: 0 }, worldSize, frameMs: soldier.frameMs, tween: 'none' };
const licenseOrigin = 'Generated with built-in image_gen for this project; Soldier B walk (person.militia/guard) rigged in Unity and baked by Astra (Release soldier-b-rank-rig-r3-20261019), packed without resampling; ally marker B+ layer by Astra (Release ally-soldier-marker-bplus-20261022), shifted per walk frame without resampling';
for (const file of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const m = read(path.join(root, file));
  m.licenses.find(l => l.id === 'generated-crops-people').origin = licenseOrigin;
  const existing = m.roles.findIndex(r => r.id === role.id);
  if (existing >= 0) m.roles[existing] = role; else m.roles.splice(m.roles.findIndex(r => r.id === 'person.militia.idle'), 0, role);
  fs.writeFileSync(path.join(root, file), JSON.stringify(m, null, 2) + '\n');
}
const record = {
  schemaVersion: 1, release, deliverySha256: Object.fromEntries(Object.entries(delivery).map(([key, [file, hash]]) => [file, hash])),
  soldierAtlasSha256: soldier.atlasSha256, atlas: atlasPath, atlasSha256: sha(atlasBytes),
  helmetPatch: patch, searchReachPx: reach, tracks,
  window: { left: wx0, top: wy0, width: cellW, height: cellH }, offsetPixels, unitsPerPixel: soldier.unitsPerPixel, worldSize,
  standingFrame: soldier.standingFrame, cells
};
fs.writeFileSync(path.join(root, 'tools/art-authoring/ally-marker/packed-frames.json'), JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ window: record.window, offsetPixels, worldSize, shifts: tracks.map(t => [t.dx, t.dy]) }));
