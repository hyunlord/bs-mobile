import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { packageStage } from './package-stage.mjs';
test('packaging verifies binary/space/non-ASCII files and excludes untracked files',async()=>{
 const root=await mkdtemp(join(tmpdir(),'package-test-'));try{
  execFileSync('git',['init','-q'],{cwd:root});await mkdir(join(root,'내용'));await writeFile(join(root,'내용','a b.bin'),Buffer.from([0,255,13,10]));execFileSync('git',['add','.'],{cwd:root});execFileSync('git',['-c','user.name=Test','-c','user.email=test@example.invalid','commit','-qm','fixture'],{cwd:root});await writeFile(join(root,'untracked'),'excluded');
  const result=await packageStage(root,'R1','20261008',join(root,'out'));assert.equal(result.fileCount,1);assert.equal(result.cleanExtractionHashes,'PASS');
  const manifest=JSON.parse(execFileSync('unzip',['-p',result.zip,'MANIFEST.json'],{encoding:'utf8'}));assert.equal(manifest.files[0].bytes,4);assert.equal(manifest.files[0].path,'내용/a b.bin');assert.equal(manifest.stage,'R1');
  await symlink('untracked',join(root,'link'));execFileSync('git',['add','link'],{cwd:root});await assert.rejects(packageStage(root,'R1','20261008',join(root,'out')),/regular file/);
 }finally{await rm(root,{recursive:true,force:true});}
});
