import assert from 'node:assert/strict';
import { chmod, mkdtemp, mkdir, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { inputSnapshot, outputSnapshot, outputs, prepare, verify } from './unity-export.mjs';
import { validateContent } from './validate-content.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'unity-provenance-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const [relative, text] of Object.entries({
    'core/src/Core/Simulation.cs': 'source', 'core/src/Core/Core.csproj': 'project',
    'Directory.Build.props': 'props', 'global.json': 'sdk',
    'tools/prepare-unity.sh': 'script', 'tools/unity-export.mjs': 'helper',
    'data/tuning.json': '{}', 'data/bin/included.json': '{}', 'data/test/fixture.json': '{}',
    [outputs.bridge]: 'bridge', [outputs.core]: 'dll',
  })) {
    await mkdir(path.dirname(path.join(root, relative)), { recursive: true });
    await writeFile(path.join(root, relative), text);
  }
  const manifest = { contractVersion: 1, profileName: 'production', inputs: await inputSnapshot(root), outputs: await outputSnapshot(root) };
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

for (const relative of ['core/src/Core/Simulation.cs', 'Directory.Build.props', 'global.json', 'data/tuning.json', 'data/bin/included.json', outputs.bridge, outputs.core]) {
  test(`preparation rejects stale ${relative}`, async t => {
    const root = await fixture(t);
    await writeFile(path.join(root, relative), 'changed');
    await assert.rejects(verify(root), /Stale Unity/);
  });
}

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
