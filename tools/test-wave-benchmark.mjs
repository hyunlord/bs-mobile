import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { formatCsv } from './csv.mjs';
import { compareWaveBenchmarks, frameHeaders, timingHeaders } from './summarize-wave-benchmark.mjs';

const contract = JSON.parse(fs.readFileSync(new URL('../benchmarks/wave-c05/contract.json', import.meta.url), 'utf8'));
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'wave-benchmark-test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const variant of ['before', 'after']) for (let repetition = 1; repetition <= 3; repetition++) {
    const dir = path.join(root, variant, `run-${repetition}`); fs.mkdirSync(dir, { recursive: true });
    const rows = [];
    for (const [window, [start, end]] of contract.windows.entries()) for (let i = 0; i < 20; i++) {
      rows.push({ frame: window * 100 + i, tick: i === 19 ? end : start + i,
        enemies: [450, 650, 850][window], commands: i === 0 ? 0 : i === 19 ? 3 : 1,
        wallMs: i + 1, simulationMs: .5, snapshotMs: .2, acceptMs: .3, presentMs: .7, hudMs: .1,
        allocatedBytes: 128, gcCollections: i === 19 ? 1 : 0, focused: 1 });
    }
    const identity = { commit: (variant === 'before' ? 'a' : 'b').repeat(40), sourceHash: (variant === 'before' ? 'c' : 'd').repeat(64),
      sourceDirty: false, verified: true, interrupted: false, developmentBuild: false, platform: 'OSXPlayer',
      dataHash: contract.dataHash, replaySha256: contract.replaySha256, stateHash: contract.endHash, expectedStateHash: contract.endHash,
      error: '', tick: 14400, commands: 14403, width: 1600, height: 900, targetFrameRate: 60, vSyncCount: 0,
      renderWarmupFrames: 120, windows: contract.windows.map(w => w.join('-')).join(','),
      graphicsApi: 'Metal', quality: 'High', frameTimingEnabled: true, frames: rows.length, renderTimingSamples: 2 };
    const device = { model: 'Mac16,5', os: 'Mac OS X 26', unityVersion: '6000.6.4f1', backend: 'Mono', graphics: 'Metal', sourceHash: identity.sourceHash, sourceDirty: false };
    const timings = [1, 2].map(i => ({ observedFrame: i, frameStartTimestamp: i * 100000, cpuFrameMs: 16, cpuMainThreadMs: 5, cpuRenderThreadMs: 2, cpuPresentWaitMs: 9, gpuMs: i === 1 ? 0 : 4 }));
    fs.writeFileSync(path.join(dir, 'benchmark.json'), JSON.stringify(identity));
    fs.writeFileSync(path.join(dir, 'device.json'), JSON.stringify(device));
    fs.writeFileSync(path.join(dir, 'frames.csv'), formatCsv(frameHeaders, rows));
    fs.writeFileSync(path.join(dir, 'render-timings.csv'), formatCsv(timingHeaders, timings));
  }
  return { root, run: path.join(root, 'after/run-1'), compare: () => compareWaveBenchmarks(path.join(root, 'before'), path.join(root, 'after')) };
}
function mutateJson(file, update) { const value = JSON.parse(fs.readFileSync(file, 'utf8')); update(value); fs.writeFileSync(file, JSON.stringify(value)); }
function mutateFrames(run, update) {
  const file = path.join(run, 'frames.csv'); const lines = fs.readFileSync(file, 'utf8').trimEnd().split('\n');
  fs.writeFileSync(file, `${update(lines).join('\n')}\n`);
}
test('nearest-rank p95 includes zero-command and catch-up frames; costs remain separate', t => {
  const result = fixture(t).compare();
  assert.equal(result.after.bins[0].wallMs.p95, 19); assert.equal(result.after.bins[0].wallMs.max, 20);
  assert.equal(result.after.bins[0].wallMs.mean, 10.5); assert.equal(result.after.bins[0].frames, 60);
  assert.equal(result.after.bins[0].zeroCommandFrames, 3); assert.equal(result.after.bins[0].multiCommandFrames, 3);
  assert.equal(result.after.bins[0].allocatedBytesPerFrame.mean, 128); assert.equal(result.after.bins[0].gcCollections, 3);
  assert.equal(result.after.bins[0].simulationMs.p95, .5); assert.equal(result.after.status, 'fail');
  assert.equal(result.after.renderTimings.gpuMs.samples, 3); assert.equal(result.after.renderTimings.gpuMs.mean, 4);
});
test('missing density in one repeat is incomplete even if pooled density exists', t => {
  const f = fixture(t);
  mutateFrames(f.run, lines => lines.map(line => line.replaceAll('"850"', '"650"')));
  assert.equal(f.compare().after.bins[2].status, 'incomplete'); assert.equal(f.compare().after.status, 'incomplete');
});
test('all repetitions within target pass', t => {
  const f = fixture(t);
  for (let repetition = 1; repetition <= 3; repetition++) mutateFrames(path.join(f.root, 'after', `run-${repetition}`), lines => lines.map((line, i) => {
    if (!i) return line; const cells = line.split(','); cells[4] = '"16"'; return cells.join(',');
  }));
  assert.equal(f.compare().after.status, 'pass');
});
for (const [name, change] of [
  ['interrupted', x => { x.interrupted = true; }], ['missing interruption status', x => { delete x.interrupted; }],
  ['unverified', x => { x.verified = false; }], ['development', x => { x.developmentBuild = true; }],
  ['dirty source', x => { x.sourceDirty = true; }], ['wrong replay', x => { x.replaySha256 = 'e'.repeat(64); }],
  ['wrong end hash', x => { x.stateHash = 'e'.repeat(64); }], ['wrong data', x => { x.dataHash = 'e'.repeat(64); }],
  ['wrong viewport', x => { x.width = 900; }], ['wrong graphics quality', x => { x.quality = 'Low'; }],
  ['truncated frame count', x => { x.frames++; }], ['editor platform', x => { x.platform = 'OSXEditor'; }],
]) test(`rejects ${name}`, t => {
  const f = fixture(t); mutateJson(path.join(f.run, 'benchmark.json'), change); assert.throws(f.compare);
});
test('rejects reordered or missing frame within a window', t => {
  const f = fixture(t); mutateFrames(f.run, lines => lines.filter((_, index) => index !== 2));
  mutateJson(path.join(f.run, 'benchmark.json'), x => { x.frames--; }); assert.throws(f.compare, /Dropped frame/);
});
test('rejects nonfinite and unfocused frame samples', t => {
  const f = fixture(t); mutateFrames(f.run, lines => lines.map((line, i) => i === 1 ? line.replace('"0.5"', '"NaN"') : line));
  assert.throws(f.compare, /Invalid simulationMs/);
});
test('rejects unfocused frames rather than filtering them', t => {
  const f = fixture(t); mutateFrames(f.run, lines => lines.map((line, i) => i === 1 ? line.replace(/"1"$/, '"0"') : line));
  assert.throws(f.compare, /Unfocused/);
});
