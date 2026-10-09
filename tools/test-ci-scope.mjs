import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { changedPaths, classify, parsePaths, selectScope } from './ci-scope.mjs';
import { checkMarkdown } from './check-docs.mjs';

test('docs-only additions/deletions and mixed paths classify conservatively', () => {
  assert.equal(classify(parsePaths('A\0docs/new.md\0D\0docs/old.md\0')), 'docs');
  for (const name of ['core/a.cs', 'data/a.json', 'data/schema/a.json', '.github/workflows/check.yml', 'tools/a.mjs', 'AGENTS.md']) {
    assert.equal(classify(parsePaths(`M\0docs/a.md\0M\0${name}\0`)), 'full');
  }
  assert.equal(classify([]), 'full');
  assert.equal(classify(parsePaths('D\0core/a.cs\0')), 'full');
  assert.equal(classify(parsePaths('R100\0core/a.cs\0docs/a.md\0')), 'full');
  assert.equal(classify(parsePaths('R100\0docs/a.md\0core/a.cs\0')), 'full');
  assert.equal(classify(parsePaths('R100\0docs/a.md\0docs/b.md\0')), 'docs');
  assert.deepEqual(parsePaths('M\0docs/a\nb.md\0'), ['docs/a\nb.md']);
  for (const raw of ['M\0docs/a.md', 'R100\0docs/a.md\0', 'X\0docs/a.md\0', 'M\0docs/../core/a.cs\0']) assert.throws(() => parsePaths(raw));
  assert.equal(selectScope({ GITHUB_EVENT_NAME: 'push' }), 'full');
  assert.equal(selectScope({ GITHUB_EVENT_NAME: 'workflow_dispatch' }), 'full');
  assert.equal(selectScope({ GITHUB_EVENT_NAME: 'pull_request', GITHUB_EVENT_PATH: '/missing-event' }), 'full');
});

test('real Git diff includes rename source, deleted paths, and unusual names', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bs-ci-scope-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init'); git('config', 'user.email', 'test@example.invalid'); git('config', 'user.name', 'test');
  fs.mkdirSync(path.join(root, 'docs')); fs.mkdirSync(path.join(root, 'core'));
  fs.writeFileSync(path.join(root, 'core/a.cs'), 'unique source\n'); fs.writeFileSync(path.join(root, 'docs/old.md'), '# Old\n');
  git('add', '.'); git('commit', '-m', 'base'); const base = git('rev-parse', 'HEAD');
  git('mv', 'core/a.cs', 'docs/moved.md'); git('rm', 'docs/old.md'); fs.writeFileSync(path.join(root, 'docs/a\nb.md'), '# New\n');
  git('add', '.'); git('commit', '-m', 'head'); const head = git('rev-parse', 'HEAD');
  const paths = changedPaths(base, head, root);
  assert.deepEqual(new Set(paths), new Set(['core/a.cs', 'docs/moved.md', 'docs/old.md', 'docs/a\nb.md']));
  assert.equal(classify(paths), 'full');
});

test('Markdown checks local inline/reference links while ignoring code, URLs and anchors', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bs-docs-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, 'docs')); fs.writeFileSync(path.join(root, 'docs/space (one).md'), '# ok\n');
  const valid = '[ok](<space (one).md#heading>)\n[web](https://example.invalid/a)\n[anchor](#whatever)\n`[code](missing.md)`\n```md\n[example](missing.md)\n```\n    [indented](missing.md)\n[ref]: space%20%28one%29.md\n';
  assert.deepEqual(checkMarkdown(valid, 'docs/a.md', root), []);
  assert.match(checkMarkdown('[bad](missing.md)\n', 'docs/a.md', root).join(), /missing repository link/);
  assert.match(checkMarkdown('[ref]: missing.md\n', 'docs/a.md', root).join(), /missing repository link/);
  assert.match(checkMarkdown('<<<<<<< HEAD\n', 'docs/a.md', root).join(), /merge conflict/);
  assert.match(checkMarkdown('no newline', 'docs/a.md', root).join(), /final newline/);
});
