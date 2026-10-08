import test from 'node:test';
import assert from 'node:assert/strict';
import { makeCases, evaluate, reportContents } from './first-playable-report.mjs';
import { args } from './first-playable-league.mjs';
function rows() {
  return makeCases('full').map(c => ({ ...c, repeatCount: 3, repeatVerified: true, survived: true, ticks: 27000,
    level: c.policy === (c.seed % 2 ? 'weapon' : 'people') ? 30 : 20,
    weaponDamage: 1, toolActivationDamage: 0, toolGrowthDamage: 0, allyDamage: 0 }));
}
function survival(runs, rule, policy, count) {
  runs.filter(r => r.peopleRule === rule && r.policy === policy).forEach((r, i) => {
    r.survived = i < count; r.ticks = r.survived ? 27000 : 100;
  });
}
test('new holdout is 576 independent cases and 1728 executions at 15 minutes', () => {
  const cases = makeCases('full');
  assert.equal(cases.length, 576);
  assert.deepEqual([...new Set(cases.map(c => c.seed))], Array.from({ length: 32 }, (_, i) => 40000 + i));
  assert.ok(cases.every(c => c.requestedTicks === 27000));
  assert.equal(makeCases('smoke').length, 18);
  assert.ok(makeCases('smoke').every(c => c.seed === 9300 && c.requestedTicks === 900));
  const invocation = args(cases[0], '/tmp/packet.json');
  assert.equal(invocation[invocation.indexOf('--profile') + 1], 'first-playable');
  assert.equal(invocation[invocation.indexOf('--iterations') + 1], '3');
});
test('mixed must reach random survival in at least two rules, independent of other gates', () => {
  const r = rows(); assert.equal(evaluate(r, 'full').overall, 'PASS');
  survival(r, 'A', 'mixed', 31); assert.equal(evaluate(r, 'full').mixedPass, true);
  survival(r, 'B', 'mixed', 31); const result = evaluate(r, 'full');
  assert.equal(result.mixedPass, false); assert.equal(result.cPrimePass, true); assert.equal(result.overall, 'FAIL');
});
test('zero random denominator never counts as mixed success or cprime pass', () => {
  const r = rows(); for (const rule of ['A', 'B']) survival(r, rule, 'random', 0);
  const result = evaluate(r, 'full'); assert.equal(result.mixedPass, false); assert.equal(result.cPrimePass, false);
  assert.equal(result.mixed.filter(x => x.status === 'NOT_EVALUABLE').length, 2);
});
test('unchanged b rejects universal ties and cprime includes exact boundaries', () => {
  const r = rows(); r.forEach(x => x.level = 20); assert.equal(evaluate(r, 'full').bPass, false);
  for (const rule of ['A', 'B', 'C']) {
    survival(r, rule, 'random', 10); survival(r, rule, 'weapon', 6);
    for (const policy of ['land', 'building', 'people']) survival(r, rule, policy, 15);
  }
  assert.equal(evaluate(r, 'full').cPrimePass, true);
  survival(r, 'A', 'weapon', 5); assert.equal(evaluate(r, 'full').cPrimePass, false);
});
test('missing duplicate or unverifiable runs fail closed; smoke is never a gate pass', () => {
  const r = rows(); assert.throws(() => evaluate(r.slice(1), 'full'));
  assert.throws(() => evaluate([...r.slice(1), r[1]], 'full'));
  for (const patch of [{ repeatVerified: false }, { repeatCount: 2 }, { ticks: 21600 }]) {
    assert.throws(() => evaluate([{ ...r[0], ...patch }, ...r.slice(1)], 'full'));
  }
  const smoke = makeCases('smoke').map(c => ({ ...r[0], ...c, ticks: 900 }));
  assert.equal(evaluate(smoke, 'smoke').overall, 'SMOKE_ONLY');
  assert.ok(reportContents(smoke, 'smoke').has('mixed.csv'));
});
test('temporal weapon emptiness uses completed attacks, preserving pending and null legacy values', async () => {
  const { weaponCounters } = await import('./first-playable-league.mjs');
  const source = { actorKind: 'weapon', sourceId: 'test:orb', suppressedHpDamage: 0, attackAttempts: 10, noTargetAttempts: 2,
    emptyActivations: null, eligibleBeforeCap: null, hitCount: 12, requestedDamage: 100, appliedHpDamage: 70 };
  const result = weaponCounters(source, { 'equipment:test:orb:completed': 8, 'equipment:test:orb:empty': 3 });
  assert.equal(result.trueEmptyCompletedActivationRate, 3 / 8); assert.equal(result.overkillRequestedMinusApplied, 30);
  assert.equal(result.emptyActivations, null); assert.equal(result.eligibleBeforeCap, null);
  assert.equal(weaponCounters(source, {}).trueEmptyCompletedActivationRate, null);
  assert.throws(() => weaponCounters(source, { 'equipment:test:orb:empty': 1 }));
});
test('observed first playable coverage requires matching three-repeat digests', async () => {
  const { validateCoveragePacket } = await import('./first-playable-league.mjs');
  const { digest } = await import('./diagnostic-packet.mjs');
  const coverage = { 'form:orbit:hit': 3 }, hash = digest(coverage), runtime = { effects: [], evolutionActivations: [] }, runtimeHash = digest(runtime);
  const packet = { firstPlayableCoverage: coverage, firstPlayableCoverageDigest: hash, firstPlayableRuntime: runtime, firstPlayableRuntimeDigest: runtimeHash,
    repeatVerification: { repeats: Array.from({ length: 3 }, () => ({ firstPlayableCoverageDigest: hash, firstPlayableRuntimeDigest: runtimeHash })) } };
  validateCoveragePacket(packet);
  assert.throws(() => validateCoveragePacket({ ...packet, firstPlayableCoverage: { ...coverage, fake: 1 } }));
  const altered = structuredClone(packet); altered.repeatVerification.repeats[2].firstPlayableCoverageDigest = 'a'.repeat(64);
  assert.throws(() => validateCoveragePacket(altered));
});
test('CSV replay binds new cohort, duration, source and profile content hashes', async () => {
  const { digest } = await import('./diagnostic-packet.mjs');
  const { validateReplayInputs, sourceIdentity } = await import('./first-playable-report.mjs');
  const provenance = { mode: 'full', profile: 'first-playable', profileId: 'core:first_playable', seeds: '40000..40031',
    distinctCases: '576', seedBlocks: '32', repeatCount: '3', totalExecutions: '1728', tickRate: '30',
    configuredDurationTicks: '27000', requestedTicks: '27000', sourceDirty: 'false', sourceCommit: 'b'.repeat(40),
    runtime: '8.0', os: 'test', architecture: 'test', coreTargetFramework: '.NETCoreApp,Version=v8.0',
    ...Object.fromEntries(['sourceTreeSha256', 'simulationAssemblySha256', 'coreAssemblySha256', 'profileSha256', 'tuningSha256', 'contentSha256', 'weaponDefinitionSha256'].map(k => [k, 'c'.repeat(64)])) };
  const runs = rows().map(row => {
    const r = { ...row, ...Object.fromEntries(['packetSha256', 'gameplayHash', 'gameplayDigest', 'diagnosticDigest', 'cardsDigest'].map(k => [k, 'A'.repeat(64)])) };
    r.inputHash = digest({ caseIdentity: { caseId: `first-playable-${r.policy}-${r.peopleRule}-${r.seed}-control`, variant: 'control', seed: r.seed, policy: r.policy, peopleRule: r.peopleRule, movement: 'circuit', requestedTicks: 27000 }, content: provenance.contentSha256.toUpperCase(), profile: provenance.profileSha256.toUpperCase(), tuning: provenance.tuningSha256.toUpperCase() }).toUpperCase();
    r.diagnosticIdentity = digest({ contractVersion: 1, variant: 'control', inputHash: r.inputHash, gameplayHash: r.gameplayHash, diagnosticDigest: r.diagnosticDigest, cardsDigest: r.cardsDigest });
    r.sourceIdentity = sourceIdentity(r, provenance); return r;
  });
  validateReplayInputs(runs, [provenance], 'full');
  for (const patch of [{ seeds: '20000..20031' }, { configuredDurationTicks: '21600' }, { sourceDirty: 'true' }, { sourceTreeSha256: 'f'.repeat(64) }, { contentSha256: 'f'.repeat(64) }]) assert.throws(() => validateReplayInputs(runs, [{ ...provenance, ...patch }], 'full'));
  assert.throws(() => validateReplayInputs([{ ...runs[0], inputHash: 'f'.repeat(64) }, ...runs.slice(1)], [provenance], 'full'));
});
test('new target parity requires fifteen minute duration and exact replay state', async () => {
  const { validateReplayResult } = await import('./verify-first-playable-target-parity.mjs');
  const golden = { Seed: 30000, Tick: 27000, StateHash: 'a'.repeat(64), EndKind: 0 };
  validateReplayResult(golden, golden);
  for (const patch of [{ Tick: 21600 }, { EndKind: 1 }, { StateHash: 'b'.repeat(64) }, { Seed: 30001 }]) assert.throws(() => validateReplayResult({ ...golden, ...patch }, golden));
});
