import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { chmod, mkdtemp, mkdir, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { execFileSync } from 'node:child_process';
import { inputSnapshot, outputSnapshot, outputs, prepare, selectedProfile, verify } from './unity-export.mjs';
import { validateContent } from './validate-content.mjs';
import { buildIdentity, identitySource } from './unity-build-identity.mjs';

function git(root, args) {
  return execFileSync('git', ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=/dev/null', ...args], { cwd: root, stdio: 'pipe', env: { ...process.env, DEVELOPER_DIR: process.env.DEVELOPER_DIR ?? '/Library/Developer/CommandLineTools' } });
}

async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'unity-provenance-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const [relative, text] of Object.entries({
    'core/src/Core/Simulation.cs': 'source', 'core/src/Core/Core.csproj': 'project',
    'Directory.Build.props': 'props', 'global.json': 'sdk',
    'tools/prepare-unity.sh': 'script', 'tools/unity-export.mjs': 'helper',
    'tools/unity-build-identity.mjs': 'identity helper',
    'data/profiles/first-playable.json': '{}', 'data/tuning.json': '{}', 'data/bin/included.json': '{}', 'data/test/fixture.json': '{}',
    [outputs.bridge]: 'public const string ProfileName = "first-playable";\npublic const string ProfileHash = "' + createHash('sha256').update('{}').digest('hex').toUpperCase() + '";', [outputs.core]: 'dll', [outputs.identity]: 'identity',
  })) {
    await mkdir(path.dirname(path.join(root, relative)), { recursive: true });
    await writeFile(path.join(root, relative), text);
  }
  await writeFile(path.join(root, '.gitignore'), 'unity/Assets/Generated/\nunity/Assets/Game/App/Generated/\n');
  git(root, ['init', '-q']); git(root, ['add', '.']); git(root, ['commit', '-qm', 'fixture']);
  const identity = await buildIdentity(root);
  await writeFile(path.join(root, outputs.identity), identitySource(identity));
  const manifest = { contractVersion: 3, profileName: 'first-playable', profileHash: createHash('sha256').update('{}').digest('hex').toUpperCase(), buildIdentity: identity, inputs: await inputSnapshot(root), outputs: await outputSnapshot(root) };
  await writeFile(path.join(root, outputs.provenance), JSON.stringify(manifest));
  return root;
}

test('preparation verification is read-only and excludes test data', async t => {
  const root = await fixture(t), file = path.join(root, outputs.provenance), before = await readFile(file);
  await verify(root);
  await writeFile(path.join(root, 'data/test/fixture.json'), 'changed fixture');
  await verify(root);
  assert.deepEqual(await readFile(file), before);
});

test('preparation rejects a new source commit until runtime identity is regenerated', async t => {
  const root = await fixture(t);
  git(root, ['commit', '--allow-empty', '-qm', 'new source identity']);
  await assert.rejects(verify(root), /Stale Unity build identity/);
});

for (const relative of ['core/src/Core/Simulation.cs', 'Directory.Build.props', 'global.json', 'data/tuning.json', 'data/bin/included.json', outputs.bridge, outputs.core, outputs.identity]) {
  test(`preparation rejects stale ${relative}`, async t => {
    const root = await fixture(t);
    await writeFile(path.join(root, relative), 'changed');
    await assert.rejects(verify(root), /Stale Unity/);
  });
}

test('Unity source edits have a distinct dirty identity and invalidate preparation', async t => {
  const root = await fixture(t), before = await buildIdentity(root);
  assert.equal(before.sourceDirty, false);
  const source = path.join(root, 'unity/Assets/Game/App/Presentation.cs');
  await writeFile(source, 'presentation source');
  const after = await buildIdentity(root);
  assert.equal(after.commit, before.commit);
  assert.equal(after.sourceDirty, true);
  assert.notEqual(after.sourceHash, before.sourceHash);
  await assert.rejects(verify(root), /Stale Unity build identity/);
  await rm(source);
  assert.deepEqual(await buildIdentity(root), before);
});

test('preparation rejects added input and symlinked output', async t => {
  const root = await fixture(t);
  await writeFile(path.join(root, 'core/src/Core/New.cs'), 'new source');
  await assert.rejects(verify(root), /Stale Unity/);
  await rm(path.join(root, outputs.core));
  await symlink(path.join(root, outputs.bridge), path.join(root, outputs.core));
  await assert.rejects(outputSnapshot(root), /Symlink forbidden/);
});

test('presentation bounds are configuration and must be ordered', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'unity-presentation-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(path.join(root, 'schema'));
  await writeFile(path.join(root, 'schema/presentation.schema.json'), await readFile(new URL('../data/schema/presentation.schema.json', import.meta.url)));
  const presentation = JSON.parse(await readFile(new URL('../data/presentation.json', import.meta.url), 'utf8'));
  await writeFile(path.join(root, 'presentation.json'), JSON.stringify(presentation));
  assert.equal((await validateContent(root)).valid, true);
  presentation.camera.minHalfHeight = presentation.camera.maxHalfHeight + 1;
  await writeFile(path.join(root, 'presentation.json'), JSON.stringify(presentation));
  assert.match((await validateContent(root)).errors.join('\n'), /minHalfHeight must not exceed/);
});


test('preparation rejects edits made while the fresh build runs', async t => {
  const root = await fixture(t);
  const fakeDotnet = path.join(root, 'fake-dotnet');
  await writeFile(fakeDotnet, '#!/usr/bin/env bash\nprintf changed >> "$UNITY_EXPORT_TEST_SOURCE"\n');
  await chmod(fakeDotnet, 0o755);
  const previousDotnet = process.env.DOTNET, previousSource = process.env.UNITY_EXPORT_TEST_SOURCE;
  process.env.DOTNET = fakeDotnet;
  process.env.UNITY_EXPORT_TEST_SOURCE = path.join(root, 'core/src/Core/Simulation.cs');
  t.after(() => {
    if (previousDotnet === undefined) delete process.env.DOTNET; else process.env.DOTNET = previousDotnet;
    if (previousSource === undefined) delete process.env.UNITY_EXPORT_TEST_SOURCE; else process.env.UNITY_EXPORT_TEST_SOURCE = previousSource;
  });
  const manifest = await readFile(path.join(root, outputs.provenance));
  await assert.rejects(prepare(root), /Source\/data changed during Unity preparation/);
  assert.deepEqual(await readFile(path.join(root, outputs.provenance)), manifest);
});

for (const mutation of ['manifest-profile', 'manifest-hash', 'bridge-profile', 'bridge-hash']) {
  test('preparation rejects wrong profile even with internally consistent output hashes: ' + mutation, async t => {
    const root = await fixture(t), file = path.join(root, outputs.provenance);
    const manifest = JSON.parse(await readFile(file, 'utf8'));
    if (mutation === 'manifest-profile') manifest.profileName = 'production';
    if (mutation === 'manifest-hash') manifest.profileHash = '0'.repeat(64);
    if (mutation.startsWith('bridge-')) {
      const bridge = path.join(root, outputs.bridge);
      const text = await readFile(bridge, 'utf8');
      await writeFile(bridge, mutation === 'bridge-profile' ? text.replace('first-playable', 'production') : text.replace(manifest.profileHash, '0'.repeat(64)));
      manifest.outputs = await outputSnapshot(root);
    }
    await writeFile(file, JSON.stringify(manifest));
    await assert.rejects(verify(root), /profile/i);
  });
}

test('Unity profile selection is explicit and bounded', () => {
  assert.equal(selectedProfile('first-playable'), 'first-playable');
  assert.equal(selectedProfile('wave-1a'), 'wave-1a');
  assert.throws(() => selectedProfile('../wave-1a'), /Unsupported Unity content profile/);
});

test('wave bridge cannot pass verification as a historical profile', async t => {
  const root = await fixture(t);
  await assert.rejects(verify(root, 'wave-1a'), /profile mismatch/);
});
