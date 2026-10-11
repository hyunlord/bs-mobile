import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { baselineCommit, fixtures, frameStats, order, summarizeStutter50 } from './summarize-stutter50.mjs';

function cohort(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'stutter50-test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const run of order) for (const [name, fixture] of Object.entries(fixtures)) {
    const directory = path.join(root, run, name); fs.mkdirSync(directory, { recursive: true });
    const side = run[0];
    const identity = { ...fixture, commit: side === 'A' ? baselineCommit : 'b'.repeat(40), sourceHash: side === 'A' ? 'B07DF87A5FF46FECFA43142D6533D787C13D662D08FB36D747A40413CF636678' : 'b'.repeat(64), expectedStateHash: fixture.stateHash, sourceDirty: false, developmentBuild: false, profilerEnabled: false, interrupted: false, diagnosticOnly: false, actorGroundingSkipped: false, verified: true, performanceEligible: true, frameTimingEnabled: true, error: '', platform: 'OSXPlayer', graphicsApi: 'Metal', quality: 'Ultra', renderWarmupFrames: 120, gcMode: 'Enabled', metalDisplayLinkEnvironment: 'unset', coreTickSamples: 0, heroRig: name === 'crowd411', frames: name === 'crowd411' ? 500 : 1500 };
    fs.writeFileSync(path.join(directory, 'benchmark.json'), JSON.stringify(identity));
    const windows = fixture.windows.split(',').map(value => value.split('-').map(Number));
    const lines = windows.flatMap(([start, end], window) => Array.from({ length: 500 }, (_, index) => `${window * 500 + index + 1},${index === 499 ? end : start},${name === 'crowd411' ? 411 : window === 2 ? 800 : 500},16.7,1`));
    fs.writeFileSync(path.join(directory, 'frames.csv'), 'frame,tick,enemies,wallMs,focused\n' + lines.join('\n') + '\n');
  }
  return root;
}
function changeIdentity(root, run, field, value) {
  const file = path.join(root, run, 'c05capped', 'benchmark.json');
  const data = JSON.parse(fs.readFileSync(file, 'utf8')); data[field] = value; fs.writeFileSync(file, JSON.stringify(data));
}

test('exact thresholds:25 is not over,50 passes only with ratio <=0.2%, no rounding', () => {
  const values = Array.from({ length: 500 }, () => ({ wallMs: 25 }));
  assert.equal(frameStats(values).over25, 0);
  values[0].wallMs = 50;
  assert.equal(frameStats(values).pass, true);
  assert.equal(frameStats(values).over25_percent, .2);
  assert.equal(frameStats(values.slice(0, 499)).pass, false);
  values[0].wallMs = 50.000001; assert.equal(frameStats(values).pass, false);
});
test('six complete runs produce18 rows and preserve a failing repetition instead of pooling', t => {
  const root = cohort(t); const report = summarizeStutter50(root);
  assert.equal(report.rows.length, 18); assert.equal(report.pass, true);
  const file = path.join(root, 'B2', 'c05capped', 'frames.csv');
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replace('500,16.7,1', '500,325,1'));
  const failed = summarizeStutter50(root);
  assert.equal(failed.pass, false);
  assert.equal(failed.rows.find(row => row.run === 'B2' && row.scope === 'C05-all').max_ms, 325);
  assert.equal(failed.rows.filter(row => row.side === 'B' && !row.pass).length, 1);
});
test('baseline failure stays visible but does not substitute for candidate gate', t => {
  const root = cohort(t), file = path.join(root, 'A1', 'crowd411', 'frames.csv');
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replace('411,16.7,1', '411,325,1'));
  const result = summarizeStutter50(root); assert.equal(result.pass, true);
  assert.equal(result.rows[0].pass, false); assert.equal(result.rows[0].max_ms, 325);
});
test('missing or extra repetitions fail closed', t => {
  const root = cohort(t); fs.renameSync(path.join(root, 'B3'), path.join(root, 'B4'));
  assert.throws(() => summarizeStutter50(root), /Exactly/);
});
for (const [field, value] of [['sourceDirty', true], ['developmentBuild', true], ['profilerEnabled', true], ['interrupted', true], ['verified', false], ['performanceEligible', false], ['stateHash', '0'.repeat(64)], ['width', 100], ['commit', 'c'.repeat(40)]]) {
  test(`rejects ${field} mismatch`, t => {
    const root = cohort(t); changeIdentity(root, 'B2', field, value);
    assert.throws(() => summarizeStutter50(root));
  });
}
test('unfocused frame is rejected rather than removed', t => {
  const root = cohort(t), file = path.join(root, 'B1', 'crowd411', 'frames.csv');
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replace('411,16.7,1', '411,16.7,0'));
  assert.throws(() => summarizeStutter50(root), /non-foreground/);
});
test('missing800 density fails closed', t => {
  const root = cohort(t), file = path.join(root, 'B3', 'c05capped', 'frames.csv');
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replaceAll(',800,', ',799,'));
  assert.throws(() => summarizeStutter50(root), /density group/);
});

test('truncated frame CSV fails closed', t => {
  const root = cohort(t), file = path.join(root, 'B3', 'crowd411', 'frames.csv');
  const lines = fs.readFileSync(file, 'utf8').trimEnd().split('\n'); lines.pop();
  fs.writeFileSync(file, lines.join('\n') + '\n');
  assert.throws(() => summarizeStutter50(root), /missing\/truncated frames/);
});

test('pacing change is explicit rather than rejected as an unrelated config mismatch', t => {
  const root = cohort(t);
  for (const run of ['B1', 'B2', 'B3']) changeIdentity(root, run, 'pacingPolicy', 'one-advance-per-render-frame-debt-preserved-v1');
  const report = summarizeStutter50(root);
  assert.equal(report.rows.find(row => row.run === 'A1').pacing_policy, 'legacy-unbounded-catch-up');
  assert.equal(report.rows.find(row => row.run === 'B1' && row.scope === 'C05-all').pacing_policy, 'one-advance-per-render-frame-debt-preserved-v1');
});
