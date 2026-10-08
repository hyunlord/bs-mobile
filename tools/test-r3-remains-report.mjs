import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { COUNTERS, verifyLedger, aggregate, checkBaselineResults, writeReport } from './r3-remains-report.mjs';
const empty = () => Object.fromEntries(COUNTERS.map(key => [key, 0]));
function fixture() {
  const run = { hash: 'a'.repeat(64), ticks: 600, policy: 'mixed', peopleRule: 'C', seed: 42, survived: true, endReason: 'fixture-duration', harvests: 8 };
  const final = { created: 10, absorbed: 6, expired: 2, dropped: 3, active: 2, fertilityTransferred: 12, fertilityConsumed: 7, growthBonusApplied: 14, fertilizedHarvests: 4 };
  const remains = { ...final, samples: [{ tick: 0, ...empty() }, { tick: 300, created: 4, absorbed: 2, expired: 1, dropped: 0, active: 1, fertilityTransferred: 4, fertilityConsumed: 2, growthBonusApplied: 4, fertilizedHarvests: 1 }, { tick: 600, ...final }] };
  return { run, results: Array.from({ length: 3 }, () => structuredClone({ ...run, remains })), timeline: [{ tick: 0 }, { tick: 300 }, { tick: 600 }] };
}
test('actual admitted conservation keeps dropped separate and counts repeat0 once', () => {
  const f = fixture(), counters = verifyLedger(f.results, f.run, f.timeline);
  const groups = aggregate([{ policy: 'mixed', peopleRule: 'C', movementMode: 'circuit', ...counters }, { policy: 'mixed', peopleRule: 'C', movementMode: 'circuit', ...counters }]);
  assert.equal(groups[0].distinctCases, 2); assert.equal(groups[0].created, 20); assert.equal(groups[0].dropped, 6);
});
test('rejects conservation, repeat drift, regressions, sample gaps and excess credited consumption', () => {
  const mutations = [f => f.results[0].remains.created++, f => f.results[1].remains.dropped++, f => f.results[0].remains.samples.splice(1, 1), f => f.results[0].remains.samples[1].fertilizedHarvests = 7, f => f.results[0].remains.fertilityConsumed = 13, f => f.results[0].remains.fertilizedHarvests = 9, f => f.results[2].hash = 'b'.repeat(64), f => f.results[0].remains.samples[0].dropped = 1];
  for (const mutate of mutations) { const f = fixture(); mutate(f); assert.throws(() => verifyLedger(f.results, f.run, f.timeline)); }
});
test('absence is not instrumentation with zero counters', () => {
  const f = fixture(); const old = f.results.map(({ remains, ...result }) => result);
  checkBaselineResults(old, f.run); assert.throws(() => verifyLedger(old, f.run, f.timeline), /not instrumented/);
  assert.throws(() => checkBaselineResults(f.results, f.run), /unexpectedly/);
  assert.throws(() => checkBaselineResults(old.map(result => ({ ...result, remains: null })), f.run), /unexpectedly/);
});
test('compact output preserves provenance and never overwrites an existing directory', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'r3-remains-')); t.after(() => fs.rm(temp, { recursive: true, force: true }));
  const f = fixture(), counters = verifyLedger(f.results, f.run, f.timeline);
  const evidence = { metadata: { sourceCommit: 'a'.repeat(40), sourceTreeSha256: 'b'.repeat(64), profileId: 'test:profile', profileSha256: 'c'.repeat(64), contentSha256: 'd'.repeat(64), mode: 'smoke-A', distinctCaseCount: '1', seedBlockCount: '1' }, groups: aggregate([{ policy: 'mixed', peopleRule: 'C', movementMode: 'circuit', ...counters }]), remainsProvenance: { remainsLoop: { capacity: 10, lifetimeTicks: 60, absorptionRadius: 10 }, fertilityPerKill: 2, fertilityGrowthBonus: 2 }, baseline: { status: 'not-instrumented', distinctCases: 1, sourceCommit: 'e'.repeat(40), profileId: 'test:old' }, inputManifest: [{ file: 'runs.csv', bytes: 10, sha256: 'f'.repeat(64) }] };
  const output = path.join(temp, 'output'); await writeReport(evidence, output);
  assert.equal((await fs.readdir(output)).length, 4); const markdown = await fs.readFile(path.join(output, 'report.md'), 'utf8');
  assert.match(markdown, /^관문: 부분/); assert.match(markdown, /not-instrumented/); assert.match(markdown, /0개라는 뜻이 아니다/);
  await assert.rejects(writeReport(evidence, output), /already exists/);
});

test('same bad ledger in all repeats still fails regression and conservation independently', () => {
  for (const [mutate, expected] of [[r => { r.samples[1].fertilizedHarvests = 7; }, /regressed/], [r => { r.samples[1].active++; }, /conservation/], [r => { r.samples[1].tick++; }, /grid/]]) {
    const f = fixture(); mutate(f.results[0].remains); f.results = f.results.map(result => ({ ...result, remains: structuredClone(f.results[0].remains) }));
    assert.throws(() => verifyLedger(f.results, f.run, f.timeline), expected);
  }
});
