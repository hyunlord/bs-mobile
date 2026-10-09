import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { formatCsv } from './csv.mjs';
import { headers } from './device-metrics.mjs';
import { analyzeWindow } from './first-playable-frame-window.mjs';
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
function fixture(t, count = 4000) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'fp-window-')); t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const identity = { sessionId: 'sample-session', seed: 30000, build: 'a'.repeat(40), dataHash: 'b'.repeat(64), sourceHash: 'c'.repeat(64), sourceDirty: false };
  const facts = { model: 'samsung SM-F966N', os: 'Android OS 16', unityVersion: '6000.6.4f1', backend: 'IL2CPP', sourceHash: identity.sourceHash, sourceDirty: false };
  const summary = { sessionId: identity.sessionId, build: identity.build, dataHash: identity.dataHash, durationTicks: 27000, lateStartTick: 20250, frames: count };
  const rows = Array.from({ length: count }, (_, frame) => ({ ...Object.fromEntries(headers.map(k => [k, 0])), frame, tick: frame, deltaMs: 16, speed: 1, enemies: 500, width: 900, height: 1200, safeWidth: 900, safeHeight: 1200, thermal: 'none' }));
  const declaration = { schemaVersion: 1, identity: { ...identity, ...facts }, profile: 'first-playable', mode: 'stress', startTick: 10, posture: 'unfolded', setup: 'Declared fixed-seed stress setup; no best-window search.', capture: { screenRecording: false, replayRecording: true, profilerRecording: false, deepProfiling: false }, preregistrationReference: 'external-log:before-window-001' };
  const write = (name, value) => fs.writeFileSync(path.join(dir, name), JSON.stringify(value));
  const flush = () => { write('recording.json', identity); write('device.json', facts); write('frame-summary.json', summary); fs.writeFileSync(path.join(dir, 'frames.csv'), formatCsv(headers, rows)); };
  const run = () => { flush(); const bytes = Buffer.from(JSON.stringify(declaration)); fs.writeFileSync(path.join(dir, 'declaration.json'), bytes); return analyzeWindow(dir, path.join(dir, 'declaration.json'), sha(bytes)); };
  return { dir, rows, identity, facts, summary, declaration, flush, run };
}
test('declared contiguous 60sec at exactly500 passes and preserves provenance/capture scope', t => {
  const f = fixture(t); f.rows[0].enemies = 4;
  const result = f.run(); assert.equal(result.passed, true); assert.equal(result.window.startFrame, 10); assert.equal(result.window.endFrame, 3759);
  assert.equal(result.window.durationMs, 60000); assert.equal(result.window.minEnemies, 500); assert.equal(result.window.p95Ms, 16);
  assert.equal(result.captureEvidence, 'declaration-attested; not independently recorded by frames.csv'); assert.equal(result.identity.sourceHash, f.identity.sourceHash);
});
for (const [field, value] of [['enemies', 499], ['paused', 1], ['suspended', 1], ['partial', 1], ['speed', 2]]) {
  test(`one ${field} violation fails without selecting later good frames`, t => {
    const f = fixture(t, 8000); f.rows[11][field] = value; const result = f.run();
    assert.equal(result.passed, false); assert.equal(result.window.startFrame, 10); assert.equal(result.window.endFrame, 3759);
    assert.ok(result.violations.some(v => v.frame === 11 && v.reason.includes(field)));
  });
}
test('first row meeting tick trigger stays selected even when paused and below population', t => {
  const f = fixture(t); f.rows[10].paused = 1; f.rows[10].enemies = 499;
  const result = f.run(); assert.equal(result.window.startFrame, 10); assert.equal(result.passed, false);
});
test('entire crossing slow frame is retained and nearest-rank does not become max', t => {
  const f = fixture(t, 3750); f.declaration.startTick = 0; f.rows.at(-1).deltaMs = 100;
  const result = f.run(); assert.equal(result.window.durationMs, 60084); assert.equal(result.window.endFrame, 3749);
  assert.equal(result.window.p95Ms, 16); assert.equal(result.window.maxMs, 100); assert.deepEqual(result.slowFrames.map(r => r.frame), [3749]);
});
test('p95 over16.7 fails even when count and elapsed duration pass', t => {
  const f = fixture(t); for (const row of f.rows) row.deltaMs = 17;
  const result = f.run(); assert.equal(result.window.p95Ms, 17); assert.equal(result.passed, false); assert.ok(result.violations.some(v => v.reason === 'p95Ms>16.7'));
});
test('short or absent declared window fails explicitly', t => {
  const f = fixture(t, 100); let result = f.run(); assert.equal(result.passed, false); assert.ok(result.violations.some(v => v.reason === 'durationMs<60000'));
  f.declaration.startTick = 1000; result = f.run(); assert.equal(result.passed, false); assert.equal(result.window.startFrame, null);
});
for (const mutate of [f => { f.rows[22].frame = 21; }, f => { f.rows[22].frame = 23; }, f => { f.rows[22].deltaMs = 'NaN'; }, f => { f.summary.frames++; }]) {
  test('corrupt raw sequence/numeric/count is rejected', t => { const f = fixture(t); mutate(f); assert.throws(f.run); });
}
for (const key of ['build', 'dataHash', 'sourceHash', 'sourceDirty', 'seed', 'sessionId']) {
  test(`declaration ${key} must bind actual recording identity`, t => {
    const f = fixture(t); f.declaration.identity[key] = key === 'seed' ? 42 : key === 'sourceDirty' ? true : 'different'; assert.throws(f.run, /identity/);
  });
}
for (const mutate of [f => { f.facts.backend = 'Mono'; }, f => { f.facts.sourceDirty = true; }, f => { f.declaration.capture.profilerRecording = true; }, f => { f.declaration.capture.deepProfiling = true; }, f => { f.declaration.capture.replayRecording = false; }, f => { f.declaration.thresholds = { minEnemies: 1 }; }]) {
  test('unsupported runtime, capture setup or threshold override rejects', t => { const f = fixture(t); mutate(f); assert.throws(f.run); });
}
test('declaration digest must match the externally preregistered exact bytes', t => {
  const f = fixture(t); f.run(); assert.throws(() => analyzeWindow(f.dir, path.join(f.dir, 'declaration.json'), '0'.repeat(64)), /preregistered/);
});

test('capture beginning after declared tick fails instead of silently shifting the start', t => {
  const f = fixture(t); f.rows.forEach(row => { row.tick += 100; });
  const result = f.run(); assert.equal(result.passed, false); assert.ok(result.violations.some(v => v.reason === 'capture-starts-after-declared-trigger'));
});
test('screen recording and dirty build declarations remain visible rather than relabeled', t => {
  const f = fixture(t); f.identity.sourceDirty = f.facts.sourceDirty = f.declaration.identity.sourceDirty = true;
  f.declaration.capture.screenRecording = true;
  const result = f.run(); assert.equal(result.passed, true); assert.equal(result.identity.sourceDirty, true); assert.equal(result.capture.screenRecording, true);
});
test('CLI emits failed verdict, exits nonzero and refuses to overwrite existing evidence', t => {
  const f = fixture(t); f.rows[11].enemies = 499; f.run();
  const declaration = path.join(f.dir, 'declaration.json'), output = path.join(f.dir, 'result.json');
  const args = ['tools/first-playable-frame-window.mjs', f.dir, declaration, '--declaration-sha256', sha(fs.readFileSync(declaration)), '--output', output];
  const failed = spawnSync(process.execPath, args, { encoding: 'utf8' });
  assert.equal(failed.status, 1); assert.equal(JSON.parse(fs.readFileSync(output)).passed, false);
  const original = fs.readFileSync(output); const repeated = spawnSync(process.execPath, args, { encoding: 'utf8' });
  assert.equal(repeated.status, 1); assert.match(repeated.stderr, /EEXIST/); assert.deepEqual(fs.readFileSync(output), original);
});
