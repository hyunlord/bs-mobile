import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { changedPaths } from './ci-scope.mjs';
import { comparison } from './repository-budget.mjs';

// Check file destinations, not external availability or renderer-specific anchors.
export function checkMarkdown(text, file, root) {
  const errors = [];
  if (!text.endsWith('\n')) errors.push('missing final newline');
  let fence = null;
  const prose = text.split('\n').map(line => {
    const marker = line.match(/^\s{0,3}(`{3,}|~{3,})/);
    if (marker) {
      if (!fence) fence = marker[1];
      else if (marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
      return '';
    }
    if (fence || /^(?: {4}|\t)/.test(line)) return '';
    if (/^(?:<{7}|>{7})(?:\s|$)/.test(line)) errors.push('merge conflict marker');
    return line.replace(/(`+).*?\1/g, '');
  }).join('\n');
  // Balanced parentheses in unquoted destinations are supported to one nested level.
  const destinations = [...prose.matchAll(/!?\[[^\]\n]*\]\(\s*(<[^>\n]+>|(?:[^\s()]+|\([^()]*\))+)(?:\s+["'][^\n]*["'])?\s*\)/g)].map(match => match[1]);
  for (const match of prose.matchAll(/^\s{0,3}\[[^\]\n]+\]:\s*(<[^>\n]+>|\S+)/gm)) destinations.push(match[1]);
  for (let target of destinations) {
    target = target.replace(/^<|>$/g, '');
    if (/^(?:[a-z][a-z0-9+.-]*:|\/\/|#)/i.test(target)) continue;
    target = target.split(/[?#]/)[0];
    if (!target) continue;
    try { target = decodeURIComponent(target); } catch { errors.push(`invalid URL encoding: ${target}`); continue; }
    const resolved = target.startsWith('/') ? path.resolve(root, `.${target}`) : path.resolve(root, path.dirname(file), target);
    if (!resolved.startsWith(root + path.sep) || !fs.existsSync(resolved)) errors.push(`missing repository link: ${target}`);
  }
  return errors;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const refs = comparison();
  const resolve = ref => execFileSync('git', ['rev-parse', '--verify', '--end-of-options', `${ref}^{commit}`], { encoding: 'utf8' }).trim();
  const files = changedPaths(resolve(refs.base), resolve(refs.head)).filter(file => file.startsWith('docs/') && file.endsWith('.md') && fs.existsSync(file));
  const errors = files.flatMap(file => checkMarkdown(fs.readFileSync(file, 'utf8'), file, process.cwd()).map(error => `${file}: ${error}`));
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else console.log(`Document checks passed: ${files.length} changed Markdown files (file links and format)`);
}
