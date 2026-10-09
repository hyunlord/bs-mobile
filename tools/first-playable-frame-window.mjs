import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { readDeviceRecording } from './device-metrics.mjs';

const limits = Object.freeze({ durationMs: 60000, minEnemies: 500, p95Ms: 16.7 });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
function keys(value, expected, label) {
  assert.ok(value && typeof value === 'object' && !Array.isArray(value), `${label} must be an object`);
  assert.deepEqual(Object.keys(value).sort(), expected.slice().sort(), `${label} fields mismatch`);
}
function declarationFrom(bytes, preregisteredDigest) {
  assert.match(preregisteredDigest ?? '', /^[a-f\d]{64}$/i, 'Externally preregistered declaration SHA256 is required');
  assert.equal(digest(bytes), preregisteredDigest.toLowerCase(), 'Declaration bytes differ from externally preregistered SHA256');
  const value = JSON.parse(bytes);
  keys(value, ['schemaVersion', 'identity', 'profile', 'mode', 'startTick', 'posture', 'setup', 'capture', 'preregistrationReference'], 'declaration');
  assert.equal(value.schemaVersion, 1); assert.equal(value.profile, 'first-playable');
  assert.ok(['stress', 'normal'].includes(value.mode), 'Unknown declared mode');
  assert.ok(Number.isSafeInteger(value.startTick) && value.startTick >= 0 && value.startTick < 27000, 'Invalid startTick');
  assert.ok(['folded', 'unfolded', 'mixed'].includes(value.posture), 'Observed posture is required');
  for (const name of ['setup', 'preregistrationReference']) assert.ok(typeof value[name] === 'string' && value[name].trim(), `Missing ${name}`);
  keys(value.capture, ['screenRecording', 'replayRecording', 'profilerRecording', 'deepProfiling'], 'capture');
  assert.equal(typeof value.capture.screenRecording, 'boolean', 'Declare screen recording state');
  assert.equal(value.capture.replayRecording, true, 'Replay recording must remain active');
  assert.equal(value.capture.profilerRecording, false, 'Profiler recording must be disabled for acceptance');
  assert.equal(value.capture.deepProfiling, false, 'Deep Profiling must be disabled for acceptance');
  keys(value.identity, ['sessionId', 'seed', 'build', 'dataHash', 'sourceHash', 'sourceDirty', 'model', 'os', 'unityVersion', 'backend'], 'identity');
  assert.match(value.identity.sessionId ?? '', /^[a-zA-Z0-9_-]{1,128}$/, 'Invalid session identity');
  assert.ok(Number.isSafeInteger(value.identity.seed) && value.identity.seed >= 0 && value.identity.seed <= 2147483647, 'Invalid seed identity');
  return value;
}

export function analyzeWindow(directory, declarationFile, preregisteredDigest) {
  const declarationBytes = fs.readFileSync(declarationFile);
  const declaration = declarationFrom(declarationBytes, preregisteredDigest);
  const { identity, facts, summary, rows } = readDeviceRecording(directory, { commit: declaration.identity.build, posture: declaration.posture });
  assert.equal(summary.durationTicks, 27000, 'First Playable duration must be 27000');
  for (const key of ['sessionId', 'seed', 'build', 'dataHash', 'sourceHash', 'sourceDirty'])
    assert.equal(identity[key], declaration.identity[key], `Declared recording identity mismatch: ${key}`);
  for (const key of ['model', 'os', 'unityVersion', 'backend'])
    assert.equal(facts[key], declaration.identity[key], `Declared device identity mismatch: ${key}`);
  assert.match(facts.model, /\bSM-F966N\b/i, 'This acceptance gate requires the actual Fold7 SM-F966N model');
  for (const row of rows) {
    for (const key of ['frame', 'tick', 'enemies']) assert.ok(Number.isSafeInteger(row[key]), `Unsafe integer ${key}`);
    assert.ok(row.deltaMs > 0, 'Rendered intervals must have positive duration');
  }
  const start = rows.findIndex(row => row.tick >= declaration.startTick);
  const selected = []; let durationMs = 0;
  if (start >= 0) {
    for (let index = start; index < rows.length && durationMs < limits.durationMs; index++) {
      const row = rows[index]; selected.push(row); durationMs += row.deltaMs;
    }
  }
  const violations = [];
  for (const row of selected) {
    if (row.enemies < limits.minEnemies) violations.push({ frame: row.frame, reason: 'enemies<500' });
    for (const key of ['paused', 'suspended', 'partial']) if (row[key] !== 0) violations.push({ frame: row.frame, reason: `${key}!=0` });
    if (row.speed !== 1) violations.push({ frame: row.frame, reason: 'speed!=1' });
  }
  if (rows.length && rows[0].tick > declaration.startTick) violations.push({ frame: rows[0].frame, reason: 'capture-starts-after-declared-trigger' });
  if (start < 0) violations.push({ frame: null, reason: 'declared-start-not-reached' });
  if (durationMs < limits.durationMs) violations.push({ frame: null, reason: 'durationMs<60000' });
  const times = selected.map(row => row.deltaMs).sort((a, b) => a - b);
  const p95 = times.length ? times[Math.ceil(times.length * .95) - 1] : null;
  if (p95 !== null && p95 > limits.p95Ms) violations.push({ frame: null, reason: 'p95Ms>16.7' });
  const rawHashes = Object.fromEntries(['recording.json', 'device.json', 'frame-summary.json', 'frames.csv'].map(name => [name, digest(fs.readFileSync(path.join(directory, name)))]));
  return {
    schemaVersion: 1, passed: violations.length === 0, gate: 'First Playable Fold7 sustained500 rendered-frame budget', limits,
    identity: { ...identity, model: facts.model, os: facts.os, unityVersion: facts.unityVersion, backend: facts.backend },
    declarationSha256: digest(declarationBytes), preregistrationReference: declaration.preregistrationReference,
    preregistrationEvidence: 'Exact bytes match caller-supplied externally registered digest; registration timing/reference must be audited externally. JSON timestamps alone are not proof.',
    capture: declaration.capture, captureEvidence: 'declaration-attested; not independently recorded by frames.csv',
    profile: declaration.profile, mode: declaration.mode, setup: declaration.setup, posture: declaration.posture,
    measurementScope: 'Every contiguous recorded interval from the first row with tick>=declared startTick through the full crossing interval at60000ms; population is captured interval snapshot, not subframe tracking. No normal-run/cheat-free, profiler-state or replay-correctness certification.',
    window: { declaredStartTick: declaration.startTick, startFrame: selected[0]?.frame ?? null, endFrame: selected.at(-1)?.frame ?? null,
      startTick: selected[0]?.tick ?? null, endTick: selected.at(-1)?.tick ?? null, durationMs, samples: selected.length,
      minEnemies: selected.length ? selected.reduce((min, row) => Math.min(min, row.enemies), Infinity) : null,
      p95Ms: p95, maxMs: times.at(-1) ?? null },
    thermalStates: [...new Set(selected.map(row => row.thermal))],
    slowFrames: selected.filter(row => row.deltaMs > 50).map(row => ({ ...row })), violations, rawHashes,
  };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [directory, declarationFile, ...args] = process.argv.slice(2), options = {};
    assert.ok(directory && declarationFile && args.length === 4, 'Usage: first-playable-frame-window.mjs RUN_DIRECTORY DECLARATION_JSON --declaration-sha256 PREREGISTERED_SHA --output NEW_FILE');
    for (let i = 0; i < args.length; i += 2) {
      assert.ok(['--declaration-sha256', '--output'].includes(args[i]) && !Object.hasOwn(options, args[i]), 'Unknown/duplicate argument');
      options[args[i]] = args[i + 1];
    }
    assert.ok(options['--output'], '--output is required');
    const result = analyzeWindow(directory, declarationFile, options['--declaration-sha256']);
    fs.writeFileSync(options['--output'], `${JSON.stringify(result, null, 2)}\n`, { flag: 'wx' });
    console.log(`${result.passed ? 'PASS' : 'FAIL'} sustained500: ${result.window.samples} frames / ${result.window.durationMs}ms / p95 ${result.window.p95Ms}ms`);
    if (!result.passed) process.exitCode = 1;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
