import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { formatCsv } from './csv.mjs';
import { headers } from './device-metrics.mjs';
import { analyzeMacWave } from './mac-wave-frame-window.mjs';
function fixture(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mac-wave-')); t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const identity = { build: 'a'.repeat(40), dataHash: 'b'.repeat(64), sourceHash: 'c'.repeat(64), sourceDirty: false, seed: 1, sessionId: 'test' };
  const facts = { model: 'Mac16,5', os: 'Mac OS X 27.0.1', backend: 'Mono', unityVersion: '6000.6.4f1', sourceHash: identity.sourceHash, sourceDirty: false };
  const rows = Array.from({ length: 4000 }, (_, frame) => ({ ...Object.fromEntries(headers.map(k => [k, 0])), frame, tick: frame === 3999 ? 27000 : 20250 + frame, deltaMs: 16, speed: 1, enemies: 500, width: 720, height: 1560, safeWidth: 720, safeHeight: 1560, thermal: 'unavailable' }));
  const summary = { ...identity, durationTicks: 27000, lateStartTick: 20250, frames: rows.length };
  const run = () => {
    for (const [name, value] of [['recording', identity], ['device', facts], ['frame-summary', summary]]) fs.writeFileSync(path.join(dir, `${name}.json`), JSON.stringify(value));
    fs.writeFileSync(path.join(dir, 'frames.csv'), formatCsv(headers, rows));
    return analyzeMacWave(dir, { commit: identity.build });
  };
  return { rows, facts, summary, identity, run };
}
test('full late interval and absent density bins stay distinct', t => {
  const f = fixture(t), r = f.run(); assert.equal(r.lateComplete, true); assert.equal(r.denseLateTarget, 'within-target');
  assert.equal(r.late.p95Ms, 16); assert.equal(r.denseLate.longestContiguousMs, 64000); assert.equal(r.lateBins[1].p95Ms, null); assert.equal(r.lateBins[2].samples, 0);
});
for (const [key, value] of [['paused', 1], ['suspended', 1], ['partial', 1], ['speed', 2], ['enemies', 399]]) {
  test(`${key} splits density continuity without stitching`, t => {
    const f = fixture(t); f.rows[2000][key] = value; const r = f.run();
    assert.equal(r.denseLate.segments.length, 2); assert.equal(r.denseLate.longestContiguousMs, 32000); assert.equal(r.denseLateTarget, 'incomplete');
  });
}
test('all slow samples are retained and nearest rank is used', t => {
  const f = fixture(t); f.rows[3].deltaMs = 500; let r = f.run(); assert.equal(r.late.maxMs, 500); assert.equal(r.late.p95Ms, 16);
  for (let i = 0; i < 250; i++) f.rows[i].deltaMs = 20;
  r = f.run(); assert.equal(r.late.p95Ms, 20); assert.equal(r.denseLateTarget, 'over-target');
});
test('bin boundaries and missing terminal coverage are explicit', t => {
  const f = fixture(t); [399, 400, 599, 600, 799, 800].forEach((v, i) => { f.rows[i].enemies = v; });
  f.rows.at(-1).tick = 26999; const r = f.run(); assert.equal(r.lateBins[1].samples, 2); assert.equal(r.lateBins[2].samples, 1); assert.equal(r.lateComplete, false);
});
for (const mutate of [f => { f.rows[0].deltaMs = 'NaN'; }, f => { f.rows[0].deltaMs = ''; }, f => { f.rows[4].frame = 3; }, f => { f.rows[4].tick = 2; }, f => { f.rows[4].enemies = -1; }, f => { f.rows.length = 0; f.summary.frames = 0; }, f => { f.facts.backend = 'IL2CPP'; }, f => { f.facts.sourceHash = 'd'.repeat(64); }, f => { f.summary.frames++; }]) {
  test('invalid samples or provenance fail closed', t => { const f = fixture(t); mutate(f); assert.throws(f.run); });
}
