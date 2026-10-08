import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { contentRecords } from './content-files.mjs';

export async function weaponDefinitionHash(directory, profile) {
  const selected = new Set([...profile.selection.weapons, ...profile.testSelection.weapons]);
  const sources = new Map(), seen = new Set();
  if (profile.weaponCombat.definitionsFile) {
    const file = profile.weaponCombat.definitionsFile;
    sources.set(file, await fs.readFile(path.join(directory, file)));
  }
  for (const folder of ['weapons', 'test/weapons']) {
    let records;
    try { records = await contentRecords(path.join(directory, folder)); }
    catch (error) { if (error.code === 'ENOENT' && folder === 'test/weapons') continue; throw error; }
    for (const { relative: file, bytes, record: weapon } of records.values()) {
      assert.ok(!seen.has(weapon.id), `duplicate content ID: ${weapon.id}`);
      seen.add(weapon.id);
      if (!selected.has(weapon.id)) continue;
      assert.ok(weapon.growth, `canonical growth missing for ${weapon.id}`);
      const relative = `${folder}/${file}`;
      sources.set(relative, bytes);
      selected.delete(weapon.id);
    }
  }
  assert.equal(selected.size, 0, 'selected canonical weapon source missing');
  const hash = createHash('sha256');
  for (const [file, bytes] of [...sources].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) hash.update(file + '\0').update(bytes).update('\0');
  return hash.digest('hex');
}
