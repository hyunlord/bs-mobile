import test from 'node:test';
import assert from 'node:assert/strict';
import { formatCsv, parseCsv } from './csv.mjs';

test('CSV preserves Korean, commas, escaped quotes, multiline and empty cells', () => {
  const rows = [{ id: 'core:씨앗', text: '밭, "수확"\r\n다음 줄', blank: '' }];
  assert.deepEqual(parseCsv(formatCsv(['id', 'text', 'blank'], rows)), rows);
});
test('CSV accepts CRLF and optional final newline without trimming', () => {
  assert.deepEqual(parseCsv('a,b\r\n" x ",""'), [{ a: ' x ', b: '' }]);
  assert.deepEqual(parseCsv('a,b\r\n'), []);
});
test('CSV serializes only explicit scalars, finite numbers and null', () => {
  assert.deepEqual(parseCsv(formatCsv(['n', 'b', 'z'], [{ n: 0.125, b: false, z: null }])), [{ n: '0.125', b: 'false', z: '' }]);
  for (const value of [Infinity, NaN, {}, [], undefined]) assert.throws(() => formatCsv(['x'], [{ x: value }]));
});
test('CSV rejects malformed quotes, duplicate/empty headers and wrong widths', () => {
  for (const source of ['', 'a,a\nx,y\n', 'a,\nx,y\n', 'a,b\nx\n', 'a\n"unclosed', 'a\na"b\n', 'a\n"x"garbage\n', 'a\n\nx\n', 'a,b\nx,y,z\n']) assert.throws(() => parseCsv(source), source);
});
test('CSV enforces exact schema and row keys', () => {
  assert.throws(() => parseCsv('a,b\nx,y\n', ['b', 'a']));
  assert.throws(() => formatCsv(['a'], [{ b: 'x' }]));
  assert.throws(() => formatCsv(['a'], [{ a: 'x', extra: 'y' }]));
  assert.throws(() => formatCsv(['a', 'a'], []));
  assert.throws(() => formatCsv([''], []));
});
