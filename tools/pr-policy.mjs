import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
const eventPath = process.env.GITHUB_EVENT_PATH;
const event = eventPath ? JSON.parse(fs.readFileSync(eventPath, 'utf8')) : {};
const pr = event.pull_request;
if (pr) {
  if (!/^(feat|fix|docs|test|ci|build|chore|refactor|perf)(\([\w/-]+\))?!?: .+/.test(pr.title)) throw new Error('PR title must use Conventional Commits');
  const issue = (pr.body ?? '').match(/(?:Closes|Fixes|Resolves)\s+#([1-9]\d*)\b/i);
  if (!issue) throw new Error('PR must link an issue with Closes #N');
  const record = JSON.parse(execFileSync('gh', ['api', `repos/${process.env.GITHUB_REPOSITORY}/issues/${issue[1]}`], {encoding:'utf8'}));
  if (record.pull_request) throw new Error('Linked number must be an issue, not a PR');
  const changed = execFileSync('git', ['diff', '--name-only', `${pr.base.sha}...HEAD`], {encoding:'utf8'}).trim().split('\n');
  const structural = changed.some(p => /(^|\/)([^/]+\.csproj|Directory\.Build\.[^/]+|global\.json|package(?:-lock)?\.json|[^/]+\.sln)$|^\.github\/workflows\/|^tools\/ArchitectureGuard\/|^data\/schema\//.test(p));
  if (structural && !changed.some(p => /^docs\/adr\/\d{4}-.+\.md$/.test(p))) throw new Error('Architecture/build/schema changes require an ADR in the same PR');
  for (const heading of ['Gate result', 'Verification', 'Screens']) if (!(pr.body ?? '').includes(heading)) throw new Error(`PR missing ${heading}`);
}
function commentText(line, file) {
  let quote = null;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quote) {
      if (c === '\\') i++;
      else if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '\x60') { quote = c; continue; }
    if (line.startsWith('//', i) || line.startsWith('/*', i) || (c === '*' && !line.slice(0, i).trim()) || (c === '#' && /\.(sh|ya?ml)$/.test(file))) return line.slice(i);
  }
  return '';
}
const tracked = execFileSync('git', ['ls-files','-z'], {encoding:'utf8'}).split('\0').filter(Boolean);
for (const file of tracked.filter(p => /\.(cs|mjs|sh|ya?ml)$/.test(p))) {
  const lines = fs.readFileSync(file, 'utf8').split('\n');
  lines.forEach((line,index) => {
    const comment = commentText(line, file);
    if (/\bTO[D]O\b/.test(comment) && !/#\d+\b/.test(comment)) throw new Error(`${file}:${index+1}: TODO must reference issue #N`);
  });
}
console.log('PR policy: issue, ADR, template and comment checks passed');
