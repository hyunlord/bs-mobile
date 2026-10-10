import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFile, lstat, mkdir, readFile, readdir, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { buildIdentity, identitySource } from './unity-build-identity.mjs';
import { prepareWaveBenchmark } from './prepare-wave-benchmark.mjs';

const benchmarkBridge = 'unity/Assets/Game/App/Generated/FrozenWaveBenchmark.g.cs';

export const outputs = Object.freeze({
  bridge: 'unity/Assets/Game/App/Generated/CanonicalContent.g.cs',
  core: 'unity/Assets/Generated/Plugins/SowSiege.Core.dll',
  identity: 'unity/Assets/Game/App/Generated/BuildIdentity.g.cs',
  provenance: 'unity/Assets/Generated/preparation.json',
});
export const profileName = 'first-playable';
export function selectedProfile(value = process.env.UNITY_CONTENT_PROFILE ?? profileName) {
  assert.ok(['first-playable', 'wave-1a'].includes(value), 'Unsupported Unity content profile');
  return value;
}
const digest = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();
const portable = relative => relative.split(path.sep).join('/');

async function files(root, relative, select) {
  const target = path.join(root, relative);
  assert.ok(!(await lstat(target)).isSymbolicLink(), `Symlink forbidden: ${relative}`);
  const result = [];
  for (const entry of await readdir(target, { withFileTypes: true })) {
    if ((relative.startsWith('core/') && ['bin', 'obj'].includes(entry.name)) || (relative === 'data' && entry.name === 'test')) continue;
    const next = portable(path.join(relative, entry.name));
    assert.match(entry.name, /^[A-Za-z0-9_.-]+$/, `Unsafe preparation path: ${next}`);
    assert.ok(!entry.isSymbolicLink(), `Symlink forbidden: ${next}`);
    if (entry.isDirectory()) result.push(...await files(root, next, select));
    else if (entry.isFile() && select(next)) result.push(next);
  }
  return result.sort();
}

async function safeOutput(root, relative) {
  let current = root;
  for (const segment of relative.split('/')) {
    current = path.join(current, segment);
    const stat = await lstat(current).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    assert.ok(!stat?.isSymbolicLink(), `Symlink forbidden: ${current}`);
  }
  return current;
}

export async function inputSnapshot(root) {
  const source = await files(root, 'core/src', name => /\.(?:cs|csproj|props|targets|json)$/.test(name));
  for (const entry of await readdir(root, { withFileTypes: true })) {
    if (/^(?:Directory\..*\.(?:props|targets)|global\.json|NuGet\.[Cc]onfig)$/.test(entry.name)) {
      assert.ok(entry.isFile() && !entry.isSymbolicLink(), `Unsafe build input: ${entry.name}`);
      source.push(entry.name);
    }
  }
  source.push('tools/prepare-unity.sh', 'tools/unity-export.mjs', 'tools/unity-build-identity.mjs');
  if (process.env.UNITY_WAVE_BENCHMARK === '1') source.push('tools/prepare-wave-benchmark.mjs', 'benchmarks/wave-c05/contract.json', 'benchmarks/wave-c05/run.ssreplay');
  const data = await files(root, 'data', name => name.endsWith('.json'));
  const entries = [];
  for (const relativePath of [...source, ...data].sort()) {
    const bytes = await readFile(await safeOutput(root, relativePath));
    entries.push({ relativePath, byteLength: bytes.length, sha256: digest(bytes) });
  }
  return entries;
}

export async function outputSnapshot(root) {
  const entries = [];
  for (const relativePath of [outputs.bridge, outputs.core, outputs.identity, ...(process.env.UNITY_WAVE_BENCHMARK === '1' ? [benchmarkBridge] : [])]) {
    const bytes = await readFile(await safeOutput(root, relativePath));
    entries.push({ relativePath, byteLength: bytes.length, sha256: digest(bytes) });
  }
  return entries;
}

export async function verify(root, profile = selectedProfile()) {
  selectedProfile(profile);
  const manifest = JSON.parse(await readFile(await safeOutput(root, outputs.provenance), 'utf8'));
  assert.equal(manifest.contractVersion, 3, 'Unsupported Unity preparation manifest; regenerate');
  assert.deepEqual(manifest.inputs, await inputSnapshot(root), 'Stale Unity source/data; run tools/prepare-unity.sh');
  assert.deepEqual(manifest.outputs, await outputSnapshot(root), 'Stale Unity bridge/DLL; run tools/prepare-unity.sh');
  assert.deepEqual(manifest.buildIdentity, await buildIdentity(root), 'Stale Unity build identity; run tools/prepare-unity.sh');
  assert.equal(manifest.profileName, profile, 'Unity preparation profile mismatch');
  const profileHash = digest(await readFile(await safeOutput(root, `data/profiles/${profile}.json`)));
  assert.equal(manifest.profileHash, profileHash, 'Unity profile hash mismatch');
  const bridge = await readFile(await safeOutput(root, outputs.bridge), 'utf8');
  assert.ok(bridge.includes('public const string ProfileName = "' + profile + '";'), 'Unity bridge profile mismatch');
  assert.ok(bridge.includes('public const string ProfileHash = "' + profileHash + '";'), 'Unity bridge profile hash mismatch');
  return manifest;
}

function run(root, arguments_) {
  const result = spawnSync(process.env.DOTNET ?? 'dotnet', arguments_, { cwd: root, stdio: 'inherit' });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `dotnet ${arguments_.join(' ')} failed (${result.signal ?? result.status})`);
}

export async function prepare(root, profile = selectedProfile()) {
  selectedProfile(profile);
  const before = await inputSnapshot(root);
  const identity = await buildIdentity(root);
  const provenance = await safeOutput(root, outputs.provenance);
  const bridge = await safeOutput(root, outputs.bridge);
  const core = await safeOutput(root, outputs.core);
  run(root, ['build', 'core/src/SowSiege.Sim/SowSiege.Sim.csproj', '--configuration', 'Release', '--no-incremental', '--nologo']);
  run(root, ['build', 'core/src/SowSiege.Core/SowSiege.Core.csproj', '--configuration', 'Release', '--framework', 'netstandard2.1', '--no-incremental', '--nologo']);
  run(root, ['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll', '--export-unity', path.join(root, 'data'), bridge, profile]);
  if (process.env.UNITY_WAVE_BENCHMARK === '1') {
    assert.equal(profile, 'wave-1a', 'Wave benchmark requires wave-1a presentation');
    await prepareWaveBenchmark();
  } else {
    await unlink(await safeOutput(root, benchmarkBridge)).catch(error => { if (error.code !== 'ENOENT') throw error; });
  }
  assert.deepEqual(await inputSnapshot(root), before, 'Source/data changed during Unity preparation; retry after edits finish');
  await mkdir(path.dirname(core), { recursive: true });
  await copyFile(path.join(root, 'core/src/SowSiege.Core/bin/Release/netstandard2.1/SowSiege.Core.dll'), core);
  await writeFile(await safeOutput(root, outputs.identity), identitySource(identity));
  const manifest = { contractVersion: 3, profileName: profile, profileHash: digest(await readFile(await safeOutput(root, `data/profiles/${profile}.json`))), buildIdentity: identity, inputs: before, outputs: await outputSnapshot(root) };
  assert.deepEqual(await inputSnapshot(root), before, 'Source/data changed before Unity preparation completed');
  assert.deepEqual(await buildIdentity(root), identity, 'Source changed before Unity build identity completed');
  await writeFile(provenance, `${JSON.stringify(manifest, null, 2)}\n`);
  return manifest;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  try {
    const verifyOnly = args[0] === '--verify';
    if (verifyOnly) args.shift();
    assert.ok(args.length === 0 || (args.length === 2 && args[0] === '--profile'), 'Usage: tools/prepare-unity.sh [--verify] [--profile first-playable|wave-1a]');
    const profile = selectedProfile(args[1]);
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    if (verifyOnly) await verify(root, profile);
    else await prepare(root, profile);
    console.log(`Unity canonical preparation ${verifyOnly ? 'verified' : 'generated'} (${outputs.provenance})`);
  } catch (error) {
    console.error(`Unity preparation failed: ${error.message}`);
    process.exitCode = 1;
  }
}
