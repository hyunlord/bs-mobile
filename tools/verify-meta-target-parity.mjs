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
export function validateMetaPacket(packet) {
  assert.equal(packet.schemaVersion, 1);
  assert.match(packet.dataHash, /^[a-f0-9]{64}$/i);
  assert.equal(packet.observations.length, 15, 'five seeds times three repeats required');
  const chapters = [1, 3, 5, 7, 10];
  for (let index = 0; index < chapters.length; index++) {
    const rows = packet.observations.slice(index * 3, index * 3 + 3);
    let expected;
    for (let repeat = 0; repeat < 3; repeat++) {
      const row = rows[repeat];
      assert.equal(row.seed, 52000 + index);
      assert.equal(row.chapter, chapters[index]);
      assert.equal(row.repeat, repeat);
      assert.equal(row.tick, 300, 'probe must actually advance 300 ticks');
      assert.ok(Number.isSafeInteger(row.commands) && row.commands >= 300 && row.commands < 1000);
      for (const key of ['catalogHash', 'gameplayHash', 'replayContextHash', 'replayVerifiedHash', 'settlementHash', 'idleHash']) assert.match(row[key], /^[a-f0-9]{64}$/i);
      assert.equal(row.gameplayHash, row.replayVerifiedHash);
      assert.equal(row.migratedSaveHashes.length, 3);
      for (const hash of row.migratedSaveHashes) assert.match(hash, /^[a-f0-9]{64}$/i);
      const { repeat: ignored, ...outcome } = row;
      expected ??= outcome;
      assert.deepEqual(outcome, expected, 'meta repeat diverged');
    }
  }
  return packet.observations;
}

export async function runMetaParity(output) {
  assert.ok(output, 'usage: node tools/verify-meta-target-parity.mjs OUTPUT');
  await fs.mkdir(output);
  const sourceCommit = (await execute('git', ['rev-parse', 'HEAD'])).stdout.trim();
  const sourceDirty = Boolean((await execute('git', ['status', '--porcelain=v1'])).stdout.trim());
  const dotnet = process.env.DOTNET ?? 'dotnet';
  const targets = [
    { name: 'net8', host: 'SowSiege.Sim', framework: 'net8.0', tfm: '.NETCoreApp,Version=v8.0' },
    { name: 'standard21', host: 'SowSiege.CompatSim', framework: 'netstandard2.1', tfm: '.NETStandard,Version=v2.1' },
  ];
  const packets = [];
  for (const target of targets) {
    const dll = path.resolve(`core/src/${target.host}/bin/Release/net8.0/${target.host}.dll`);
    const built = path.resolve(`core/src/SowSiege.Core/bin/Release/${target.framework}/SowSiege.Core.dll`);
    const loaded = await fs.realpath(path.join(path.dirname(dll), 'SowSiege.Core.dll'));
    const expected = { targetFramework: target.tfm, location: loaded, sha256: sha(await fs.readFile(built)) };
    assert.equal(sha(await fs.readFile(loaded)), expected.sha256, 'host copy differs from target build');
    const destination = path.resolve(output, `${target.name}.json`);
    await runProcess(dotnet, [dll, 'meta-parity-probe', path.resolve('data'), destination], target.name);
    const packet = JSON.parse(await fs.readFile(destination, 'utf8'));
    validateBinding(packet.coreAssembly, expected);
    assert.equal(await fs.realpath(packet.coreAssembly.location), loaded);
    assert.equal(sha(await fs.readFile(loaded)), expected.sha256, 'assembly changed during probe');
    validateMetaPacket(packet);
    packets.push(packet);
  }
  validatePair(packets[0].coreAssembly, packets[1].coreAssembly);
  assert.equal(packets[0].dataHash, packets[1].dataHash, 'data changed between targets');
  assert.deepEqual(packets[0].observations, packets[1].observations, 'meta target outcomes diverged');
  assert.equal((await execute('git', ['rev-parse', 'HEAD'])).stdout.trim(), sourceCommit);
  const summary = { schemaVersion: 1, passed: true, sourceCommit, sourceDirty, dataHash: packets[0].dataHash,
    seeds: 5, chapters: [1, 3, 5, 7, 10], repeatsPerTarget: 3, executions: 30, ticksPerExecution: 300,
    targetBindings: packets.map(packet => packet.coreAssembly),
    scope: 'Actual net8.0 and netstandard2.1 Core in separate .NET hosts. Short projected runs, snapshot replay, settlement, idle, save migrations. Not full-run survival, Unity, or device validation.' };
  await fs.writeFile(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
  console.log(`PASS meta parity: 5 seeds × 3 repeats × 2 actual Core targets; ${output}`);
  return summary;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) runMetaParity(process.argv[2]).catch(error => { console.error(error.message); process.exitCode = 1; });
