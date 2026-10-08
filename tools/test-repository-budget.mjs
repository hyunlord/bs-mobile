import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
const guard = path.resolve('tools/repository-budget.mjs');
function fixture(t) {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'repository-budget-'));
  t.after(() => fs.rmSync(cwd, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init', '-q'); git('config', 'user.name', 'Test'); git('config', 'user.email', 'test@example.invalid');
  function write(file, data) { fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true }); fs.writeFileSync(path.join(cwd, file), data); }
  function commit() { git('add', '-A'); git('commit', '-qm', 'fixture'); return git('rev-parse', 'HEAD'); }
  write('baseline', 'small'); const base = commit();
  function run(baseRef = base, head = 'HEAD') { return spawnSync(process.execPath, [guard, '--base', baseRef, '--head', head], { cwd, encoding: 'utf8' }); }
  return { cwd, git, write, commit, base, run };
}
test('exact decimal limit passes; one byte above fails from committed blobs', t => {
  const f = fixture(t); f.write('asset.bin', Buffer.alloc(5_000_000)); f.commit();
  assert.equal(f.run().status, 0); f.write('extra', 'x'); f.commit();
  f.write('asset.bin', 'working tree cannot hide blob');
  const result = f.run(); assert.equal(result.status, 1); assert.match(result.stderr, /RB001.*5000001/);
});
test('copies and renames count destination bytes even if Git detects similarity', t => {
  const f = fixture(t); f.write('old.bin', Buffer.alloc(3_000_000)); const base = f.commit();
  f.git('mv', 'old.bin', 'moved.bin'); f.write('copy.bin', Buffer.alloc(3_000_000)); f.commit();
  const result = f.run(base); assert.equal(result.status, 1); assert.match(result.stderr, /RB001.*6000000/);
});
test('modification growth cannot evade size budget', t => {
  const f = fixture(t); f.write('baseline', Buffer.alloc(5_000_006)); f.commit();
  assert.match(f.run().stderr, /RB001/);
});
for (const file of ['docs/a.ZIP', 'docs/evidence/S8/raw/a.json', 'docs/league/raw/a.csv', 'tools/new.PY', 'tools/test.pyi', 'odd\nname.py']) {
  test(`reject prohibited destination ${JSON.stringify(file)}`, t => {
    const f = fixture(t); f.write(file, 'sample'); f.commit();
    assert.equal(f.run().status, 1); assert.match(f.run().stderr, /RB00[23]/);
  });
}
test('renaming Python or archive to extensionless cannot bypass header detection', t => {
  const f = fixture(t); f.write('tools/helper', '#!/usr/bin/env python3\nprint(1)'); f.write('docs/disguised', Buffer.from([0x50,0x4b,3,4])); f.commit();
  const result = f.run(); assert.equal(result.status, 1); assert.match(result.stderr, /RB002/); assert.match(result.stderr, /RB003/);
});
test('unknown refs and unrelated histories fail closed', t => {
  const f = fixture(t); assert.equal(f.run('missing-branch').status, 1);
  f.git('checkout', '--orphan', 'unrelated'); f.git('rm', '-rf', '.'); f.write('other', 'x'); f.commit();
  assert.equal(f.run().status, 1);
});
test('merge-base excludes unrelated base-branch additions', t => {
  const f = fixture(t); f.git('branch', 'feature'); f.write('unrelated.bin', Buffer.alloc(6_000_000)); const base = f.commit();
  f.git('checkout', 'feature'); f.write('allowed.mjs', 'console.log(1)'); f.commit();
  assert.equal(f.run(base).status, 0);
});
test('symlinks and submodules cannot hide payloads', t => {
  const f = fixture(t); fs.symlinkSync('baseline', path.join(f.cwd, 'link')); f.commit();
  assert.match(f.run().stderr, /RB004/);
});
test('deletions do not offset additions and filenames are NUL-safe', t => {
  const f = fixture(t); f.write('old', Buffer.alloc(6_000_000)); const base = f.commit();
  f.git('rm', 'old'); f.write('space and\nnewline.bin', Buffer.alloc(5_000_001)); f.commit();
  assert.match(f.run(base).stderr, /RB001/);
});
test('raw-* folders are blocked while compact metrics/history and generic full/load are allowed', t => {
  const f = fixture(t); for (const name of ['docs/metrics/history/commit.json', 'docs/example/full/summary.md', 'docs/example/load/summary.md']) f.write(name, '{}');
  const allowed = f.commit(); assert.equal(f.run().status, 0);
  f.write('docs/evidence/R1/raw-runs/result.json', '{}'); f.commit(); assert.match(f.run(allowed).stderr, /RB002/);
});
test('new gitlinks fail closed instead of counting as empty files', t => {
  const f = fixture(t); f.git('update-index', '--add', '--cacheinfo', `160000,${f.base},embedded`); f.git('commit', '-qm', 'gitlink');
  assert.match(f.run().stderr, /RB004/);
});
test('PR comparison reads event head instead of checkout merge commit; invalid events fail', async t => {
  const { comparison } = await import('./repository-budget.mjs');
  const f = fixture(t); const eventFile = path.join(f.cwd, 'event.json');
  fs.writeFileSync(eventFile, JSON.stringify({pull_request:{base:{sha:'base'},head:{sha:'head'}}}));
  assert.deepEqual(comparison({GITHUB_EVENT_PATH:eventFile,GITHUB_EVENT_NAME:'pull_request'}), {base:'base',head:'head'});
  fs.writeFileSync(eventFile, '{}');
  assert.throws(() => comparison({GITHUB_EVENT_PATH:eventFile,GITHUB_EVENT_NAME:'pull_request'}), /RB000/);
  assert.throws(() => comparison({GITHUB_EVENT_PATH:eventFile,GITHUB_EVENT_NAME:'push'}), /RB000/);
  fs.writeFileSync(eventFile, JSON.stringify({before:'previous',after:'next'}));
  assert.deepEqual(comparison({GITHUB_EVENT_PATH:eventFile,GITHUB_EVENT_NAME:'push'}), {base:'previous',head:'next'});
});
test('PR CLI defaults use event head and never accept a missing event commit', t => {
  const f = fixture(t); f.write('docs/blocked.zip', 'bad'); const head = f.commit();
  f.git('checkout', '--detach', f.base);
  const eventFile = path.join(f.cwd, 'event.json');
  const run = () => spawnSync(process.execPath, [guard], {cwd:f.cwd,encoding:'utf8',env:{...process.env,GITHUB_EVENT_PATH:eventFile,GITHUB_EVENT_NAME:'pull_request'}});
  fs.writeFileSync(eventFile, JSON.stringify({pull_request:{base:{sha:f.base},head:{sha:head}}}));
  assert.match(run().stderr, /RB002/);
  fs.writeFileSync(eventFile, JSON.stringify({pull_request:{base:{sha:f.base},head:{sha:'missing'}}}));
  assert.match(run().stderr, /RB000/); assert.equal(run().status, 1);
});
for (const file of ['docs/evidence/S8/custom.json', 'docs/evidence/S8/run.results.json', 'docs/evidence/S8/run.metrics.JSON', 'docs/evidence/R1/manifest.json', 'docs/evidence/R1/original-hashes.json']) {
  test(`new evidence JSON is external, including manifest-like names: ${file}`, t => {
    const f = fixture(t); f.write(file, '{}'); f.commit();
    assert.match(f.run().stderr, /RB002/);
  });
}
test('compact evidence manifests and metrics history retain their distinct contracts', t => {
  const f = fixture(t);
  for (const name of ['docs/evidence/R1/manifest.csv','docs/evidence/R1/original-hashes.csv','docs/metrics/history/commit.json']) f.write(name, '{}');
  f.commit(); assert.equal(f.run().status, 0);
});
