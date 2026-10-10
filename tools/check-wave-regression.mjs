import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { summarizeWaveBenchmarkV2 } from './summarize-wave-benchmark-v2.mjs';

export function enforceWaveRegression(summary, explanation = '') {
  assert.equal(summary.mode, 'uncapped', 'Regression requires uncapped native evidence');
  assert.ok(summary.reference, 'A same-config reference is required');
  assert.ok(summary.status === 'within-trend' || summary.status === 'warning', 'Missing or invalid benchmark evidence cannot be excused');
  if (summary.status === 'within-trend') return { status: 'pass', explanation: null };
  assert.ok(explanation.trim().length >= 40 && /#[1-9]\d*/.test(explanation),
    'C05 regression exceeds 20%; provide an explicit reason with a tracking issue, or fail');
  return { status: 'explained-regression', explanation: explanation.trim() };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const [current, reference, flag, reasonPath, ...extra] = process.argv.slice(2);
    assert.ok(current && reference && extra.length === 0 &&
      (flag === undefined && reasonPath === undefined || flag === '--reason' && reasonPath),
    'Usage: check-wave-regression.mjs CURRENT_ROOT REFERENCE_ROOT [--reason REASON.md]');
    const summary = summarizeWaveBenchmarkV2(current, { mode: 'uncapped', referenceRoot: reference });
    const decision = enforceWaveRegression(summary, reasonPath ? fs.readFileSync(reasonPath, 'utf8') : '');
    console.log(JSON.stringify({ ...decision, measuredCommit: summary.measured.commit,
      referenceCommit: summary.reference.commit, trend: summary.trend }, null, 2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
