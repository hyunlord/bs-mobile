import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { ratioVerdict, adjudicate, review } from './retrospective-c-prime.mjs';
import { formatCsv } from './csv.mjs';
const policies = ['weapon', 'land', 'building', 'people', 'mixed', 'random'];
function fixture() {
  return {
    'outcomes.csv': policies.flatMap(policy => ['A', 'B', 'C'].map(peopleRule => ({ policy, peopleRule, movementMode: 'circuit', caseCount: '32', seedBlockCount: '32', survived: '16', survivalRate: '0.5', truncatedAlive: '0', meanReachedSeconds: '600', meanLevel: '30', meanCombatDamage: '100' }))),
    'condition-ranks.csv': ['A', 'B', 'C'].flatMap(peopleRule => Array.from({ length: 32 }, (_, i) => ({ peopleRule, seed: String(42 + i), cofirstPoliciesJson: JSON.stringify([i % 2 ? 'weapon' : 'land']) }))),
    'gates.csv': ['validity', 'a', 'b', 'c', 'd', 'XP', 'mixed-ledger'].map(gate => ({ gate, pass: gate === 'c' ? 'false' : 'true', applicable: 'true' })),
  };
}
test('inclusive exact rational bounds, just-outside values, and zero random denominator', () => {
  assert.equal(ratioVerdict(3, 10, 5, 10).status, 'PASS');
  assert.equal(ratioVerdict(15, 32, 10, 32).status, 'PASS');
  assert.equal(ratioVerdict(299, 1000, 500, 1000).status, 'FAIL');
  assert.equal(ratioVerdict(1501, 2000, 1000, 2000).status, 'FAIL');
  assert.equal(ratioVerdict(0, 32, 0, 32).status, 'NOT_EVALUABLE');
  assert.equal(ratioVerdict(1, 32, 0, 32).ratio, null);
  assert.throws(() => ratioVerdict(33, 32, 1, 32));
});
test('all12cells required and historical c remains untouched', () => {
  const tables = fixture(), original = structuredClone(tables);
  const result = adjudicate('R2', tables);
  assert.equal(result.cells.length, 12); assert.equal(result.summary.passCells, 12); assert.equal(result.summary.bPass, true); assert.equal(result.summary.historicalCPass, false);
  assert.deepEqual(tables, original);
  const weapon = tables['outcomes.csv'][0]; weapon.survived = '5'; weapon.survivalRate = String(5 / 32);
  assert.equal(adjudicate('R2', tables).summary.cPrimePass, false);
  for (const row of tables['outcomes.csv']) if (row.policy === 'random' && row.peopleRule === 'A') { row.survived = '0'; row.survivalRate = '0'; }
  const zero = adjudicate('R2', tables); assert.equal(zero.summary.evaluableCells, 8); assert.equal(zero.summary.retrospectiveReviewPass, false);
});
test('reject missing, duplicated, altered cohorts/conditions and contradictory b', () => {
  const mutations = [t => t['outcomes.csv'].pop(), t => t['outcomes.csv'].push(t['outcomes.csv'][0]), t => { t['outcomes.csv'][0].caseCount = '31'; }, t => { t['outcomes.csv'][0].survivalRate = '0.6'; }, t => t['condition-ranks.csv'].pop(), t => t['condition-ranks.csv'].push(t['condition-ranks.csv'][0]), t => { t['condition-ranks.csv'][0].cofirstPoliciesJson = '["unknown"]'; }, t => { t['gates.csv'].find(r => r.gate === 'b').pass = 'false'; }];
  for (const mutate of mutations) { const tables = fixture(); mutate(tables); assert.throws(() => adjudicate('R3', tables)); }
});
test('unchanged b fails all-policy ties even when all c-prime ratios pass', () => {
  const tables = fixture(); for (const row of tables['condition-ranks.csv']) row.cofirstPoliciesJson = JSON.stringify(policies);
  tables['gates.csv'].find(row => row.gate === 'b').pass = 'false';
  const result = adjudicate('R3', tables); assert.equal(result.summary.cPrimePass, true); assert.equal(result.summary.bPass, false); assert.equal(result.summary.retrospectiveReviewPass, false);
});
test('replay from moved CSVs is byte-identical, compact, and refuses overwrite', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'c-prime-')); t.after(() => fs.rm(temp, { recursive: true, force: true }));
  const r2 = path.join(temp, 'r2'), r3 = path.join(temp, 'r3');
  for (const dir of [r2, r3]) { await fs.mkdir(dir); for (const [name, rows] of Object.entries(fixture())) await fs.writeFile(path.join(dir, name), formatCsv(Object.keys(rows[0]), rows)); }
  const first = path.join(temp, 'first'), second = path.join(temp, 'second'); await review(r2, r3, first);
  const moved2 = path.join(temp, 'moved2'), moved3 = path.join(temp, 'moved3'); await fs.rename(r2, moved2); await fs.rename(r3, moved3); await review(moved2, moved3, second);
  const files = (await fs.readdir(first)).sort(); assert.equal(files.length, 4); assert.deepEqual(files, (await fs.readdir(second)).sort());
  for (const file of files) assert.deepEqual(await fs.readFile(path.join(first, file)), await fs.readFile(path.join(second, file)));
  assert.match(await fs.readFile(path.join(first, 'report.md'), 'utf8'), /후향 재판정/);
  await assert.rejects(review(moved2, moved3, first), /already exists/);
});
