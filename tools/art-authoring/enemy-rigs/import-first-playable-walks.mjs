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
const occupied = [];
for (const entry of spec.roles) {
    assert.equal(entry.role.frames.length, 16);
    const reconstructed = Buffer.alloc(4096 * 256 * 4);
    const source = check ? null : read(entry.sourcePath);
    if (source) assert.equal(sha(source), entry.sourceSha256);
    const decoded = source ? decodeRgbaPng(source) : null;
    if (decoded) { assert.equal(decoded.width, 4096); assert.equal(decoded.height, 256); }
    for (const [frame, rect] of entry.role.frames.entries()) {
        occupied.push(rect);
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
// Preserve the historical cells while sharing one texture across all enemy states.
// The source contract stays fixed so repeated imports cannot copy already-remapped UVs.
const legacyBytes = read(`unity/${spec.legacy.sourceAtlas.path}`);
const legacyPixels = decodeRgbaPng(legacyBytes);
// Unrelated approved actor crops may change independently; pin only the cells copied here.
const protectedCells = new Map(spec.legacy.sourceCells.map(cell => [JSON.stringify(cell.rect), cell.pixelSha256]));
assert.equal(protectedCells.size, spec.legacy.sourceCells.length, 'unique protected historical cells');
const legacyRects = new Map();
for (const original of spec.legacy.roles) {
    const frames = original.frames.map(source => {
        const key = JSON.stringify(source);
        if (legacyRects.has(key)) return legacyRects.get(key);
        const index = legacyRects.size;
        const target = { ...source,
            x: index % spec.legacy.columns * spec.legacy.cellWidth + 2,
            y: spec.legacy.startY + Math.floor(index / spec.legacy.columns) * spec.legacy.cellHeight + 2 };
        assert.ok(target.width + 4 <= spec.legacy.cellWidth && target.height + 4 <= spec.legacy.cellHeight);
        assert.ok(target.x + target.width + 2 <= spec.atlas.width && target.y + target.height + 2 <= spec.atlas.height);
        const originalPixels = Buffer.alloc(source.width * source.height * 4);
        for (let y = 0; y < source.height; y++) {
            const from = ((source.y + y) * legacyPixels.width + source.x) * 4;
            legacyPixels.pixels.copy(originalPixels, y * source.width * 4, from, from + source.width * 4);
        }
        assert.equal(sha(originalPixels), protectedCells.get(key), `${original.id}: protected historical source RGBA`);
        for (let y = 0; y < source.height; y++) {
            const from = ((source.y + y) * legacyPixels.width + source.x) * 4;
            const to = ((target.y + y) * spec.atlas.width + target.x) * 4;
            const row = legacyPixels.pixels.subarray(from, from + source.width * 4);
            if (!check) row.copy(pixels, to);
            assert.ok(row.equals(pixels.subarray(to, to + row.length)), `${original.id}: historical RGBA preserved`);
        }
        legacyRects.set(key, target);
        occupied.push(target);
        return target;
    });
    const role = { ...original, atlas: spec.atlas.id, frames };
    const index = manifest.roles.findIndex(existing => existing.id === role.id);
    assert.ok(index >= 0, role.id);
    if (check) assert.deepEqual(manifest.roles[index], role, `${role.id}: historical display contract preserved`);
    else manifest.roles[index] = role;
}
assert.equal(legacyRects.size, protectedCells.size, 'every protected historical cell is copied');
assert.ok(manifest.roles.filter(role => role.id.startsWith('enemy.')).every(role => role.atlas === spec.atlas.id), 'all first-playable enemy states share one texture');
if (check) {
    const decoded = decodeRgbaPng(read(atlasPath));
    assert.equal(decoded.width, spec.atlas.width); assert.equal(decoded.height, spec.atlas.height);
    assert.deepEqual(manifest.atlases.find(atlas => atlas.id === spec.atlas.id), spec.atlas);
    const coverage = new Uint8Array(spec.atlas.width * spec.atlas.height);
    for (const rect of occupied) for (let y = rect.y; y < rect.y + rect.height; y++) {
        for (let x = rect.x; x < rect.x + rect.width; x++) {
            const offset = y * spec.atlas.width + x;
            assert.equal(coverage[offset], 0, 'packed cells never overlap');
            coverage[offset] = 1;
        }
    }
    for (let offset = 0; offset < coverage.length; offset++) {
        if (!coverage[offset]) assert.equal(pixels[offset * 4 + 3], 0, 'unused atlas area and gutters remain transparent');
    }
} else {
    const atlasIndex = manifest.atlases.findIndex(atlas => atlas.id === spec.atlas.id);
    if (atlasIndex < 0) manifest.atlases.push(spec.atlas);
    else manifest.atlases[atlasIndex] = spec.atlas;
    fs.writeFileSync(path.join(root, atlasPath), encodeRgbaPng({ width: spec.atlas.width, height: spec.atlas.height, pixels }));
    fs.writeFileSync(path.join(root, manifestPath), `${JSON.stringify(manifest, null, 2)}\n`);
}
console.log(`${check ? 'verified' : 'imported'} ${spec.roles.length} approved first-playable roles, ${spec.roles.length * 16} full 256px cells + ${legacyRects.size} historical cells (${spec.legacy.roles.length} roles); source RGBA preserved`);
