import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { validateArmyPacket } from './verify-army-auto-target-parity.mjs';
const packet = () => ({ schemaVersion: 1, observations: [{ name: 'fixture', inputHash: 'A'.repeat(64), bytes: Buffer.from('result').toString('base64'), stateHash: createHash('sha256').update('result').digest('hex').toUpperCase() }] });
test('army parity binds output bytes to state digest', () => validateArmyPacket(packet(), ['fixture']));
test('army parity rejects missing fixture', () => assert.throws(() => validateArmyPacket(packet(), ['fixture', 'missing'])));
test('army parity rejects modified output', () => { const p = packet(); p.observations[0].bytes = 'eA=='; assert.throws(() => validateArmyPacket(p, ['fixture'])); });
test('army parity rejects missing provenance', () => { const p = packet(); p.observations[0].inputHash = ''; assert.throws(() => validateArmyPacket(p, ['fixture'])); });
