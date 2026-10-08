import fs from 'node:fs';
import { execFileSync, spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export const BYTE_LIMIT = 5_000_000;
function git(cwd, args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
}
function resolveCommit(cwd, ref) {
  if (!ref || ref.startsWith('-')) throw new Error('RB000: missing or invalid commit reference');
  return git(cwd, ['rev-parse', '--verify', '--end-of-options', `${ref}^{commit}`]).trim();
}
function tree(cwd, ref) {
  return new Map(git(cwd, ['ls-tree', '-rlz', ref]).split('\0').filter(Boolean).map(entry => {
    const tab = entry.indexOf('\t');
    const [mode, type, oid, size] = entry.slice(0, tab).trim().split(/\s+/);
    return [entry.slice(tab + 1), { mode, type, oid, size: Number(size) }];
  }));
}
function header(cwd, oid) {
  return new Promise((resolve, reject) => {
    const child = spawn('git', ['cat-file', 'blob', oid], { cwd, stdio: ['ignore', 'pipe', 'pipe'] });
    let prefix = Buffer.alloc(0); let complete = false;
    child.stdout.on('data', chunk => {
      prefix = Buffer.concat([prefix, chunk.subarray(0, Math.max(0, 512 - prefix.length))]);
      if (prefix.length === 512) { complete = true; child.kill(); }
    });
    child.on('error', reject);
    child.on('close', code => code === 0 || complete ? resolve(prefix) : reject(new Error(`RB000: cannot read blob ${oid}`)));
  });
}
function archive(prefix) {
  const hex = prefix.subarray(0, 8).toString('hex');
  return /^(504b0304|504b0506|504b0708|1f8b|377abcaf271c|52617221|425a68|fd377a585a00)/.test(hex)
    || prefix.subarray(257, 262).toString() === 'ustar';
}
export async function checkBudget({ cwd = process.cwd(), base, head = 'HEAD' }) {
  const baseCommit = resolveCommit(cwd, base);
  const headCommit = resolveCommit(cwd, head);
  const ancestor = git(cwd, ['merge-base', baseCommit, headCommit]).trim();
  if (!ancestor) throw new Error('RB000: no merge base');
  const before = tree(cwd, ancestor), after = tree(cwd, headCommit);
  const errors = [];
  let addedBytes = 0, growthBytes = 0, addedFiles = 0;
  for (const [file, entry] of after) {
    const old = before.get(file);
    if (old?.oid === entry.oid && old.mode === entry.mode) continue;
    const label = JSON.stringify(file);
    if (entry.type !== 'blob' || !['100644', '100755'].includes(entry.mode)) {
      errors.push(`RB004: ${label}: new/changed symlinks and submodules are not repository tool/content blobs`);
      continue;
    }
    if (!old) { addedBytes += entry.size; addedFiles++; }
    else growthBytes += Math.max(0, entry.size - (Number.isFinite(old.size) ? old.size : 0));
    const lower = file.toLowerCase();
    const docs = lower.startsWith('docs/');
    if (lower.startsWith('docs/evidence/') && lower.endsWith('.json')) {
      errors.push(`RB002: ${label}: evidence JSON belongs in external assets; keep compact CSV manifests/summaries in the repository`);
    }
    if (docs && (/(^|\/)raw(?:-[^/]*)?\//.test(lower)
      || /\.(zip|7z|rar|tar|tgz|gz|bz2|xz|zst|lz4|cab|iso)$/.test(lower))) {
      errors.push(`RB002: ${label}: docs archives and raw/raw-* directories belong in external evidence assets`);
    }
    if (/\.(py|pyi|pyw|pyc|pyo|pyz)$/.test(lower)) {
      errors.push(`RB003: ${label}: Python additions/changes are forbidden; use Node.js or .NET`);
    }
    const prefix = await header(cwd, entry.oid);
    if (/^#![^\r\n]*\bpython(?:\d+(?:\.\d+)*)?\b/i.test(prefix.toString('utf8'))) {
      errors.push(`RB003: ${label}: Python shebang is forbidden; use Node.js or .NET`);
    }
    if (docs && archive(prefix)) errors.push(`RB002: ${label}: archive signature is forbidden in docs`);
  }
  const chargedBytes = addedBytes + growthBytes;
  if (chargedBytes > BYTE_LIMIT) errors.unshift(`RB001: ${chargedBytes} bytes exceeds ${BYTE_LIMIT} bytes (new paths ${addedBytes} + positive modified growth ${growthBytes}; deletions never offset)`);
  return { base: baseCommit, mergeBase: ancestor, head: headCommit, limitBytes: BYTE_LIMIT, addedFiles, addedBytes, growthBytes, chargedBytes, errors };
}
export function comparison(env = process.env) {
  if (env.GITHUB_EVENT_PATH) {
    const event = JSON.parse(fs.readFileSync(env.GITHUB_EVENT_PATH, 'utf8'));
    if (env.GITHUB_EVENT_NAME === 'pull_request' || event.pull_request) {
      if (!event.pull_request?.base?.sha || !event.pull_request?.head?.sha) throw new Error('RB000: PR event lacks base/head SHA');
      return { base: event.pull_request.base.sha, head: event.pull_request.head.sha };
    }
    if (env.GITHUB_EVENT_NAME === 'push') {
      if (!event.before || /^0+$/.test(event.before) || !event.after) throw new Error('RB000: push event lacks existing before/after commits');
      return { base: event.before, head: event.after };
    }
  }
  return { base: 'origin/main', head: 'HEAD' };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const args = process.argv.slice(2);
    let refs;
    if (args.length) {
      if (args.length !== 4 || args[0] !== '--base' || args[2] !== '--head') throw new Error('RB000: usage: --base REF --head REF');
      refs = { base: args[1], head: args[3] };
    } else refs = comparison();
    const result = await checkBudget(refs);
    console.log(JSON.stringify(result, null, 2));
    if (result.errors.length) { console.error(result.errors.join('\n')); process.exitCode = 1; }
  } catch (error) {
    console.error(`RB000: repository budget failed closed: ${error.message}`);
    process.exitCode = 1;
  }
}
