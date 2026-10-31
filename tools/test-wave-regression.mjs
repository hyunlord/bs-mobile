import assert from 'node:assert/strict';
import test from 'node:test';
import { enforceWaveRegression } from './check-wave-regression.mjs';

const summary = { mode: 'uncapped', reference: { commit: 'baseline' }, status: 'within-trend' };
test('same-config trend passes without an exception', () => {
  assert.equal(enforceWaveRegression(summary).status, 'pass');
});
test('a measured regression fails unless its explicit reason is recorded', () => {
  const regressed = { ...summary, status: 'warning' };
  assert.throws(() => enforceWaveRegression(regressed), /exceeds 20%/);
  assert.throws(() => enforceWaveRegression(regressed, 'ok #200'), /exceeds 20%/);
  const reason = '#200 adds measured visual work; the per-density increase and remaining mitigation are documented in the PR.';
  assert.deepEqual(enforceWaveRegression(regressed, reason), { status: 'explained-regression', explanation: reason });
});
test('an explanation cannot excuse missing evidence or a missing reference', () => {
  const reason = '#200 has an explanation long enough to test invalid evidence rejection without granting an exemption.';
  for (const changed of [{ status: 'incomplete' }, { status: 'baseline' }, { reference: null }, { mode: 'capped' }])
    assert.throws(() => enforceWaveRegression({ ...summary, ...changed }, reason));
});

test('CLI options preserve C05 default and accept contract/reason in either order', async () => {
  const { parseWaveRegressionArgs } = await import('./check-wave-regression.mjs');
  assert.deepEqual(parseWaveRegressionArgs(['current', 'base']), { current: 'current', reference: 'base' });
  for (const options of [ ['--contract', 'fp.json', '--reason', 'why.md'], ['--reason', 'why.md', '--contract', 'fp.json'] ])
    assert.deepEqual(parseWaveRegressionArgs(['current', 'base', ...options]), { current: 'current', reference: 'base', contractPath: 'fp.json', reasonPath: 'why.md' });
  assert.deepEqual(parseWaveRegressionArgs(['current', 'base', '--reason', 'why.md']), { current: 'current', reference: 'base', reasonPath: 'why.md' });
});
test('CLI rejects unknown, duplicate, missing-value flags and extra positionals', async () => {
  const { parseWaveRegressionArgs } = await import('./check-wave-regression.mjs');
  assert.equal(typeof parseWaveRegressionArgs, 'function');
  for (const args of [[], ['current'], ['--contract', 'fp.json'],
    ['current','base','--contract'], ['current','base','--reason'],
    ['current','base','--contract','--reason','why.md'],
    ['current','base','--contract','a','--contract','b'],
    ['current','base','--reason','a','--reason','b'],
    ['current','base','--unknown','a'], ['current','base','extra']])
    assert.throws(() => parseWaveRegressionArgs(args), undefined, JSON.stringify(args));
});

test('C05 default and explicit contract preserve output and reason compatibility', async t => {
  const { default: fs } = await import('node:fs');
  const { default: path } = await import('node:path');
  const { default: os } = await import('node:os');
  const { spawnSync } = await import('node:child_process');
  const { formatCsv } = await import('./csv.mjs');
  const { frameHeaders, timingHeaders } = await import('./summarize-wave-benchmark.mjs');
  const contractFile = 'benchmarks/wave-c05/contract-v2.json';
  const contract = JSON.parse(fs.readFileSync(contractFile));
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'c05-cli-contract-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  // Six synthetic rows/run use only the three pooled p95 aggregates from #382's
  // analysis-p382/check.out (2026-10-27). These are output-compatibility samples,
  // not copied successful raw runs or a new performance measurement.
  const commits = { current: 'e81e44a9f58c967c7abd054a23d79900c28405a3', reference: 'd71be4d278dd3d5ea67b0db00558a27fa98d863a' };
  const costs = { current: [3.0032, 4.009, 4.808], reference: [2.7197, 3.7112, 4.4118] };
  const writeRuns = currentCosts => {
    for (const variant of ['current', 'reference']) for (let repeat = 1; repeat <= 3; repeat++) {
      const run = path.join(root, variant, `run-${repeat}`); fs.mkdirSync(run, { recursive: true });
      const rows = contract.windows.flatMap(([start, end], i) => [start, end].map((tick, j) => ({
        frame: i * 3 + j, tick, enemies: [450, 650, 850][i], commands: 1,
        wallMs: (variant === 'current' ? currentCosts : costs.reference)[i], simulationMs: 1,
        snapshotMs: .1, acceptMs: .1, presentMs: .1, hudMs: .1, allocatedBytes: 128, gcCollections: 0, focused: 1
      })));
      const sourceHash = (variant === 'current' ? 'c' : 'd').repeat(64);
      fs.writeFileSync(path.join(run, 'benchmark.json'), JSON.stringify({ commit: commits[variant], sourceHash, sourceDirty: false,
        verified: true, interrupted: false, developmentBuild: false, platform: 'OSXPlayer', dataHash: contract.dataHash,
        replaySha256: contract.replaySha256, stateHash: contract.endHash, expectedStateHash: contract.endHash, error: '',
        tick: 14400, commands: 14403, width: 1600, height: 900, targetFrameRate: -1, vSyncCount: 0, renderWarmupFrames: 120,
        windows: contract.windows.map(w => w.join('-')).join(','), graphicsApi: 'Metal', quality: 'Ultra', frameTimingEnabled: true,
        frames: rows.length, renderTimingSamples: 0, benchmarkMode: 'uncapped', profilerEnabled: false, gcMode: 'Enabled' }));
      fs.writeFileSync(path.join(run, 'device.json'), JSON.stringify({ model: 'Mac16,5', os: 'Mac OS X 27',
        unityVersion: '6000.6.4f1', backend: 'Mono', graphics: 'Metal', sourceHash, sourceDirty: false }));
      fs.writeFileSync(path.join(run, 'frames.csv'), formatCsv(frameHeaders, rows));
      fs.writeFileSync(path.join(run, 'render-timings.csv'), formatCsv(timingHeaders, []));
    }
  };
  const cli = (...options) => spawnSync(process.execPath, ['tools/check-wave-regression.mjs', path.join(root, 'current'), path.join(root, 'reference'), ...options], { encoding: 'utf8' });
  const expected = (status, values, explanation = null) => JSON.stringify({ status, explanation, measuredCommit: commits.current,
    referenceCommit: commits.reference, trend: contract.densityBins.map(([min, max], i) => ({ min, max, p95: values[i],
      referenceP95: costs.reference[i], increaseRatio: values[i] / costs.reference[i] - 1,
      status: values[i] > costs.reference[i] * 1.2 ? 'warning' : 'within-trend' })) }, null, 2) + '\n';
  writeRuns(costs.current);
  for (const options of [[], ['--contract', contractFile]]) {
    const result = cli(...options); assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout, expected('pass', costs.current)); assert.equal(result.stderr, '');
  }
  const regressed = costs.reference.map(value => value * 1.21); writeRuns(regressed);
  assert.equal(cli().status, 1);
  assert.equal(cli().stderr, 'C05 regression exceeds 20%; provide an explicit reason with a tracking issue, or fail\n');
  const reason = '#451 explicitly records this synthetic regression; output compatibility does not mean a performance pass.';
  const reasonPath = path.join(root, 'reason.md'); fs.writeFileSync(reasonPath, reason);
  for (const options of [['--reason', reasonPath], ['--contract', contractFile, '--reason', reasonPath]]) {
    const result = cli(...options); assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout, expected('explained-regression', regressed, reason));
  }
});
