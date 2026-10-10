import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export async function prepareWaveBenchmark({ exportCatalog = true } = {}) {
  const fixture = path.join(root, 'benchmarks/wave-c05');
  const contract = JSON.parse(await readFile(path.join(fixture, 'contract.json'), 'utf8'));
  const replay = await readFile(path.join(fixture, 'run.ssreplay'));
  assert.equal(createHash('sha256').update(replay).digest('hex'), contract.replaySha256, 'C05 original replay changed');
  assert.match(contract.sourceCommit, /^[a-f0-9]{40}$/);
  const git = args => {
    const result = spawnSync('git', args, { cwd: root, maxBuffer: 16 * 1024 * 1024 });
    if (result.error) throw result.error;
    assert.equal(result.status, 0, result.stderr.toString());
    return result.stdout;
  };
  const files = git(['ls-tree', '-r', '--name-only', contract.sourceCommit, '--', 'data']).toString().trim().split('\n');
  const destination = path.join(root, 'artifacts/benchmarks/c05');
  for (const file of files) {
    assert.match(file, /^data\/[A-Za-z0-9_./-]+\.json$/);
    assert.ok(!file.split('/').includes('..'));
    const output = path.join(destination, file);
    await mkdir(path.dirname(output), { recursive: true });
    await writeFile(output, git(['show', `${contract.sourceCommit}:${file}`]));
  }
  if (exportCatalog) {
    const output = path.join(root, 'unity/Assets/Game/App/Generated/FrozenWaveBenchmark.g.cs');
    const result = spawnSync(process.env.DOTNET ?? 'dotnet', [
      'core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll',
      '--export-wave-benchmark', path.join(destination, 'data'), output,
    ], { cwd: root, stdio: 'inherit' });
    if (result.error) throw result.error;
    assert.equal(result.status, 0, 'Frozen benchmark catalog export failed');
  }
  return destination;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  assert.ok(process.argv.length === 2 || (process.argv.length === 3 && process.argv[2] === '--data-only'));
  console.log(await prepareWaveBenchmark({ exportCatalog: process.argv[2] !== '--data-only' }));
}
