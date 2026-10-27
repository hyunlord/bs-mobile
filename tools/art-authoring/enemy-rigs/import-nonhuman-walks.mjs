import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';
const root = fileURLToPath(new URL('../../../', import.meta.url));
const read = file => fs.readFileSync(path.join(root, file));
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const spec = JSON.parse(read('tools/art-authoring/enemy-rigs/nonhuman-approved.json'));
const manifestPath = 'unity/Assets/Art/wave1a-manifest.json';
const atlasPath = 'unity/' + spec.atlas.path;
const manifest = JSON.parse(read(manifestPath));
const actual = decodeRgbaPng(read(atlasPath));
const check = process.argv.includes('--check');
const sourceDir = process.argv.find(a => a.startsWith('--sources='))?.slice(10) ?? 'artifacts/nonhuman455/source';
assert.ok(actual.width === spec.baseAtlas.width || actual.width === spec.atlas.width);
assert.ok(actual.height === spec.baseAtlas.height || actual.height === spec.atlas.height);
const base = Buffer.alloc(spec.baseAtlas.width * spec.baseAtlas.height * 4);
for (let y = 0; y < spec.baseAtlas.height; y++) actual.pixels.copy(base, y * spec.baseAtlas.width * 4, y * actual.width * 4, (y * actual.width + spec.baseAtlas.width) * 4);
assert.equal(sha(base), spec.baseAtlas.pixelSha256, 'all original main RGBA preserved, including human enemy walks');
const unchanged = { ...manifest, atlases: manifest.atlases.filter(a => a.id !== spec.atlas.id), roles: manifest.roles.filter(r => !spec.roles.some(e => e.role.id === r.id)) };
assert.equal(sha(Buffer.from(JSON.stringify(unchanged))), spec.preservedManifestSha256, 'unrelated roles, atlas records and bindings unchanged');
const pixels = check ? actual.pixels : Buffer.alloc(spec.atlas.width * spec.atlas.height * 4);
if (!check) for (let y = 0; y < spec.baseAtlas.height; y++) base.copy(pixels, y * spec.atlas.width * 4, y * spec.baseAtlas.width * 4, (y + 1) * spec.baseAtlas.width * 4);
for (const entry of spec.roles) {
    const { role, prior } = entry;
    assert.deepEqual(role.pivot, prior.pivot); assert.deepEqual(role.worldSize, prior.worldSize);
    assert.equal(role.frames.length, 16); assert.equal(role.atlas, spec.atlas.id);
    const sourceBytes = check ? null : read(path.join(sourceDir, entry.source));
    if (sourceBytes) assert.equal(sha(sourceBytes), entry.sourceSha256);
    const source = sourceBytes ? decodeRgbaPng(sourceBytes) : null;
    if (source) { assert.equal(source.width, 2048); assert.equal(source.height, 512); }
    const reconstructed = Buffer.alloc(2048 * 512 * 4);
    for (const [i, frame] of role.frames.entries()) {
        assert.equal(frame.width, 248); assert.equal(frame.height, 248);
        const x = frame.x - 4, y = frame.y - 4;
        assert.ok(x >= spec.baseAtlas.width && x + 256 <= spec.atlas.width && y >= 0 && y + 256 <= spec.atlas.height);
        for (let row = 0; row < 256; row++) {
            const from = ((Math.floor(i / 8) * 256 + row) * 2048 + i % 8 * 256) * 4;
            const to = ((y + row) * spec.atlas.width + x) * 4;
            if (source) source.pixels.copy(pixels, to, from, from + 1024);
            pixels.copy(reconstructed, from, to, to + 1024);
            for (let column = 0; column < 256; column++) if (row < 4 || row >= 252 || column < 4 || column >= 252)
                assert.equal(pixels[to + column * 4 + 3], 0, role.id + ' four-pixel transparent border');
        }
    }
    assert.equal(sha(reconstructed), entry.pixelSha256, role.id + ' all sixteen approved RGBA cells');
    const index = manifest.roles.findIndex(r => r.id === role.id); assert.ok(index >= 0);
    if (check) assert.deepEqual(manifest.roles[index], role); else manifest.roles[index] = role;
}
const index = manifest.atlases.findIndex(a => a.id === spec.atlas.id); assert.ok(index >= 0);
if (check) {
    assert.equal(actual.width, spec.atlas.width); assert.equal(actual.height, spec.atlas.height);
    assert.deepEqual(manifest.atlases[index], spec.atlas);
} else {
    manifest.atlases[index] = spec.atlas;
    fs.writeFileSync(path.join(root, atlasPath), encodeRgbaPng({ ...spec.atlas, pixels }));
    fs.writeFileSync(path.join(root, manifestPath), JSON.stringify(manifest, null, 2) + '\n');
}
console.log(`${check ? 'verified' : 'imported'} 48 approved nonhuman cells; original ${spec.baseAtlas.width}x${spec.baseAtlas.height} RGBA and unrelated role contracts preserved`);
