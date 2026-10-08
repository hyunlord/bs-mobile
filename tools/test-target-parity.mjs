import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { validateBinding, validatePair, validateResults, runProcess } from './verify-target-parity.mjs';
import { canonical } from './s4b-contract.mjs';
const hash = text => createHash('sha256').update(text).digest('hex');
const a = { targetFramework: '.NETCoreApp,Version=v8.0', mvid: '11111111-1111-1111-1111-111111111111', sha256: hash('net8'), location: '/host/net8/Core.dll' };
const b = { targetFramework: '.NETStandard,Version=v2.1', mvid: '22222222-2222-2222-2222-222222222222', sha256: hash('standard'), location: '/host/compat/Core.dll' };
test('binding validates actual target, path and bytes; rejects wrong target DLL', () => {
  const expected = { targetFramework: a.targetFramework, location: a.location, sha256: a.sha256 };
  validateBinding(a, expected);
  for (const change of [{ targetFramework: b.targetFramework }, { location: b.location }, { sha256: b.sha256 }, { mvid: 'invalid' }]) assert.throws(() => validateBinding({ ...a, ...change }, expected));
});
test('targets must have distinct MVID, bytes and physical locations', () => {
  validatePair(a, b);
  for (const key of ['mvid', 'sha256', 'location']) assert.throws(() => validatePair(a, { ...b, [key]: a[key] }));
});
test('all three entire results match immutable baseline, including semantic fields', () => {
  const result = { hash: 'A'.repeat(64), ticks: 21600, survived: true, damage: 3 };
  const golden = { hash: result.hash, gameplaySha256: hash(canonical(result)) };
  validateResults([result, result, result], golden);
  validateResults([1, 2, 3].map(n => ({ ...result, runMetadata: { commit: String(n) } })), golden);
  assert.throws(() => validateResults([result, result], golden));
  assert.throws(() => validateResults([result, result, { ...result, damage: 4 }], golden));
  assert.throws(() => validateResults([result, result, result], { ...golden, hash: 'B'.repeat(64) }));
});
test('configuration metadata remains frozen while build provenance may change', () => {
  const result = { hash: 'A'.repeat(64), damage: 3, runMetadata: { profileId: 'core:fixed', commit: 'old' } };
  const golden = { hash: result.hash, gameplaySha256: hash(canonical({ hash: result.hash, damage: 3 })), metadataDefinitionSha256: hash(canonical({ profileId: 'core:fixed' })) };
  validateResults(Array(3).fill({ ...result, runMetadata: { profileId: 'core:fixed', commit: 'new' } }), golden);
  assert.throws(() => validateResults(Array(3).fill({ ...result, runMetadata: { profileId: 'core:changed', commit: 'new' } }), golden));
});

test('large CLI stdout is drained without buffering; failures identify case and target', async () => {
  await runProcess(process.execPath, ['-e', "process.stdout.write('x'.repeat(5 * 1024 * 1024))"], 'large/net8');
  await assert.rejects(runProcess(process.execPath, ['-e', "process.stderr.write('failure');process.exit(7)"], 'case/standard21'), /case\/standard21.*7.*failure/);
});

test('historical export requires exact tree and reachable published commit before writing', async () => {
  const fs = await import('node:fs/promises');
  const os = await import('node:os');
  const path = await import('node:path');
  const { execFileSync } = await import('node:child_process');
  const { exportFrozenData } = await import('./verify-target-parity.mjs');
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'parity-tree-'));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
  try {
    git('init', '-q'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid');
    await fs.mkdir(path.join(root, 'data')); await fs.writeFile(path.join(root, 'data/input.json'), '{"frozen":true}\n');
    git('add', 'data'); git('commit', '-qm', 'Freeze fixture');
    const commit = git('rev-parse', 'HEAD'), tree = git('rev-parse', 'HEAD:data');
    const target = path.join(root, 'export');
    await assert.rejects(exportFrozenData(commit, target, '0'.repeat(40), root), /data tree mismatch/);
    await assert.rejects(fs.stat(target), { code: 'ENOENT' });
    await assert.rejects(exportFrozenData('0'.repeat(40), target, tree, root), /Missing historical parity commit/);
    assert.equal(await exportFrozenData(commit, target, tree, root), 1);
    assert.equal(await fs.readFile(path.join(target, 'input.json'), 'utf8'), '{"frozen":true}\n');
  } finally { await fs.rm(root, { recursive: true, force: true }); }
});
