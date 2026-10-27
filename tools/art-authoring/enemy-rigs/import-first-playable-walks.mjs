import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';

// Approved source sheets are downloaded into this worktree's ignored artifacts only.
const root = fileURLToPath(new URL('../../../', import.meta.url));
const read = p => fs.readFileSync(path.join(root, p));
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const spec = JSON.parse(read('tools/art-authoring/enemy-rigs/first-playable-approved-walks.json'));
const manifestPath = 'unity/Assets/Art/first-playable-manifest.json';
const atlasPath = `unity/${spec.atlas.path}`;
const manifest = JSON.parse(read(manifestPath));
const check = process.argv.includes('--check');
const pixels = check ? decodeRgbaPng(read(atlasPath)).pixels : Buffer.alloc(spec.atlas.width * spec.atlas.height * 4);
for (const entry of spec.roles) {
    assert.equal(entry.role.frames.length, 16);
    const reconstructed = Buffer.alloc(4096 * 256 * 4);
    const source = check ? null : read(entry.sourcePath);
    if (source) assert.equal(sha(source), entry.sourceSha256);
    const decoded = source ? decodeRgbaPng(source) : null;
    if (decoded) { assert.equal(decoded.width, 4096); assert.equal(decoded.height, 256); }
    for (const [frame, rect] of entry.role.frames.entries()) {
        assert.equal(rect.width, 256); assert.equal(rect.height, 256);
        for (let y = 0; y < 256; y++) {
            const src = (y * 4096 + frame * 256) * 4;
            const dst = ((rect.y + y) * spec.atlas.width + rect.x) * 4;
            if (decoded) decoded.pixels.copy(pixels, dst, src, src + 1024);
            pixels.copy(reconstructed, src, dst, dst + 1024);
        }
    }
    assert.equal(sha(reconstructed), entry.sourcePixelSha256, `${entry.role.id}: full original RGBA`);
    const index = manifest.roles.findIndex(role => role.id === entry.role.id);
    assert.ok(index >= 0, entry.role.id);
    if (check) assert.deepEqual(manifest.roles[index], entry.role);
    else manifest.roles[index] = entry.role;
}
if (check) {
    const decoded = decodeRgbaPng(read(atlasPath));
    assert.equal(decoded.width, spec.atlas.width); assert.equal(decoded.height, spec.atlas.height);
    assert.deepEqual(manifest.atlases.find(atlas => atlas.id === spec.atlas.id), spec.atlas);
    for (let y = 0; y < spec.atlas.height; y++) for (let x = 0; x < spec.atlas.width; x++) {
        if (x % 260 < 2 || x % 260 >= 258 || y % 260 < 2 || y % 260 >= 258)
            assert.equal(pixels[(y * spec.atlas.width + x) * 4 + 3], 0, 'transparent two-pixel gutters');
    }
} else {
    if (!manifest.atlases.some(atlas => atlas.id === spec.atlas.id)) manifest.atlases.push(spec.atlas);
    fs.writeFileSync(path.join(root, atlasPath), encodeRgbaPng({ width: spec.atlas.width, height: spec.atlas.height, pixels }));
    fs.writeFileSync(path.join(root, manifestPath), `${JSON.stringify(manifest, null, 2)}\n`);
}
console.log(`${check ? 'verified' : 'imported'} 3 approved first-playable roles, 48 full 256px cells; source RGBA preserved`);
