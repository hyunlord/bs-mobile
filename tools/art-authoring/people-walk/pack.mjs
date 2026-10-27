// #436: losslessly append approved people walks to actors; no old pixel/crop is moved.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const source = path.resolve(process.argv[2] || 'artifacts/people433-source');
const base = 'a44bf7ae6a5811de057c8f12410ce4a5d8fb0d3f';
const readBase = name => {
  const blob = execFileSync('git', ['show', `${base}:${name}`], { cwd: root, maxBuffer: 32 * 1024 * 1024 });
  return blob.subarray(0, 42).toString().startsWith('version https://git-lfs.github.com/spec/v1')
    ? execFileSync('git', ['lfs', 'smudge'], { cwd: root, input: blob, maxBuffer: 32 * 1024 * 1024 }) : blob;
};
const sha = b => createHash('sha256').update(b).digest('hex');
const definitions = [
  ['vassal', 'dc875996c8b7b46a217e0be82103d6f9d7344ff9a89a881eb62d1971c9374cf6', 125, .5031055900621119, 59.985697116093824],
  ['peasant', '2482ad9d70a3397b636dfe6849f618e2e0b0b5ac97e4fb2710e7750cff301175', 110, .517080745341615, 61.06071581787939],
  ['returning', '302d9d5cd9941f1857fdf0b7dc89e4a7c2d6b74ac5f36c7c05a85a6a9ce1deb4', 110, .517080745341615, 61.06071581787939],
];
const atlasPath = 'unity/Assets/Art/actors.png';
const oldBytes = readBase(atlasPath), old = decodeRgbaPng(oldBytes);
assert.deepEqual([old.width, old.height], [1254, 1254]);
const layout = JSON.parse(fs.readFileSync(path.join(root, 'tools/art-authoring/people-walk/layout.json'), 'utf8'));
const { width, height, padding } = layout;
assert.equal(layout.legacySize, old.width);
assert.equal(layout.frames.length, 48);
const reserved = [{ x: 0, y: 0, width: old.width, height: old.height }];
for (const frame of layout.frames) {
  const rect = { x: frame.x - padding, y: frame.y - padding,
    width: frame.width + padding * 2, height: frame.height + padding * 2 };
  assert.ok(rect.x >= 0 && rect.y >= 0 && rect.x + rect.width <= width && rect.y + rect.height <= height);
  for (const other of reserved) assert.ok(rect.x + rect.width <= other.x || other.x + other.width <= rect.x ||
    rect.y + rect.height <= other.y || other.y + other.height <= rect.y, 'layout padding/legacy overlap');
  reserved.push(rect);
}
const pixels = Buffer.alloc(width * height * 4);
for (let y = 0; y < old.height; y++) old.pixels.copy(pixels, y * width * 4, y * old.width * 4, (y + 1) * old.width * 4);
// Reserve the WHOLE old atlas, including undeclared legacy sprites and currently transparent pixels.
let slot = 0;
const clips = definitions.map(([kind, expected, anchorX, cellWorldSize, cyclePixels]) => {
  const filename = `person-${kind}-r1-16.png`, bytes = fs.readFileSync(path.join(source, filename));
  assert.equal(sha(bytes), expected, filename);
  const image = decodeRgbaPng(bytes);
  assert.deepEqual([image.width, image.height], [4096, 256]);
  let left = 256, top = 256, right = 0, bottom = 0;
  for (let y = 0; y < 256; y++) for (let x = 0; x < 4096; x++) if (image.pixels[(y * 4096 + x) * 4 + 3]) {
    left = Math.min(left, x % 256); right = Math.max(right, x % 256 + 1); top = Math.min(top, y); bottom = Math.max(bottom, y + 1);
  }
  const w = right - left, h = bottom - top;
  assert.ok(w + padding * 2 <= 191 && h + padding * 2 <= 228);
  const frames = [], rgbaSha256 = [];
  for (let index = 0; index < 16; index++) {
    const placement = layout.frames.find(f => f.kind === kind && f.index === index);
    assert.ok(placement, kind + ':' + index);
    assert.deepEqual([placement.width, placement.height], [w, h], 'approved union crop geometry');
    const { x, y } = placement;
    slot++;
    assert.ok(x + w + padding <= width && y + h + padding <= height);
    const crop = Buffer.alloc(w * h * 4);
    for (let row = 0; row < h; row++) {
      const start = ((top + row) * 4096 + index * 256 + left) * 4;
      image.pixels.copy(crop, row * w * 4, start, start + w * 4);
      image.pixels.copy(pixels, ((y + row) * width + x) * 4, start, start + w * 4);
    }
    frames.push({ x, y, width: w, height: h }); rgbaSha256.push(sha(crop));
  }
  return { kind, filename, sourceSha256: expected, crop: { left, top, width: w, height: h },
    pivot: { x: (anchorX - left) / w, y: (bottom - 225) / h },
    worldSize: { x: w * cellWorldSize / 256, y: h * cellWorldSize / 256 },
    unitsPerPixel: cellWorldSize / 256, cyclePixels, frameMs: 40, frames, rgbaSha256 };
});
for (let y = 0; y < old.height; y++) assert.ok(old.pixels.subarray(y * old.width * 4, (y + 1) * old.width * 4).equals(pixels.subarray(y * width * 4, (y * width + old.width) * 4)));
const atlasBytes = encodeRgbaPng({ width, height, pixels });
fs.writeFileSync(path.join(root, atlasPath), atlasBytes);
// Unity's legacy SpriteImporter rectangles are bottom-left; keep their decoded source crops unchanged too.
const metaPath = `${atlasPath}.meta`;
let rectCount = 0;
const meta = readBase(metaPath).toString().replace(/(      rect:\n        serializedVersion: 2\n        x: \d+\n        y: )(\d+)/g, (_, prefix, y) => { rectCount++; return prefix + (Number(y) + height - old.height); });
assert.ok(rectCount > 0);
fs.writeFileSync(path.join(root, metaPath), meta);
for (const profile of ['first-playable', 'wave1a']) {
  const name = `unity/Assets/Art/${profile}-manifest.json`;
  const m = JSON.parse(fs.readFileSync(path.join(root, name), 'utf8'));
  const atlas = m.atlases.find(a => a.id === 'actors');
  assert.ok(atlas && ([old.width, 2048, width].includes(atlas.width)));
  atlas.width = width; atlas.height = height;
  for (const clip of clips) {
    const role = m.roles.find(r => r.id === `person.${clip.kind}.move`);
    assert.ok(role);
    Object.assign(role, { atlas: 'actors', frames: clip.frames, pivot: clip.pivot, worldSize: clip.worldSize, frameMs: clip.frameMs, tween: 'none', cyclePixels: clip.cyclePixels });
  }
  const license = m.licenses.find(l => l.id === 'generated-actors');
  const suffix = '; approved people SpriteSkin16 walks, Release m3-people-walk-review-433-20261027; lossless union crops (#436)';
  if (!license.origin.includes(suffix)) license.origin += suffix;
  fs.writeFileSync(path.join(root, name), JSON.stringify(m, null, 2) + '\n');
}
const record = { version: 1, release: 'm3-people-walk-review-433-20261027', base, atlas: atlasPath, originalSize: [old.width, old.height], size: [width, height],
  originalAtlasSha256: sha(oldBytes), originalDecodedRgbaSha256: sha(old.pixels), atlasSha256: sha(atlasBytes),
  oldPixelsPreserved: old.width * old.height, importerRectsPreserved: rectCount, textureCountDelta: 0,
  uncompressedRgbaBytesDelta: (width * height - old.width * old.height) * 4, padding,
  strideProvenance: 'Approved PeopleGait.cs formula + approved rig-source PNG alpha contour, Node double precision; not Unity float measurement. See derive-stride.mjs.', clips };
fs.writeFileSync(path.join(root, 'tools/art-authoring/people-walk/packed-frames.json'), JSON.stringify(record, null, 2) + '\n');
console.log(JSON.stringify({ clips: clips.length, frames: slot, oldPixelsPreserved: record.oldPixelsPreserved, importerRectsPreserved: rectCount, textureCountDelta: 0, uncompressedRgbaBytesDelta: record.uncompressedRgbaBytesDelta }));
