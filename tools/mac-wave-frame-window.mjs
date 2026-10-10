import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';
import { headers } from './device-metrics.mjs';

const eligible = row => row.speed === 1 && !row.paused && !row.suspended && !row.partial;
const integerFields = 'frame tick speed paused suspended partial enemies projectiles visualProjectiles people farms buildings'.split(' ');
const thermalStates = new Set(['unavailable', 'none', 'light', 'moderate', 'severe', 'critical', 'emergency', 'shutdown']);
function summarize(rows, predicate = () => true) {
  const selected = [], segments = [];
  let segment = null;
  for (const row of rows) {
    if (!eligible(row) || !predicate(row)) { segment = null; continue; }
    selected.push(row);
    if (segment === null) {
      segment = { startFrame: row.frame, endFrame: row.frame, startTick: row.tick, endTick: row.tick, durationMs: 0, frames: 0 };
      segments.push(segment);
    }
    segment.endFrame = row.frame; segment.endTick = row.tick; segment.durationMs += row.deltaMs; segment.frames++;
  }
  const times = selected.map(row => row.deltaMs).sort((a, b) => a - b);
  const p95Ms = times.length ? times[Math.ceil(times.length * .95) - 1] : null;
  const enemyMin = selected.reduce((min, row) => Math.min(min, row.enemies), Infinity);
  return {
    samples: times.length, durationMs: times.reduce((sum, value) => sum + value, 0),
    p95Ms, maxMs: times.at(-1) ?? null, p95WithinTarget: p95Ms === null ? null : p95Ms <= 16.7,
    enemyMin: Number.isFinite(enemyMin) ? enemyMin : null,
    enemyMax: selected.length ? selected.reduce((max, row) => Math.max(max, row.enemies), 0) : null,
    longestContiguousMs: segments.reduce((max, value) => Math.max(max, value.durationMs), 0), segments,
  };
}
export function analyzeMacWave(directory, { commit }) {
  const read = name => JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8'));
  const identity = read('recording.json'), facts = read('device.json'), summary = read('frame-summary.json');
  assert.match(commit ?? '', /^[a-f\d]{40}$/i, 'Expected full commit SHA');
  assert.equal(identity.build, commit, 'Build identity mismatch');
  for (const key of ['sessionId', 'build', 'dataHash']) assert.equal(identity[key], summary[key], `Summary ${key} mismatch`);
  assert.ok(typeof identity.sessionId === 'string' && identity.sessionId.length > 0);
  assert.ok(Number.isSafeInteger(identity.seed) && identity.seed >= 0);
  assert.match(identity.dataHash, /^[a-f\d]{64}$/i);
  assert.match(identity.sourceHash, /^[a-f\d]{64}$/i);
  assert.equal(typeof identity.sourceDirty, 'boolean');
  for (const key of ['sourceHash', 'sourceDirty']) assert.equal(identity[key], facts[key], `Source ${key} mismatch`);
  for (const key of ['model', 'os', 'unityVersion', 'backend']) assert.ok(typeof facts[key] === 'string' && facts[key].length > 0, `Missing ${key}`);
  assert.match(facts.model, /^Mac/); assert.match(facts.os, /^Mac OS X /); assert.equal(facts.backend, 'Mono');
  assert.equal(summary.durationTicks, 27000); assert.equal(summary.lateStartTick, 20250);
  const rows = parseCsv(fs.readFileSync(path.join(directory, 'frames.csv'), 'utf8'), headers);
  assert.ok(rows.length > 0, 'Empty frame samples'); assert.equal(rows.length, summary.frames, 'Incomplete frame CSV');
  let previousTick = -1;
  for (const [index, row] of rows.entries()) {
    for (const key of headers.filter(key => key !== 'thermal')) {
      assert.ok(row[key].trim() && Number.isFinite(Number(row[key])) && Number(row[key]) >= 0, `Invalid numeric ${key}`);
      row[key] = Number(row[key]);
    }
    for (const key of integerFields) assert.ok(Number.isSafeInteger(row[key]), `Noninteger ${key}`);
    assert.equal(row.frame, index, 'Noncontiguous frame');
    assert.ok(row.tick >= previousTick && row.tick <= summary.durationTicks, 'Invalid tick sequence'); previousTick = row.tick;
    assert.ok([1, 2, 4].includes(row.speed));
    for (const key of ['paused', 'suspended', 'partial']) assert.ok([0, 1].includes(row[key]), `Invalid ${key}`);
    assert.ok(thermalStates.has(row.thermal), 'Invalid thermal state');
    assert.ok(row.width > 0 && row.height > 0 && row.safeWidth > 0 && row.safeHeight > 0 && row.safeX + row.safeWidth <= row.width && row.safeY + row.safeHeight <= row.height, 'Invalid screen bounds');
  }
  const lateRows = rows.filter(row => row.tick >= summary.lateStartTick);
  const lateComplete = rows[0].tick <= summary.lateStartTick && rows.at(-1).tick === summary.durationTicks
    && lateRows.some(row => row.tick === summary.durationTicks && eligible(row))
    && lateRows.every(row => !row.suspended && !row.partial && (row.paused || row.speed === 1));
  const dense = summarize(lateRows, row => row.enemies >= 400);
  return {
    schemaVersion: 1, identity, device: facts, profile: 'wave-1a',
    scope: 'Mac Mono frame telemetry diagnostic; profile and profiler-off status require separate source/capture provenance; not Fold7 acceptance',
    targetMs: 16.7, minimumDenseContinuousMs: 60000, lateComplete,
    excludedFrames: rows.filter(row => !eligible(row)).length,
    all: summarize(rows), late: summarize(lateRows), denseLate: dense,
    denseLateTarget: !lateComplete || dense.longestContiguousMs < 60000 ? 'incomplete' : dense.p95WithinTarget ? 'within-target' : 'over-target',
    lateBins: [[400, 599], [600, 799], [800, null]].map(([min, max]) => ({ min, max, ...summarize(lateRows, row => row.enemies >= min && (max === null || row.enemies <= max)) })),
  };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [directory, flag, commit, ...extra] = process.argv.slice(2);
    assert.ok(directory && flag === '--commit' && extra.length === 0, 'Usage: mac-wave-frame-window.mjs RUN_DIRECTORY --commit SHA');
    console.log(JSON.stringify(analyzeMacWave(directory, { commit }), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
