import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import Ajv2020 from 'ajv/dist/2020.js';
import { validateArmyAuto } from './validate-army-auto.mjs';
const schema = JSON.parse(await readFile(new URL('../data/opt-in/schema/army-auto.schema.json', import.meta.url), 'utf8'));
const fixture = JSON.parse(await readFile(new URL('../data/opt-in/army-auto/hand-fixtures-v1.json', import.meta.url), 'utf8'));
const validate = new Ajv2020({ strict: true, allErrors: true }).compile(schema);
const errors = value => validate(value) ? validateArmyAuto(value) : validate.errors;
test('auto hand fixtures conform to opt-in schema and bounds', () => assert.deepEqual(errors(fixture), []));
for (const [name, mutate] of [
  ['unknown field', s => { s.utc = 1; }], ['missing contract', s => { delete s.contractHash; }],
  ['negative defense', s => { s.objectiveDefense = -1; }], ['unknown mode', s => { s.mode = 'direct-pve'; }],
  ['duplicate policy', s => { s.rules.policies[1] = s.rules.policies[0]; }],
  ['missing matchup', s => { s.rules.matchups = []; }],
  ['duplicate troop', s => { s.attacker.troops.push(s.attacker.troops[0]); }],
  ['duplicate ordinal', s => { s.attacker.troops[0].ordinals = [0, 0]; }],
  ['too many slots', s => { s.rules.maxSlotsPerSide = 4097; }],
  ['work overflow', s => { s.rules.maxRounds = 2147483647; }],
  ['stat overflow', s => { s.rules.maxStat = 2147483647; }],
  ['unavailable hero', s => { s.attacker.effects = [{ kind: 'hero', id: 'test:hero', contentHash: 'A'.repeat(64), attack: 1, defense: 0, health: 0 }]; }],
]) test(`auto rejects ${name}`, () => { const value = structuredClone(fixture); mutate(value.fixtures[0].input); assert.notDeepEqual(errors(value), []); });
