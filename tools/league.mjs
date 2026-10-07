import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

if (process.argv[2]?.startsWith('s4-')) {
  const { runS4Cli } = await import('./s4-league.mjs');
  await runS4Cli(process.argv.slice(2));
} else {
const mode = process.argv[2] ?? 'smoke';
if (!['smoke', 'stage', 'long'].includes(mode)) throw new Error('league mode must be smoke, stage or long');
const tuning = JSON.parse(fs.readFileSync('data/tuning.json', 'utf8'));
const policies = Object.keys(tuning.policies);
if (policies.length !== 6) throw new Error('S2 league requires six configured policies');
const seeds = Array.from({ length: mode === 'long' ? 128 : 3 }, (_, index) => 42 + index);
const requestedTicks = mode === 'smoke' ? Math.min(900, tuning.durationTicks) : tuning.durationTicks;
const repeatCount = 3;
const directory = `artifacts/league-${mode}`;
fs.mkdirSync(directory, { recursive: true });
const rows = [];
const finite = (value, label) => {
  if (!Number.isFinite(value)) throw new Error(`Nonfinite or missing ${label}`);
  return value;
};
for (const policy of policies) {
  if (!/^[a-z][a-z0-9_-]*$/.test(policy)) throw new Error(`Unsafe policy filename: ${policy}`);
  for (const seed of seeds) {
    const prefix = path.join(directory, `${policy}-${seed}`);
    const args = ['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll', '--data', 'data', '--seed', String(seed), '--policy', policy, '--scenario', 'normal', '--iterations', String(repeatCount), '--output', `${prefix}.results.json`, '--metrics', `${prefix}.metrics.json`];
    if (mode === 'smoke') args.push('--duration-ticks', String(requestedTicks));
    execFileSync('dotnet', args, { stdio: 'pipe', timeout: 300_000 });
    const results = JSON.parse(fs.readFileSync(`${prefix}.results.json`, 'utf8'));
    if (!Array.isArray(results) || results.length !== repeatCount || results.some((result) => typeof result.hash !== 'string' || !/^[a-f0-9]{64}$/i.test(result.hash)) || new Set(results.map((result) => result.hash)).size !== 1) {
      throw new Error(`Determinism failure ${policy}/${seed}`);
    }
    const result = results[0];
    for (const field of ['damage', 'growth', 'ticks', 'level', 'killExperience', 'harvestExperience', 'taxExperience']) finite(result[field], `${policy}/${seed}/${field}`);
    if (typeof result.survived !== 'boolean' || result.scenario !== 'normal' || result.policy !== policy || result.seed !== seed || result.ticks > requestedTicks || result.ticks <= 0) throw new Error(`Invalid gameplay result ${policy}/${seed}`);
    const metrics = JSON.parse(fs.readFileSync(`${prefix}.metrics.json`, 'utf8'));
    if (metrics.stage !== 'S2' || metrics.model !== 'headless-gameplay') throw new Error('Refusing scaffold results as gameplay league evidence');
    finite(metrics.tickP95Ms, 'tickP95Ms');
    rows.push({ policy, seed, result, metrics, resultsPath: `${prefix}.results.json`, metricsPath: `${prefix}.metrics.json` });
  }
}
const average = (group, field) => group.reduce((sum, row) => sum + row.result[field], 0) / group.length;
const policyMeans = policies.map((policy) => {
  const group = rows.filter((row) => row.policy === policy);
  return { policy, independentSeeds: group.length, survivalRate: group.filter((row) => row.result.survived).length / group.length, damage: average(group, 'damage'), growth: average(group, 'growth'), level: average(group, 'level'), ticks: average(group, 'ticks'), killExperience: average(group, 'killExperience'), harvestExperience: average(group, 'harvestExperience'), taxExperience: average(group, 'taxExperience') };
});
const mean = policyMeans.reduce((sum, row) => sum + row.damage, 0) / policyMeans.length;
const policyDamageCv = mean === 0 ? null : Math.sqrt(policyMeans.reduce((sum, row) => sum + (row.damage - mean) ** 2, 0) / policyMeans.length) / Math.abs(mean);
const baseline = rows.find((row) => row.policy === 'mixed' && row.seed === seeds[0]);
if (!baseline) throw new Error('Missing mixed baseline');
const shortenedSimulation = requestedTicks < tuning.durationTicks;
const metrics = { ...baseline.metrics, balanceDispersion: null, gameplayBalanceClaim: false, policyDamageCv, balanceMetric: 'S2 diagnostic coefficient of variation of policy mean actual damage; not the formal S4 balance gate', league: { mode, seeds, policies, peopleRule: baseline.result.peopleRule, scenario: 'normal', requestedTicks, fullDurationTicks: tuning.durationTicks, shortenedSimulation, seasonsAccelerated: false, runCount: rows.length, repeatCount, totalExecutions: rows.length * repeatCount, independentPolicySeedCases: rows.length, timingScope: 'tick p95 is the mixed/first-seed baseline, not a pooled league percentile', policyMeans, sourceRecords: rows.map(({ resultsPath, metricsPath }) => ({ resultsPath, metricsPath })) } };
fs.writeFileSync('artifacts/metrics.json', `${JSON.stringify(metrics, null, 2)}\n`);
fs.writeFileSync(`${directory}/summary.json`, `${JSON.stringify(metrics, null, 2)}\n`);
const columns = ['policy', 'seed', 'peopleRule', 'ticks', 'survived', 'endReason', 'damage', 'growth', 'level', 'killExperience', 'harvestExperience', 'taxExperience', 'deathCause', 'hash'];
const csv = (value) => `"${String(value ?? '').replaceAll('"', '""')}"`;
fs.writeFileSync(`${directory}/outcomes.csv`, `${columns.join(',')}\n${rows.map(({ result }) => columns.map((field) => csv(result[field])).join(',')).join('\n')}\n`);
console.log(`S2 ${mode}: ${rows.length} policy/seed cases × ${repeatCount} deterministic repeats; ${requestedTicks} tick cap; policy damage CV=${policyDamageCv ?? 'undefined (zero mean)'}; diagnostic only, not S4 balance.`);

}
