// Offline authoring only. Packs Astra's approved M2 building effects (#347, user decisions 2026-10-23) into estate-effects.png:
// the watchtower arrow and impact and the well heal ring (Release m2-building-fx-20261022) and the granary inward ring r2
// (Release m2-granary-magnet-r2-20261022). Every delivered cell is reduced to its approved display size at the 115 px hero
// (review scale: 240 px = 1.081423 Unity units) by an area average over premultiplied alpha. Nothing is redrawn or recoloured.
// The arrow and impact are tiny on screen (46 px, 14 px), so they keep two texels per display pixel.
// Usage: node tools/art-authoring/estate-effects/pack.mjs <fx release dir> <granary r2 release dir>
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { decodeRgbaPng } from '../../wave1a-art.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [fxDir, ringDir] = process.argv.slice(2).map(p => path.resolve(p));
assert.ok(fxDir && ringDir, 'usage: pack.mjs <fx release dir> <granary r2 release dir>');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const reviewPixelsPerUnit = 240 / 1.081423;

const delivery = {
  arrow: [fxDir, 'fx.m2.watchtower.arrow.png', '0d5e2c4990436b1feaa32729dac09728c1ba38ccdf0358e7fc8e4a8dd9970db8'],
  impact: [fxDir, 'fx.m2.watchtower.impact.png', '446b48760452c7059b8b9e333d189b4c271fbd5148f07e9d2c2642f20c3872a7'],
  heal: [fxDir, 'fx.m2.well.heal_ring.png', 'a6fd105d824a21735cc4a12511a1601f14d4b4cd32f824761ee8125e6a482dcf'],
  fxDraft: [fxDir, 'm2-building-fx-manifest-draft.json', '9dd0d069d33b136b7315d529ccdddc3e895ca4bff6cb1c823c8be5d639ac8403'],
  ring: [ringDir, 'fx.m2.granary.magnet_ring.png', 'c9974f63c996ba6d0aa2c855d082c44106186b415b45427c8eb960ca46076890'],
  ringDraft: [ringDir, 'm2-granary-ring-manifest-draft.json', '1f69fc5475bbb5fe4220f94f9320d514afd6e10480582b1b0fe4fb042a8f5387'],
};
const bytes = {};
for (const [key, [dir, file, hash]] of Object.entries(delivery)) {
  bytes[key] = fs.readFileSync(path.join(dir, file));
  assert.equal(sha(bytes[key]), hash, `${file} differs from the delivery`);
}
const fxDraft = JSON.parse(bytes.fxDraft), ringDraft = JSON.parse(bytes.ringDraft);
const draftRole = id => fxDraft.roles.find(r => r.roleProposal === id) ?? assert.fail(`${id} missing from the draft`);

// role id, delivered sheet, draft entry, approved display width (review px), texels per display pixel
const specs = [
  { id: 'fx.estate.granary.magnet_ring', sheet: 'ring', draft: { ...ringDraft, cellSize: ringDraft.cellSize, rects: ringDraft.rects }, width: ringDraft.displayCanvasPx[0], density: 1, frameMs: ringDraft.frameMs },
  { id: 'fx.estate.well.heal_ring', sheet: 'heal', draft: draftRole('fx.m2.well.heal_ring'), width: draftRole('fx.m2.well.heal_ring').previewWidthPx, density: 1 },
  { id: 'fx.estate.watchtower.arrow', sheet: 'arrow', draft: draftRole('fx.m2.watchtower.arrow'), width: draftRole('fx.m2.watchtower.arrow').previewWidthPx, density: 2 },
  { id: 'fx.estate.watchtower.impact', sheet: 'impact', draft: draftRole('fx.m2.watchtower.impact'), width: draftRole('fx.m2.watchtower.impact').previewWidthPx, density: 2 },
];
assert.equal(ringDraft.outerDiameterGamePx / ringDraft.approvedBuildingVisibleWidthPx, 2.5, 'granary ring is not 2.5x the building');

// Area average over premultiplied alpha: each output texel covers an exact rectangle of source pixels (fractional edges weighted).
function reduce(src, sx, sy, sw, sh, w, h) {
  const out = Buffer.alloc(w * h * 4), fx = sw / w, fy = sh / h;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const x0 = x * fx, x1 = x0 + fx, y0 = y * fy, y1 = y0 + fy;
    let r = 0, g = 0, b = 0, a = 0, area = 0;
    for (let py = Math.floor(y0); py < Math.ceil(y1); py++) {
      const wy = Math.min(y1, py + 1) - Math.max(y0, py);
      for (let px = Math.floor(x0); px < Math.ceil(x1); px++) {
        const wx = Math.min(x1, px + 1) - Math.max(x0, px), weight = wx * wy, i = ((sy + py) * src.width + sx + px) * 4;
        const alpha = src.pixels[i + 3] / 255;
        r += src.pixels[i] * alpha * weight; g += src.pixels[i + 1] * alpha * weight; b += src.pixels[i + 2] * alpha * weight; a += alpha * weight; area += weight;
      }
    }
    const o = (y * w + x) * 4, alpha = a / area;
    if (alpha > 0) { out[o] = Math.round(r / a); out[o + 1] = Math.round(g / a); out[o + 2] = Math.round(b / a); }
    out[o + 3] = Math.round(alpha * 255);
  }
  return out;
}

const padding = 4;
for (const spec of specs) {
  const sheet = decodeRgbaPng(bytes[spec.sheet]);
  const [cw, ch] = spec.draft.cellSize;
  const display = [spec.width, spec.width * ch / cw];
  const w = Math.round(display[0] * spec.density), h = Math.max(1, Math.round(display[1] * spec.density));
  spec.frames = spec.draft.rects.map(([x, y, rw, rh]) => {
    assert.deepEqual([rw, rh], [cw, ch]);
    return { pixels: reduce(sheet, x, y, cw, ch, w, h), width: w, height: h };
  });
  spec.worldSize = { x: +(display[0] / reviewPixelsPerUnit).toFixed(6), y: +(display[1] / reviewPixelsPerUnit).toFixed(6) };
  spec.pivot = { x: +spec.draft.pivotUnity[0].toFixed(6), y: +spec.draft.pivotUnity[1].toFixed(6) };
  spec.alphaMaximum = spec.frames.reduce((m, f) => { for (let i = 3; i < f.pixels.length; i += 4) m = Math.max(m, f.pixels[i]); return m; }, 0);
}
// Layout: the granary ring 2x2, then the heal ring frames in a row, then the arrow and the impact.
let x = padding, y = padding, width = 0;
const ring = specs[0];
ring.frames.forEach((f, i) => { f.x = padding + (i % 2) * (f.width + padding); f.y = padding + Math.floor(i / 2) * (f.height + padding); });
width = padding + 2 * (ring.frames[0].width + padding);
y = padding + 2 * (ring.frames[0].height + padding);
let rowHeight = 0;
for (const spec of specs.slice(1)) for (const f of spec.frames) {
  if (x + f.width + padding > width) { x = padding; y += rowHeight + padding; rowHeight = 0; }
  f.x = x; f.y = y; x += f.width + padding; rowHeight = Math.max(rowHeight, f.height);
}
const height = y + rowHeight + padding;
const atlas = Buffer.alloc(width * height * 4);
for (const spec of specs) for (const f of spec.frames) for (let row = 0; row < f.height; row++) f.pixels.copy(atlas, ((f.y + row) * width + f.x) * 4, row * f.width * 4, (row + 1) * f.width * 4);

function crc(data) { let c = 0xffffffff; for (const v of data) { c ^= v; for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) { const size = Buffer.alloc(4), sum = Buffer.alloc(4); size.writeUInt32BE(data.length); const body = Buffer.concat([Buffer.from(type), data]); sum.writeUInt32BE(crc(body)); return Buffer.concat([size, body, sum]); }
function png(w, h, pixels) {
  const header = Buffer.alloc(13); header.writeUInt32BE(w); header.writeUInt32BE(h, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc((w * 4 + 1) * h);
  for (let r = 0; r < h; r++) pixels.copy(rows, r * (w * 4 + 1) + 1, r * w * 4, (r + 1) * w * 4);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const atlasPath = path.join(root, 'unity/Assets/Art/estate-effects.png');
const atlasBytes = png(width, height, atlas);
fs.writeFileSync(atlasPath, atlasBytes);

const manifestFile = path.join(root, 'unity/Assets/Art/wave1a-manifest.json');
const manifest = JSON.parse(fs.readFileSync(manifestFile, 'utf8'));
const licenseId = 'generated-estate-effects', atlasId = 'estate-effects';
manifest.licenses = manifest.licenses.filter(l => l.id !== licenseId).concat({ id: licenseId,
  origin: 'Generated with built-in image_gen for this project by Astra (Releases m2-building-fx-20261022 and m2-granary-magnet-r2-20261022, user-approved 2026-10-22), reduced to the approved display sizes by area averaging for game use without redrawing',
  source: 'https://openai.com/policies/terms-of-use/', prompt: 'docs/licenses/prompts/m2-estate-effects-provenance.txt',
  commercialUse: 'Project-generated output used and distributed under the applicable OpenAI output terms, subject to their restrictions.' });
manifest.atlases = manifest.atlases.filter(a => a.id !== atlasId).concat({ id: atlasId, path: 'Assets/Art/estate-effects.png', width, height, padding, license: licenseId });
manifest.roles = manifest.roles.filter(r => !specs.some(s => s.id === r.id)).concat(specs.map(s => ({ id: s.id, atlas: atlasId,
  frames: s.frames.map(f => ({ x: f.x, y: f.y, width: f.width, height: f.height })), pivot: s.pivot, worldSize: s.worldSize,
  frameMs: s.frames.length > 1 ? (s.frameMs ?? s.draft.frameMs) : 1000, tween: 'none' })));
fs.writeFileSync(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
fs.writeFileSync(path.join(root, 'tools/art-authoring/estate-effects/packed-frames.json'), JSON.stringify({
  schemaVersion: 1, releases: ['m2-building-fx-20261022', 'm2-granary-magnet-r2-20261022'],
  delivery: Object.fromEntries(Object.entries(delivery).map(([k, [, file, hash]]) => [k, { file, sha256: hash }])),
  reviewPixelsPerUnit: +reviewPixelsPerUnit.toFixed(6), atlas: 'unity/Assets/Art/estate-effects.png', atlasSha256: sha(atlasBytes), width, height,
  roles: specs.map(s => ({ id: s.id, displayWidthPx: s.width, texelsPerDisplayPixel: s.density, cell: [s.frames[0].width, s.frames[0].height], frames: s.frames.length,
    pivot: s.pivot, worldSize: s.worldSize, alphaMaximum: s.alphaMaximum })) }, null, 2) + '\n');
console.log(JSON.stringify({ width, height, atlasSha256: sha(atlasBytes), roles: specs.map(s => [s.id, s.frames[0].width, s.frames[0].height, s.frames.length, s.alphaMaximum]) }));
