import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { validateBinding, validatePair } from './verify-target-parity.mjs';
import { captureProvenance } from './diagnostic-provenance.mjs';
import { canonical } from './s4b-contract.mjs';
const execute = promisify(execFile);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const profile = 'wave-2a-dark';
// ADR0064 gate: the opt-in estate profile gives the same state hashes on the net8.0 and netstandard2.1 Core targets.
// 1) The format 7 estate fixture (normal input; every building effect fires) recorded by the net8 host, replayed twice on each target.
// 2) A bot run with the "estate-wake" movement on each target.
export async function run(output) {
  assert.ok(output, 'New output directory required');
  await fs.mkdir(output);
  const frozen = await captureProvenance();
  const launch = args => execute(process.env.DOTNET ?? 'dotnet', args, { timeout: 600000, maxBuffer: 1024 * 1024 });
  const targets = [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ];
  for (const target of targets) {
    target.dll = path.resolve(`core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    target.expected = { targetFramework: target.tfm, location: await fs.realpath(path.join(path.dirname(target.dll), 'SowSiege.Core.dll')),
      sha256: sha(await fs.readFile(`core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`)) };
    const metadata = path.resolve(output, `${target.name}-assembly.json`);
    await launch([target.dll, '--assembly-metadata', metadata]);
    target.binding = JSON.parse(await fs.readFile(metadata)).core;
    validateBinding(target.binding, target.expected);
  }
  validatePair(targets[0].binding, targets[1].binding);
  const fixturePath = path.resolve(output, 'estate-replays');
  await launch([targets[0].dll, 'estate-fixture', 'data', fixturePath, profile]);
  const [golden] = JSON.parse(await fs.readFile(path.join(fixturePath, 'hashes.json')));
  const observation = JSON.parse(await fs.readFile(path.join(fixturePath, 'estate-observation.json')));
  assert.equal(observation.FormatVersion, 7);
  assert.ok(observation.Shots > 0 && observation.Heals > 0 && observation.MagnetPulls > 0, 'Every building effect must fire in the fixture');
  assert.equal(observation.PullsOutsideGranaries, 0);
  assert.equal(golden.Tick, observation.Tick);
  const replay = path.join(fixturePath, `${golden.Seed}.ssreplay`), inputSha256 = sha(await fs.readFile(replay));
  for (const target of targets) for (let repeat = 0; repeat < 2; repeat++) {
    const actual = JSON.parse((await launch([target.dll, 'interactive-replay', 'data', replay, profile])).stdout.trim());
    assert.deepEqual(actual, golden, 'Format 7 estate replay differs across actual Core targets');
    assert.equal(sha(await fs.readFile(replay)), inputSha256);
    assert.equal(sha(await fs.readFile(target.expected.location)), target.expected.sha256);
  }
  console.log(`PASS format7 estate seed ${golden.Seed}: ${golden.Tick} ticks, ${golden.StateHash}, 2 repeats × 2 actual Core targets`);
  const bots = [];
  for (const target of targets) {
    const result = path.resolve(output, `bot-${target.name}.json`);
    await launch([target.dll, '--data', 'data', '--profile', profile, '--movement', 'estate-wake', '--seed', String(golden.Seed), '--iterations', '1', '--duration-ticks', '3000', '--output', result]);
    const [row] = JSON.parse(await fs.readFile(result));
    bots.push({ target: target.name, seed: row.seed, ticks: row.ticks, hash: row.hash });
  }
  assert.equal(bots[0].hash, bots[1].hash, 'Estate bot run differs across actual Core targets');
  console.log(`PASS estate-wake bot seed ${golden.Seed}: ${bots[0].ticks} ticks, ${bots[0].hash} on both targets`);
  assert.equal(canonical(await captureProvenance()), canonical(frozen), 'Source/content/DLL changed during parity');
  const summary = { passed: true, profile, bindings: targets.map(t => t.binding), fixture: { ...golden, inputSha256, observation }, bots,
    scope: 'Opt-in estate profile: one format 7 normal-input replay where the watchtower, well and granary effects fire, and one 3000-tick estate-wake bot run, on actual net8.0/netstandard2.1 Core DLLs in .NET hosts. Determinism only; no balance, Unity or device gate.' };
  await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) run(process.argv[2]).catch(error => { console.error(error); process.exitCode = 1; });
