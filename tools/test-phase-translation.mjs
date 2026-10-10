import assert from 'node:assert/strict';
import test from 'node:test';
import { fft2, phaseTranslation } from './phase-translation.mjs';
import { grayPatch } from './analyze-motion-video.mjs';

const texture = (x, y) => 100 + 50 * Math.sin(x * .71 + y * .31) + 35 * Math.cos(x * .13 - y * .93) + 20 * Math.sin(x * y * .017);
// Independent Python/numpy 2.4.3 + OpenCV 4.13.0 reference values, generated once.
// Runtime and CI use only these constants; no Python, OpenCV, cache, or artifact dependency.
for (const [width, height, dx, dy, expectedX, expectedY, response] of [
  [64, 48, .35, -.65, -.03, -.1, .941673405801593],
  [31, 27, 2, -1, .19, -.23, .7844496156877468],
  [32, 32, -.4, .7, -.02, .1, .9147981205647192],
  [17, 19, 0, 0, 0, 0, .9999999999999667],
]) {
  test(`phase/local DFT/OpenCV response parity ${width}x${height}`, () => {
    const a = Float64Array.from({ length: width * height }, (_, i) => texture(i % width, Math.floor(i / width)));
    const b = Float64Array.from(a, (_, i) => texture(i % width - dx, Math.floor(i / width) - dy));
    const result = phaseTranslation(a, b, width, height);
    assert.ok(Math.abs(result.dx - expectedX) < 1e-10);
    assert.ok(Math.abs(result.dy - expectedY) < 1e-10);
    assert.ok(Math.abs(result.response - response) < 1e-8);
  });
}
test('arbitrary prime FFT shape preserves complex values on round trip', () => {
  const width = 31, height = 19, a = Float64Array.from({ length: width * height }, (_, i) => Math.sin(i * .34));
  const b = Float64Array.from(a, (_, i) => Math.cos(i * .72));
  const f = fft2(a, b, width, height), back = fft2(f.re, f.im, width, height, true);
  for (let i = 0; i < a.length; i++) { assert.ok(Math.abs(back.re[i] - a[i]) < 1e-10); assert.ok(Math.abs(back.im[i] - b[i]) < 1e-10); }
});
test('band-limited synthetic translation resolves fractional pixels', () => {
  const width = 64, height = 64;
  let state = 123456;
  const a = Float64Array.from({ length: width * height }, () => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state / 4294967296 * 255; });
  const f = fft2(a, null, width, height);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const i = y * width + x, fx = (x < width / 2 ? x : x - width) / width, fy = (y < height / 2 ? y : y - height) / height;
    const angle = -2 * Math.PI * (fx * .35 + fy * -.65), c = Math.cos(angle), s = Math.sin(angle), r = f.re[i] * c - f.im[i] * s;
    f.im[i] = f.re[i] * s + f.im[i] * c; f.re[i] = r;
  }
  const b = fft2(f.re, f.im, width, height, true).re, result = phaseTranslation(a, b, width, height);
  assert.ok(Math.abs(result.dx - .35) <= .02); assert.ok(Math.abs(result.dy + .65) <= .02);
});
test('BGR2GRAY uses OpenCV 15-bit coefficients without channel reversal', () => {
  assert.deepEqual([...grayPatch(Uint8Array.from([255, 0, 0, 0, 255, 0, 0, 0, 255, 13, 72, 181]))], [29, 150, 76, 98]);
});
test('invalid or undersized patches fail instead of returning plausible motion', () => {
  assert.throws(() => phaseTranslation(new Float64Array(225), new Float64Array(225), 15, 15));
  const a = new Float64Array(256); a[0] = NaN;
  assert.throws(() => phaseTranslation(a, a, 16, 16));
});
