import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const tool = path.join(import.meta.dirname, 'metrics.mjs');
const sample = {
  schemaVersion: 2, timestamp: '2026-10-08T00:00:00Z', commit: 'a'.repeat(40),
  stage: 'Phase1A', model: 'unity-frame', seed: 30000,
  runtime: { unity: '6000.6.4f1', backend: 'IL2CPP' }, config: { window: 'late-1x' },
  measurementScope: 'Synthetic device-metrics test fixture, not device evidence',
  tickP95Ms: null, balanceDispersion: null, frameP95Ms: 15, frameMaxMs: 32,
  device: { model: 'fixture', os: 'fixture-os', screen: '1080x2400', posture: 'folded' },
  sampleCount: 100, lateComplete: true,
};
function fixture(run) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'device-metrics-'));
  const input = path.join(directory, 'input.json');
  const output = path.join(directory, 'report');
  const invoke = value => {
    fs.writeFileSync(input, JSON.stringify(value));
    return spawnSync(process.execPath, [tool, input, output], { encoding: 'utf8', env: { ...process.env, METRICS_SHA: sample.commit } });
  };
  try { run(invoke, output); } finally { fs.rmSync(directory, { recursive: true, force: true }); }
}
test('device frame series remains separate by device and from historical Core ticks', () => fixture((invoke, output) => {
  let result = invoke(sample); assert.equal(result.status, 0, result.stderr);
  result = invoke({ ...sample, device: { ...sample.device, posture: 'unfolded', screen: '2100x2200' } });
  assert.equal(result.status, 0, result.stderr);
  result = invoke({ ...sample, schemaVersion: 1, device: undefined, frameP95Ms: undefined, frameMaxMs: undefined, tickP95Ms: 0.5, stage: 'S0', model: 'scaffold' });
  assert.equal(result.status, 0, result.stderr);
  const html = fs.readFileSync(path.join(output, 'index.html'), 'utf8');
  assert.equal((html.match(/<section /g) ?? []).length, 3);
  assert.match(html, /Frame p95/); assert.match(html, /Tick p95/);
  assert.match(html, /folded/); assert.match(html, /unfolded/); assert.match(html, /Complete late window/);
}));
test('missing late samples stay unmeasured and device strings are escaped', () => fixture((invoke, output) => {
  const result = invoke({ ...sample, sampleCount: 0, lateComplete: false, frameP95Ms: null, frameMaxMs: null, device: { ...sample.device, model: '<script>bad</script>' } });
  assert.equal(result.status, 0, result.stderr);
  const html = fs.readFileSync(path.join(output, 'index.html'), 'utf8');
  assert.match(html, /Not measured/); assert.match(html, /Incomplete late window/);
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>bad/);
}));
test('rejects frame-as-tick, invalid device identity, impossible counts and timing', () => fixture(invoke => {
  for (const patch of [
    { tickP95Ms: 1 }, { device: null }, { device: { ...sample.device, os: '' } },
    { sampleCount: -1 }, { sampleCount: 0 }, { frameP95Ms: -1 }, { frameMaxMs: 10 },
    { frameP95Ms: null }, { lateComplete: 'true' }, { balanceDispersion: 2 },
  ]) {
    const result = invoke({ ...sample, ...patch });
    assert.notEqual(result.status, 0, JSON.stringify(patch));
  }
}));
test('environment commit cannot relabel a recorded device build', () => fixture((invoke, output) => {
  const result = invoke({ ...sample, commit: 'b'.repeat(40) });
  assert.notEqual(result.status, 0, 'Device commit mismatch must fail before publishing');
  assert.equal(fs.existsSync(output), false);
}));
