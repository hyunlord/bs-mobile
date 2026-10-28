import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isRateLimit, runGitHub, RateLimitSkipped } from './github-rate-limit.mjs';
import { selectHistory } from './durable-metrics.mjs';

test('archive only successful main push CI, not PR or manual CI', () => {
  const workflow = readFileSync(new URL('../.github/workflows/metrics.yml', import.meta.url), 'utf8');
  assert.match(workflow, /head_branch == 'main'/);
  assert.match(workflow, /workflow_run.event == 'push'/);
  assert.match(workflow, /workflow_run.conclusion == 'success'/);
  assert.doesNotMatch(workflow, /download.*--pattern '\*\.json'/);
});

test('only API rate limits qualify, not permission403/network500', () => {
  for (const text of ['gh: API rate limit exceeded (HTTP 403)', 'HTTP/2 403\nx-ratelimit-remaining: 0', 'gh: Too Many Requests (HTTP 429)', 'gh: secondary rate limit (HTTP 403)']) assert.equal(isRateLimit(text), true, text);
  for (const text of ['Resource not accessible by integration (HTTP 403)', 'Bad credentials (HTTP 401)', 'HTTP 500', 'timeout', 'rate limit without HTTP status', 'HTTP 404']) assert.equal(isRateLimit(text), false, text);
});

test('rate limit retries at most3 total attempts then SKIPPED; others fail immediately', async () => {
  let attempts = 0; const sleeps = [];
  await assert.rejects(runGitHub(['api', 'test'], { execute: async () => { attempts++; return { status: 1, stdout: '', stderr: 'API rate limit exceeded (HTTP 403)' }; }, sleep: async ms => sleeps.push(ms), notice: () => {} }), RateLimitSkipped);
  assert.equal(attempts, 3); assert.deepEqual(sleeps, [1000, 2000]);
  attempts = 0;
  await assert.rejects(runGitHub(['api', 'test'], { execute: async () => { attempts++; return { status: 1, stderr: 'permission denied (HTTP 403)', stdout: '' }; }, sleep: async () => assert.fail('must not retry') }), /GitHub command failed/);
  assert.equal(attempts, 1);
});

test('recovered quota succeeds and malformed success content remains caller failure', async () => {
  let attempts = 0;
  const output = await runGitHub(['api', 'test'], { execute: async () => ++attempts < 3 ? { status: 1, stderr: 'HTTP 429' } : { status: 0, stdout: 'invalid JSON' }, sleep: async () => {} });
  assert.equal(attempts, 3); assert.throws(() => JSON.parse(output));
});

test('429 historical JSON assets are bounded to30, deterministically newest first', () => {
  const assets = Array.from({ length: 429 }, (_, i) => ({ name: `${i.toString(16).padStart(40, '0')}-0123456789abcdef.json`, createdAt: new Date(1700000000000 + i * 1000).toISOString() }));
  assets.push({ name: 'index.html', createdAt: '2099-01-01T00:00:00Z' }, { name: '../escape.json', createdAt: '2099-01-01T00:00:00Z' });
  const chosen = selectHistory(assets); assert.equal(chosen.length, 30); assert.equal(chosen[0], assets[428].name); assert.equal(chosen.at(-1), assets[399].name);
  assert.equal(selectHistory([]).length, 0);
});

import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const fixture = { schemaVersion: 1, timestamp: '2026-01-01T00:00:00Z', commit: 'a'.repeat(40), stage: 'S0', model: 'scaffold', config: { iterations: 3 }, runtime: { version: 'test' }, seed: 42, tickP95Ms: 0.01, balanceDispersion: null, measurementScope: 'infrastructure' };
const fakeDirectory = mkdtempSync(join(tmpdir(), 'metrics-fake-gh-'));
writeFileSync(join(fakeDirectory, 'gh'), `#!/usr/bin/env node
import {readFileSync,writeFileSync,appendFileSync,existsSync} from 'node:fs';
import {join} from 'node:path';
const args=process.argv.slice(2); appendFileSync(process.env.FAKE_LOG, JSON.stringify(args)+'\\n');
const stage=args.slice(0,2).join(' ');
if(stage===process.env.FAKE_FAIL_STAGE){
  if(process.env.FAKE_ERROR==='partial'){
    const file=join(args[args.indexOf('--dir')+1],stage==='run download'?'metrics.json':args[args.indexOf('--pattern')+1]);
    const calls=readFileSync(process.env.FAKE_LOG,'utf8').trim().split('\\n').map(JSON.parse).filter(a=>a.slice(0,2).join(' ')===stage);
    if(calls.length===1){writeFileSync(file,'partial');console.error('HTTP 429');process.exit(1);}
    if(existsSync(file)&&!args.includes('--clobber')){console.error('file already exists');process.exit(1);}
  }else{console.error(process.env.FAKE_ERROR);process.exit(1);}
}
const sample=JSON.parse(process.env.FAKE_SAMPLE);
if(args[0]==='api') console.log(JSON.stringify([{name:'evidence-test',expired:false}]));
else if(stage==='run download') writeFileSync(join(args[args.indexOf('--dir')+1],'metrics.json'), JSON.stringify(sample));
else if(stage==='release view') console.log(JSON.stringify({assets:Array.from({length:429},(_,i)=>({name:i.toString(16).padStart(40,'0')+'-0123456789abcdef.json',createdAt:new Date(1700000000000+i*1000).toISOString()}))}));
else if(stage==='release download') writeFileSync(join(args[args.indexOf('--dir')+1],args[args.indexOf('--pattern')+1]),JSON.stringify({data:sample}));
else if(stage!=='release upload') throw new Error('Unexpected gh '+args);
`, { mode: 0o755 });

function runPublisher(stage = '', error = '') {
  const log = join(fakeDirectory, `calls-${Math.random()}.jsonl`);
  writeFileSync(log, '');
  const result = spawnSync(process.execPath, ['tools/durable-metrics.mjs'], {
    encoding: 'utf8', env: { ...process.env, PATH: `${fakeDirectory}:${process.env.PATH}`, GITHUB_REPOSITORY: 'test/repo', RUN_ID: '123', METRICS_SHA: fixture.commit, FAKE_SAMPLE: JSON.stringify(fixture), FAKE_LOG: log, FAKE_FAIL_STAGE: stage, FAKE_ERROR: error },
  });
  return { ...result, calls: readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line)) };
}

test('fake gh archives only30 of429 assets, uploads exactly new record+index and never deletes', () => {
  const result = runPublisher(); assert.equal(result.status, 0, result.stderr);
  assert.equal(result.calls.filter(args => args[1] === 'download' && args[0] === 'release').length, 30);
  const uploads = result.calls.filter(args => args[1] === 'upload'); assert.equal(uploads.length, 1);
  assert.equal(uploads[0].length, 8); assert.match(uploads[0][3], /[a-f0-9]{40}-[a-f0-9]{16}\.json$/); assert.match(uploads[0][4], /index\.html$/);
  assert.ok(result.calls.every(args => !args.includes('delete') && !args.includes('*.json')));
});

for (const stage of ['api repos/test/repo/actions/runs/123/artifacts', 'run download', 'release view', 'release download', 'release upload']) {
  test(`fake gh ${stage} rate limit ends in explicit SKIPPED exit0 after3 attempts`, () => {
    const result = runPublisher(stage, 'gh: Too Many Requests (HTTP 429)');
    assert.equal(result.status, 0, result.stderr); assert.match(result.stdout, /::notice::SKIPPED/);
    assert.equal(result.calls.filter(args => args.slice(0, 2).join(' ') === stage).length, 3);
    assert.equal(result.calls.at(-1).slice(0, 2).join(' '), stage);
  });
}
for (const error of ['Resource not accessible by integration (HTTP 403)', 'Bad credentials (HTTP 401)', 'Not found (HTTP 404)', 'HTTP 500', 'timeout']) {
  test(`fake gh fails without retry or SKIPPED: ${error}`, () => {
    const result = runPublisher('release view', error); assert.equal(result.status, 1);
    assert.doesNotMatch(result.stdout, /SKIPPED/); assert.equal(result.calls.filter(args => args[1] === 'view').length, 1);
  });
}

for (const stage of ['run download', 'release download']) {
  test('recovers after partial file writes and429 during '+stage, () => {
    const result = runPublisher(stage, 'partial');
    assert.equal(result.status, 0, result.stderr); assert.doesNotMatch(result.stdout, /SKIPPED/);
    assert.equal(result.calls.filter(args => args[1] === 'upload').length, 1);
  });
}
