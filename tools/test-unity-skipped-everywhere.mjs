import assert from 'node:assert/strict';
import test from 'node:test';
import { checkSkippedEverywhere, testResults } from './unity-skipped-everywhere.mjs';

const xml = cases => `<test-run>${cases.map(([name, result, reason]) =>
  `<test-case id="1" name="${name.split('.').pop()}" fullname="${name}" runstate="Runnable" result="${result}">${reason ? `<reason><message><![CDATA[${reason}]]></message></reason>` : ''}</test-case>`).join('')}</test-run>`;
const firstPlayable = xml([['T.A', 'Passed'], ['T.B', 'Skipped', 'Requires wave-1a export.'], ['T.C', 'Skipped', 'Optional export.'], ['T.D', 'Skipped', 'Counter unavailable.']]);
const wave = xml([['T.A', 'Skipped', 'Requires first-playable export.'], ['T.B', 'Passed'], ['T.C', 'Skipped', 'Optional export.'], ['T.D', 'Skipped', 'Counter unavailable.']]);

test('reads names, results and ignore reasons', () => {
  const results = testResults(xml([['T.Case(&quot;a&amp;b&quot;)', 'Skipped', 'why']]));
  assert.deepEqual(results.get('T.Case("a&b")'), { result: 'Skipped', reason: 'why' });
});
test('only tests skipped in every profile count, and listed ones with a reason are excused', () => {
  const report = checkSkippedEverywhere([firstPlayable, wave], 'T.C | on-demand export\n# comment\n');
  assert.deepEqual(report.skipped.map(t => t.name), ['T.C', 'T.D']);
  assert.deepEqual(report.unexplained.map(t => t.name), ['T.D']);
  assert.deepEqual(report.skipped[1].reasons, ['Counter unavailable.']);
  assert.deepEqual(report.errors, []);
});
test('an exception without a reason, or one that runs somewhere now, is an error', () => {
  const report = checkSkippedEverywhere([firstPlayable, wave], 'T.C\nT.A | used to be skipped\nT.D | counter\n');
  assert.equal(report.unexplained.length, 0);
  assert.match(report.errors.join('\n'), /T\.C has no reason/);
  assert.match(report.errors.join('\n'), /T\.A is not skipped in every profile/);
});
