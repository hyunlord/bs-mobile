import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import { contentRecords } from './content-files.mjs';

const [approvedDirectory, outputDirectory] = process.argv.slice(2);
assert.ok(approvedDirectory && outputDirectory, 'usage: node tools/verify-production-parity.mjs <pinned-approved-data> <new-output-directory>');
const output = path.resolve(outputDirectory);
await fs.mkdir(output, { recursive: false });
const json = async filename => JSON.parse(await fs.readFile(filename, 'utf8'));
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.entries(value).sort(([a], [b]) => a.localeCompare(b)).map(([key, item]) => [key, canonical(item)])) : value;
const digest = value => createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');

async function effectiveInputs(directory, name) {
  const profile = await json(path.join(directory, 'profiles', `${name}.json`));
  const baseline = await json(path.join(directory, profile.tuningFile ?? 'tuning.json'));
  const gameplay = profile.experiment ? await json(path.join(directory, profile.experiment.tuningFile)) : profile.gameplay;
  const table = profile.weaponCombat.contractVersion === 1 ? await json(path.join(directory, profile.weaponCombat.definitionsFile)) : null;
  const content = {};
  for (const [kind, ids] of Object.entries(profile.selection)) {
    const records = await contentRecords(path.join(directory, kind));
    content[kind] = Object.fromEntries(ids.map(id => {
      const record = records.get(id)?.record;
      assert.ok(record, `missing selected ${id}`);
      if (kind !== 'weapons') return [id, record];
      const growth = record.growth ?? table.weapons[id];
      const first = growth.levels[0];
      return [id, { id, tags: record.tags, activation: { shape: record.activation.shape, damage: first.damage, range: first.range, cooldownTicks: first.cooldownTicks, knockback: first.knockback }, growth }];
    }));
  }
  for (const kind of ['charters', 'items', 'evolutions']) {
    const records = await contentRecords(path.join(directory, kind));
    content[kind] = Object.fromEntries(profile.runtime[kind].map(id => [id, records.get(id)?.record]));
  }
  return { tuning: gameplay.tuning ?? baseline, enemyOverrides: gameplay.enemyOverrides, experiment: gameplay.experiment, runtime: profile.runtime, content };
}

const approved = await effectiveInputs(approvedDirectory, 'weapon-growth-79');
const production = await effectiveInputs('data', 'production');
assert.deepEqual(canonical(production), canonical(approved), 'production effective rules or numeric inputs differ from pinned candidate');
const cases = [{ seed: 20000, policy: 'weapon', rule: 'C' }, { seed: 20001, policy: 'random', rule: 'A' }];
const evidence = [];
for (const sample of cases) {
  const results = [];
  for (const [label, directory, profile] of [['approved', approvedDirectory, 'weapon-growth-79'], ['production', 'data', 'production']]) {
    const filename = path.join(output, `${sample.seed}-${label}.json`);
    const args = ['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll', '--data', directory, '--profile', profile, '--policy', sample.policy, '--people-rule', sample.rule, '--seed', String(sample.seed), '--movement', 'circuit', '--scenario', 'normal', '--iterations', '1', '--output', filename, '--metrics', path.join(output, `${sample.seed}-${label}-metrics.json`)];
    const run = spawnSync(process.env.DOTNET ?? 'dotnet', args, { stdio: ['ignore', 'ignore', 'pipe'], encoding: 'utf8' });
    assert.equal(run.status, 0, `${label} run failed: ${run.error ?? run.stderr}`);
    const [result] = await json(filename);
    assert.equal(result.runMetadata.configuredDurationTicks, 21600);
    assert.equal(result.runMetadata.requestedDurationTicks, 21600);
    const { runMetadata, ...gameplay } = result;
    results.push({ gameplay, metadata: runMetadata });
  }
  assert.deepEqual(results[1].gameplay, results[0].gameplay, `gameplay mismatch for seed ${sample.seed}`);
  assert.notEqual(results[1].metadata.profileSha256, results[0].metadata.profileSha256);
  assert.notEqual(results[1].metadata.contentSha256, results[0].metadata.contentSha256);
  evidence.push({ ...sample, configuredTicks: 21600, actualTicks: results[0].gameplay.ticks, hash: results[0].gameplay.hash, gameplayDigest: digest(results[0].gameplay), approvedProfileHash: results[0].metadata.profileSha256, productionProfileHash: results[1].metadata.profileSha256, approvedContentHash: results[0].metadata.contentSha256, productionContentHash: results[1].metadata.contentSha256 });
}
const summary = { passed: true, approvedData: path.resolve(approvedDirectory), effectiveInputsHash: digest(approved), cases: evidence, scope: 'Current .NET host resolves pinned legacy data and canonical production data; full configured runs, no Unity or device claim. Profile/content provenance intentionally differs.' };
await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
console.log(JSON.stringify(summary, null, 2));
