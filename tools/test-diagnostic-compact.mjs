import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { compact } from './diagnostic-compact.mjs';
import { formatCsv,parseCsv } from './csv.mjs';

async function fixture(root){
 const dir=path.join(root,'input');await fs.mkdir(dir);
 const identities=['a','b'].map((caseId,i)=>({caseId,cohort:'r3',policy:'weapon',peopleRule:'C',seed:42+i,arm:'control'}));
 const runs=identities.map(r=>({...r,ticks:300,survived:true,gameplayHash:'a'.repeat(64)}));
 const provenance=identities.map((r,i)=>({...r,packetFile:`packets/${r.caseId}.json`,packetSha256:String(i+1).repeat(64),inputHash:String(i+3).repeat(64),commit:'b'.repeat(40),gitDirty:false,contentSha256:'c'.repeat(64),profileSha256:'d'.repeat(64),tuningSha256:'e'.repeat(64),sourceTreeSha256:'f'.repeat(64),coreAssemblySha256:'a'.repeat(64),simulationAssemblySha256:'b'.repeat(64)}));
 const terminal=identities.map(r=>({...r,tick:300,weaponCount:1,weaponRankSum:5,nearLordRadius:100,targetDamageJson:JSON.stringify([{targetKind:'lord',attackAttempts:2,rawTargetDamage:4,appliedTargetDamage:3}]),interceptedTargetKindsJson:JSON.stringify({lord:2})}));
 const snapshots=[{tick:0,nAlive:2,aliveSumsJson:JSON.stringify({damage:0,radius:200}),denominator:'alive'},{tick:300,nAlive:0,aliveSumsJson:'{}',denominator:'alive'}];
 const attacks=identities.map(r=>({...r,sourceId:'weapon:one',appliedHpDamage:7,suppressedHpDamage:0,noTargetAttempts:3,knockbackDistance:2}));
 const population=identities.map(r=>({...r,role:'peasant',entities:2,members:10,memberTicks:3000}));
 const equipment=identities.flatMap(r=>[{...r,id:'weapon:one',kind:'weapon',rank:5},{...r,id:'tool:one',kind:'tool',rank:2}]);
 const cards=identities.flatMap(r=>[{...r,tick:30,level:2,chosen:'weapon:one',rarity:'rare',offeredJson:JSON.stringify(['weapon:one','tool:one'])},{...r,tick:60,level:3,chosen:'tool:one',rarity:'common',offeredJson:JSON.stringify(['weapon:one','tool:one'])}]);
 const tables={'runs.csv':runs,'provenance.csv':provenance,'terminal-counters.csv':terminal,'snapshots.csv':snapshots,'attack-sources.csv':attacks,'population.csv':population,'equipment.csv':equipment,'card-choices.csv':cards};
 for(const name of ['paired.csv','interactions.csv','paired-summary.csv','policy-effect-differences.csv','public-selection.csv'])tables[name]=[{caseId:'a',value:1}];
 for(const[name,rows]of Object.entries(tables))await fs.writeFile(path.join(dir,name),formatCsv(Object.keys(rows[0]),rows));return dir;
}
async function usingFixture(work){const root=await fs.mkdtemp(path.join(os.tmpdir(),'diagnostic-compact-ci-'));try{await work(root,await fixture(root));}finally{await fs.rm(root,{recursive:true,force:true});}}

test('normalization preserves zero versus absent observations and input identity under shared provenance',async()=>usingFixture(async(root,input)=>{
 const result=await compact(input,path.join(root,'out'));
 assert.equal(result.tables['provenance.csv'].length,1);
 const runs=result.tables['runs.csv'];assert.equal(runs[0].provenanceId,runs[1].provenanceId);assert.notEqual(runs[0].inputHash,runs[1].inputHash);assert.notEqual(runs[0].packetSha256,runs[1].packetSha256);
 const snapshots=parseCsv(await fs.readFile(path.join(root,'out','snapshots.csv'),'utf8'));assert.equal(snapshots[0].sum_damage,'0');assert.equal(snapshots[1].sum_damage,'');
 const original=parseCsv(await fs.readFile(path.join(input,'attack-sources.csv'),'utf8'));
 const restored=result.tables['attack-sources.csv'].map(r=>{const run=runs.find(x=>x.caseId===r.caseId);return {...r,...Object.fromEntries(['cohort','policy','peopleRule','seed','arm'].map(k=>[k,run[k]]))};});assert.deepEqual(restored,original);
 assert.equal(result.tables['target-damage.csv'][0].appliedTargetDamage,3);
}));
test('weapon summary keeps actual offers, choices, rarity and final rank without treating tool rank as weapon',async()=>usingFixture(async(root,input)=>{
 const result=await compact(input,path.join(root,'out'));assert.equal(result.tables['weapon-equipment.csv'].length,2);
 for(const r of result.tables['weapon-card-summary.csv']){assert.equal(r.weaponId,'weapon:one');assert.equal(r.offerCount,2);assert.equal(r.chosenCount,1);assert.equal(r.firstOfferedTick,30);assert.equal(r.lastOfferedTick,60);assert.equal(r.firstChosenTick,30);assert.equal(r.lastChosenTick,30);assert.equal(r.finalRank,5);assert.deepEqual(JSON.parse(r.rarityCountsJson),{rare:1});}
}));
test('export is deterministic, refuses overwrite and fails budget',async()=>usingFixture(async(root,input)=>{
 const a=path.join(root,'a'),b=path.join(root,'b');await compact(input,a);await compact(input,b);const files=await fs.readdir(a);assert.equal(files.length,17);for(const file of files)assert.deepEqual(await fs.readFile(path.join(a,file)),await fs.readFile(path.join(b,file)));
 await assert.rejects(compact(input,a),{code:'EEXIST'});const tiny=path.join(root,'tiny');await assert.rejects(compact(input,tiny,1),/budget exceeded/);await assert.rejects(fs.access(tiny),{code:'ENOENT'});
}));
