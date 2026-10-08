import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const project = path.join(import.meta.dirname, 'UnityResultCheck');
const dotnet = process.env.DOTNET ?? path.join(os.homedir(), '.dotnet/dotnet');
test('NUnit gate accepts positive pass and rejects empty, failed, malformed, DTD', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-results-'));
  try {
    const file = path.join(dir, 'results.xml');
    const cases = [
      ['<test-run total="2" passed="2" failed="0" result="Passed"/>', true],
      ['<test-run total="0" passed="0" failed="0" result="Passed"/>', false],
      ['<test-run total="2" passed="1" failed="1" result="Failed"/>', false],
      ['<test-run', false],
      ['<!DOCTYPE test-run [<!ENTITY x "2">]><test-run total="&x;" passed="2" failed="0" result="Passed"/>', false],
    ];
    for (const [xml, pass] of cases) {
      fs.writeFileSync(file, xml);
      const result = spawnSync(dotnet, ['run', '--project', project, '--', file], { encoding: 'utf8' });
      assert.equal(result.status === 0, pass, result.stdout + result.stderr);
    }
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});
