// #436: verify source provenance, untouched legacy pixels/roles and new shared-atlas crops.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { validateManifest } from '../../first-playable-art.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const read = p => fs.readFileSync(path.join(root, p));
const record = JSON.parse(read('tools/art-authoring/people-walk/packed-frames.json'));
const sha = b => createHash('sha256').update(b).digest('hex');
const atlas = decodeRgbaPng(read(record.atlas));
assert.equal(sha(read(record.atlas)), record.atlasSha256);
assert.deepEqual([atlas.width, atlas.height], [1920, 1940]);
const crop = rect => {
  const bytes = Buffer.alloc(rect.width * rect.height * 4);
  for (let row = 0; row < rect.height; row++) atlas.pixels.copy(bytes, row * rect.width * 4, ((rect.y + row) * atlas.width + rect.x) * 4, ((rect.y + row) * atlas.width + rect.x + rect.width) * 4);
  return bytes;
};
assert.equal(sha(crop({ x: 0, y: 0, width: 1254, height: 1254 })), record.originalDecodedRgbaSha256, 'every original pixel including transparent areas is unchanged');
const layout = JSON.parse(read('tools/art-authoring/people-walk/layout.json'));
assert.deepEqual(record.size, [layout.width, layout.height]);
assert.equal(record.padding, layout.padding);
assert.equal(layout.frames.length, 48);
assert.equal(record.uncompressedRgbaBytesDelta, 8609136);
const changed = new Set(record.clips.map(c => `person.${c.kind}.move`));
let preservedRoles = 0, frames = 0;
const occupied = new Set();
for (const clip of record.clips) for (let index = 0; index < 16; index++) {
  const rect = clip.frames[index];
  const placement = layout.frames.find(f => f.kind === clip.kind && f.index === index);
  assert.ok(placement);
  assert.deepEqual(rect, { x: placement.x, y: placement.y, width: placement.width, height: placement.height });
  assert.deepEqual([rect.width, rect.height], [clip.crop.width, clip.crop.height]);
  assert.equal(sha(crop(rect)), clip.rgbaSha256[index]);
  for (let y = rect.y - 2; y < rect.y + rect.height + 2; y++) for (let x = rect.x - 2; x < rect.x + rect.width + 2; x++) {
    assert.ok(x >= 0 && y >= 0 && x < atlas.width && y < atlas.height);
    assert.ok(x >= 1254 || y >= 1254, 'no frame/padding overlaps old atlas');
    const key = y * atlas.width + x;
    assert.ok(!occupied.has(key), 'frame/padding overlap'); occupied.add(key);
    if (x < rect.x || y < rect.y || x >= rect.x + rect.width || y >= rect.y + rect.height) assert.equal(atlas.pixels[key * 4 + 3], 0, 'padding alpha');
  }
  frames++;
}
for (const profile of ['first-playable', 'wave1a']) {
  const p = `unity/Assets/Art/${profile}-manifest.json`;
  const current = JSON.parse(read(p));
  const before = JSON.parse(execFileSync('git', ['show', `${record.base}:${p}`], { cwd: root }));
  validateManifest(current);
  assert.equal(current.atlases.length, before.atlases.length, 'texture count unchanged');
  assert.deepEqual(current.bindings, before.bindings);
  for (const role of before.roles) if (!changed.has(role.id)) { assert.deepEqual(current.roles.find(r => r.id === role.id), role, role.id); preservedRoles++; }
  for (const clip of record.clips) {
    const role = current.roles.find(r => r.id === `person.${clip.kind}.move`);
    assert.deepEqual(role.frames, clip.frames); assert.deepEqual(role.pivot, clip.pivot); assert.deepEqual(role.worldSize, clip.worldSize);
    assert.equal(role.atlas, 'actors'); assert.equal(role.frameMs, 40); assert.equal(role.tween, 'none'); assert.equal(role.cyclePixels, clip.cyclePixels);
    assert.equal(new Set(clip.rgbaSha256).size, 16);
    assert.ok(Math.abs(role.worldSize.y / clip.crop.height - clip.unitsPerPixel) < 1e-12);
  }
}
const metaPath = `${record.atlas}.meta`;
const beforeMeta = execFileSync('git', ['show', `${record.base}:${metaPath}`], { cwd: root }).toString();
const restoredMeta = read(metaPath).toString().replace(/(      rect:\n        serializedVersion: 2\n        x: \d+\n        y: )(\d+)/g, (_, p, y) => p + (Number(y) - (atlas.height - record.originalSize[1])));
assert.equal(restoredMeta, beforeMeta, 'only bottom-origin sprite offsets may change');
assert.equal((read(metaPath).toString().match(/maxTextureSize: 2048/g) || []).length, 6);
console.log(JSON.stringify({ pass: true, frames, preservedRoles, preservedOldPixels: 1254 * 1254, textureCountDelta: 0, uncompressedRgbaBytesDelta: record.uncompressedRgbaBytesDelta }));
