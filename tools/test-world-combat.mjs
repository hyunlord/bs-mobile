import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import Ajv2020 from 'ajv/dist/2020.js';
import { validateWorldCombat } from './validate-world-combat.mjs';
import { validateContent } from './validate-content.mjs';

const schema = JSON.parse(await readFile(new URL('../data/opt-in/schema/world-combat.schema.json', import.meta.url), 'utf8'));
const fixture = JSON.parse(await readFile(new URL('../data/opt-in/world-combat/world-combat-v1.json', import.meta.url), 'utf8'));
const validateSchema = new Ajv2020({ allErrors: true, strict: true }).compile(schema);
const errors = value => validateSchema(value) ? validateWorldCombat(value) : [JSON.stringify(validateSchema.errors)];

test('world-combat opt-in example satisfies strict schema and semantic checks', () => {
  assert.deepEqual(errors(fixture), []);
});

test('world-combat opt-in kind is registered with content validation', async () => {
  const result = await validateContent(new URL('../data', import.meta.url).pathname);
  assert.deepEqual(result.errors, []);
});

for (const [name, mutate] of [
  ['unknown property', s => { s.utc = '2026-11-02'; }],
  ['unknown mode', s => { s.mode = 'survivor'; }],
  ['unknown rules version', s => { s.provenance.rulesVersion = 2; }],
  ['mode/rules mismatch', s => { s.mode = 'auto-pve'; }],
  ['lowercase hash', s => { s.contractHash = 'a'.repeat(64); }],
  ['noncanonical identifier', s => { s.hero.id = 'hero\u0000id'; }],
  ['negative grace', s => { s.input.inputGraceMilliseconds = -1; }],
  ['zero chunk bytes', s => { s.input.maximumChunkBytes = 0; }],
  ['oversized chunk', s => { s.input.maximumChunkBytes = 1048577; }],
  ['fractional coordinate', s => { s.markers[0].x = 0.5; }],
  ['integer overflow', s => { s.troops[0].stats.health = 2147483648; }],
  ['reversed battlefield', s => { s.battlefield.minX = s.battlefield.maxX; }],
  ['outside battlefield', s => { s.troops[0].slots[0].x = s.battlefield.maxX + 1; }],
  ['duplicate objective', s => { s.objectives[1].id = s.objectives[0].id; }],
  ['reversed objectives', s => { s.objectives.reverse(); }],
  ['missing time limit', s => { s.objectives.pop(); }],
  ['duplicate time limit', s => { s.objectives.push({ ...s.objectives.at(-1), id: 'slg:time_second' }); }],
  ['time limit target payload', s => { s.objectives.at(-1).targetId = 'slg:field_mites'; }],
  ['unknown cohort', s => { s.objectives[0].targetId = 'slg:missing'; }],
  ['impossible kill count', s => { s.objectives[0].requiredCount = s.cohorts[0].count + 1; }],
  ['unknown marker', s => { s.objectives[1].targetId = 'slg:missing'; }],
  ['hold after time limit', s => { s.objectives[1].holdTicks = s.objectives.at(-1).limitTicks + 1; }],
  ['unused objective value', s => { s.objectives[1].requiredCount = 1; }],
  ['late cohort', s => { s.cohorts[0].spawnTick = s.objectives.at(-1).limitTicks; }],
  ['N/slots mismatch', s => { s.troops[0].n--; }],
  ['duplicate slot', s => { s.troops[0].slots[1].ordinal = 0; }],
  ['reversed slots', s => { s.troops[0].slots.reverse(); }],
  ['total cohort overflow', s => { s.cohorts[0].count = 4096; s.cohorts.push({ ...s.cohorts[0], id: 'slg:second', count: 1 }); }],
  ['missing ability', s => { s.effects.pop(); }],
  ['reversed abilities', s => { s.effects.reverse(); }],
  ['incorrect raw sum', s => { s.effects[0].raw = 1; }],
  ['cap applied incorrectly', s => { s.effects[0].bonus = 1; }],
  ['duplicate sources', s => { const source = { kind: 'hero', sourceId: 'core:frontier_knight', contentHash: 'E'.repeat(64), raw: 0 }; s.effects[0].sources = [source, source]; }],
  ['reversed source kinds', s => { const source = { sourceId: 'core:frontier_knight', contentHash: 'E'.repeat(64), raw: 0 }; s.effects[0].sources = [{ ...source, kind: 'research' }, { ...source, kind: 'hero' }]; }],
  ['unknown hero provenance', s => { s.effects[0].sources = [{ kind: 'hero', sourceId: 'core:frontier_knight', contentHash: 'F'.repeat(64), raw: 0 }]; }],
  ['raw sum overflow', s => { s.effects[0].sources = [{ kind: 'hero', sourceId: 'core:frontier_knight', contentHash: 'E'.repeat(64), raw: 2147483647 }, { kind: 'research', sourceId: 'slg:study', contentHash: 'F'.repeat(64), raw: 1 }]; }],
]) {
  test(`world-combat rejects ${name}`, () => {
    const value = structuredClone(fixture);
    mutate(value.snapshot);
    assert.notDeepEqual(errors(value), []);
  });
}

test('world-combat accepts explicit N=0 and inclusive battlefield edge', () => {
  const value = structuredClone(fixture);
  value.snapshot.troops[0].n = 0;
  value.snapshot.troops[0].slots = [];
  value.snapshot.markers[0].x = value.snapshot.battlefield.maxX;
  assert.deepEqual(errors(value), []);
});

test('world-combat sums raw sources then applies cap once', () => {
  const value = structuredClone(fixture);
  value.snapshot.effects[0] = { ...value.snapshot.effects[0], raw: 120, cap: 100, bonus: 100, sources: [
    { kind: 'hero', sourceId: 'core:frontier_knight', contentHash: 'E'.repeat(64), raw: 60 },
    { kind: 'research', sourceId: 'slg:study', contentHash: 'F'.repeat(64), raw: 60 },
  ] };
  assert.deepEqual(errors(value), []);
});

for (const suffix of ['\n', '\r', '\r\n', '\u2028', '\u2029']) {
  for (const field of ['hash', 'identifier', 'target']) {
    test(`world-combat schema rejects trailing ${JSON.stringify(suffix)} in ${field}`, () => {
      const value = structuredClone(fixture);
      if (field === 'hash') { value.snapshot.contractHash += suffix; }
      if (field === 'identifier') { value.snapshot.provenance.profileId += suffix; }
      if (field === 'target') { value.snapshot.objectives[0].targetId += suffix; }
      assert.equal(validateSchema(value), false);
    });
  }
}
