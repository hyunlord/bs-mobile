import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';

export const frameHeaders = 'frame tick enemies commands wallMs simulationMs snapshotMs acceptMs presentMs hudMs allocatedBytes gcCollections focused'.split(' ');
export const timingHeaders = 'observedFrame frameStartTimestamp cpuFrameMs cpuMainThreadMs cpuRenderThreadMs cpuPresentWaitMs gpuMs'.split(' ');
const cpuFields = 'wallMs simulationMs snapshotMs acceptMs presentMs hudMs'.split(' ');
const configFields = 'width height targetFrameRate vSyncCount renderWarmupFrames windows graphicsApi quality frameTimingEnabled platform'.split(' ');
const deviceFields = 'model os unityVersion backend graphics'.split(' ');
const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const defaultContract = new URL('../benchmarks/wave-c05/contract.json', import.meta.url);
function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  return { samples: sorted.length, mean: sorted.length ? sorted.reduce((a, b) => a + b, 0) / sorted.length : null,
    p95: sorted.length ? sorted[Math.ceil(sorted.length * .95) - 1] : null, max: sorted.at(-1) ?? null };
}
function summarize(rows) {
  const allocationAvailable = rows.some(row => row.allocatedBytes > 0);
  return { frames: rows.length, zeroCommandFrames: rows.filter(r => r.commands === 0).length,
    multiCommandFrames: rows.filter(r => r.commands > 1).length,
    ...Object.fromEntries(cpuFields.map(key => [key, stats(rows.map(row => row[key]))])),
    allocationMeasurement: allocationAvailable ? 'available' : rows.length ? 'unavailable/all-zero-counter' : 'unavailable/no-samples',
    allocatedBytesPerFrame: allocationAvailable ? stats(rows.map(row => row.allocatedBytes)) : null,
    gcCollections: rows.reduce((sum, row) => sum + row.gcCollections, 0) };
}
function summarizePooled(repetitions) {
  const summary = summarize(repetitions.flat());
  if (summary.allocatedBytesPerFrame !== null && repetitions.some(rows => rows.length > 0 && !rows.some(row => row.allocatedBytes > 0))) {
    summary.allocationMeasurement = 'unavailable/mixed-counter-support';
    summary.allocatedBytesPerFrame = null;
  }
  return summary;
}
function parseNumbers(file, headers) {
  return parseCsv(fs.readFileSync(file, 'utf8'), headers).map(row => Object.fromEntries(headers.map(key => {
    assert.ok(row[key].trim() && Number.isFinite(Number(row[key])) && Number(row[key]) >= 0, `Invalid ${key} in ${file}`);
    return [key, Number(row[key])];
  })));
}
function loadRun(directory, contract) {
  const identity = readJson(path.join(directory, 'benchmark.json'));
  const device = readJson(path.join(directory, 'device.json'));
  assert.equal(identity.verified, true, 'Replay was not verified');
  assert.equal(identity.interrupted, false, 'Benchmark was interrupted');
  assert.ok(!identity.error, 'Benchmark reported an error');
  assert.equal(identity.developmentBuild, false, 'Development build is not acceptance evidence');
  assert.equal(identity.sourceDirty, false, 'Dirty source is not a repeatable benchmark');
  assert.equal(identity.platform, 'OSXPlayer', 'Native Mac player required');
  for (const [key, length] of [['commit', 40], ['sourceHash', 64], ['dataHash', 64], ['replaySha256', 64], ['stateHash', 64], ['expectedStateHash', 64]]) {
    assert.ok(typeof identity[key] === 'string' && new RegExp(`^[a-f0-9]{${length}}$`, 'i').test(identity[key]), `Invalid ${key}`);
  }
  for (const [key, value] of Object.entries({ replaySha256: contract.replaySha256, dataHash: contract.dataHash, stateHash: contract.endHash, expectedStateHash: contract.endHash })) {
    assert.equal(identity[key].toUpperCase(), value.toUpperCase(), `${key} differs from frozen fixture`);
  }
  assert.equal(identity.tick, contract.endTick);
  assert.equal(identity.commands, 14403, 'Incomplete original C05 command stream');
  assert.equal(identity.width, contract.viewport.width); assert.equal(identity.height, contract.viewport.height);
  assert.equal(identity.targetFrameRate, contract.targetFrameRate); assert.equal(identity.vSyncCount, 0);
  assert.equal(identity.renderWarmupFrames, 120);
  assert.equal(identity.windows, contract.windows.map(w => w.join('-')).join(','));
  assert.equal(typeof identity.frameTimingEnabled, 'boolean');
  for (const key of ['quality', 'graphicsApi']) assert.ok(typeof identity[key] === 'string' && identity[key].length > 0, `Missing ${key}`);
  assert.equal(device.sourceHash, identity.sourceHash); assert.equal(device.sourceDirty, false);
  for (const key of deviceFields) assert.ok(typeof device[key] === 'string' && device[key].length > 0, `Missing device ${key}`);
  assert.match(device.model, /^Mac/); assert.match(device.os, /^Mac OS X /); assert.equal(device.backend, 'Mono');
  assert.equal(device.graphics, identity.graphicsApi);
  const rows = parseNumbers(path.join(directory, 'frames.csv'), frameHeaders);
  assert.ok(rows.length > 0, 'No frame samples'); assert.equal(rows.length, identity.frames, 'Truncated frame CSV');
  let previous;
  for (const row of rows) {
    for (const key of ['frame', 'tick', 'enemies', 'commands', 'allocatedBytes', 'gcCollections', 'focused']) assert.ok(Number.isSafeInteger(row[key]), `Noninteger ${key}`);
    assert.equal(row.focused, 1, 'Unfocused benchmark frame'); assert.ok(row.wallMs > 0, 'Empty frame duration');
    const window = contract.windows.findIndex(([start, end]) => row.tick >= start && row.tick <= end);
    assert.ok(window >= 0, 'Frame outside fixed measurement windows');
    if (previous) {
      assert.ok(row.frame > previous.frame && row.tick >= previous.tick, 'Frame/tick order');
      const priorWindow = contract.windows.findIndex(([start, end]) => previous.tick >= start && previous.tick <= end);
      if (window === priorWindow) assert.equal(row.frame, previous.frame + 1, 'Dropped frame within measurement window');
    }
    previous = row;
  }
  assert.equal(rows.at(-1).tick, contract.endTick, 'Missing terminal rendered state');
  for (const [start, end] of contract.windows) assert.ok(rows.some(r => r.tick >= start && r.tick <= end), 'Missing measurement window');
  const timings = parseNumbers(path.join(directory, 'render-timings.csv'), timingHeaders);
  assert.equal(timings.length, identity.renderTimingSamples, 'Truncated render timing CSV');
  let priorTiming;
  for (const row of timings) {
    assert.ok(Number.isSafeInteger(row.observedFrame), 'Invalid timing observation frame');
    if (priorTiming) assert.ok(row.observedFrame > priorTiming.observedFrame && row.frameStartTimestamp > priorTiming.frameStartTimestamp, 'Duplicate/reordered render timing');
    priorTiming = row;
  }
  return { identity, device, rows, timings };
}
function summarizeVariant(runs, contract) {
  const bins = contract.densityBins.map(([min, max]) => {
    const contains = row => row.enemies >= min && (max === null || row.enemies <= max);
    const selected = runs.map(run => run.rows.filter(contains));
    const repetitions = selected.map((rows, index) => ({ repetition: index + 1, ...summarize(rows) }));
    const complete = repetitions.every(run => run.frames > 0);
    return { min, max, ...summarizePooled(selected), repetitions,
      status: !complete ? 'incomplete' : repetitions.every(run => run.wallMs.p95 <= contract.p95LimitMilliseconds) ? 'pass' : 'fail' };
  });
  return { commit: runs[0].identity.commit, sourceHash: runs[0].identity.sourceHash, allFrames: summarizePooled(runs.map(run => run.rows)), bins,
    status: bins.some(bin => bin.status === 'incomplete') ? 'incomplete' : bins.every(bin => bin.status === 'pass') ? 'pass' : 'fail',
    renderTimingScope: 'Delayed observations across warmup and measurement; not joined to density; zero GPU timings mean unavailable, not zero cost.',
    renderTimings: Object.fromEntries(timingHeaders.slice(2).map(key => [key, stats(runs.flatMap(run => run.timings.map(row => row[key]).filter(value => value > 0)))])) };
}
export function compareWaveBenchmarks(beforeRoot, afterRoot, contract = readJson(defaultContract)) {
  const load = root => Array.from({ length: contract.repetitions }, (_, i) => loadRun(path.join(root, `run-${i + 1}`), contract));
  const before = load(beforeRoot), after = load(afterRoot);
  const reference = before[0];
  for (const runs of [before, after]) {
    for (const run of runs) {
      for (const key of ['commit', 'sourceHash']) assert.equal(run.identity[key], runs[0].identity[key], `Different ${key} within variant`);
      for (const key of configFields) assert.equal(run.identity[key], reference.identity[key], `Different config ${key}`);
      for (const key of deviceFields) assert.equal(run.device[key], reference.device[key], `Different device ${key}`);
    }
  }
  return { version: 1, fixture: { replaySha256: contract.replaySha256, dataHash: contract.dataHash, endHash: contract.endHash },
    targetMs: contract.p95LimitMilliseconds, repetitions: contract.repetitions, windows: contract.windows,
    gateRule: 'Every density bin in every repetition must be present and have nearest-rank wall p95 <= target. No slow, zero-command, or catch-up frames excluded.',
    config: Object.fromEntries(configFields.map(key => [key, reference.identity[key]])), device: Object.fromEntries(deviceFields.map(key => [key, reference.device[key]])),
    before: summarizeVariant(before, contract), after: summarizeVariant(after, contract) };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [before, after, ...extra] = process.argv.slice(2);
    assert.ok(before && after && extra.length === 0, 'Usage: summarize-wave-benchmark.mjs BEFORE_ROOT AFTER_ROOT (each contains run-1/run-2/run-3)');
    console.log(JSON.stringify(compareWaveBenchmarks(before, after), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
