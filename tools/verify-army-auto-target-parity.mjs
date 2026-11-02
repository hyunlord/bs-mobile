import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { validateBinding, validatePair, runProcess } from './verify-target-parity.mjs';
const execute = promisify(execFile);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
export function validateArmyPacket(packet, names) {
  assert.equal(packet.schemaVersion, 1);
  assert.deepEqual(packet.observations.map(o => o.name), names);
  assert.ok(names.length > 0);
  for (const row of packet.observations) {
    assert.match(row.inputHash, /^[A-F0-9]{64}$/);
    assert.equal(sha(Buffer.from(row.bytes, 'base64')).toUpperCase(), row.stateHash);
    assert.ok(Buffer.from(row.bytes, 'base64').length > 0);
  }
}
export async function runArmyAutoParity(output) {
  assert.ok(output, 'usage: node tools/verify-army-auto-target-parity.mjs OUTPUT');
  await fs.mkdir(output);
  const sourceCommit = (await execute('git', ['rev-parse', 'HEAD'])).stdout.trim();
  const sourceDirty = Boolean((await execute('git', ['status', '--porcelain=v1'])).stdout.trim());
  const fixture = path.resolve('data/opt-in/army-auto/hand-fixtures-v1.json');
  const fixtureBytes = await fs.readFile(fixture);
  const names = JSON.parse(fixtureBytes).fixtures.map(f => f.name);
  const packets = [];
  for (const target of [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ]) {
    const dll = path.resolve(`core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    const built = path.resolve(`core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`);
    const loaded = await fs.realpath(path.join(path.dirname(dll), 'SowSiege.Core.dll'));
    const expected = { targetFramework: target.tfm, location: loaded, sha256: sha(await fs.readFile(built)) };
    assert.equal(sha(await fs.readFile(loaded)), expected.sha256);
    const destination = path.resolve(output, `${target.name}.json`);
    await runProcess(process.env.DOTNET ?? 'dotnet', [dll, 'army-auto-fixtures', fixture, destination], target.name);
    const packet = JSON.parse(await fs.readFile(destination, 'utf8'));
    validateBinding(packet.coreAssembly, expected);
    assert.equal(await fs.realpath(packet.coreAssembly.location), loaded);
    assert.equal(sha(await fs.readFile(loaded)), expected.sha256);
    assert.deepEqual(await fs.readFile(fixture), fixtureBytes);
    validateArmyPacket(packet, names); packets.push(packet);
  }
  validatePair(packets[0].coreAssembly, packets[1].coreAssembly);
  assert.deepEqual(packets[0].observations, packets[1].observations, 'auto full result bytes/hash differ across actual targets');
  assert.equal((await execute('git', ['rev-parse', 'HEAD'])).stdout.trim(), sourceCommit);
  const summary = { passed: true, sourceCommit, sourceDirty, fixtureHash: sha(fixtureBytes), casesPerTarget: names.length,
    targetBindings: packets.map(p => p.coreAssembly), scope: 'Actual net8/netstandard Core DLLs in separate hosts; not Unity or performance evidence.' };
  await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
  console.log(`PASS army-auto parity: ${names.length} fixtures × 2 actual targets`);
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) runArmyAutoParity(process.argv[2]).catch(e => { console.error(e.message); process.exitCode = 1; });
