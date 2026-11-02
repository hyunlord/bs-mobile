// Offline authoring only. Packs three SLG hero sheets (field_guide, river_warden, ruin_seeker)
// from Releases hero-walk-world-status-20261030-r1 and hero-basic-gem-20261030-r1 into one
// atlas PNG per hero and registers hero.slg.* roles in both
// first-playable and wave1a manifests, mirroring the frontier_knight pattern.
//
// Usage: node tools/art-authoring/slg-hero-sheets/pack.mjs <release-dir>
// Where <release-dir> contains both releases' files downloaded with gh release download.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [releaseDir] = process.argv.slice(2).map(p => path.resolve(p));
assert.ok(releaseDir, 'usage: pack.mjs <release-dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));

// Cell geometry (confirmed by user): 320x240 cells at 1x, stride 320, 2px outer margin.
const cellW = 320, cellH = 240, padding = 2;

// Pivot: foot line y=184 from top of cell (same as walk) → Unity y = (240-184)/240
const pivotX = 0.5, pivotY = 56 / 240; // 56/240 = 0.23333...

// WorldSize: match knight (same cell dimensions; draft heroes — will refine with art review).
const worldSize = { x: 1.441897, y: 1.081423 };

// FrameMs: match knight timing for identical animation states.
const frameMsMap = { walk: 36, idle: 200, hit: 83, death: 83, attack: 83 };

// Three SLG heroes. Walk release uses river-warden (hyphen); basic-gem uses river_warden (underscore).
const heroes = [
  {
    contentId: 'slg:field_guide',
    name: 'field_guide',
    walkFile: 'field_guide-walk16-r1-1x.png',
    idleFile: 'field_guide-idle6-r1-1x.png',
    hitFile: 'field_guide-hit3-r1-1x.png',
    deathFile: 'field_guide-death1-r1-1x.png',
    attackFile: 'field_guide-staff-attack8-r1-1x.png',
    attackSha: '43d64f5ade4f07ab6a000d37321b612bd978237961fb8f6a1b7862140a58c838',
  },
  {
    contentId: 'slg:river_warden',
    name: 'river_warden',
    walkFile: 'river-warden-walk16-r1-1x.png',     // note: hyphen in walk release
    idleFile: 'river_warden-idle6-r1-1x.png',
    hitFile: 'river_warden-hit3-r1-1x.png',
    deathFile: 'river_warden-death1-r1-1x.png',
    attackFile: 'river-warden-thrust8-r1-1x.png',
    attackSha: '8201e0b91ba302b4b1a8d0765f71ffa224964c5a752c62d856662f562bf778ba',
  },
  {
    contentId: 'slg:ruin_seeker',
    name: 'ruin_seeker',
    walkFile: 'ruin_seeker-walk16-r1-1x.png',
    idleFile: 'ruin_seeker-idle6-r1-1x.png',
    hitFile: 'ruin_seeker-hit3-r1-1x.png',
    deathFile: 'ruin_seeker-death1-r1-1x.png',
  },
];

// Four base rows (walk×2, idle, hit), plus an attack row when supplied.
// Width is 2564; base height 964 grows to 1204 for the two attack heroes.
const atlasW = padding * 2 + 8 * cellW;   // 2564
const baseAtlasH = padding * 2 + 4 * cellH;
assert.equal(atlasW, 2564);
assert.equal(baseAtlasH, 964);

// Destination Y offsets per animation row in atlas.
const rowY = { walk0: padding, walk1: padding + cellH, idle: padding + 2 * cellH, hit: padding + 3 * cellH };

function crc(bytes) {
  let c = 0xffffffff;
  for (const v of bytes) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); }
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type, bytes) {
  const size = Buffer.alloc(4), sum = Buffer.alloc(4);
  size.writeUInt32BE(bytes.length);
  const data = Buffer.concat([Buffer.from(type), bytes]);
  sum.writeUInt32BE(crc(data));
  return Buffer.concat([size, data, sum]);
}
function encodePng(width, height, pixels) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width); header.writeUInt32BE(height, 4);
  header[8] = 8; header[9] = 6; // bit depth 8, RGBA
  const rows = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++)
    pixels.copy(rows, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

// Copy exactly `count` scanlines from src starting at srcRow, writing to atlas starting at destY.
// src may be narrower than atlasW; the rest of each atlas row stays transparent (zero).
function copyCellRows(atlas, src, srcRow, count, destY) {
  for (let i = 0; i < count; i++) {
    const srcOff = (srcRow + i) * src.width * 4;
    const dstOff = (destY + i) * atlasW * 4;
    src.pixels.copy(atlas, dstOff, srcOff, srcOff + src.width * 4);
  }
}

// Build frame rect list for an animation: n frames starting at destY in atlas.
function frames(n, destY) {
  return Array.from({ length: n }, (_, c) => ({
    x: padding + c * cellW, y: destY, width: cellW, height: cellH,
  }));
}

const packedHeroes = [];
const allRoles = [];

for (const hero of heroes) {
  const load = f => {
    const b = fs.readFileSync(path.join(releaseDir, f));
    return { bytes: b, sha256: sha(b), img: decodeRgbaPng(b) };
  };
  const walkSrc = load(hero.walkFile);
  const idleSrc = load(hero.idleFile);
  const hitSrc  = load(hero.hitFile);
  const deathSrc = load(hero.deathFile);
  const attackSrc = hero.attackFile ? load(hero.attackFile) : null;
  const atlasH = baseAtlasH + (attackSrc ? cellH : 0);
  if (attackSrc) {
    assert.equal(attackSrc.sha256, hero.attackSha, `${hero.name} approved attack hash`);
    assert.deepEqual([attackSrc.img.width, attackSrc.img.height], [2564, 244]);
  }

  // Validate source dimensions against specification.
  assert.deepEqual([walkSrc.img.width, walkSrc.img.height], [2564, 484], `${hero.name} walk dims`);
  assert.deepEqual([idleSrc.img.width, idleSrc.img.height], [1924, 244], `${hero.name} idle dims`);
  assert.deepEqual([hitSrc.img.width, hitSrc.img.height],   [964, 244],  `${hero.name} hit dims`);
  assert.deepEqual([deathSrc.img.width, deathSrc.img.height], [324, 244], `${hero.name} death dims`);

  const atlas = Buffer.alloc(atlasW * atlasH * 4); // zero-initialized (transparent)

  // Each source has 2px outer margin; cells live at src y=[padding..padding+cellH).
  // Walk source: 2 rows, so cells at src y=2..241 (row 0) and y=242..481 (row 1).
  // Copy only cell rows, discarding source margins, to the correct atlas row positions.

  // Walk row 0: src y=2..241 → atlas y=2..241
  copyCellRows(atlas, walkSrc.img, padding, cellH, rowY.walk0);
  // Walk row 1: src y=242..481 → atlas y=242..481
  copyCellRows(atlas, walkSrc.img, padding + cellH, cellH, rowY.walk1);

  // Idle row 0: src y=2..241 → atlas y=482..721
  copyCellRows(atlas, idleSrc.img, padding, cellH, rowY.idle);

  // Hit row 0: src y=2..241 → atlas y=722..961
  copyCellRows(atlas, hitSrc.img, padding, cellH, rowY.hit);

  // Death: "pixel-identical to the last hit cell" — no separate copy needed;
  // the death role in the manifest points to the same rect as hit frame 2.

  if (attackSrc) copyCellRows(atlas, attackSrc.img, padding, cellH, padding + 4 * cellH);

  // Verify the separate delivered death cell before sharing the last hit frame.
  for (let y = 0; y < cellH; y++) {
    const deathStart = ((padding + y) * deathSrc.img.width + padding) * 4;
    const hitStart = ((padding + y) * hitSrc.img.width + padding + 2 * cellW) * 4;
    assert.ok(deathSrc.img.pixels.subarray(deathStart, deathStart + cellW * 4).equals(hitSrc.img.pixels.subarray(hitStart, hitStart + cellW * 4)), `${hero.name} death source differs from last hit cell`);
  }
  const atlasBytes = encodePng(atlasW, atlasH, atlas);
  const atlasName = `hero-${hero.name}.png`;
  const atlasRelPath = `Assets/Art/${atlasName}`;
  const atlasAbsPath = path.join(root, 'unity', atlasRelPath);
  fs.writeFileSync(atlasAbsPath, atlasBytes);
  console.log(`Written: ${atlasRelPath}  (${atlasW}x${atlasH}, ${atlasBytes.length} bytes, sha256=${sha(atlasBytes).slice(0, 16)}...)`);

  const pivot = { x: pivotX, y: pivotY };
  const atlasId = `hero-${hero.name}`;
  const rolePrefix = 'hero.' + hero.contentId.replace(':', '.');

  // Walk: 16 frames across 2 atlas rows.
  const walkFrames = [...frames(8, rowY.walk0), ...frames(8, rowY.walk1)];
  // Idle: 6 frames at atlas row 2.
  const idleFrames = frames(6, rowY.idle);
  // Hit: 3 frames at atlas row 3.
  const hitFrames = frames(3, rowY.hit);
  // Death: last hit frame (frame index 2).
  const deathFrame = hitFrames[hitFrames.length - 1];

  const heroRoles = [
    { id: `${rolePrefix}.walk`,  atlas: atlasId, frames: walkFrames,   pivot, worldSize, frameMs: frameMsMap.walk,  tween: 'none' },
    { id: `${rolePrefix}.idle`,  atlas: atlasId, frames: idleFrames,   pivot, worldSize, frameMs: frameMsMap.idle,  tween: 'none' },
    { id: `${rolePrefix}.hit`,   atlas: atlasId, frames: hitFrames,    pivot, worldSize, frameMs: frameMsMap.hit,   tween: 'none' },
    { id: `${rolePrefix}.death`, atlas: atlasId, frames: [deathFrame], pivot, worldSize, frameMs: frameMsMap.death, tween: 'none' },
  ];
  if (attackSrc) heroRoles.push({ id: `${rolePrefix}.attack`, atlas: atlasId, frames: frames(8, padding + 4 * cellH), pivot, worldSize, frameMs: frameMsMap.attack, tween: 'none' });
  allRoles.push(...heroRoles);

  const states = ['idle', 'walk', 'hit', 'death', ...(attackSrc ? ['attack'] : [])];
  const heroBindings = states.map(state => ({
    kind: 'hero', contentId: hero.contentId, state, roleId: `${rolePrefix}.${state}`,
  }));

  packedHeroes.push({
    contentId: hero.contentId, name: hero.name, atlasId, atlasRelPath,
    atlasSha256: sha(atlasBytes), atlasWidth: atlasW, atlasHeight: atlasH,
    sources: {
      ...(attackSrc ? { attack: { file: hero.attackFile, sha256: attackSrc.sha256 } } : {}),
      walk: { file: hero.walkFile, sha256: walkSrc.sha256 },
      idle: { file: hero.idleFile, sha256: idleSrc.sha256 },
      hit:  { file: hero.hitFile,  sha256: hitSrc.sha256  },
      death:{ file: hero.deathFile,sha256: deathSrc.sha256 },
    },
    roles: heroRoles, bindings: heroBindings,
  });
}

// License entry for all three SLG hero sheets.
const license = {
  id: 'slg-hero-sheets',
  origin: 'Generated with built-in image_gen for this project (Character Forge sheet pipeline); packed without resampling',
  source: 'https://openai.com/policies/terms-of-use/',
  prompt: 'tools/art-authoring/slg-hero-sheets/README.md',
  commercialUse: 'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.',
};

// Update both manifests.
for (const manifestFile of ['unity/Assets/Art/first-playable-manifest.json', 'unity/Assets/Art/wave1a-manifest.json']) {
  const m = read(path.join(root, manifestFile));

  // Add/replace license.
  m.licenses = m.licenses.filter(l => l.id !== license.id).concat([license]);

  // Add/replace atlas entries.
  for (const hero of packedHeroes) {
    const atlasEntry = { id: hero.atlasId, path: hero.atlasRelPath, width: hero.atlasWidth, height: hero.atlasHeight, padding, license: license.id };
    m.atlases = m.atlases.filter(a => a.id !== hero.atlasId).concat([atlasEntry]);
  }

  // Add/replace role entries.
  const allNewRoleIds = new Set(allRoles.map(r => r.id));
  m.roles = m.roles.filter(r => !allNewRoleIds.has(r.id)).concat(allRoles);

  // Add/replace binding entries.
  const allNewBindingKeys = new Set(packedHeroes.flatMap(h => h.bindings).map(b => `${b.kind}|${b.contentId}|${b.state}`));
  m.bindings = m.bindings.filter(b => !allNewBindingKeys.has(`${b.kind}|${b.contentId}|${b.state}`))
    .concat(packedHeroes.flatMap(h => h.bindings));

  fs.writeFileSync(path.join(root, manifestFile), JSON.stringify(m, null, 2) + '\n');
  console.log(`Updated: ${manifestFile}`);
}

// Write packed-frames record.
const record = {
  schemaVersion: 1,
  releases: {
    walk: 'hero-walk-world-status-20261030-r1',
    basicGem: 'hero-basic-gem-20261030-r1',
    attack: 'hero-attack-consumables-20261101-r1',
  },
  cellSizePx: [cellW, cellH],
  padding,
  atlasWidth: atlasW,
  pivotUnityNormalized: [pivotX, pivotY],
  worldSize,
  frameMsMap,
  heroes: packedHeroes,
};
const recordPath = path.join(root, 'tools/art-authoring/slg-hero-sheets/packed-frames.json');
fs.writeFileSync(recordPath, JSON.stringify(record, null, 2) + '\n');
console.log(`Written: tools/art-authoring/slg-hero-sheets/packed-frames.json`);
