import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, rm, writeFile, symlink } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { downloadFont, fontDirectory, prepareFonts } from './font-assets.mjs';
const bytes = Buffer.from('fixture-font');
const asset = { name: 'GowunBatang-Regular.ttf', bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') };
const manifest = { revision: 'a'.repeat(40), files: [asset] };
const url = `https://raw.githubusercontent.com/google/fonts/${manifest.revision}/ofl/gowunbatang/${asset.name}`;
async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'font-assets-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const license = path.join(root, 'unity/Assets/Resources/Fonts/GowunBatang');
  await mkdir(license, { recursive: true }); await writeFile(path.join(license, 'OFL.txt'), 'fixture license');
  return root;
}
test('pinned preparation bundles font, importer and license; verification stays offline', async t => {
  const root = await fixture(t); let requests = 0;
  await prepareFonts(root, { manifest, fetcher: async requested => { requests++; assert.equal(requested, url); return new Response(bytes); } });
  assert.equal(requests, 1);
  assert.deepEqual(await readFile(path.join(root, fontDirectory, asset.name)), bytes);
  const offline = async () => assert.fail('cache/verify must not download');
  await prepareFonts(root, { manifest, fetcher: offline });
  await prepareFonts(root, { manifest, verifyOnly: true, fetcher: offline });
});
test('missing or corrupt generated assets fail closed without downloading in verify', async t => {
  const root = await fixture(t); const offline = async () => assert.fail('must not download');
  await assert.rejects(prepareFonts(root, { manifest, verifyOnly: true, fetcher: offline }), /Missing generated font/);
  await mkdir(path.join(root, fontDirectory), { recursive: true });
  await writeFile(path.join(root, fontDirectory, asset.name), Buffer.alloc(bytes.length));
  await assert.rejects(prepareFonts(root, { manifest, fetcher: offline }), /SHA256 mismatch/);
});
test('download rejects wrong hashes, oversized responses, insecure redirects and redirect loops', async () => {
  await assert.rejects(downloadFont(url, asset, async () => new Response(Buffer.alloc(bytes.length))), /SHA256 mismatch/);
  await assert.rejects(downloadFont(url, asset, async () => new Response(Buffer.alloc(bytes.length + 1))), /exceeds pinned size/);
  await assert.rejects(downloadFont(url, asset, async () => new Response(null, { status: 302, headers: { location: 'http://raw.githubusercontent.com/font' } })), /HTTPS/);
  await assert.rejects(downloadFont(url, asset, async () => new Response(null, { status: 302, headers: { location: url } })), /redirect limit/);
});
test('generated path refuses symlinks', async t => {
  const root = await fixture(t); await symlink(os.tmpdir(), path.join(root, 'unity/Assets/Generated'));
  await assert.rejects(prepareFonts(root, { manifest }), /Symlink forbidden/);
});
