import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import test from 'node:test';
import { checkUnityRecord, parseUnityRecord } from './unity-test-record.mjs';

function repo() {
  const cwd = mkdtempSync(path.join(tmpdir(), 'bs-unity-record-'));
  const git = (...args) => execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
  git('init', '-q'); git('config', 'user.email', 't@example.invalid'); git('config', 'user.name', 'Test');
  mkdirSync(path.join(cwd, 'unity'), { recursive: true });
  const commit = (file, text) => { writeFileSync(path.join(cwd, file), text); git('add', '.'); git('commit', '-q', '-m', file); return git('rev-parse', 'HEAD'); };
  return { cwd, commit, cleanup: () => rmSync(cwd, { recursive: true, force: true }) };
}
const record = (sha, failed = 0) => [
  '## Unity tests', `commit: ${sha}`,
  `first-playable EditMode: passed 496, failed ${failed}, skipped 22`,
  'first-playable PlayMode: passed 27, failed 0, skipped 25',
  'wave-1a EditMode: passed 504, failed 0, skipped 14',
  'wave-1a PlayMode: passed 41, failed 0, skipped 11', '', '## Screens', 'N/A'].join('\n');

test('a PR without unity/ changes needs no record', () => {
  assert.deepEqual(checkUnityRecord({ changed: ['docs/a.md', 'core/x.cs'], body: '', head: 'HEAD' }), []);
});
test('a unity/ PR without the section is rejected with the command to run', () => {
  const errors = checkUnityRecord({ changed: ['unity/Assets/A.cs'], body: '## Gate result\nPASS', head: 'HEAD' });
  assert.equal(errors.length, 1); assert.match(errors[0], /tools\/unity-tests\.sh/);
});
test('parses the four suites and the commit', () => {
  const parsed = parseUnityRecord(record('abc1234'));
  assert.equal(parsed.commit, 'abc1234'); assert.equal(Object.keys(parsed.suites).length, 4);
  assert.deepEqual(parsed.suites['wave-1a PlayMode'], { passed: 41, failed: 0, skipped: 11 });
});
test('a record pasted beneath the template heading still parses', () => {
  const body = ['Closes #1', '## Unity tests', 'unity/를 바꾸는 PR은 필수. ./tools/unity-tests.sh 출력을 붙인다.', '', record('abc1234')].join('\n');
  const parsed = parseUnityRecord(body);
  assert.equal(parsed.commit, 'abc1234'); assert.equal(Object.keys(parsed.suites).length, 4);
});
test('a record at the head with zero failures passes; failures, gaps and later unity/ edits do not', () => {
  const r = repo();
  try {
    const tested = r.commit('unity/A.cs', 'a');
    assert.deepEqual(checkUnityRecord({ changed: ['unity/A.cs'], body: record(tested), head: tested, cwd: r.cwd }), []);
    assert.match(checkUnityRecord({ changed: ['unity/A.cs'], body: record(tested, 2), head: tested, cwd: r.cwd }).join('\n'), /2 failure\(s\) in first-playable EditMode/);
    assert.match(checkUnityRecord({ changed: ['unity/A.cs'], body: record(tested).replace(/^wave-1a PlayMode.*$/m, ''), head: tested, cwd: r.cwd }).join('\n'), /lacks "wave-1a PlayMode/);
    const docsOnly = r.commit('notes.md', 'n');
    assert.deepEqual(checkUnityRecord({ changed: ['unity/A.cs'], body: record(tested), head: docsOnly, cwd: r.cwd }), [], 'later non-unity commits keep the record valid');
    const later = r.commit('unity/A.cs', 'b');
    assert.match(checkUnityRecord({ changed: ['unity/A.cs'], body: record(tested), head: later, cwd: r.cwd }).join('\n'), /unity\/ changed after the recorded/);
    assert.match(checkUnityRecord({ changed: ['unity/A.cs'], body: record('deadbeef'), head: later, cwd: r.cwd }).join('\n'), /not in this repository/);
  } finally { r.cleanup(); }
});
