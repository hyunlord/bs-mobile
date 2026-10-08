import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { parseCsv } from './csv.mjs';

export function verifyEvidence(manifest, directory) {
  const content = new TextDecoder('utf-8', { fatal: true }).decode(fs.readFileSync(manifest));
  const rows = parseCsv(content);
  if (!rows.length) throw new Error('Empty manifest');
  const assetMode = Object.hasOwn(rows[0], 'asset');
  const field = assetMode ? 'asset' : 'path';
  parseCsv(content, [field, 'bytes', 'sha256']);
  const root = path.resolve(directory);
  if (fs.lstatSync(root).isSymbolicLink() || !fs.lstatSync(root).isDirectory()) throw new Error('Root must be a real directory');
  const seen = new Set();
  let bytes = 0;
  for (const row of rows) {
    const name = row[field];
    if (!name || name.includes('\\') || name.includes('\0') || name.startsWith('/') || /^[A-Za-z]:/.test(name) || name.split('/').some(part => !part || part === '.' || part === '..')) throw new Error(`Unsafe path: ${name}`);
    if (assetMode && name.includes('/')) throw new Error('Asset names must be basenames');
    if (seen.has(name)) throw new Error(`Duplicate path: ${name}`);
    seen.add(name);
    if (!/^(0|[1-9][0-9]*)$/.test(row.bytes) || !Number.isSafeInteger(Number(row.bytes)) || !/^[a-f0-9]{64}$/.test(row.sha256)) throw new Error(`Invalid size/hash: ${name}`);
    let target = root;
    for (const part of name.split('/')) { target = path.join(target, part); if (fs.lstatSync(target).isSymbolicLink()) throw new Error(`Symlink: ${name}`); }
    if (!fs.lstatSync(target).isFile()) throw new Error(`Not a file: ${name}`);
    const data = fs.readFileSync(target);
    if (data.length !== Number(row.bytes) || createHash('sha256').update(data).digest('hex') !== row.sha256) throw new Error(`Size/hash mismatch: ${name}`);
    bytes += data.length;
  }
  const actual = [];
  function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const target = path.join(dir, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`Symlink: ${target}`);
      if (entry.isDirectory()) walk(target);
      else if (entry.isFile()) actual.push(path.relative(root, target).split(path.sep).join('/'));
      else throw new Error(`Unsupported file: ${target}`);
    }
  }
  walk(root);
  const extras = actual.filter(name => !seen.has(name)).sort();
  if (!assetMode && extras.length) throw new Error(`Unexpected files: ${extras.join(', ')}`);
  return { mode: assetMode ? 'asset-payloads-only' : 'source-exact-set', verifiedFiles: rows.length, verifiedBytes: bytes, unverifiedExtraFiles: extras };
}
if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  try {
    if (process.argv.length !== 4) throw new Error('usage: node tools/verify-evidence.mjs MANIFEST.csv ROOT_DIRECTORY');
    console.log(JSON.stringify(verifyEvidence(process.argv[2], process.argv[3]), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
