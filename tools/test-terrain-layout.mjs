import assert from 'node:assert/strict';
import { readFile, mkdtemp, mkdir, copyFile, rm } from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import test from 'node:test';
import Ajv2020 from 'ajv/dist/2020.js';
import { validateContent } from './validate-content.mjs';
import { validateTerrainLayout } from './validate-terrain-layout.mjs';
import { polygonsOverlap, rectangle } from './art/terrain-layout-geometry.mjs';
const root = path.resolve(import.meta.dirname, '..');
const read = async file => JSON.parse(await readFile(path.join(root, file), 'utf8'));
const original = await read('data/opt-in/estate/terrain-layout-r8.json');
const runtime = await read('data/opt-in/account-estate/m3a-prototype-r8.json');
const schema = await read('data/opt-in/schema/estate-terrain-layout.schema.json');
const validate = new Ajv2020({ strict: true, allErrors: true }).compile(schema);
const check = record => validate(record) ? validateTerrainLayout(record, runtime) : [JSON.stringify(validate.errors)];

test('terrain opt-in exports all approved instances without runtime buildings or protected overlap', () => {
  assert.deepEqual(check(original), []);
  assert.equal(original.provenance.sourcePlacementCount, 2110);
  assert.equal(original.instances.length + original.reviewOnly.buildings.length + original.reviewOnly.excludedInstances.length, 2110);
});
const mutations = [
  ['unknown nested field', x => { x.instances[0].nativeTopLeft = [0, 0]; }, /additionalProperties/],
  ['runtime activation', x => { x.runtimeEnabled = true; }, /const/],
  ['unresolved asset', x => { x.instances[0].assetId = 'missing'; }, /unresolved asset/],
  ['duplicate draw order', x => { x.instances[1].drawOrder = x.instances[0].drawOrder; }, /duplicate draw order/],
  ['fractional overlap restored', x => { const p = x.reviewOnly.excludedInstances.shift(); delete p.exclusionReasons; x.instances.push(p); }, /reserved footprint collision/],
  ['missing river reservations', x => { x.reservations = x.reservations.filter(r => r.type !== 'river'); }, /approved river.bridge reservations differ/],
  ['missing bridge reservations', x => { x.reservations = x.reservations.filter(r => r.type !== 'bridge'); }, /approved river.bridge reservations differ/],
  ['changed bridge geometry', x => { x.reservations.find(r => r.type === 'bridge').polygon[0][0]++; }, /approved river.bridge reservations differ/],
  ['shrunk nominal footprint', x => { x.instances.find(p => p.category === 'prop').footprint = [0.1, 0.1]; }, /nominal footprint mismatch/],
  ['missing Lv0 slot', x => { x.reservations = x.reservations.filter(r => r.id !== 'slot:meta:granary'); }, /missing building\/Lv0 reservation/],
  ['missing rotated road', x => { x.reservations = x.reservations.filter(r => r.type !== 'path'); }, /missing path reservation/],
  ['building in terrain draws', x => { x.instances.push(x.reviewOnly.buildings[0]); }, /runtime building duplicate/],
  ['zero footprint evasion', x => { x.instances.find(p => p.category === 'prop').footprint = [0, 0]; }, /empty decorative footprint/],
  ['geometry drift', x => { x.zones[0].rect[0]++; }, /current r8 zone geometry differs/],
  ['unexplained exclusion', x => { x.reviewOnly.excludedInstances[0].exclusionReasons = ['bogus']; }, /incorrect exclusion reasons/],
  ['local-only asset URL', x => { x.assets[0].source.url = '/Users/test.png'; }, /pattern/],
  ['composition cycle', x => { const a = x.assets.find(a => a.composition); a.composition.parts[0].assetId = a.id; }, /composition cycle/],
];
for (const [name, mutate, expected] of mutations) test(`terrain rejects ${name}`, () => {
  const record = structuredClone(original); mutate(record);
  assert.match(check(record).join('\n'), expected);
});
test('footprint SAT catches edge overlap even when prop anchor is outside reserved cell', () => {
  assert.equal(polygonsOverlap(rectangle([1.9, 0, 1, 1]), rectangle([0, 0, 2, 2])), true);
  assert.equal(polygonsOverlap(rectangle([2, 0, 1, 1]), rectangle([0, 0, 2, 2])), false);
});
test('terrain registry accepts only the registered opt-in path and remains outside fullPool gameplay', async t => {
  const result = await validateContent(path.join(root, 'data'), { fullPool: true });
  assert.deepEqual(result.errors, []);
  const directory = await mkdtemp(path.join(os.tmpdir(), 'terrain-path-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await mkdir(path.join(directory, 'opt-in/schema'), { recursive: true });
  await mkdir(path.join(directory, 'opt-in/estate'), { recursive: true });
  await copyFile(path.join(root, 'data/opt-in/schema/estate-terrain-layout.schema.json'), path.join(directory, 'opt-in/schema/estate-terrain-layout.schema.json'));
  await copyFile(path.join(root, 'data/opt-in/estate/terrain-layout-r8.json'), path.join(directory, 'opt-in/estate/unregistered.json'));
  assert.match((await validateContent(directory)).errors.join('\n'), /missing schema/);
});
