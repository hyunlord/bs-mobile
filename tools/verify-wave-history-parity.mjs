import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { validateBinding, validatePair } from './verify-target-parity.mjs';
import { prepareWaveBenchmark } from './prepare-wave-benchmark.mjs';

const execute = promisify(execFile);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

export async function run(output, { build = true } = {}) {
  assert.ok(output, 'A fresh historical parity output directory is required');
  output = path.resolve(output);
  await fs.mkdir(output);
  const launch = args => execute(process.env.DOTNET ?? 'dotnet', args, { cwd: root, timeout: 600000, maxBuffer: 1024 * 1024 });
  const historyRoot = path.join(root, 'benchmarks/wave-history');
  const history = JSON.parse(await fs.readFile(path.join(historyRoot, 'contract.json'), 'utf8'));
  const c05Root = path.join(root, 'benchmarks/wave-c05');
  const c05 = JSON.parse(await fs.readFile(path.join(c05Root, 'contract.json'), 'utf8'));
  assert.equal(history.sourceCommit, c05.sourceCommit);
  assert.equal(history.dataHash, c05.dataHash);
  assert.equal(history.profile, 'wave-1a');
  assert.equal(history.fixtures.length, 2, 'Both historical 27000-tick samples are required');
  assert.equal(new Set(history.fixtures.map(f => f.name)).size, 2);
  const fixtures = history.fixtures.map(f => {
    assert.match(f.file, /^[a-z0-9-]+\.ssreplay$/);
    assert.equal(f.expected.Tick, 27000);
    assert.equal(f.expected.EndKind, 0);
    return { ...f, file: path.join(historyRoot, f.file) };
  });
  fixtures.push({ name: 'c05', file: path.join(c05Root, 'run.ssreplay'), sha256: c05.replaySha256,
    expected: { Seed: 30001, Tick: c05.endTick, StateHash: c05.endHash, EndKind: 2 } });
  for (const fixture of fixtures) {
    assert.equal(sha(await fs.readFile(fixture.file)), fixture.sha256, `Original ${fixture.name} replay changed`);
  }
  const data = path.join(await prepareWaveBenchmark({ exportCatalog: false }), 'data');
  if (build) {
    await launch(['build', 'core/src/SowSiege.Core/SowSiege.Core.csproj', '--configuration', 'Release']);
    await launch(['build', 'core/src/SowSiege.Sim/SowSiege.Sim.csproj', '--configuration', 'Release']);
    await launch(['build', 'core/src/SowSiege.CompatSim/SowSiege.CompatSim.csproj', '--configuration', 'Release']);
  }
  const targets = [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ];
  for (const target of targets) {
    target.dll = path.join(root, `core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    target.expected = { targetFramework: target.tfm,
      location: await fs.realpath(path.join(path.dirname(target.dll), 'SowSiege.Core.dll')),
      sha256: sha(await fs.readFile(path.join(root, `core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`))) };
    const metadata = path.join(output, `${target.name}-assembly.json`);
    await launch([target.dll, '--assembly-metadata', metadata]);
    target.binding = JSON.parse(await fs.readFile(metadata, 'utf8')).core;
    validateBinding(target.binding, target.expected);
  }
  validatePair(targets[0].binding, targets[1].binding);
  const results = [];
  for (const fixture of fixtures) {
    for (const target of targets) {
      const { stdout } = await launch([target.dll, 'interactive-replay', data, fixture.file, history.profile]);
      const actual = JSON.parse(stdout.trim());
      assert.deepEqual(actual, fixture.expected, `${fixture.name} historical result changed on ${target.name}`);
      assert.equal(sha(await fs.readFile(fixture.file)), fixture.sha256, 'Replay mutated during verification');
      assert.equal(sha(await fs.readFile(target.expected.location)), target.expected.sha256, 'Core DLL changed during verification');
      results.push({ fixture: fixture.name, target: target.name, inputSha256: fixture.sha256, ...actual });
      console.log(`PASS historical ${fixture.name}: ${actual.Tick} ticks, ${target.name}, ${actual.StateHash}`);
    }
  }
  const summary = { passed: true, profile: history.profile, frozenDataSource: history.sourceCommit, dataHash: history.dataHash,
    bindings: targets.map(t => t.binding), results,
    scope: 'Original native C07/A03 27000-tick and C05 14400-tick input bytes and historical end hashes; actual net8.0/netstandard2.1 Core DLLs in .NET hosts. No Unity, performance, balance or device acceptance claim.' };
  await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
  return summary;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  assert.ok(process.argv.length === 3 || (process.argv.length === 4 && process.argv[3] === '--no-build'),
    'Usage: node tools/verify-wave-history-parity.mjs OUTPUT [--no-build]');
  run(process.argv[2], { build: process.argv[3] !== '--no-build' }).catch(error => { console.error(error); process.exitCode = 1; });
}
