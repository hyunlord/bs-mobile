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

test('all-zero allocation counter is unavailable while collection counts remain measured', t => {
  const f = fixture(t);
  for (let repetition = 1; repetition <= 3; repetition++) mutateFrames(path.join(f.root, 'after', `run-${repetition}`), lines => lines.map((line, i) => {
    if (!i) return line; const cells = line.split(','); cells[10] = '"0"'; return cells.join(',');
  }));
  const result = f.compare().after;
  assert.equal(result.allFrames.allocationMeasurement, 'unavailable/all-zero-counter');
  assert.equal(result.allFrames.allocatedBytesPerFrame, null);
  assert.equal(result.allFrames.gcCollections, 9);
  for (const bin of result.bins) {
    assert.equal(bin.allocatedBytesPerFrame, null);
    assert.equal(bin.allocationMeasurement, 'unavailable/all-zero-counter');
    for (const repeat of bin.repetitions) assert.equal(repeat.allocatedBytesPerFrame, null);
  }
});

test('pooled allocation is unavailable if any contributing repetition counter is all zero', t => {
  const f = fixture(t);
  mutateFrames(path.join(f.root, 'after/run-2'), lines => lines.map((line, i) => {
    if (!i) return line; const cells = line.split(','); cells[10] = '"0"'; return cells.join(',');
  }));
  mutateFrames(f.run, lines => lines.map((line, i) => {
    if (i !== 1) return line; const cells = line.split(','); cells[10] = '"0"'; return cells.join(',');
  }));
  const result = f.compare().after;
  assert.equal(result.allFrames.allocationMeasurement, 'unavailable/mixed-counter-support');
  assert.equal(result.allFrames.allocatedBytesPerFrame, null);
  assert.equal(result.allFrames.gcCollections, 9);
  for (const bin of result.bins) {
    assert.equal(bin.allocationMeasurement, 'unavailable/mixed-counter-support');
    assert.equal(bin.allocatedBytesPerFrame, null);
  }
  const supported = result.bins[0].repetitions[0];
  assert.equal(supported.allocationMeasurement, 'available');
  assert.equal(supported.allocatedBytesPerFrame.samples, 20);
  assert.equal(supported.allocatedBytesPerFrame.mean, 121.6);
  assert.equal(result.bins[0].repetitions[1].allocatedBytesPerFrame, null);
});

test('accepts documented appended diagnostics while preserving base statistics', t => {
  const f = fixture(t);
  mutateFrames(f.run, lines => lines.map((line, index) => line + (index === 0
    ? ',advances,advanceMs,worldBatchDrawCalls,worldInstances,entryTimestamp,exitTimestamp,nextEntryTimestamp,inputDeltaSeconds,accumulatorBefore,accumulatorAfter'
    : `,0,0,32,1409,${index * 1000},${index * 1000 + 10},${index * 1000 + 20},0.016,0,0`)));
  const timing = path.join(f.run, 'render-timings.csv');
  fs.writeFileSync(timing, fs.readFileSync(timing, 'utf8').trimEnd().split('\n').map((line, i) => `${line},${i === 0 ? 'observationTimestamp' : i * 200000}`).join('\n') + '\n');
  const result = f.compare().after;
  assert.equal(result.bins[0].wallMs.p95, 19);
  assert.equal(result.renderTimings.gpuMs.mean, 4);
});

const current = ',advances,advanceMs,worldBatchDrawCalls,worldInstances,entryTimestamp,exitTimestamp,nextEntryTimestamp,inputDeltaSeconds,accumulatorBefore,accumulatorAfter,partialBoundary,originalWallMs,excludedBackgroundMs';
test('accepts the current harness columns (#321) when no background time was excluded', t => {
  const f = fixture(t);
  mutateFrames(f.run, lines => lines.map((line, index) => line + (index === 0 ? current : `,0,0,32,1409,${index * 1000},${index * 1000 + 10},${index * 1000 + 20},0.016,0,0,0,0,0`)));
  mutateJson(path.join(f.run, 'benchmark.json'), value => { value.focusEvents = []; value.excludedIntervals = []; });
  assert.equal(f.compare().after.bins[0].wallMs.p95, 19);
});

for (const [name, row, identity] of [
  ['a resume boundary row', ',1,0,0', null], ['excluded background time', ',0,25,5', null],
  ['a recorded focus event', ',0,0,0', value => { value.focusEvents = [{ focus: false }]; }],
  ['an excluded interval', ',0,0,0', value => { value.excludedIntervals = [{ reason: 'background' }]; }]
]) test(`rejects a run with ${name}`, t => {
  const f = fixture(t);
  mutateFrames(f.run, lines => lines.map((line, index) => line + (index === 0 ? current : `,0,0,32,1409,${index * 1000},${index * 1000 + 10},${index * 1000 + 20},0.016,0,0${index === 2 ? row : ',0,0,0'}`)));
  mutateJson(path.join(f.run, 'benchmark.json'), value => { value.focusEvents = []; value.excludedIntervals = []; identity?.(value); });
  assert.throws(() => f.compare(), /run is invalid/);
});

test('explicit minus-one allocation support remains unavailable, never averaged as negative bytes', t => {
  const f = fixture(t);
  mutateFrames(f.run, lines => lines.map((line, i) => {
    if (i !== 1) return line;
    const cells = line.split(','); cells[10] = '-1'; return cells.join(',');
  }));
  const result = f.compare().after;
  assert.equal(result.allFrames.allocatedBytesPerFrame, null);
  assert.equal(result.bins[0].repetitions[0].allocationMeasurement, 'unavailable/sentinel');
  assert.equal(result.allFrames.gcCollections, 9);
});

for (const [name, column, value] of [['other negative allocation', 10, '-2'], ['negative wall', 4, '-1'], ['fractional allocation', 10, '-0.5']]) {
  test(`rejects ${name}`, t => {
    const f = fixture(t);
    mutateFrames(f.run, lines => lines.map((line, i) => {
      if (i !== 1) return line;
      const cells = line.split(','); cells[column] = value; return cells.join(',');
    }));
    assert.throws(f.compare, /Invalid/);
  });
}

for (const [header, value] of [['unknownColumn', '1'], ['advances', 'NaN'], ['advances', '-1'], ['advances', '0.5']]) {
  test(`rejects invalid appended ${header}=${value}`, t => {
    const f = fixture(t);
    mutateFrames(f.run, lines => lines.map((line, i) => `${line},${i === 0 ? header : value}`));
    assert.throws(f.compare, /schema mismatch|Invalid|Noninteger/);
  });
}

for (const [key, value] of [['diagnosticOnly', true], ['performanceEligible', false], ['diagnosticOnly', 'false'], ['performanceEligible', null]]) {
  test(`rejects non-acceptance metadata ${key}=${value}`, t => {
    const f = fixture(t);
    mutateJson(path.join(f.run, 'benchmark.json'), identity => { identity[key] = value; });
    assert.throws(f.compare, /Diagnostic-only|Ineligible/);
  });
}

test('accepts explicit eligibility without requiring it for historical runs', t => {
  const f = fixture(t);
  mutateJson(path.join(f.run, 'benchmark.json'), identity => { identity.diagnosticOnly = false; identity.performanceEligible = true; });
  assert.equal(f.compare().after.bins[0].wallMs.p95, 19);
});
