import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { summarizeWaveBenchmarkV2 } from './summarize-wave-benchmark-v2.mjs';

export function enforceWaveRegression(summary, explanation = '', fixture = 'C05') {
  assert.equal(summary.mode, 'uncapped', 'Regression requires uncapped native evidence');
  assert.ok(summary.reference, 'A same-config reference is required');
  assert.ok(summary.status === 'within-trend' || summary.status === 'warning', 'Missing or invalid benchmark evidence cannot be excused');
  if (summary.status === 'within-trend') return { status: 'pass', explanation: null };
  assert.ok(explanation.trim().length >= 40 && /#[1-9]\d*/.test(explanation),
    `${fixture} regression exceeds 20%; provide an explicit reason with a tracking issue, or fail`);
  return { status: 'explained-regression', explanation: explanation.trim() };
}

export function parseWaveRegressionArgs(args) {
  const usage = 'Usage: check-wave-regression.mjs CURRENT_ROOT REFERENCE_ROOT [--contract CONTRACT.json] [--reason REASON.md]';
  const [current, reference, ...options] = args;
  assert.ok(current && reference && !current.startsWith('--') && !reference.startsWith('--'), usage);
  const parsed = { current, reference }; const seen = new Set();
  for (let i = 0; i < options.length; i++) {
    const flag = options[i];
    assert.ok(flag === '--contract' || flag === '--reason', usage);
    assert.ok(!seen.has(flag), 'Duplicate option: ' + flag); seen.add(flag);
    const value = options[++i];
    assert.ok(value && !value.startsWith('--'), 'Missing value for ' + flag);
    parsed[flag === '--contract' ? 'contractPath' : 'reasonPath'] = value;
  }
  return parsed;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const { current, reference, contractPath, reasonPath } = parseWaveRegressionArgs(process.argv.slice(2));
    const contract = contractPath ? JSON.parse(fs.readFileSync(contractPath, 'utf8')) : undefined;
    const summary = summarizeWaveBenchmarkV2(current, { mode: 'uncapped', referenceRoot: reference }, contract);
    const decision = enforceWaveRegression(summary, reasonPath ? fs.readFileSync(reasonPath, 'utf8') : '', contract?.fixture ?? 'C05');
    console.log(JSON.stringify({ ...decision, measuredCommit: summary.measured.commit,
      referenceCommit: summary.reference.commit, trend: summary.trend }, null, 2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
