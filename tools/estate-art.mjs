// Estate map art (#375): turns Astra's Release PNGs into the runtime atlases under unity/Assets/Resources/EstateArt.
//   node tools/estate-art.mjs --source DIR   rebuild from downloaded Release files (all listed files in one directory)
//   node tools/estate-art.mjs --check        verify the committed atlases against the committed manifest
// Each listed asset is downsampled with a premultiplied-alpha box filter so its longer side is at most maxSize, then packed
// in spec order onto shelves. Taking a re-exported file is: download it over the old one and run --source again.
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { decodeRgbaPng } from './wave1a-art.mjs';
import { crc32 } from './s4-plot.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const outputDirectory = 'unity/Assets/Resources/EstateArt';
const fail = message => { throw new Error('estate art: ' + message); };

// RGBA8 PNG with per-row adaptive filtering (the usual minimum sum of absolute differences heuristic).
export function encodeRgbaPng({ width, height, pixels }) {
  const stride = width * 4, scan = Buffer.alloc((stride + 1) * height), row = Buffer.alloc(stride);
  const paeth = (l, u, ul) => { const p = l + u - ul, dl = Math.abs(p - l), du = Math.abs(p - u), dul = Math.abs(p - ul); return dl <= du && dl <= dul ? l : du <= dul ? u : ul; };
  for (let y = 0; y < height; y++) {
    let best = -1, bestSum = Infinity;
    for (let filter = 0; filter <= 4; filter++) {
      let sum = 0;
      for (let x = 0; x < stride; x++) {
        const i = y * stride + x, v = pixels[i], l = x >= 4 ? pixels[i - 4] : 0, u = y > 0 ? pixels[i - stride] : 0, ul = x >= 4 && y > 0 ? pixels[i - stride - 4] : 0;
        const pred = filter === 0 ? 0 : filter === 1 ? l : filter === 2 ? u : filter === 3 ? (l + u) >> 1 : paeth(l, u, ul);
        const d = (v - pred) & 255; row[x] = d; sum += d < 128 ? d : 256 - d;
      }
      if (sum < bestSum) { bestSum = sum; best = filter; row.copy(scan, y * (stride + 1) + 1); }
    }
    scan[y * (stride + 1)] = best;
  }
  const chunk = (name, data) => {
    const type = Buffer.from(name), header = Buffer.alloc(4), tail = Buffer.alloc(4);
    header.writeUInt32BE(data.length); tail.writeUInt32BE(crc32(Buffer.concat([type, data])));
    return Buffer.concat([header, type, data, tail]);
  };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(scan, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

// Area-weighted box filter on premultiplied colour, so transparent pixels do not darken soft edges.
export function downsample(image, width, height) {
  if (width === image.width && height === image.height) return { width, height, pixels: Buffer.from(image.pixels) };
  const sx = image.width / width, sy = image.height / height, out = Buffer.alloc(width * height * 4);
  for (let y = 0; y < height; y++) {
    const y0 = y * sy, y1 = y0 + sy;
    for (let x = 0; x < width; x++) {
      const x0 = x * sx, x1 = x0 + sx;
      let r = 0, g = 0, b = 0, a = 0, area = 0;
      for (let py = Math.floor(y0); py < Math.ceil(y1); py++) {
        const wy = Math.min(y1, py + 1) - Math.max(y0, py);
        for (let px = Math.floor(x0); px < Math.ceil(x1); px++) {
          const w = wy * (Math.min(x1, px + 1) - Math.max(x0, px)), i = (py * image.width + px) * 4, alpha = image.pixels[i + 3] / 255;
          r += image.pixels[i] * alpha * w; g += image.pixels[i + 1] * alpha * w; b += image.pixels[i + 2] * alpha * w; a += alpha * w; area += w;
        }
      }
      const o = (y * width + x) * 4;
      out[o] = a > 0 ? Math.round(r / a) : 0; out[o + 1] = a > 0 ? Math.round(g / a) : 0; out[o + 2] = a > 0 ? Math.round(b / a) : 0;
      out[o + 3] = Math.round(a / area * 255);
    }
  }
  return { width, height, pixels: out };
}

// Shelf packing in input order: deterministic, good enough for a few dozen sprites.
export function pack(images, atlasWidth, padding) {
  let x = padding, y = padding, shelf = 0; const rects = [];
  for (const image of images) {
    if (image.width + 2 * padding > atlasWidth) fail('sprite wider than the atlas');
    if (x + image.width + padding > atlasWidth) { x = padding; y += shelf + padding; shelf = 0; }
    rects.push([x, y, image.width, image.height]); x += image.width + padding; shelf = Math.max(shelf, image.height);
  }
  const height = Math.ceil((y + shelf + padding) / 4) * 4, pixels = Buffer.alloc(atlasWidth * height * 4);
  images.forEach((image, i) => {
    const [rx, ry] = rects[i];
    for (let row = 0; row < image.height; row++) image.pixels.copy(pixels, ((ry + row) * atlasWidth + rx) * 4, row * image.width * 4, (row + 1) * image.width * 4);
  });
  return { atlas: { width: atlasWidth, height, pixels }, rects };
}

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

// Width of the opaque body inside an atlas cell, as assembly.js trim() measures it (alpha > 8): Astra scales a building by
// screenWidth / bodyWidth, so the game must use the same number (not the manifest's looser drawRect).
export function opaqueWidth(image, [x, y, w, h]) {
  let left = w, right = -1;
  for (let row = y; row < y + h; row++)
    for (let col = x; col < x + w; col++)
      if (image.pixels[(row * image.width + col) * 4 + 3] > 8) { left = Math.min(left, col - x); right = Math.max(right, col - x); }
  return right < 0 ? 0 : right - left + 1;
}

export function build(spec, readSource) {
  const files = {}, entries = [];
  for (const atlasId of [...new Set(spec.assets.map(a => a.atlas))]) {
    const members = spec.assets.filter(a => a.atlas === atlasId), images = [];
    for (const asset of members) {
      const bytes = readSource(asset.file), image = decodeRgbaPng(bytes);
      const scale = Math.min(1, asset.maxSize / Math.max(image.width, image.height));
      images.push(downsample(image, Math.max(1, Math.round(image.width * scale)), Math.max(1, Math.round(image.height * scale))));
      entries.push({ id: asset.id, atlas: atlasId, source: { release: asset.release, file: asset.file, sha256: sha256(bytes), size: [image.width, image.height] },
        pivot: asset.pivot, displayWidth: asset.displayWidth, layer: asset.layer });
    }
    const { atlas, rects } = pack(images, spec.atlasWidth, spec.padding);
    members.forEach((asset, i) => { entries.find(e => e.id === asset.id).rect = rects[i]; });
    files[`estate-${atlasId}.png`] = encodeRgbaPng(atlas);
  }
  const b = spec.buildings, body = readSource(b.body), bodyImage = decodeRgbaPng(body), shadow = decodeRgbaPng(readSource(b.shadow));
  files['estate-buildings.png'] = body;
  files['estate-building-shadows.png'] = encodeRgbaPng(downsample(shadow, Math.round(shadow.width * b.shadowScale), Math.round(shadow.height * b.shadowScale)));
  const records = JSON.parse(readSource(b.manifest).toString('utf8')).records;
  const buildings = records.map(r => ({ id: r.id, kind: r.kind, stage: r.stage, rect: r.atlasRect, pivot: r.pivotNormalizedBottomLeft, drawRect: r.drawRect,
    bodyWidth: opaqueWidth(bodyImage, r.atlasRect) }));
  const manifest = { version: spec.version, note: spec.note,
    atlases: Object.keys(files).sort().map(name => ({ file: name, sha256: sha256(files[name]) })),
    buildings: { release: b.release, body: { file: b.body, sha256: sha256(body) }, shadowScale: b.shadowScale, canvas: [256, 320], records: buildings },
    sprites: entries };
  files['estate-art.json'] = Buffer.from(JSON.stringify(manifest, null, 2) + '\n');
  return files;
}

export function check(directory = path.join(root, outputDirectory)) {
  const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'estate-art.json'), 'utf8'));
  for (const atlas of manifest.atlases) {
    const bytes = fs.readFileSync(path.join(directory, atlas.file));
    if (sha256(bytes) !== atlas.sha256) fail(atlas.file + ' differs from the manifest; rerun --source');
  }
  for (const sprite of manifest.sprites) {
    const image = decodeRgbaPng(fs.readFileSync(path.join(directory, `estate-${sprite.atlas}.png`)));
    const [x, y, w, h] = sprite.rect;
    if (x < 0 || y < 0 || x + w > image.width || y + h > image.height) fail(sprite.id + ' rect outside its atlas');
    if (!(sprite.pivot?.length === 2)) fail(sprite.id + ' pivot');
  }
  if (manifest.buildings.records.length !== 30) fail('expected 10 building kinds x 3 stages');
  if (manifest.buildings.records.some(r => !(r.bodyWidth > 0))) fail('every building stage needs its opaque body width');
  return manifest;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args[0] === '--check') { const m = check(); console.log(`estate art ok: ${m.sprites.length} sprites, ${m.buildings.records.length} building stages`); }
  else if (args[0] === '--source' && args[1]) {
    const spec = JSON.parse(fs.readFileSync(path.join(root, 'tools/estate-art.json'), 'utf8'));
    const files = build(spec, file => fs.readFileSync(path.join(args[1], file)));
    const out = path.join(root, outputDirectory); fs.mkdirSync(out, { recursive: true });
    for (const [name, bytes] of Object.entries(files)) fs.writeFileSync(path.join(out, name), bytes);
    check(out); console.log('wrote', Object.keys(files).join(', '));
  } else fail('usage: node tools/estate-art.mjs --source DIR | --check');
}
