import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { validateBinding, validatePair } from './verify-target-parity.mjs';
import { boundedMap } from './s4-league.mjs';
import { captureProvenance } from './diagnostic-provenance.mjs';
import { canonical } from './s4b-contract.mjs';
const execute = promisify(execFile);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
export function validateReplayResult(actual, expected) {
  assert.ok(Number.isSafeInteger(actual.Seed) && actual.Seed >= 30000 && actual.Seed <= 30004);
  assert.equal(actual.Tick, 27000, 'full fifteen-minute fixture required');
  assert.equal(actual.EndKind, 0, 'duration terminal required, not early death/quit');
  assert.match(actual.StateHash, /^[a-f0-9]{64}$/i);
  assert.deepEqual(actual, expected, 'same input differs between actual Core target assemblies');
}
export async function run(output, workers = 4) {
  assert.ok(output, 'output directory required');
  assert.ok(Number.isInteger(workers) && workers > 0 && workers <= 4);
  await fs.mkdir(output);
  const frozen = await captureProvenance();
  const sourceCommit = (await execute('git', ['rev-parse', 'HEAD'])).stdout.trim();
  const sourceStatus = (await execute('git', ['status', '--porcelain=v1'])).stdout;
  const dotnet = process.env.DOTNET ?? 'dotnet';
  const launch = args => execute(dotnet, args, { timeout: 600000, maxBuffer: 1024 * 1024 });
  const targets = [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ];
  for (const target of targets) {
    target.dll = path.resolve(`core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    target.expected = { targetFramework: target.tfm,
      location: await fs.realpath(path.join(path.dirname(target.dll), 'SowSiege.Core.dll')),
      sha256: sha(await fs.readFile(`core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`)) };
    const metadataFile = path.resolve(output, `${target.name}-assembly.json`);
    await launch([target.dll, '--assembly-metadata', metadataFile]);
    target.binding = JSON.parse(await fs.readFile(metadataFile)).core;
    validateBinding(target.binding, target.expected);
  }
  validatePair(targets[0].binding, targets[1].binding);
  const fixtures = path.resolve(output, 'replays');
  await launch([targets[0].dll, 'interactive-fixtures', 'data', fixtures, 'first-playable']);
  const goldens = JSON.parse(await fs.readFile(path.join(fixtures, 'hashes.json')));
  assert.deepEqual(goldens.map(g => g.Seed).sort(), [30000, 30001, 30002, 30003, 30004]);
  const rows = await boundedMap(goldens, workers, async golden => {
    validateReplayResult(golden, golden);
    const file = path.join(fixtures, `${golden.Seed}.ssreplay`), inputSha256 = sha(await fs.readFile(file));
    for (const target of targets) for (let repeat = 0; repeat < 3; repeat++) {
      const { stdout } = await launch([target.dll, 'interactive-replay', 'data', file, 'first-playable']);
      const actual = JSON.parse(stdout.trim()); validateReplayResult(actual, golden);
      await fs.writeFile(path.join(output, `${golden.Seed}.${target.name}.${repeat}.json`), `${JSON.stringify(actual)}\n`);
      assert.equal(sha(await fs.readFile(file)), inputSha256, 'replay input bytes changed');
      assert.equal(sha(await fs.readFile(target.expected.location)), target.expected.sha256, 'target DLL changed');
    }
    console.log(`PASS first playable seed ${golden.Seed}: 27000 ticks × 3 repeats × 2 actual Core targets`);
    return { seed: golden.Seed, tick: golden.Tick, inputSha256, stateHash: golden.StateHash, repeatsPerTarget: 3 };
  });
  assert.equal(canonical(await captureProvenance()), canonical(frozen), 'source/content/DLL changed during parity');
  assert.equal((await execute('git', ['rev-parse', 'HEAD'])).stdout.trim(), sourceCommit);
  assert.equal((await execute('git', ['status', '--porcelain=v1'])).stdout, sourceStatus);
  const summary = { passed: true, sourceCommit, sourceDirty: Boolean(sourceStatus.trim()), profile: 'first-playable', executions: 30, fixtures: rows,
    bindings: targets.map(t => t.binding), scope: 'Full 27000-tick invulnerable interactive correctness fixtures, replayed in .NET hosts with distinct net8.0/netstandard2.1 Core DLLs. Not balance, Unity Mono, or Android IL2CPP evidence.' };
  await fs.writeFile(path.join(output, 'summary.json'), `${JSON.stringify(summary, null, 2)}\n`);
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) run(process.argv[2], Number(process.argv[3] ?? 4)).catch(error => { console.error(error.message); process.exitCode = 1; });
