// Offline authoring only. Packs Astra's side-view knight action sheets (idle, five attacks, five moving keys, hit; Release
// hero-side-actions-game-r3-20261020, #336) and the current approved walk into hero-knight.png without resampling. The walk cells
// are copied from the current atlas (the cells packed-frames.json records). A walk-to-idle transition clip is packed only when
// the release delivers it (Astra r4); without it the role is absent and the game skips the transition. Gather B (from walk
// frame 7, user decision 2026-10-20) is packed the same way when delivered. Identical idle frames share one cell (the idle's
// six frames are three pictures), so B fits in the 8x9 atlas; the idle's frame order and times are unchanged.
// Usage: node tools/art-authoring/knight-sheets/pack-side.mjs <release tag> <release dir> [<addendum tag> <addendum dir>]
// An addendum release (gather B r2: one extra role and its manifest addendum) adds its role to the base release's roles.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [release, releaseDir, addendumRelease, addendumDir] = process.argv.slice(2);
assert.ok(release && releaseDir, 'usage: pack-side.mjs <release tag> <release dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const recordPath = path.join(root, 'tools/art-authoring/knight-sheets/packed-frames.json');
const previous = read(recordPath);
const draftPath = path.join(releaseDir, 'actions-manifest-draft.json');
const draft = read(draftPath);
const [cellW, cellH] = draft.cellSize;
assert.deepEqual([cellW, cellH], previous.cell);
assert.ok(Math.abs(draft.pivotBottomLeft[1] - previous.pivotUnityNormalized[1]) < 1e-6 && draft.pivotBottomLeft[0] === previous.pivotUnityNormalized[0], 'pivot');
// User decision 2026-10-20: the standing cast fires on its raised-sword frame 2 (r3 recorded 3, the lowered sword; r4 records 2).
const effectOverride = { 'action.cast': 2 };
const ticks = ms => Math.round(ms * 60 / 1000), ms = t => Math.round(t * 1000 / 60);

// Clips in atlas order: the walk first (its cells come from the current atlas), then the delivered roles.
const order = ['idle', 'action.slash', 'action.aim', 'action.cast', 'action.blow', 'action.throw', 'hit',
  'key.slash', 'key.aim', 'key.cast', 'key.blow', 'key.throw', 'transition.walk-to-idle', 'transition.walk-to-idle-b'];
const optional = new Set(['transition.walk-to-idle', 'transition.walk-to-idle-b']);
const shareIdentical = new Set(['idle']);
const prefix = 'hero.core.frontier_knight.';
const delivered = new Map(draft.roles.map(r => [r.role.slice(prefix.length), { ...r, dir: releaseDir }]));
let addendum = null;
if (addendumRelease) {
  const file = fs.readdirSync(addendumDir).find(f => f.endsWith('manifest-addendum.json'));
  assert.ok(file, 'addendum manifest missing');
  addendum = read(path.join(addendumDir, file));
  assert.equal(addendum.baseRelease, release, 'addendum is for another base release');
  assert.deepEqual(addendum.cellSize, draft.cellSize); assert.deepEqual(addendum.pivotBottomLeft, draft.pivotBottomLeft);
  const suffix = addendum.role.role.slice(prefix.length);
  assert.ok(optional.has(suffix) && !delivered.has(suffix), `unexpected addendum role ${suffix}`);
  // The user approved gather B r2 on 2026-10-20 (Astra's walk-7 -> B0 moving-foot gate was judged the wrong criterion).
  delivered.set(suffix, { ...addendum.role, dir: addendumDir, release: addendumRelease, approval: 'approved by the user 2026-10-20', manifestSha256: sha(fs.readFileSync(path.join(addendumDir, file))) });
}
for (const suffix of order) if (!optional.has(suffix)) assert.ok(delivered.has(suffix), `release lacks ${suffix}`);

const columns = 8, rows = 9, padding = 2;
const atlasW = padding * 2 + columns * cellW, atlasH = padding * 2 + rows * cellH;
assert.ok(atlasW <= 4096 && atlasH <= 4096);
const atlas = Buffer.alloc(atlasW * atlasH * 4);
let next = 0;
const place = cell => {
  assert.ok(next < columns * rows, 'atlas full');
  const x = padding + (next % columns) * cellW, y = padding + Math.floor(next / columns) * cellH; next++;
  for (let r = 0; r < cellH; r++) cell.copy(atlas, ((y + r) * atlasW + x) * 4, r * cellW * 4, (r + 1) * cellW * 4);
  return { x, y, width: cellW, height: cellH, decodedRgbaSha256: sha(cell) };
};

const current = decodeRgbaPng(fs.readFileSync(path.join(root, previous.atlas)));
const walkClip = previous.clips.find(c => c.role === 'walk');
const walk = walkClip.frames.map(f => {
  const cell = Buffer.alloc(cellW * cellH * 4);
  for (let r = 0; r < cellH; r++) current.pixels.copy(cell, r * cellW * 4, ((f.y + r) * current.width + f.x) * 4, ((f.y + r) * current.width + f.x + cellW) * 4);
  assert.equal(sha(cell), f.decodedRgbaSha256, 'current walk cell differs from its record');
  return { ...place(cell), frameTicks60: f.frameTicks60, ...(f.sourceFrame === undefined ? {} : { sourceFrame: f.sourceFrame }) };
});
const clips = [{ ...walkClip, frames: walk }];
const sheets = {};
for (const suffix of order) {
  const role = delivered.get(suffix);
  if (!role) continue;
  const bytes = fs.readFileSync(path.join(role.dir, role.file));
  const sheet = decodeRgbaPng(bytes);
  assert.equal(sheet.height, cellH); assert.equal(sheet.width, cellW * role.frames, role.role);
  const packed = new Map();
  const frames = role.cellPixelSha256.map((hash, i) => {
    const cell = Buffer.alloc(cellW * cellH * 4);
    for (let r = 0; r < cellH; r++) sheet.pixels.copy(cell, r * cellW * 4, (r * sheet.width + i * cellW) * 4, (r * sheet.width + (i + 1) * cellW) * 4);
    assert.equal(sha(cell), hash, `${role.role}[${i}] differs from the delivery`);
    if (shareIdentical.has(suffix) && packed.has(hash)) return { ...packed.get(hash).at, frameTicks60: ticks(role.frameMs), sharesCellOf: packed.get(hash).frame };
    const at = place(cell);
    if (shareIdentical.has(suffix)) packed.set(hash, { at, frame: i });
    return { ...at, frameTicks60: ticks(role.frameMs) };
  });
  const effectFrame = effectOverride[suffix] ?? role.effectFrame;
  clips.push({ clip: path.basename(role.file, '.png'), role: suffix, status: role.approval ?? draft.status, sheet: role.file, sheetSha256: sha(bytes),
    deliveredFrameMs: role.frameMs, deliveredEffectFrame: role.effectFrame, effectFrame, ...(role.release ? { release: role.release } : {}), frames });
  sheets[suffix] = true;
}
// Reserve each missing transition's two cells, so delivering it later moves no other cell.
for (const suffix of optional) if (!sheets[suffix]) next += 2;

function crc(bytes) { let c = 0xffffffff; for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, bytes) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(bytes.length); const data = Buffer.concat([Buffer.from(type), bytes]); sum.writeUInt32BE(crc(data)); return Buffer.concat([size, data, sum]); }
function png(width, height, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rowsBuffer = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) pixels.copy(rowsBuffer, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rowsBuffer, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasBytes = png(atlasW, atlasH, atlas);
fs.writeFileSync(path.join(root, previous.atlas), atlasBytes);

const pivot = { x: previous.pivotUnityNormalized[0], y: previous.pivotUnityNormalized[1] };
const rect = f => ({ x: f.x, y: f.y, width: f.width, height: f.height });
const roles = clips.map(c => ({ id: prefix + c.role, atlas: 'hero-knight', frames: c.frames.map(rect), pivot, worldSize: previous.worldSize,
  frameMs: c.role === 'walk' ? walkClip.frameMs : ms(c.frames[0].frameTicks60), tween: 'none' }));
const hit = clips.find(c => c.role === 'hit');
roles.push({ id: prefix + 'death', atlas: 'hero-knight', frames: [rect(hit.frames[hit.frames.length - 1])], pivot, worldSize: previous.worldSize,
  frameMs: ms(hit.frames[0].frameTicks60), tween: 'none' });
const license = {
  id: 'generated-knight-sheets', origin: 'Generated with built-in image_gen for this project; side-view knight rigged in Unity SpriteSkin and baked by Astra (walk r8.5, actions ' + release + (addendumRelease ? ', gather B ' + addendumRelease : '') + '); packed without resampling',
  source: 'https://openai.com/policies/terms-of-use/', prompt: 'docs/licenses/prompts/knight-sheets-r4-prompt.txt',
  commercialUse: 'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.'
};
for (const file of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const m = read(path.join(root, file));
  const at = m.roles.findIndex(r => r.id.startsWith(prefix));
  m.roles = m.roles.filter(r => !r.id.startsWith(prefix));
  m.roles.splice(at, 0, ...roles);
  m.licenses = m.licenses.map(l => l.id === license.id ? license : l);
  const spec = m.atlases.find(a => a.id === 'hero-knight');
  assert.deepEqual([spec.width, spec.height, spec.padding], [atlasW, atlasH, padding], 'atlas size stays');
  fs.writeFileSync(path.join(root, file), JSON.stringify(m, null, 2) + '\n');
}
const record = { ...previous, releases: { walk: previous.releases.walk, actions: release, ...(addendumRelease ? { gatherB: addendumRelease } : {}) }, actionsManifestSha256: sha(fs.readFileSync(draftPath)),
  atlasSha256: sha(atlasBytes), width: atlasW, height: atlasH, padding, effectOverride, clips };
delete record.approvedManifestSha256; delete record.additionsManifestSha256; delete record.movingKeyHoldTicks60;   // moving keys now play their own frames and frameMs
fs.writeFileSync(recordPath, JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ cells: next, of: columns * rows, roles: roles.length, transition: !!sheets['transition.walk-to-idle'], atlasSha256: record.atlasSha256 }));
