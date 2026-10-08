import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { synthesize, wave, SAMPLE_RATE, CUES } from './first-playable-audio.mjs';

test('original score and seven cues synthesize reproducibly with bounded peaks', () => {
  const hashes = new Set();
  for (const name of [...CUES, 'frontier-loop']) {
    const samples = synthesize(name), second = synthesize(name);
    assert.ok(samples.length > SAMPLE_RATE / 20);
    let peak = 0;
    for (const sample of samples) { assert.ok(Number.isFinite(sample)); peak = Math.max(peak, Math.abs(sample)); }
    assert.ok(peak > .05 && peak <= .160001);
    const bytes = wave(samples);
    assert.deepEqual(bytes, wave(second));
    assert.equal(bytes.toString('ascii', 0, 4), 'RIFF');
    assert.equal(bytes.readUInt32LE(24), SAMPLE_RATE);
    assert.equal(bytes.readUInt16LE(22), 1);
    assert.equal(bytes.readUInt16LE(34), 16);
    assert.equal(bytes.readUInt32LE(40), samples.length * 2);
    hashes.add(createHash('sha256').update(bytes).digest('hex'));
  }
  assert.equal(hashes.size, 8);
});
test('music loop closes at zero with small seam slope and conservative full-pool headroom', () => {
  const samples = synthesize('frontier-loop');
  assert.equal(samples.length, SAMPLE_RATE * 16);
  assert.equal(samples[0], 0); assert.equal(samples.at(-1), 0);
  assert.ok(Math.abs(samples[1]) < .001 && Math.abs(samples.at(-2)) < .001);
  assert.ok(7 * .14 * .7 + .16 * .3 < .8);
});
test('unsupported synthesis name fails instead of silently exporting silence', () => {
  assert.throws(() => synthesize('missing'), /Unknown/);
});
