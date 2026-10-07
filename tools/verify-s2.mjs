import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const directory = process.argv[2] ?? 'artifacts/s2-gate';
fs.mkdirSync(directory, { recursive: true });
const tuning = JSON.parse(fs.readFileSync('data/tuning.json', 'utf8'));
const dummyHero = JSON.parse(fs.readFileSync('data/test/heroes/scout.json', 'utf8'));
const dummyEstate = JSON.parse(fs.readFileSync('data/test/estates/moor.json', 'utf8'));
const runs = [];
for (const peopleRule of ['A', 'B', 'C']) {
  for (const alternate of [false, true]) {
    const name = `${alternate ? 'dummy' : 'default'}-${peopleRule}`;
    const prefix = path.join(directory, name);
    const args = ['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll', '--data', 'data', '--seed', '42', '--policy', 'mixed', '--people-rule', peopleRule, '--iterations', '3', '--output', `${prefix}.results.json`, '--metrics', `${prefix}.metrics.json`];
    if (alternate) args.push('--include-test', '--hero', dummyHero.id, '--estate', dummyEstate.id);
    execFileSync('dotnet', args, { stdio: 'inherit', timeout: 300_000 });
    const results = JSON.parse(fs.readFileSync(`${prefix}.results.json`, 'utf8'));
    if (!Array.isArray(results) || results.length !== 3 || results.some((result) => typeof result.hash !== 'string' || !/^[a-f0-9]{64}$/i.test(result.hash)) || new Set(results.map((result) => result.hash)).size !== 1) throw new Error(`${name}: triple hash mismatch`);
    const result = results[0];
    if (result.ticks !== tuning.durationTicks || !result.survived) throw new Error(`${name}: full-run fixture did not survive all requested ticks`);
    if (result.season !== tuning.world.seasons.length - 1) throw new Error(`${name}: final season missing`);
    if (result.heroId !== (alternate ? dummyHero.id : tuning.defaultHero) || result.estateId !== (alternate ? dummyEstate.id : tuning.defaultEstate)) throw new Error(`${name}: wrong content selection`);
    if (result.peopleRule !== peopleRule) throw new Error(`${name}: wrong people rule`);
    runs.push({ name, hash: result.hash, ticks: result.ticks, level: result.level, peopleRule, perTool: result.perTool, source: `${name}.results.json` });
  }
}
const toolIds = fs.readdirSync('data/tools').filter((file) => file.endsWith('.json')).map((file) => JSON.parse(fs.readFileSync(path.join('data/tools', file), 'utf8')).id);
for (const id of toolIds) {
  if (!runs.some((run) => run.perTool[id]?.activationDamage > 0 && run.perTool[id]?.growthProduced > 0)) throw new Error(`${id}: no observed run with both actual damage and growth`);
}
const report = { stage: 'S2', fullDurationTicks: tuning.durationTicks, normalGameSeconds: tuning.durationTicks / tuning.tickRate, independentConfigurations: runs.length, repeatCount: 3, toolsWithObservedDualOutput: toolIds.length, runs };
fs.writeFileSync(path.join(directory, 'summary.json'), `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify({ gate: 'PASS', configurations: runs.length, tools: toolIds.length, ticks: tuning.durationTicks }));
