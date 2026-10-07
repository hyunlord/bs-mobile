import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import test from 'node:test';
const checker=path.resolve('tools/pr-policy.mjs');
for(const [name,comment,passes] of [
  ['standalone missing issue','// TODO: review this',false],
  ['inline missing issue','var count = 1; // TODO: review this',false],
  ['block missing issue','/* TODO: review this */',false],
  ['shell missing issue','# TODO: review this',false],
  ['linked inline','var count = 1; // TODO #27: review this',true],
  ['linked shell','# TODO #27: review this',true]
]) test(name,()=>{
 const directory=mkdtempSync(path.join(tmpdir(),'bs-policy-'));
 try {
  execFileSync('git',['init','-q'],{cwd:directory});
  writeFileSync(path.join(directory,comment.startsWith('#')?'fixture.sh':'Fixture.cs'),comment+'\n');
  execFileSync('git',['add','.'],{cwd:directory});
  const result=spawnSync(process.execPath,[checker],{cwd:directory,encoding:'utf8',env:{...process.env,GITHUB_EVENT_PATH:''}});
  assert.equal(result.status===0,passes,result.stderr);
  if(!passes) assert.match(result.stderr,/TODO must reference issue/);
 } finally {rmSync(directory,{recursive:true,force:true});}
});
