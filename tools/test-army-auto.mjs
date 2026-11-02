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

for (const suffix of ['\n', '\r', '\r\n', '\u2028', '\u2029']) {
  for (const field of ['document-id', 'contract-hash', 'content-hash', 'troop-id', 'matchup-attacker', 'matchup-defender', 'effect-id', 'effect-hash']) {
    test(`auto schema rejects ${field} suffix ${JSON.stringify(suffix)}`, () => {
      const value = structuredClone(fixture);
      const input = value.fixtures[0].input;
      if (field === 'document-id') value.id += suffix;
      if (field === 'contract-hash') input.contractHash += suffix;
      if (field === 'content-hash') input.contentHash += suffix;
      if (field === 'troop-id') input.attacker.troops[0].id += suffix;
      if (field === 'matchup-attacker') input.rules.matchups[0].attackerId += suffix;
      if (field === 'matchup-defender') input.rules.matchups[0].defenderId += suffix;
      if (field.startsWith('effect-')) {
        const effect = { kind: 'research', id: 'test:effect', contentHash: 'A'.repeat(64), attack: 0, defense: 0, health: 0 };
        effect[field === 'effect-id' ? 'id' : 'contentHash'] += suffix;
        input.defender.effects = [effect];
      }
      assert.equal(validate(value), false, 'schema itself must reject trailing characters');
    });
  }
}
