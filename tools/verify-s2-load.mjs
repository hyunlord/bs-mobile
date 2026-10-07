import fs from 'node:fs';
import path from 'node:path';

const directory = process.argv[2] ?? 'artifacts/s2-final/load';
const metrics = JSON.parse(fs.readFileSync(path.join(directory, 'metrics.json'), 'utf8'));
const lines = fs.readFileSync(path.join(directory, 'ticks.csv'), 'utf8').trim().split('\n');
const header = lines.shift().split(',');
const rows = lines.map((line) => Object.fromEntries(line.split(',').map((value, index) => [header[index], Number(value)])));
if (!rows.length || !metrics.load.enabled || !metrics.load.allSamplesExact) throw new Error('Missing verified load run');
for (const row of rows) {
  if (!Number.isFinite(row.elapsed_ms) || row.elapsed_ms < 0) throw new Error('Invalid timing');
  for (const side of ['before', 'after']) {
    for (const [kind, count] of Object.entries({ enemies: 1000, farms: 300, buildings: 20, people: 60 })) {
      if (row[`${side}_${kind}`] !== count) throw new Error(`Load drift ${side}/${kind} at ${row.iteration}/${row.tick}`);
    }
  }
}
const times = rows.map((row) => row.elapsed_ms).sort((left, right) => left - right);
const p95 = times[Math.ceil(times.length * .95) - 1];
if (p95 !== metrics.tickP95Ms || rows.length !== metrics.tickSampleCount || rows.length !== metrics.load.verifiedSamples) throw new Error('Percentile or sample count mismatch');
const proof = { source: 'ticks.csv', samples: rows.length, p95Ms: p95, targetMs: 5, targetMet: p95 <= 5, allBeforeAfterCountsExact: true, commit: metrics.commit, dirty: metrics.sourceMetadata.gitDirty, populationMembers: metrics.load.populationMembers };
fs.writeFileSync(path.join(directory, 'verification.json'), `${JSON.stringify(proof, null, 2)}\n`);
console.log(JSON.stringify(proof));
