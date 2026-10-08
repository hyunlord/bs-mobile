import assert from 'node:assert/strict';
import { lstat, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

export async function jsonFiles(directory) {
  if ((await lstat(directory)).isSymbolicLink()) throw new Error(`Symbolic links are not content: ${directory}`);
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.sort((a, b) => a.name.localeCompare(b.name)).map(async entry => {
    const filename = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error(`Symbolic links are not content: ${filename}`);
    if (entry.isDirectory()) return jsonFiles(filename);
    return entry.isFile() && entry.name.endsWith('.json') ? [filename] : [];
  }));
  return nested.flat();
}

export async function contentRecords(directory) {
  const records = new Map();
  for (const filename of await jsonFiles(directory)) {
    const bytes = await readFile(filename), record = JSON.parse(bytes);
    assert.equal(typeof record.id, 'string', `content record ID required: ${filename}`);
    assert.ok(!records.has(record.id), `duplicate content ID: ${record.id}`);
    records.set(record.id, { relative: path.relative(directory, filename).split(path.sep).join('/'), bytes, record });
  }
  return records;
}
