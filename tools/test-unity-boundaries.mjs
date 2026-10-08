import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const validator = path.join(import.meta.dirname, 'verify-unity-boundaries.mjs');
function fixture(run) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-boundary-'));
  try {
    for (const name of ['Game.App', 'Game.View', 'Game.Input', 'Game.Debug', 'Game.Editor', 'Tests.EditMode', 'Tests.PlayMode']) {
      fs.writeFileSync(path.join(dir, `${name}.asmdef`), JSON.stringify({ name, references: [] }));
    }
    run(dir, () => spawnSync(process.execPath, [validator, dir], { encoding: 'utf8' }));
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}
test('accepts acyclic boundaries and rejects app-state reference from View', () => fixture((dir, check) => {
  assert.equal(check().status, 0);
  fs.writeFileSync(path.join(dir, 'Game.View.asmdef'), JSON.stringify({ name: 'Game.View', references: ['Game.App'] }));
  assert.notEqual(check().status, 0);
}));
test('rejects cycles and runtime host DLLs', () => fixture((dir, check) => {
  fs.writeFileSync(path.join(dir, 'Game.App.asmdef'), JSON.stringify({ name: 'Game.App', references: ['Game.App'] }));
  assert.notEqual(check().status, 0);
  fs.writeFileSync(path.join(dir, 'Game.App.asmdef'), JSON.stringify({ name: 'Game.App', references: [] }));
  fs.writeFileSync(path.join(dir, 'SowSiege.Sim.dll'), 'forbidden host binary');
  assert.notEqual(check().status, 0);
}));
