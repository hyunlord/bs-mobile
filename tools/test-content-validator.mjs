import assert from 'node:assert/strict';
import { cp, mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { validateContent } from './validate-content.mjs';

const id = { type: 'string', pattern: '^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$' };
const objectSchema = (properties, required = Object.keys(properties)) => ({
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  type: 'object', properties, required, additionalProperties: false,
});
const positive = { type: 'integer', minimum: 1 };
const toolSchema = objectSchema({
  id,
  activation: { type: 'object', properties: { damage: positive }, required: ['damage'], additionalProperties: false },
  growth: { type: 'object', properties: { yield: positive }, required: ['yield'], additionalProperties: false },
  antiSynergy: { type: 'array', items: id },
});
const initialFiles = {
  'schema/tool.schema.json': toolSchema,
  'schema/hero.schema.json': objectSchema({ id, startingTool: id }),
  'schema/estate.schema.json': objectSchema({ id, startingTool: id }),
  'schema/tuning.schema.json': objectSchema({ defaultHero: id, defaultEstate: id }),
  'tools/seed.json': { id: 'core:seed', activation: { damage: 1 }, growth: { yield: 1 }, antiSynergy: [] },
  'heroes/lord.json': { id: 'core:lord', startingTool: 'core:seed' },
  'estates/farm.json': { id: 'core:farm', startingTool: 'core:seed' },
  'tuning.json': { defaultHero: 'core:lord', defaultEstate: 'core:farm' },
};

async function fixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-content-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  async function write(relative, value) {
    await mkdir(path.dirname(path.join(directory, relative)), { recursive: true });
    await writeFile(path.join(directory, relative), `${JSON.stringify(value)}\n`);
  }
  for (const [relative, value] of Object.entries(initialFiles)) await write(relative, value);
  async function change(relative, mutate) {
    const value = JSON.parse(await readFile(path.join(directory, relative), 'utf8'));
    mutate(value);
    await write(relative, value);
  }
  return { directory, write, change };
}

test('valid schema and cross-record references pass', async (t) => {
  const { directory } = await fixture(t);
  assert.deepEqual(await validateContent(directory), { valid: true, schemas: 4, records: 4, errors: [] });
});

const mutations = [
  ['damage below minimum', 'tools/seed.json', (v) => { v.activation.damage = -1; }, /schema violation/],
  ['unknown field', 'tools/seed.json', (v) => { v.accidental = true; }, /additional properties/],
  ['missing growth', 'tools/seed.json', (v) => { delete v.growth; }, /requires both activation and growth/],
  ['missing activation', 'tools/seed.json', (v) => { delete v.activation; }, /requires both activation and growth/],
  ['wrong scalar type', 'tools/seed.json', (v) => { v.activation.damage = '1'; }, /must be integer/],
  ['invalid namespace', 'tools/seed.json', (v) => { v.id = 'Seed'; }, /invalid namespace ID/],
  ['unresolved hero tool', 'heroes/lord.json', (v) => { v.startingTool = 'core:absent'; }, /unresolved tool reference/],
  ['wrong-kind reference', 'heroes/lord.json', (v) => { v.startingTool = 'core:farm'; }, /unresolved tool reference/],
  ['unresolved estate tool', 'estates/farm.json', (v) => { v.startingTool = 'core:absent'; }, /unresolved tool reference/],
  ['unresolved tuning hero', 'tuning.json', (v) => { v.defaultHero = 'core:absent'; }, /unresolved hero reference/],
  ['unresolved anti-synergy', 'tools/seed.json', (v) => { v.antiSynergy = ['core:absent']; }, /unresolved tool reference/],
  ['invalid schema', 'schema/tool.schema.json', (v) => { v.properties.id.type = 'not-a-json-schema-type'; }, /invalid schema/],
  ['undeclared schema draft', 'schema/tool.schema.json', (v) => { delete v.$schema; }, /Draft 2020-12/],
  ['unknown schema keyword', 'schema/tool.schema.json', (v) => { v.misspelledRule = true; }, /cannot compile schema/],
];
for (const [name, relative, mutate, expected] of mutations) {
  test(`rejects ${name}`, async (t) => {
    const { directory, change } = await fixture(t);
    await change(relative, mutate);
    const result = await validateContent(directory);
    assert.equal(result.valid, false);
    assert.match(result.errors.join('\n'), expected);
  });
}

test('recursively checks test content and global duplicate IDs', async (t) => {
  const { directory, write } = await fixture(t);
  await write('test/tools/nested/duplicate.json', initialFiles['tools/seed.json']);
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /duplicate ID core:seed/);
});

test('rejects content without matching schema', async (t) => {
  const { directory, write } = await fixture(t);
  await write('weapons/blade.json', { id: 'core:blade' });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /missing schema/);
});

test('skin numbers fail even if schema permits arbitrary fields', async (t) => {
  const { directory, write } = await fixture(t);
  await write('schema/skin.schema.json', { ...objectSchema({ id }, ['id']), additionalProperties: true });
  await write('skins/robe.json', { id: 'core:robe', nested: { damage: 9 } });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /skin must not contain numeric stats/);
});

test('valid Draft2020 unevaluatedProperties is enforced', async (t) => {
  const { directory, write } = await fixture(t);
  await write('schema/skin.schema.json', {
    $schema: 'https://json-schema.org/draft/2020-12/schema', type: 'object',
    allOf: [{ properties: { id }, required: ['id'] }], unevaluatedProperties: false,
  });
  await write('skins/robe.json', { id: 'core:robe', mystery: 'unapproved' });
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /unevaluated properties/);
});

test('rejects malformed JSON instead of silently skipping it', async (t) => {
  const { directory } = await fixture(t);
  await writeFile(path.join(directory, 'tools/broken.json'), '{');
  const result = await validateContent(directory);
  assert.equal(result.valid, false);
  assert.match(result.errors.join('\n'), /invalid JSON/);
});

const worldMutations = [
  ['season duration mismatch', (v) => { v.world.seasons[0].durationTicks += 1; }, /season durations must sum/],
  ['missing world numeric tuning', (v) => { delete v.world.people.consumePeriodTicks; }, /consumePeriodTicks/],
  ['unresolved starting weapon', (v) => { v.world.progression.startingWeapon = 'core:missing'; }, /unresolved weapon reference/],
  ['excessive load', (v) => { v.world.load.enemies = v.world.threat.enemyCap + 1; }, /load.enemies exceeds capacity/],
  ['map-incompatible spawn inset', (v) => { v.world.threat.spawnInset = v.world.map.width; }, /spawn inset exceeds map bounds/],
  ['unsupported people rule', (v) => { v.world.defaultPeopleRule = 'D'; }, /schema violation/],
  ['wrong card count', (v) => { v.world.progression.cardCount = 2; }, /schema violation/],
  ['duplicate rarity names', (v) => { v.world.progression.rarities[1].name = v.world.progression.rarities[0].name; }, /rarity names must be unique/],
  ['oversized squad', (v) => { v.world.people.squadSize = v.world.people.maxPeople + 1; }, /squad size exceeds people capacity/],
];
for (const [name, mutate, expected] of worldMutations) {
  test(`S2 rejects ${name}`, async (t) => {
    const directory = await mkdtemp(path.join(os.tmpdir(), 'bs-world-schema-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    await cp(fileURLToPath(new URL('../data', import.meta.url)), directory, { recursive: true });
    const tuningPath = path.join(directory, 'tuning.json');
    const tuning = JSON.parse(await readFile(tuningPath, 'utf8'));
    mutate(tuning);
    await writeFile(tuningPath, JSON.stringify(tuning));
    const result = await validateContent(directory);
    assert.equal(result.valid, false);
    assert.match(result.errors.join('\n'), expected);
  });
}
