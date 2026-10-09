import test from 'node:test';
import assert from 'node:assert/strict';
import { validateMetaPacket } from './verify-meta-target-parity.mjs';

function packet() {
  const hash = 'A'.repeat(64);
  return { schemaVersion: 1, dataHash: hash, observations: [1, 3, 5, 7, 10].flatMap((chapter, index) => [0, 1, 2].map(repeat => ({
    seed: 52000 + index, chapter, repeat, tick: 300, commands: 301, catalogHash: hash, gameplayHash: hash,
    replayContextHash: hash, replayVerifiedHash: hash, settlementHash: hash, idleHash: hash, migratedSaveHashes: [hash, hash, hash],
  }))) };
}
test('accepts the exact bounded matrix', () => assert.equal(validateMetaPacket(packet()).length, 15));
test('rejects diverging settlement despite equal gameplay hashes', () => {
  const value = packet(); value.observations[1].settlementHash = 'B'.repeat(64);
  assert.throws(() => validateMetaPacket(value), /repeat diverged/);
});
test('rejects missing execution and wrong seed', () => {
  const value = packet(); value.observations.pop(); assert.throws(() => validateMetaPacket(value));
  const wrong = packet(); wrong.observations[9].seed++; assert.throws(() => validateMetaPacket(wrong));
});
test('rejects unexecuted ticks and failed replay equivalence', () => {
  const value = packet(); value.observations[0].tick = 0; assert.throws(() => validateMetaPacket(value));
  const replay = packet(); replay.observations[0].replayVerifiedHash = 'B'.repeat(64); assert.throws(() => validateMetaPacket(replay));
});
