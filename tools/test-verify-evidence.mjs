import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { formatCsv } from './csv.mjs';
import { verifyEvidence } from './verify-evidence.mjs';
function fixture(t, asset = false) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'bs-verify-'));
  t.after(() => fs.rmSync(base, { recursive: true, force: true }));
  const dir = path.join(base, 'data'); fs.mkdirSync(dir);
  const filename = '원본.txt'; fs.writeFileSync(path.join(dir, filename), 'evidence');
  const field = asset ? 'asset' : 'path';
  const row = { [field]: filename, bytes: 8, sha256: createHash('sha256').update('evidence').digest('hex') };
  const manifest = path.join(base, 'manifest.csv');
  const write = rows => fs.writeFileSync(manifest, formatCsv([field, 'bytes', 'sha256'], rows));
  write([row]); return { dir, row, manifest, write };
}
test('source verifies exact file set; assets explicitly disclose unverified metadata', t => {
  const f = fixture(t); assert.equal(verifyEvidence(f.manifest, f.dir).verifiedFiles, 1);
  fs.writeFileSync(path.join(f.dir, 'extra'), 'x'); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Unexpected/);
  const a = fixture(t, true); fs.writeFileSync(path.join(a.dir, 'README.md'), 'metadata');
  assert.deepEqual(verifyEvidence(a.manifest, a.dir).unverifiedExtraFiles, ['README.md']);
});
test('rejects traversal, duplicate paths, malformed hash/size, missing and changed files', t => {
  const f = fixture(t);
  for (const name of ['../escape', '/absolute', 'C:/windows', 'a\\b', 'a//b', './file']) { f.write([{ ...f.row, path: name }]); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Unsafe/); }
  f.write([f.row, f.row]); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Duplicate/);
  for (const patch of [{ bytes: -1 }, { sha256: 'invalid' }, { bytes: 9 }]) { f.write([{ ...f.row, ...patch }]); assert.throws(() => verifyEvidence(f.manifest, f.dir)); }
  f.write([f.row]); fs.unlinkSync(path.join(f.dir, f.row.path)); assert.throws(() => verifyEvidence(f.manifest, f.dir));
});
test('rejects file, ancestor and extra symlinks', t => {
  const f = fixture(t); const original = path.join(f.dir, f.row.path);
  fs.renameSync(original, original + '.real'); fs.symlinkSync(original + '.real', original); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Symlink/);
  fs.unlinkSync(original); fs.renameSync(original + '.real', original);
  fs.symlinkSync(f.dir, path.join(f.dir, 'linked')); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Symlink/);
  f.write([{ ...f.row, path: 'linked/' + f.row.path }]); assert.throws(() => verifyEvidence(f.manifest, f.dir), /Symlink/);
});
