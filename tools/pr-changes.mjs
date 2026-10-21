import { execFileSync } from 'node:child_process';

// Files a PR changes: its head against the merge base with the base commit the event names. CI checks out the merge commit
// of the PR and the *current* base branch, while pull_request.base.sha can be older, so diffing against HEAD would count
// the base branch's newer commits as the PR's own changes (#324).
export function prChangedFiles({ base, head, cwd = process.cwd() }) {
  return execFileSync('git', ['diff', '--name-only', `${base}...${head}`], { cwd, encoding: 'utf8' }).trim().split('\n').filter(Boolean);
}
