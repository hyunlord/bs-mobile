// Resolve a catalog into a byte-verified local cache; no placement data is read.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
const [catalogPath, sourceIndexPath, cachePath] = process.argv.slice(2);
if (!catalogPath || !sourceIndexPath || !cachePath) throw new Error('usage: node cache-terrain-layout-assets.mjs layout.json local-asset-sources.json cache-directory');
const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
const remote = sourceIndexPath === '--remote';
const sourceIndex = remote ? null : JSON.parse(fs.readFileSync(sourceIndexPath, 'utf8'));
if (remote) {
  const byDigest = new Map();
  for (const asset of catalog.assets) if (asset.source.url) {
    const digest = asset.source.containerSha256 ?? asset.source.sha256;
    byDigest.set(digest, asset.source.url);
  }
  fs.mkdirSync(cachePath, { recursive: true });
  const queue = [...byDigest];
  await Promise.all(Array.from({ length: 4 }, async () => {
    while (queue.length) {
      const [digest, url] = queue.shift();
      const target = path.join(cachePath, digest + '.png');
      let bytes;
      if (fs.existsSync(target)) bytes = fs.readFileSync(target);
      else { const response = await fetch(url, { signal: AbortSignal.timeout(60000) }); if (!response.ok) throw new Error(`${response.status}: ${url}`); bytes = Buffer.from(await response.arrayBuffer()); }
      if (crypto.createHash('sha256').update(bytes).digest('hex') !== digest) throw new Error(`Remote hash mismatch: ${url}`);
      if (!fs.existsSync(target)) fs.writeFileSync(target, bytes, { flag: 'wx' });
    }
  }));
  execFileSync('swift', [path.join(path.dirname(new URL(import.meta.url).pathname), 'derive-terrain-layout-assets.swift'), catalogPath, cachePath], { stdio: 'inherit' });
  for (const asset of catalog.assets) {
    const bytes = fs.readFileSync(path.join(cachePath, asset.source.sha256 + '.png'));
    if (crypto.createHash('sha256').update(bytes).digest('hex') !== asset.source.sha256) throw new Error(`Unresolved or changed ${asset.id}`);
  }
  console.log(`Cold-cache remote resolved ${catalog.assets.length} catalog assets`);
  process.exit(0);
}
fs.mkdirSync(cachePath, { recursive: true });
for (const asset of catalog.assets) {
  const bytes = fs.readFileSync(sourceIndex[asset.id]);
  const digest = crypto.createHash('sha256').update(bytes).digest('hex');
  if (digest !== asset.source.sha256) throw new Error(`Source digest mismatch: ${asset.id}`);
  const target = path.join(cachePath, `${digest}.png`);
  if (fs.existsSync(target)) {
    if (!fs.readFileSync(target).equals(bytes)) throw new Error(`Cache collision: ${asset.id}`);
  } else fs.writeFileSync(target, bytes, { flag: 'wx' });
}
console.log(`Verified ${catalog.assets.length} catalog assets into ${cachePath}`);
