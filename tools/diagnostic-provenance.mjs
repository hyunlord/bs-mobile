import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
async function files(root){const out=[];for(const e of await fs.readdir(root,{withFileTypes:true})){if(e.name==='bin'||e.name==='obj')continue;const f=path.join(root,e.name);if(e.isDirectory())out.push(...await files(f));else if(e.isFile())out.push(f);}return out;}
async function hashFiles(list,root){const h=createHash('sha256');for(const file of list.sort()){h.update(path.relative(root,file).split(path.sep).join('/')+'\0');h.update(await fs.readFile(file));h.update('\0');}return h.digest('hex');}
export async function captureProvenance(){
  const sourceFiles=(await files('core')).filter(f=>/\.(cs|csproj|props|targets)$/.test(f));for(const f of ['global.json','Directory.Build.props','Directory.Build.targets'])try{await fs.access(f);sourceFiles.push(f);}catch(e){if(e.code!=='ENOENT')throw e;}
  const common={sourceTreeSha256:await hashFiles(sourceFiles,'.'),simulationAssemblySha256:sha(await fs.readFile('core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll')),coreAssemblySha256:sha(await fs.readFile('core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Core.dll'))};
  const cohorts={};for(const cohort of ['r2','r3']){const root=cohort==='r3'?'data':'core/tests/SowSiege.Tests/Fixtures/phase0-r2/data';const profileBytes=await fs.readFile(path.join(root,'profiles/s4b-02.json')),profile=JSON.parse(profileBytes);cohorts[cohort]={profileSha256:sha(profileBytes),tuningSha256:sha(await fs.readFile(path.join(root,profile.experiment.tuningFile))),contentSha256:await hashFiles((await files(root)).filter(f=>f.endsWith('.json')&&!path.relative(root,f).startsWith('test/')),root)};}
  return {common,cohorts};
}
export function validateProvenance(p,frozen,cohort){for(const[k,v]of Object.entries({...frozen.common,...frozen.cohorts[cohort]}))assert.equal(p[k]?.toLowerCase(),v,`frozen ${k}`);assert.equal(p.coreAssembly.sha256.toLowerCase(),frozen.common.coreAssemblySha256);assert.equal(p.coreAssembly.targetFramework,'.NETCoreApp,Version=v8.0');}
