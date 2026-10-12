import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { lstat, mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const fontDirectory = 'unity/Assets/Generated/Resources/Fonts/GowunBatang';
export const fontManifest = JSON.parse(await readFile(path.join(here, 'font-assets/manifest.json'), 'utf8'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function validate(bytes, asset) {
  assert.equal(bytes.length, asset.bytes, `${asset.name}: byte length mismatch`);
  assert.equal(hash(bytes), asset.sha256, `${asset.name}: SHA256 mismatch`);
}
async function safePath(root, relative) {
  let current = root;
  for (const segment of relative.split('/')) {
    current = path.join(current, segment);
    const stat = await lstat(current).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    assert.ok(!stat?.isSymbolicLink(), `Symlink forbidden: ${current}`);
  }
  return current;
}
async function optionalRead(file) {
  return readFile(file).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
}
export async function downloadFont(url, asset, fetcher = fetch) {
  const signal = AbortSignal.timeout(30_000);
  for (let redirects = 0; redirects <= 3; redirects++) {
    const target = new URL(url);
    assert.equal(target.protocol, 'https:', 'Font download requires HTTPS');
    assert.equal(target.hostname, 'raw.githubusercontent.com', 'Unexpected font download host');
    const response = await fetcher(target.href, { redirect: 'manual', signal });
    if ([301, 302, 303, 307, 308].includes(response.status)) {
      const location = response.headers.get('location');
      await response.body?.cancel();
      assert.ok(location && redirects < 3, 'Font redirect limit or missing location');
      url = new URL(location, target).href;
      continue;
    }
    assert.ok(response.ok, `Font download failed: HTTP ${response.status}`);
    assert.ok(response.body, 'Empty font response');
    const chunks = []; let size = 0;
    for await (const chunk of response.body) {
      size += chunk.length;
      assert.ok(size <= asset.bytes, 'Font response exceeds pinned size');
      chunks.push(chunk);
    }
    const bytes = Buffer.concat(chunks, size);
    validate(bytes, asset);
    return bytes;
  }
  throw new Error('Font redirect limit exceeded');
}
export async function prepareFonts(root, { verifyOnly = false, fetcher = fetch, manifest = fontManifest } = {}) {
  assert.match(manifest.revision, /^[0-9a-f]{40}$/);
  for (const asset of manifest.files) {
    assert.match(asset.name, /^GowunBatang-(Regular|Bold)\.ttf$/);
    assert.ok(Number.isSafeInteger(asset.bytes) && asset.bytes > 0 && asset.bytes <= 10_000_000);
    assert.match(asset.sha256, /^[0-9a-f]{64}$/);
    const destination = await safePath(root, `${fontDirectory}/${asset.name}`);
    let bytes = await optionalRead(destination);
    if (bytes === null) {
      assert.ok(!verifyOnly, `Missing generated font: ${asset.name}; run tools/prepare-unity.sh`);
      const url = `https://raw.githubusercontent.com/google/fonts/${manifest.revision}/ofl/gowunbatang/${asset.name}`;
      bytes = await downloadFont(url, asset, fetcher);
      await mkdir(path.dirname(destination), { recursive: true });
      const temporary = await safePath(root, `${fontDirectory}/${asset.name}.download-${process.pid}`);
      try { await writeFile(temporary, bytes, { flag: 'wx' }); await rename(temporary, destination); }
      finally { await rm(temporary, { force: true }); }
    }
    validate(bytes, asset);
    const meta = await readFile(path.join(here, 'font-assets', `${asset.name}.meta`));
    const metaPath = await safePath(root, `${fontDirectory}/${asset.name}.meta`);
    if (verifyOnly) assert.deepEqual(await readFile(metaPath), meta, `${asset.name}: importer settings changed`);
    else await writeFile(metaPath, meta);
  }
  const license = await readFile(await safePath(root, 'unity/Assets/Resources/Fonts/GowunBatang/OFL.txt'));
  const outputLicense = await safePath(root, `${fontDirectory}/OFL.txt`);
  if (verifyOnly) assert.deepEqual(await readFile(outputLicense), license, 'Generated font license mismatch');
  else await writeFile(outputLicense, license);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    assert.ok(args.length === 0 || (args.length === 1 && args[0] === '--verify'), 'Usage: node tools/font-assets.mjs [--verify]');
    await prepareFonts(path.resolve(here, '..'), { verifyOnly: args[0] === '--verify' });
    console.log(`Pinned offline font assets ${args.length ? 'verified' : 'prepared'}`);
  } catch (error) { console.error(`Font preparation failed: ${error.message}`); process.exitCode = 1; }
}
