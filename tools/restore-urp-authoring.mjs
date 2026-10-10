import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

function withoutRuntimeList(bytes) {
  const text = bytes.toString('utf8');
  if (!Buffer.from(text, 'utf8').equals(bytes)) throw new Error('URP settings must be valid UTF-8.');
  const list = /^    m_RuntimeSettings:\r?\n      m_List:(?: \[\]\r?\n|\r?\n(?:      - rid: -?\d+\r?\n)*)(?=  m_AssetVersion:)/gm;
  const matches = [...text.matchAll(list)];
  if (matches.length !== 1) throw new Error('Unknown URP runtime-list layout; preserving the asset for inspection.');
  return text.replace(list, '<generated URP runtime list>\n');
}

export function restoreUrpAuthoring(assetPath, snapshotPath) {
  const original = readFileSync(snapshotPath);
  const current = readFileSync(assetPath);
  if (current.equals(original)) return;
  if (withoutRuntimeList(current) !== withoutRuntimeList(original)) {
    throw new Error('URP settings changed outside the generated runtime list; preserving both versions for inspection.');
  }
  writeFileSync(assetPath, original);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (process.argv.length !== 4) throw new Error('Usage: restore-urp-authoring.mjs <asset> <pre-build-snapshot>');
  restoreUrpAuthoring(process.argv[2], process.argv[3]);
}
