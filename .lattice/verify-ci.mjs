import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';

const [cachePath, sitePath] = process.argv.slice(2);
assert(cachePath && sitePath, 'Usage: verify-ci.mjs <cache-directory> <site-directory>');
const baselineCommit = 'de2a5713ce7c9d9c9f38a663e0a1cae66bbfe5b5';
const snapshots = JSON.parse(readFileSync(join(cachePath, 'snapshots.json'), 'utf8'));
const baseline = snapshots.find(snapshot => snapshot.commit === baselineCommit && snapshot.coverage === 'current-lens-projection');
assert(baseline, 'Historical catalog must be retained with current-lens projection provenance');
for (const args of [
  [join(cachePath, 'graph.json')],
  [join(cachePath, baseline.artifactPath), '--baseline'],
]) execFileSync(process.execPath, ['.lattice/verify-designed.mjs', ...args], { stdio: 'inherit' });
execFileSync(process.execPath, ['.lattice/verify-runtime.mjs', join(cachePath, 'graph.json')], { stdio: 'inherit' });
execFileSync(process.execPath, ['.lattice/test-primitive-support.mjs', join(cachePath, 'graph.json')], { stdio: 'inherit' });
execFileSync(process.execPath, ['.lattice/verify-picture-map.mjs', join(cachePath, 'graph.json')], { stdio: 'inherit' });
const graph = JSON.parse(readFileSync(join(cachePath, 'graph.json'), 'utf8'));
const exported = JSON.parse(readFileSync(join(sitePath, 'graph.json'), 'utf8'));
assert.equal(exported.hash, graph.hash, 'Published graph must be the verified current graph');
console.log(JSON.stringify({ verifiedCurrentAndBaseline: true, graphHash: graph.hash }));
