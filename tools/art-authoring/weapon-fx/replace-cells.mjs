// Offline authoring only. Puts Astra's approved weapon game sprites (Release weapon-game-assets-20261019) into the existing
// wave1a-weapon-fx atlas without resampling (#266 weapon round after the six rejected videos). Each delivered role cell is copied
// into the cell the manifest already gives that role; the iron blade slash, whose canvas grew from 503x76 to 512x128, moves to a
// free slot and its old cell is cleared. fx.granary_sling.aroma (a proposed new role with no consumer yet) is not added.
// Every other atlas pixel and every other role must stay unchanged; the script fails otherwise.
// Usage: node tools/art-authoring/weapon-fx/replace-cells.mjs <release dir>
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [releaseDir] = process.argv.slice(2).map(p => path.resolve(p));
assert.ok(releaseDir, 'usage: replace-cells.mjs <release dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const release = 'weapon-game-assets-20261019';
const draftFile = path.join(releaseDir, 'weapon-fx-manifest-draft.json');
const draft = read(draftFile);
const manifestFile = path.join(root, 'unity/Assets/Art/wave1a-manifest.json');
const manifest = read(manifestFile);
const atlasSpec = manifest.atlases.find(a => a.id === 'wave1a-weapon-fx');
const atlasPath = path.join(root, 'unity', atlasSpec.path);
const before = decodeRgbaPng(fs.readFileSync(atlasPath));
assert.deepEqual([before.width, before.height], [atlasSpec.width, atlasSpec.height]);
const W = before.width, atlas = Buffer.from(before.pixels), touched = new Uint8Array(W * before.height);
const fill = (x, y, w, h, source) => {
  for (let r = 0; r < h; r++) {
    const at = ((y + r) * W + x) * 4;
    if (source) source.copy(atlas, at, r * w * 4, (r + 1) * w * 4); else atlas.fill(0, at, at + w * 4);
    touched.fill(1, (y + r) * W + x, (y + r) * W + x + w);
  }
};
// The slash's new slot: free in the current atlas with the 4 px padding ring around it (found by scanning occupancy and alpha).
const slashSlot = { x: 1412, y: 1284 };
const sheets = new Map(), replaced = [];
for (const role of draft.roles) {
  if (role.newRole) continue;
  const current = manifest.roles.find(r => r.id === role.id);
  assert.ok(current && current.atlas === 'wave1a-weapon-fx' && current.frames.length === 1, role.id);
  if (!sheets.has(role.atlas)) {
    const bytes = fs.readFileSync(path.join(releaseDir, role.atlas));
    sheets.set(role.atlas, { image: decodeRgbaPng(bytes), sha256: sha(bytes) });
  }
  const { image, sha256 } = sheets.get(role.atlas);
  const [sx, sy, w, h] = role.rect;
  assert.deepEqual([w, h], role.size, role.id);
  const cell = Buffer.alloc(w * h * 4);
  for (let r = 0; r < h; r++) image.pixels.copy(cell, r * w * 4, ((sy + r) * image.width + sx) * 4, ((sy + r) * image.width + sx + w) * 4);
  const old = current.frames[0];
  let frame = old;
  if (w !== old.width || h !== old.height) {
    assert.equal(role.id, 'fx.iron_blade.slash', 'only the slash changes size');
    fill(old.x, old.y, old.width, old.height, null);
    frame = { x: slashSlot.x, y: slashSlot.y, width: w, height: h };
    assert.ok(frame.x + w + atlasSpec.padding <= W && frame.y + h + atlasSpec.padding <= before.height);
    for (const other of manifest.roles) if (other.atlas === 'wave1a-weapon-fx' && other.id !== role.id)
      for (const f of other.frames) assert.ok(frame.x >= f.x + f.width + 4 || f.x >= frame.x + w + 4 || frame.y >= f.y + f.height + 4 || f.y >= frame.y + h + 4, 'slot overlaps ' + other.id);
  }
  fill(frame.x, frame.y, w, h, cell);
  current.frames = [frame];
  current.pivot = { x: role.pivot[0], y: role.pivot[1] };
  current.worldSize = { x: +role.worldSize[0].toFixed(6), y: +role.worldSize[1].toFixed(6) };
  replaced.push({ id: role.id, atlas: role.atlas, sourceRect: role.rect, sheetSha256: sha256, cellRgbaSha256: sha(cell), frame });
}
let outside = 0;
for (let p = 0; p < touched.length; p++) if (!touched[p] && atlas.readUInt32BE(p * 4) !== before.pixels.readUInt32BE(p * 4)) outside++;
assert.equal(outside, 0, 'pixels outside the replaced cells changed');

function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(W, before.height, atlas);
fs.writeFileSync(atlasPath, atlasBytes);
const license = manifest.licenses.find(l => l.id === atlasSpec.license);
license.origin = 'Generated with built-in image_gen by the Astra art pipeline for issue #266 weapon effect pictures (first set: background removed with BiRefNet, MIT; game sprites for the iron blade, canal bow, ward orbit, spore patch, ditch pike and granary sling: Release weapon-game-assets-20261019, packed without resampling)';
fs.writeFileSync(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
fs.writeFileSync(path.join(root, 'tools/art-authoring/weapon-fx/replaced-cells.json'), JSON.stringify({
  schemaVersion: 1, release, draftManifestSha256: sha(fs.readFileSync(draftFile)), atlas: path.relative(root, atlasPath), atlasSha256: sha(atlasBytes),
  skipped: draft.roles.filter(r => r.newRole).map(r => r.id), replaced }, null, 2) + '\n');
console.log(JSON.stringify({ replaced: replaced.length, atlasSha256: sha(atlasBytes), outsideChanged: outside }));
