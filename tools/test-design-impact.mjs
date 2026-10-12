import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { changedPaths, classify, parsePaths } from './ci-scope.mjs';
import { designImpact, needsDesignReview, reportDesignImpact } from './design-impact.mjs';

const base = 'a'.repeat(40), head = 'b'.repeat(40);
function fixture(t, event) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bs-design-impact-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const file = path.join(root, 'event.json');
  fs.writeFileSync(file, JSON.stringify(event));
  return file;
}

test('product paths warn only when no design path accompanies them', () => {
  for (const product of ['data/a.json', 'core/src/SowSiege.Core/World.cs', 'unity/Assets/Game/App/Hud.cs', 'unity/Assets/Game/View/Card.cs', 'unity/Assets/Game/Input/Touch.cs']) {
    assert.equal(needsDesignReview([product]), true);
    assert.equal(needsDesignReview([product, 'docs/review/report.md']), true);
    assert.equal(needsDesignReview([product, 'docs/design/gdd/05-screens.md']), false);
  }
  for (const unrelated of ['core/tests/test.cs', 'core/src/SowSiege.Sim/Program.cs', 'unity/Assets/Game/Tests/test.cs', 'tools/check.sh', 'docs/design/a.md', 'AGENTS.md']) assert.equal(needsDesignReview([unrelated]), false);
});

test('renames and deletions retain all source and destination paths', () => {
  assert.equal(needsDesignReview(parsePaths('D\0data/old.json\0')), true);
  assert.equal(needsDesignReview(parsePaths('R100\0unity/Assets/Game/View/Old.cs\0archive/Old.cs\0')), true);
  assert.equal(needsDesignReview(parsePaths('R100\0archive/Old.cs\0unity/Assets/Game/View/New.cs\0')), true);
  assert.equal(needsDesignReview(parsePaths('M\0data/a.json\0D\0docs/design/old.md\0')), false);
  assert.equal(classify(['docs/design/a.md', 'AGENTS.md']), 'full');
});

test('PR exact refs use shared changedPaths contract', t => {
  const event = fixture(t, { pull_request: { base: { sha: base }, head: { sha: head } } });
  const result = designImpact({ GITHUB_EVENT_NAME: 'pull_request', GITHUB_EVENT_PATH: event }, '/repo', (a, b, cwd) => {
    assert.deepEqual([a, b, cwd], [base, head, '/repo']);
    return ['data/a.json'];
  });
  assert.equal(result, 'missing');
  assert.equal(typeof changedPaths, 'function');
});

test('validated dependency context takes precedence over workflow dispatch payload', t => {
  const event = fixture(t, { pull_request: { base: { sha: base }, head: { sha: head } } });
  assert.equal(designImpact({ GITHUB_EVENT_NAME: 'workflow_dispatch', GITHUB_EVENT_PATH: '/missing', DEPENDENCY_EVENT_PATH: event }, '/repo', () => ['core/tests/test.csproj']), 'covered');
});

test('push refs checked while dispatch without comparison and unavailable diffs are unknown', t => {
  const event = fixture(t, { before: base, after: head });
  const env = { GITHUB_EVENT_NAME: 'push', GITHUB_EVENT_PATH: event };
  assert.equal(designImpact(env, '/repo', () => ['data/a.json']), 'missing');
  assert.equal(designImpact(env, '/repo', () => { throw new Error('missing history'); }), 'unknown');
  assert.equal(designImpact({ ...env, GITHUB_EVENT_NAME: 'workflow_dispatch' }), 'unknown');
  assert.equal(designImpact({ ...env, GITHUB_EVENT_PATH: '/missing' }), 'unknown');
  const zero = fixture(t, { before: '0'.repeat(40), after: head });
  assert.equal(designImpact({ ...env, GITHUB_EVENT_PATH: zero }), 'unknown');
});

test('one nonfailing annotation for either missing or unknown, none for covered', () => {
  for (const status of ['missing', 'unknown', 'covered']) {
    const lines = [];
    assert.doesNotThrow(() => reportDesignImpact(status, line => lines.push(line)));
    assert.equal(lines.length, 1);
    assert.equal(lines.filter(line => line.startsWith('::warning')).length, status === 'covered' ? 0 : 1);
    assert.equal(lines.some(line => line.startsWith('::error')), false);
  }
});
