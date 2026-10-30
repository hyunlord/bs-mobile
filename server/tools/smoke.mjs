#!/usr/bin/env node
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';

const base = 'http://127.0.0.1:5080';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function request(path, token, body) {
  const response = await fetch(base + path, {
    method: body ? 'POST' : 'GET',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), 'Content-Type': 'application/json' },
    ...(body ? { body: JSON.stringify(body) } : {}),
    signal: AbortSignal.timeout(10000),
  });
  assert.equal(response.status, 200, `HTTP ${response.status}: ${await response.clone().text()}`);
  return response.json();
}
function compose(...args) {
  const result = spawnSync('docker', ['compose', '-f', 'server/compose.yaml', ...args], { stdio: 'inherit' });
  assert.equal(result.status, 0, 'Compose command failed');
}
async function ready() {
  for (let attempt = 0; attempt < 30; attempt++) {
    try {
      const response = await fetch(`${base}/health`, { signal: AbortSignal.timeout(1000) });
      if (response.ok) return;
    } catch { /* bounded startup wait */ }
    await delay(1000);
  }
  throw new Error('Host did not become ready in 30 seconds');
}
await ready();
const created = await request('/prototype/accounts', null, {});
const granary = created.estate.buildings.find(building => building.kind === 'meta:granary');
assert.ok(granary);
const command = { commandId: randomUUID(), expectedVersion: created.estate.version, type: 'start-construction', buildingId: granary.id };
const first = await request('/v1/estate/commands', created.token, command);
assert.equal(first.done, true, first.reason);
assert.deepEqual(await request('/v1/estate/commands', created.token, command), first);
// Real elapsed time, no admin clock/credit endpoint. The token stays in memory only.
compose('restart', 'host');
await ready();
compose('restart', 'redis');
await delay(62000);
const resumed = await request('/v1/estate', created.token);
assert.equal(resumed.estate.buildings.find(building => building.id === granary.id).level, 1);
assert.ok(resumed.estate.version > first.estate.version);
assert.equal(resumed.estate.construction, null);
console.log(JSON.stringify({ gate: 'PASS', account: 'new', hostRestart: true, redisRestart: true,
  completed: 'meta:granary', version: resumed.estate.version, creditedSeconds: resumed.estate.creditedSeconds }));
