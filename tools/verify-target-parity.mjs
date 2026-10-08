import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { boundedMap } from './s4-league.mjs';
import { canonical } from './s4b-contract.mjs';

const execute = promisify(execFile);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const readJson = async file => JSON.parse(await fs.readFile(file, 'utf8'));
export function validateBinding(actual, expected) {
  assert.ok(actual && typeof actual === 'object', 'missing Core assembly metadata');
  assert.equal(actual.targetFramework, expected.targetFramework, 'wrong Core target framework');
  assert.match(actual.mvid, /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i, 'invalid Core MVID');
  assert.match(actual.sha256, /^[0-9a-f]{64}$/i, 'invalid Core SHA256');
  assert.equal(actual.sha256.toLowerCase(), expected.sha256.toLowerCase(), 'Core bytes differ from target build output');
  assert.equal(actual.location, expected.location, 'Core loaded from unexpected physical location');
}
export function validatePair(left, right) {
  for (const key of ['targetFramework', 'mvid', 'sha256', 'location']) assert.notEqual(left[key].toLowerCase(), right[key].toLowerCase(), `targets share ${key}`);
}
export function stableMetadata(metadata) {
  const { commit, gitDirty, sourceTreeSha256, simulationAssemblySha256, coreAssemblySha256, ...stable } = metadata;
  return stable;
}
export function gameplayResult(result) {
  const { runMetadata, ...gameplay } = result;
  return gameplay;
}
export function validateResults(results, golden) {
  assert.equal(results.length, 3, 'exactly three repeats required');
  for (const result of results) {
    assert.equal(result.hash, golden.hash, 'state hash differs from pre-migration baseline');
    assert.equal(sha(canonical(gameplayResult(result))), golden.gameplaySha256, 'entire gameplay result differs from pre-migration baseline');
    if (golden.metadataDefinitionSha256) assert.equal(sha(canonical(stableMetadata(result.runMetadata))), golden.metadataDefinitionSha256, 'content/configuration metadata changed');
  }
}

export function runProcess(command, args, label) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: ['ignore', 'ignore', 'pipe'], timeout: 600000 });
    let stderr = '';
    child.stderr.on('data', chunk => { stderr = (stderr + chunk.toString()).slice(-65536); });
    child.on('error', error => reject(new Error(`${label}: ${error.message}`)));
    child.on('close', (code, signal) => code === 0 ? resolve() : reject(new Error(`${label}: exit ${code}, signal ${signal}: ${stderr}`)));
  });
}

const frozenInputSource = 'd3c1652af921e7bf9ea36fa2ee709a1bf36e4183';
const frozenDataTree = 'f0599e5ce72d94e4286832f64f0a607f4497323b';

export async function exportFrozenData(commit, destination, expectedTree, cwd = process.cwd()) {
  const git = (args, options = {}) => execute('git', args, { ...options, cwd });
  assert.match(expectedTree, /^[0-9a-f]{40}$/i, 'historical data must have a pinned tree SHA');
  assert.match(commit, /^[0-9a-f]{40}$/i, 'historical source must be a full commit SHA');
  try { await git(['cat-file', '-e', commit + '^{commit}']); }
  catch { throw new Error('Missing historical parity commit ' + commit + '; fetch full repository history.'); }
  assert.equal((await git(['rev-parse', commit + ':data'])).stdout.trim(), expectedTree, 'historical data tree mismatch');
  const listing = (await git(['ls-tree', '-r', '-z', commit, '--', 'data/'], { maxBuffer: 4 * 1024 * 1024 })).stdout;
  const entries = listing.split('\0').filter(Boolean);
  assert.ok(entries.length > 0, 'historical data tree is empty');
  for (const entry of entries) {
    const match = /^(100644|100755) blob ([0-9a-f]{40})\t(data\/[^\0]+)$/.exec(entry);
    assert.ok(match, 'unsupported historical data entry');
    const relative = match[3].slice(5);
    assert.ok(relative && !relative.includes('\\') && !relative.split('/').some(part => ['', '.', '..'].includes(part)), 'unsafe historical data path');
    const target = path.join(destination, relative);
    await fs.mkdir(path.dirname(target), { recursive: true });
    const { stdout } = await git(['show', commit + ':' + match[3]], { encoding: 'buffer', maxBuffer: 8 * 1024 * 1024 });
    assert.equal(createHash('sha1').update(Buffer.from('blob ' + stdout.length + '\0')).update(stdout).digest('hex'), match[2], 'historical blob bytes mismatch');
    await fs.writeFile(target, stdout, { flag: 'wx' });
  }
  return entries.length;
}

export async function runParity(output, workers = 4) {
  assert.ok(output, 'usage: node tools/verify-target-parity.mjs OUTPUT [WORKERS=4]');
  assert.ok(Number.isInteger(workers) && workers >= 1 && workers <= 4, 'workers must be 1..4');
  const baseline = await readJson(new URL('./fixtures/target-parity-baseline.json', import.meta.url));
  assert.equal(baseline.cases.length, 15);
  assert.equal(new Set(baseline.cases.map(c => c.id)).size, 15);
  await fs.mkdir(output); // Refuse overwrite so failed evidence remains attributable.
  const historicalData = path.resolve(output, 'legacy-data');
  const historicalDataFiles = await exportFrozenData(frozenInputSource, historicalData, frozenDataTree);
  const raw = path.join(output, 'raw');
  await fs.mkdir(raw);
  const sourceCommit = (await execute('git', ['rev-parse', 'HEAD'])).stdout.trim();
  const sourceDirty = Boolean((await execute('git', ['status', '--porcelain=v1'])).stdout.trim());
  const dotnet = process.env.DOTNET ?? 'dotnet';
  const launch = (args, label) => runProcess(dotnet, args, label);
  const targets = [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ];
  for (const target of targets) {
    target.dll = path.resolve(`core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    const built = path.resolve(`core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`);
    const loaded = await fs.realpath(path.join(path.dirname(target.dll), 'SowSiege.Core.dll'));
    target.hostSha256 = sha(await fs.readFile(target.dll));
    target.expected = { targetFramework: target.tfm, location: loaded, sha256: sha(await fs.readFile(built)) };
    assert.equal(sha(await fs.readFile(loaded)), target.expected.sha256, 'host copy differs from target build output');
    const metadataFile = path.resolve(output, `${target.name}-assembly.json`);
    await launch([target.dll, '--assembly-metadata', metadataFile], `preflight/${target.name}`);
    target.binding = (await readJson(metadataFile)).core;
    validateBinding(target.binding, target.expected);
    assert.equal(await fs.realpath(target.binding.location), target.expected.location);
  }
  validatePair(targets[0].binding, targets[1].binding);
  const candidate = { id: 'weapon-growth-79-smoke', data: 'data', profile: 'weapon-growth-79', policy: 'weapon', peopleRule: 'C', seed: 9100, movement: 'circuit', durationTicks: 900, repeats: 3, configuredDurationTicks: 21600 };
  const rows = await boundedMap([...baseline.cases, candidate], workers, async c => {
    let actualGolden;
    assert.equal(c.repeats, 3); assert.equal(c.configuredDurationTicks, 21600);
    const observed = [];
    for (const target of targets) {
      const resultFile = path.resolve(raw, `${c.id}.${target.name}.results.json`);
      const metricsFile = path.resolve(raw, `${c.id}.${target.name}.metrics.json`);
      const args = [target.dll, '--data', c !== candidate && c.data === 'data' ? historicalData : c.data, '--profile', c.profile, '--policy', c.policy, '--people-rule', c.peopleRule, '--seed', String(c.seed), '--iterations', '3', '--output', resultFile, '--metrics', metricsFile];
      if (c.dummy) args.push('--include-test', '--hero', 'test:scout', '--estate', 'test:moor');
      if (c.durationTicks) args.push('--duration-ticks', String(c.durationTicks));
      if (c.movement) args.push('--movement', c.movement);
      await launch(args, `${c.id}/${target.name}`);
      const results = await readJson(resultFile), metrics = await readJson(metricsFile);
      validateBinding(metrics.coreAssembly, target.expected);
      assert.equal(metrics.coreAssembly.mvid.toLowerCase(), target.binding.mvid.toLowerCase(), 'assembly changed after preflight');
      if (c === candidate) {
        assert.equal(results.length, 3);
        actualGolden ??= { hash: results[0].hash, gameplaySha256: sha(canonical(gameplayResult(results[0]))) };
        validateResults(results, actualGolden);
        assert.equal(metrics.config.profileId, 'core:weapon_growth_79');
      } else { validateResults(results, c); }
      for (const result of results) {
        const metadata = result.runMetadata;
        assert.equal(metadata.commit, sourceCommit);
        assert.equal(metadata.gitDirty, sourceDirty);
        assert.equal(metadata.gitStatusAvailable, true);
        assert.match(metadata.sourceTreeSha256, /^[0-9a-f]{64}$/i);
        assert.equal(metadata.coreAssemblySha256.toLowerCase(), target.expected.sha256);
        assert.equal(metadata.simulationAssemblySha256.toLowerCase(), target.hostSha256);
      }
      observed.push(canonical(results.map(gameplayResult)));
    }
    assert.equal(observed[0], observed[1], 'target result semantic cross-comparison failed');
    console.log(`PASS ${c.id}: 3 repeats × 2 targets`);
    return { caseId: c.id, stateHash: (actualGolden ?? c).hash, gameplaySha256: (actualGolden ?? c).gameplaySha256, repeatsPerTarget: 3, passed: true };
  });
  assert.equal((await execute('git', ['rev-parse', 'HEAD'])).stdout.trim(), sourceCommit, 'commit changed during parity');
  const summary = { sourceCommit, sourceDirty, schemaVersion: 1, passed: true, baselineSource: baseline.sourceCommit, historicalInput: { sourceCommit: frozenInputSource, dataTree: frozenDataTree, files: historicalDataFiles, activeRootSubstitution: historicalData }, candidateInput: 'active data with weapon-growth-79 profile', cases: rows, targetBindings: targets.map(t => ({ name: t.name, ...t.binding })), executions: rows.length * 6, scope: 'Separate .NET 8 host processes comparing net8.0 and netstandard2.1 Core; not Unity/IL2CPP runtime validation.' };
  await fs.writeFile(path.join(output, 'summary.json'), `${JSON.stringify(summary, null, 2)}\n`);
  console.log(`PASS parity: ${rows.length} cases / ${rows.length * 6} executions; ${output}`);
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) runParity(process.argv[2], Number(process.argv[3] ?? 4)).catch(error => { console.error(error.message); process.exitCode = 1; });
