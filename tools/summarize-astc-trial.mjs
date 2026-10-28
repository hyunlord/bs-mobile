import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import { decodeRgbaPng } from './wave1a-art.mjs';

export function blockBytes(width, height, block) {
  for (const value of [width, height]) assert(Number.isInteger(value) && value > 0);
  assert([0, 4, 6].includes(block));
  return block === 0 ? width * height * 4 : Math.ceil(width / block) * Math.ceil(height / block) * 16;
}
export function pixelMetrics(source, decoded) {
  assert.equal(source.width, decoded.width); assert.equal(source.height, decoded.height);
  let alphaError = 0, alphaMax = 0, transparentLeak = 0, transparentCount = 0, opaqueLoss = 0, rgbError = 0;
  for (let p = 0; p < source.pixels.length; p += 4) {
    const a = source.pixels[p + 3], b = decoded.pixels[p + 3];
    const error = Math.abs(a - b); alphaError += error; alphaMax = Math.max(alphaMax, error);
    if (a === 0) { transparentCount++; if (b !== 0) transparentLeak++; }
    if (a === 255 && b < 255) opaqueLoss++;
    for (let c = 0; c < 3; c++) rgbError += Math.abs(source.pixels[p + c] - decoded.pixels[p + c]);
  }
  const count = source.width * source.height;
  return { pixels: count, alphaMae: alphaError / count, alphaMax, transparentCount, transparentLeak, opaqueLoss, rgbMae: rgbError / (count * 3) };
}
export function gutterMetrics(decoded, frames, padding = 4) {
  let samples = 0, nonzeroAlpha = 0, maxAlpha = 0;
  for (const f of frames) for (let y = Math.max(0, f.y - padding); y < Math.min(decoded.height, f.y + f.height + padding); y++)
    for (let x = Math.max(0, f.x - padding); x < Math.min(decoded.width, f.x + f.width + padding); x++) {
      if (x >= f.x && x < f.x + f.width && y >= f.y && y < f.y + f.height) continue;
      const a = decoded.pixels[(y * decoded.width + x) * 4 + 3]; samples++; if (a) nonzeroAlpha++; maxAlpha = Math.max(maxAlpha, a);
    }
  return { samples, nonzeroAlpha, maxAlpha, note: 'Per-role gutter samples may overlap. Compare original and decoded; do not relax source-padding contracts.' };
}
export function roleGutters(image, manifest, atlas) {
  if (!Array.isArray(manifest.roles)) return { status: 'NOT_MEASURED', reason: 'This manifest is not the combat role/frame padding contract.' };
  const frames = manifest.roles.filter(role => role.atlas === atlas).flatMap(role => role.frames);
  assert(frames.length > 0, `No role frames for ${atlas}`);
  return gutterMetrics(image, frames);
}
export function summarize(directory, manifestPath) {
  const manifest = JSON.parse(fs.readFileSync(manifestPath));
  return fs.readdirSync(directory).filter(name => name.endsWith('.json') && name !== 'inventory.json').map(name => {
    const row = JSON.parse(fs.readFileSync(path.join(directory, name))); assert(row.gpuSupported);
    assert.equal(row.rawBytes, blockBytes(row.width, row.height, row.block)); assert.equal(row.mipCount, 1);
    const source = decodeRgbaPng(fs.readFileSync(path.join(directory, row.atlas + '-source.png')));
    const decoded = decodeRgbaPng(fs.readFileSync(path.join(directory, row.atlas + '-decoded.png')));
    return { ...row, metrics: pixelMetrics(source, decoded), sourceGutters: roleGutters(source, manifest, row.atlas), decodedGutters: roleGutters(decoded, manifest, row.atlas), interpretation: 'Quality measurements only. Raw ASTC payload is not total process/GPU memory; decoded PNG is RGBA. No automatic visual PASS.' };
  });
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  assert.equal(process.argv.length, 4, 'node tools/summarize-astc-trial.mjs <trial-directory> <manifest.json>');
  console.log(JSON.stringify(summarize(process.argv[2], process.argv[3]), null, 2));
}
