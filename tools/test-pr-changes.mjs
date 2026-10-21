import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import test from 'node:test';
import { prChangedFiles } from './pr-changes.mjs';

test('a PR on an older base does not inherit the base branch changes merged after it (#324)', () => {
  const cwd = mkdtempSync(path.join(tmpdir(), 'bs-pr-changes-'));
  const git = (...args) => execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
  const commit = (file, text) => { mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true }); writeFileSync(path.join(cwd, file), text); git('add', '.'); git('commit', '-q', '-m', file); return git('rev-parse', 'HEAD'); };
  try {
    git('init', '-q', '-b', 'main'); git('config', 'user.email', 't@example.invalid'); git('config', 'user.name', 'Test');
    const oldBase = commit('README.md', 'a');
    git('switch', '-q', '-c', 'pr');
    const head = commit('core/Feature.cs', 'b');
    git('switch', '-q', 'main');
    commit('unity/Art.png', 'c');
    git('merge', '-q', '--no-edit', 'pr');
    assert.deepEqual(prChangedFiles({ base: oldBase, head, cwd }), ['core/Feature.cs']);
    assert.deepEqual(prChangedFiles({ base: oldBase, head: 'HEAD', cwd }).sort(), ['core/Feature.cs', 'unity/Art.png'], 'the merge checkout shows why HEAD is wrong');
  } finally { rmSync(cwd, { recursive: true, force: true }); }
});
