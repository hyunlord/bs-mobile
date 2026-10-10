import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { restoreUrpAuthoring } from './restore-urp-authoring.mjs';

const authoring = 'custom pre-existing authoring\n    m_RuntimeSettings:\n      m_List: []\n  m_AssetVersion: 11\nother settings\n';
const generated = authoring.replace('      m_List: []', '      m_List:\n      - rid: 42\n      - rid: 99');

for (const [name, current, rejects] of [
  ['restores only generated list while preserving pre-existing edits', generated, false],
  ['preserves identical source bytes', authoring, false],
  ['refuses unrelated authoring changes', generated.replace('other settings', 'changed settings'), true],
  ['refuses unknown runtime serialization', authoring.replace('m_List: []', 'm_List: {unknown: 1}'), true],
]) {
  test(name, () => {
    const directory = mkdtempSync(join(tmpdir(), 'urp-authoring-'));
    const asset = join(directory, 'current.asset');
    const snapshot = join(directory, 'original.asset');
    try {
      writeFileSync(snapshot, authoring); writeFileSync(asset, current);
      if (rejects) {
        assert.throws(() => restoreUrpAuthoring(asset, snapshot));
        assert.equal(readFileSync(asset, 'utf8'), current);
      } else {
        restoreUrpAuthoring(asset, snapshot);
        assert.equal(readFileSync(asset, 'utf8'), authoring);
      }
      assert.equal(readFileSync(snapshot, 'utf8'), authoring);
    } finally { rmSync(directory, { recursive: true, force: true }); }
  });
}
