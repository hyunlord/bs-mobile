import assert from 'node:assert/strict';
import test from 'node:test';
import { replaceCells } from './replace.mjs';

test('exact RGBA copy preserves every pixel outside the approved cell', () => {
  const image = { width: 4, height: 3, pixels: Buffer.alloc(48, 19) };
  const pixels = Buffer.from([255, 0, 4, 0, 8, 9, 10, 128]);
  const result = replaceCells(image, [{ rect: [1, 1, 2, 1], image: { width: 2, height: 1, pixels } }]);
  assert.deepEqual(result.pixels.subarray(20, 28), pixels);
  assert.deepEqual(result.pixels.subarray(0, 20), image.pixels.subarray(0, 20));
  assert.deepEqual(result.pixels.subarray(28), image.pixels.subarray(28));
  assert.deepEqual(image.pixels, Buffer.alloc(48, 19));
  assert.equal(result.outsideChanged, 0);
});

test('reject dimensions, bounds and overlaps instead of resampling or overwriting', () => {
  const image = { width: 4, height: 3, pixels: Buffer.alloc(48) };
  const cell = { rect: [1, 1, 2, 1], image: { width: 2, height: 1, pixels: Buffer.alloc(8) } };
  assert.throws(() => replaceCells(image, [{ ...cell, rect: [1, 1, 3, 1] }]));
  assert.throws(() => replaceCells(image, [{ ...cell, rect: [3, 1, 2, 1] }]));
  assert.throws(() => replaceCells(image, [cell, cell]));
});
