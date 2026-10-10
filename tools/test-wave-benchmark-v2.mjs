import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { formatCsv, parseCsv } from './csv.mjs';
import { frameHeaders, timingHeaders } from './summarize-wave-benchmark.mjs';
import { summarizeWaveBenchmarkV2 } from './summarize-wave-benchmark-v2.mjs';

const contract = JSON.parse(fs.readFileSync(new URL('../benchmarks/wave-c05/contract-v2.json', import.meta.url)));
function fixture(t, mode = 'capped', samples = 20) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'wave-v2-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const variant of ['current', 'reference']) for (let repetition = 1; repetition <= 3; repetition++) {
    const dir = path.join(root, variant, `run-${repetition}`); fs.mkdirSync(dir, { recursive: true });
    const rows = contract.windows.flatMap(([start, end], window) => Array.from({ length: samples }, (_, i) => ({
      frame: window * (samples + 1) + i, tick: i === samples - 1 ? end : start + i,
      enemies: [450, 650, 850][window], commands: i === 0 ? 0 : i === samples - 1 ? 3 : 1,
      wallMs: 10, simulationMs: 1, snapshotMs: .5, acceptMs: .1, presentMs: 2, hudMs: .1,
      allocatedBytes: 128, gcCollections: 0, focused: 1
    })));
    const identity = { commit: (variant === 'current' ? 'a' : 'b').repeat(40), sourceHash: (variant === 'current' ? 'c' : 'd').repeat(64),
      sourceDirty: false, verified: true, interrupted: false, developmentBuild: false, platform: 'OSXPlayer',
      dataHash: contract.dataHash, replaySha256: contract.replaySha256, stateHash: contract.endHash, expectedStateHash: contract.endHash,
      error: '', tick: 14400, commands: 14403, width: 1600, height: 900, targetFrameRate: mode === 'uncapped' ? -1 : 60, vSyncCount: 0,
      renderWarmupFrames: 120, windows: contract.windows.map(w => w.join('-')).join(','), graphicsApi: 'Metal', quality: 'Ultra',
      frameTimingEnabled: true, frames: rows.length, renderTimingSamples: 0, benchmarkMode: mode, profilerEnabled: false, gcMode: 'Enabled' };
    const device = { model: 'Mac16,5', os: 'Mac OS X 27', unityVersion: '6000.6.4f1', backend: 'Mono', graphics: 'Metal', sourceHash: identity.sourceHash, sourceDirty: false };
    fs.writeFileSync(path.join(dir, 'benchmark.json'), JSON.stringify(identity));
    fs.writeFileSync(path.join(dir, 'device.json'), JSON.stringify(device));
    fs.writeFileSync(path.join(dir, 'frames.csv'), formatCsv(frameHeaders, rows));
    fs.writeFileSync(path.join(dir, 'render-timings.csv'), formatCsv(timingHeaders, []));
  }
  const current = path.join(root, 'current'), reference = path.join(root, 'reference');
  return { root, current, reference, run: path.join(current, 'run-1'),
    summarize: options => summarizeWaveBenchmarkV2(current, { mode, ...options }) };
}
function editJson(dir, update, file = 'benchmark.json') {
  const target = path.join(dir, file); const value = JSON.parse(fs.readFileSync(target)); update(value);
  fs.writeFileSync(target, JSON.stringify(value));
}
function editFrames(dir, update) {
  const target = path.join(dir, 'frames.csv');
  const rows = parseCsv(fs.readFileSync(target, 'utf8'), frameHeaders).map(row => Object.fromEntries(Object.entries(row).map(([key, value]) => [key, Number(value)])));
  update(rows); fs.writeFileSync(target, formatCsv(frameHeaders, rows));
}
function eachRun(root, operation) { for (let i = 1; i <= 3; i++) operation(path.join(root, `run-${i}`)); }

test('uncapped baseline is trend only even with arbitrarily slow frames', t => {
  const f = fixture(t, 'uncapped'); eachRun(f.current, run => editFrames(run, rows => rows.forEach(row => { row.wallMs = 150; })));
  const result = f.summarize(); assert.equal(result.status, 'baseline');
  assert.equal(result.measured.status, 'trend-only'); assert.equal(result.trend[0].p95, 150);
  assert.equal(result.measured.bins[0].zeroCommandFrames, 3); assert.equal(result.measured.bins[0].multiCommandFrames, 3);
});
test('regression warning is strictly above 20% pooled per-bin p95', t => {
  const f = fixture(t, 'uncapped');
  eachRun(f.current, run => editFrames(run, rows => rows.forEach(row => { row.wallMs = 12; })));
  assert.equal(f.summarize({ referenceRoot: f.reference }).status, 'within-trend');
  eachRun(f.current, run => editFrames(run, rows => rows.forEach(row => { row.wallMs = row.enemies === 850 ? 12.001 : 12; })));
  const result = f.summarize({ referenceRoot: f.reference }); assert.equal(result.status, 'warning');
  assert.deepEqual(result.trend.map(bin => bin.status), ['within-trend', 'within-trend', 'warning']);
});
test('33ms and 100ms exact boundaries; 1/1000 over 33ms passes, 2/1000 fails', t => {
  const f = fixture(t, 'capped', 1000);
  eachRun(f.current, run => editFrames(run, rows => rows.forEach((row, i) => { row.wallMs = i % 1000 === 0 ? 100 : 33; })));
  let result = f.summarize(); assert.equal(result.status, 'pass');
  assert.equal(result.measured.bins[0].repetitions[0].over33msRatio, .001);
  assert.equal(result.measured.bins[0].repetitions[0].over100ms, 0);
  editFrames(f.run, rows => { rows[1].wallMs = 33.001; });
  result = f.summarize(); assert.equal(result.status, 'fail');
  assert.equal(result.measured.bins[0].repetitions[0].over33ms, 2);
});
test('one frame strictly over 100ms fails even when over33 ratio passes', t => {
  const f = fixture(t, 'capped', 1000); editFrames(f.run, rows => { rows[0].wallMs = 100.001; });
  const result = f.summarize(); assert.equal(result.status, 'fail');
  assert.equal(result.measured.bins[0].repetitions[0].over33msRatio, .001);
  assert.equal(result.measured.bins[0].repetitions[0].over100ms, 1);
});
test('one missing density repetition remains incomplete despite pooled samples', t => {
  const f = fixture(t); editFrames(f.run, rows => rows.forEach(row => { if (row.enemies === 850) row.enemies = 650; }));
  assert.equal(f.summarize().status, 'incomplete');
});
test('reference missing density remains incomplete', t => {
  const f = fixture(t, 'uncapped'); editFrames(path.join(f.reference, 'run-2'), rows => rows.forEach(row => { if (row.enemies === 850) row.enemies = 650; }));
  assert.equal(f.summarize({ referenceRoot: f.reference }).status, 'incomplete');
});
test('missing repetition is rejected', t => {
  const f = fixture(t); fs.rmSync(path.join(f.current, 'run-3'), { recursive: true }); assert.throws(f.summarize);
});
for (const [label, edit] of [
  ['development', x => { x.developmentBuild = true; }], ['profiler enabled', x => { x.profilerEnabled = true; }],
  ['missing profiler state', x => { delete x.profilerEnabled; }], ['interruption', x => { x.interrupted = true; }],
  ['dirty source', x => { x.sourceDirty = true; }], ['wrong native platform', x => { x.platform = 'OSXEditor'; }],
  ['wrong mode', x => { x.benchmarkMode = 'uncapped'; }], ['wrong cap', x => { x.targetFrameRate = -1; }],
  ['vSync', x => { x.vSyncCount = 1; }], ['missing GC mode', x => { delete x.gcMode; }],
  ['mixed GC settings', x => { x.gcMode = 'Disabled'; }], ['mixed source', x => { x.commit = 'e'.repeat(40); }],
  ['mixed source hash', x => { x.sourceHash = 'e'.repeat(64); }], ['wrong end hash', x => { x.stateHash = 'e'.repeat(64); }]
]) test(`rejects ${label}`, t => { const f = fixture(t); editJson(f.run, edit); assert.throws(f.summarize); });
for (const key of ['quality', 'gcMode', 'frameTimingEnabled']) test(`reference ${key} mismatch rejected`, t => {
  const f = fixture(t, 'uncapped'); eachRun(f.reference, run => editJson(run, x => { x[key] = key === 'frameTimingEnabled' ? false : 'Different'; }));
  assert.throws(() => f.summarize({ referenceRoot: f.reference }), /Different reference config/);
});
test('reference hardware mismatch rejected', t => {
  const f = fixture(t, 'uncapped'); eachRun(f.reference, run => editJson(run, x => { x.model = 'Mac99,1'; }, 'device.json'));
  assert.throws(() => f.summarize({ referenceRoot: f.reference }), /Different reference device/);
});
test('explicit historical capped records are labeled; missing metadata never silently accepted', t => {
  const f = fixture(t); eachRun(f.current, run => editJson(run, x => { delete x.benchmarkMode; delete x.profilerEnabled; delete x.gcMode; }));
  assert.throws(f.summarize); const result = f.summarize({ historicalCapped: true });
  assert.equal(result.status, 'pass'); assert.equal(result.measured.historical, true);
  assert.equal(result.measured.config.gcMode, null); assert.equal(result.measured.profilerMetadata, 'unrecorded/historical-opt-in');
});
test('historical flag cannot excuse partial metadata or an enabled profiler', t => {
  const f = fixture(t); eachRun(f.current, run => editJson(run, x => { delete x.benchmarkMode; delete x.gcMode; x.profilerEnabled = true; }));
  assert.throws(() => f.summarize({ historicalCapped: true }));
});
test('rejects invalid mode and mode-option combinations', t => {
  const f = fixture(t); assert.throws(() => f.summarize({ mode: 'other' }));
  assert.throws(() => f.summarize({ mode: 'uncapped', historicalCapped: true }));
  assert.throws(() => f.summarize({ referenceRoot: f.reference }));
});
test('zero-command and catch-up slow frames both contribute to capped failure', t => {
  const f = fixture(t); editFrames(f.run, rows => { rows[0].wallMs = 150; rows[19].wallMs = 150; });
  const repeat = f.summarize().measured.bins[0].repetitions[0];
  assert.equal(repeat.over100ms, 2); assert.equal(repeat.status, 'fail');
});
