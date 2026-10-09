import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function changedPaths(base, head, cwd = process.cwd()) {
  if (![base, head].every(ref => /^[0-9a-f]{40}$/.test(ref))) throw new Error('Expected exact commit SHAs');
  const raw = execFileSync('git', ['diff', '--name-status', '-z', '--find-renames', `${base}...${head}`, '--'], { cwd, encoding: 'utf8' });
  return parsePaths(raw);
}
export function parsePaths(raw) {
  if (!raw) return [];
  if (!raw.endsWith('\0')) throw new Error('Incomplete path list');
  const fields = raw.slice(0, -1).split('\0'), paths = [];
  while (fields.length) {
    const status = fields.shift();
    if (!/^(?:[AMD]|[RC]\d+)$/.test(status)) throw new Error(`Unsupported diff status: ${status}`);
    const count = /^[RC]/.test(status) ? 2 : 1;
    for (let n = 0; n < count; n++) {
      const path = fields.shift();
      if (!path || path.startsWith('/') || path.split('/').some(part => part === '..' || part === '.')) throw new Error('Invalid path');
      paths.push(path);
    }
  }
  return [...new Set(paths)];
}
export function classify(paths) {
  return paths.length > 0 && paths.every(path => path.startsWith('docs/')) ? 'docs' : 'full';
}
export function selectScope(env = process.env, cwd = process.cwd()) {
  if (env.GITHUB_EVENT_NAME !== 'pull_request') return 'full';
  try {
    const { pull_request: pr } = JSON.parse(fs.readFileSync(env.GITHUB_EVENT_PATH, 'utf8'));
    return classify(changedPaths(pr.base.sha, pr.head.sha, cwd));
  } catch (error) {
    console.warn(`Scope uncertain; running full gate: ${error.message}`);
    return 'full';
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const scope = selectScope();
  console.log(`CI scope: ${scope}`);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `scope=${scope}\n`);
}
