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
  const fixturePath = path.resolve(output, 'replays');
  await launch([targets[0].dll, 'interactive-fixtures', 'data', fixturePath, 'wave-1a']);
  const goldens = JSON.parse(await fs.readFile(path.join(fixturePath, 'hashes.json')));
  assert.deepEqual(goldens.map(g => g.Seed).sort(), [30000, 30001, 30002, 30003, 30004]);
  const counters = JSON.parse(await fs.readFile(path.join(fixturePath, 'wave-counters.json')));
  const rows = [];
  for (const golden of goldens) {
    assert.ok(golden.Tick > 0 && golden.Tick <= 3000);
    const replay = path.join(fixturePath, `${golden.Seed}.ssreplay`), inputSha256 = sha(await fs.readFile(replay));
    for (const target of targets) for (let repeat = 0; repeat < 2; repeat++) {
      const actual = JSON.parse((await launch([target.dll, 'interactive-replay', 'data', replay, 'wave-1a'])).stdout.trim());
      assert.deepEqual(actual, golden, 'Wave input replay differs across actual Core targets');
      assert.equal(sha(await fs.readFile(replay)), inputSha256);
      assert.equal(sha(await fs.readFile(target.expected.location)), target.expected.sha256);
    }
    const observed = counters[golden.Seed];
    assert.ok(observed && Object.values(observed).every(Number.isSafeInteger), 'Missing bounded fixture behavior counters');
    const pendingActivations = Object.entries(observed).filter(([key]) => key.startsWith('activation-pending:')).reduce((sum, [, value]) => sum + value, 0);
    rows.push({ seed: golden.Seed, tick: golden.Tick, endKind: golden.EndKind, stateHash: golden.StateHash, inputSha256, pendingActivations, counters: observed });
    console.log(`PASS wave-1a seed ${golden.Seed}: ${golden.Tick} ticks, 2 repeats × 2 actual Core targets`);
  }
  assert.equal(canonical(await captureProvenance()), canonical(frozen), 'Source/content/DLL changed during parity');
  const summary = { passed: true, profile: 'wave-1a', designRevision: 'designed-v1.1', bindings: targets.map(t => t.binding), fixtures: rows,
    scope: 'Bounded 3000-tick normal-input replay, explicit Quit at bound (or earlier natural completion), no debug grants or invulnerability; determinism and no-crash only, not full-run completion. No balance, economy, Unity or device gate.' };
  await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) run(process.argv[2]).catch(error => { console.error(error); process.exitCode = 1; });
