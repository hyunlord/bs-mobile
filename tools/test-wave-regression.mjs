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
