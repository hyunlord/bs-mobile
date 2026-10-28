import test from 'node:test';
import assert from 'node:assert/strict';
import { blockBytes, pixelMetrics, gutterMetrics, roleGutters } from './summarize-astc-trial.mjs';
test('ASTC payload rounds each dimension independently, no silent resize', () => {
  assert.equal(blockBytes(2564, 2164, 4), 5548496);
  assert.equal(blockBytes(2564, 2164, 6), 2472128);
  assert.equal(blockBytes(2048, 2304, 4), 4718592);
  assert.equal(blockBytes(2048, 2304, 6), 2101248);
  assert.equal(blockBytes(2048, 2304, 0), 18874368);
  assert.throws(() => blockBytes(0, 4, 6));
});
test('alpha losses and transparent leakage are reported, not hidden by RGB', () => {
  const source = { width: 2, height: 1, pixels: Buffer.from([10,20,30,0,10,20,30,255]) };
  const decoded = { ...source, pixels: Buffer.from([10,20,30,3,10,20,30,250]) };
  const m = pixelMetrics(source, decoded);
  assert.equal(m.transparentLeak, 1); assert.equal(m.opaqueLoss, 1); assert.equal(m.alphaMae, 4); assert.equal(m.alphaMax, 5);
});
test('gutter scan excludes interior and clips atlas edges', () => {
  const pixels = Buffer.alloc(4 * 4 * 4); pixels[3] = 9; pixels[(1 * 4 + 1) * 4 + 3] = 255;
  const m = gutterMetrics({ width: 4, height: 4, pixels }, [{ x: 1, y: 1, width: 2, height: 2 }], 4);
  assert.equal(m.samples, 12); assert.equal(m.nonzeroAlpha, 1); assert.equal(m.maxAlpha, 9);
});

test('estate padding is explicitly unmeasured, never a zero-sample pass', () => {
  assert.equal(roleGutters({}, { version: "estate-art-r1" }, "estate-decor").status, "NOT_MEASURED");
  assert.throws(() => roleGutters({}, { roles: [] }, "missing"));
});
