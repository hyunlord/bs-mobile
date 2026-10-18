import { execFileSync } from 'node:child_process';

// A PR that changes unity/ must carry the result of tools/unity-tests.sh: both content profiles, EditMode and PlayMode,
// run on the Mac before merging (CI has no Unity runner; ADR0068). The record names the tested commit, which must be in
// the PR head's history with no unity/ change after it, and every suite must have zero failures.
export const SUITES = ['first-playable EditMode', 'first-playable PlayMode', 'wave-1a EditMode', 'wave-1a PlayMode'];

export function parseUnityRecord(body) {
  const text = body ?? '';
  const start = text.search(/^## Unity tests\s*$/m);
  if (start < 0) return null;
  const rest = text.slice(start).split('\n').slice(1);
  const end = rest.findIndex(line => /^## /.test(line));
  const section = (end < 0 ? rest : rest.slice(0, end)).join('\n');
  const commit = section.match(/^commit:\s*`?([0-9a-f]{7,40})`?\s*$/m)?.[1] ?? null;
  const suites = {};
  for (const name of SUITES) {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = section.match(new RegExp(`^${escaped}:\\s*passed (\\d+), failed (\\d+), skipped (\\d+)\\s*$`, 'm'));
    if (match) suites[name] = { passed: Number(match[1]), failed: Number(match[2]), skipped: Number(match[3]) };
  }
  return { commit, suites };
}

export function checkUnityRecord({ changed, body, head, cwd = process.cwd() }) {
  if (!changed.some(path => path.startsWith('unity/'))) return [];
  const how = 'run tools/unity-tests.sh on a clean checkout of the PR head and paste its "## Unity tests" block into the PR body (docs/runbooks/unity-tests.md)';
  const record = parseUnityRecord(body);
  if (!record) return [`PR changes unity/ but has no "## Unity tests" record; ${how}`];
  const errors = [];
  if (!record.commit) errors.push('Unity tests record has no "commit: <sha>" line');
  for (const name of SUITES) {
    const suite = record.suites[name];
    if (!suite) errors.push(`Unity tests record lacks "${name}: passed N, failed N, skipped N"`);
    else if (suite.failed !== 0) errors.push(`Unity tests record shows ${suite.failed} failure(s) in ${name}`);
    else if (suite.passed === 0) errors.push(`Unity tests record shows no passing tests in ${name}`);
  }
  if (record.commit) {
    const git = args => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    let full = null;
    try { full = git(['rev-parse', '--verify', `${record.commit}^{commit}`]).trim(); }
    catch { errors.push(`Unity tests record commit ${record.commit} is not in this repository`); }
    if (full) {
      try { git(['merge-base', '--is-ancestor', full, head]); }
      catch { errors.push(`Unity tests record commit ${record.commit} is not in the PR head's history`); }
      const later = git(['diff', '--name-only', full, head, '--', 'unity']).trim();
      if (later) errors.push(`unity/ changed after the recorded Unity test commit ${record.commit}; ${how}`);
    }
  }
  return errors;
}
