#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { mkdtemp, readdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { RateLimitSkipped, runGitHub } from './github-rate-limit.mjs';

const tag = 'metrics-history';
export function selectHistory(assets) {
  return assets.filter(asset => /^[a-f0-9]{40}-[a-f0-9]{16}\.json$/i.test(asset.name))
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt) || a.name.localeCompare(b.name))
    .slice(0, 30).map(asset => asset.name);
}

export async function publishMetrics({ repository, runId, github = runGitHub }) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !/^\d+$/.test(runId ?? '')) {
    throw new Error('GITHUB_REPOSITORY and numeric RUN_ID are required');
  }
  const root = await mkdtemp(join(tmpdir(), 'durable-metrics-'));
  let incoming;
  const output = join(root, 'durable');
  const history = join(output, 'history');
  const pages = JSON.parse(await github(['api', `repos/${repository}/actions/runs/${runId}/artifacts`, '--paginate', '--slurp']));
  const artifacts = pages.flatMap(page => page.artifacts).filter(artifact => artifact.name.startsWith('evidence-'));
  if (artifacts.length !== 1 || artifacts[0].expired) throw new Error('Expected exactly one unexpired evidence artifact');
  await github(async () => {
    incoming = await mkdtemp(join(root, 'incoming-'));
    return ['run', 'download', runId, '--repo', repository, '--name', artifacts[0].name, '--dir', incoming];
  });
  const render = () => {
    const result = spawnSync(process.execPath, [fileURLToPath(new URL('./metrics.mjs', import.meta.url)), join(incoming, 'metrics.json'), output], { encoding: 'utf8' });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`Metrics validation/render failed: ${result.stderr}`);
  };
  render();
  const [record] = await readdir(history);
  const release = JSON.parse(await github(['release', 'view', tag, '--repo', repository, '--json', 'assets']));
  for (const name of selectHistory(release.assets)) {
    if (name !== record) await github(['release', 'download', tag, '--repo', repository, '--pattern', name, '--dir', history, '--clobber']);
  }
  render();
  await github(['release', 'upload', tag, join(history, record), join(output, 'index.html'), '--repo', repository, '--clobber']);
  console.log(`Published ${record} and index.html; historical release assets retained.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  publishMetrics({ repository: process.env.GITHUB_REPOSITORY, runId: process.env.RUN_ID }).catch(error => {
    if (!(error instanceof RateLimitSkipped)) {
      console.error(error.message);
      process.exitCode = 1;
    }
  });
}
