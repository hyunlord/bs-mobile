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
