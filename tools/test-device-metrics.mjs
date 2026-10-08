import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { formatCsv } from './csv.mjs';
import { headers, summarizeDevice } from './device-metrics.mjs';
const commit = 'a'.repeat(40), hash = 'b'.repeat(64);
function fixture(t, samples) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(),'device-metrics-')); t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));
  const write = (name,value)=>fs.writeFileSync(path.join(dir,name),JSON.stringify(value));
  write('recording.json',{sessionId:'anonymous',seed:30000,build:commit,dataHash:hash,sourceHash:hash,sourceDirty:true});
  write('device.json',{model:'test',os:'Android',unityVersion:'6000.6.4f1',backend:'IL2CPP',sourceHash:hash,sourceDirty:true});
  write('frame-summary.json',{sessionId:'anonymous',build:commit,dataHash:hash,durationTicks:100,lateStartTick:75,frames:samples.length});
  const rows = samples.map((sample,index)=>({...Object.fromEntries(headers.map(k=>[k,0])),frame:index,width:400,height:800,safeWidth:400,safeHeight:800,thermal:'unavailable',speed:1,...sample}));
  fs.writeFileSync(path.join(dir,'frames.csv'),formatCsv(headers,rows)); return {dir,rows};
}
test('keeps stutter across boundary; excludes paused sample; retains maxima and source provenance',t=>{
  const {dir}=fixture(t,[{tick:74,deltaMs:16},{tick:80,deltaMs:500},{tick:80,deltaMs:9000,paused:1,speed:4,enemies:99},{tick:100,deltaMs:20}]);
  const m=summarizeDevice(dir,{commit}); assert.equal(m.frameP95Ms,500); assert.equal(m.frameMaxMs,500); assert.equal(m.sampleCount,2); assert.equal(m.lateComplete,true); assert.equal(m.maxCounts.enemies,99); assert.equal(m.config.sourceDirty,true); assert.equal(m.tickP95Ms,null);
});
test('accelerated late window is incomplete even with final 1x sample',t=>{
 const {dir}=fixture(t,[{tick:70,deltaMs:16},{tick:80,deltaMs:10,speed:2},{tick:100,deltaMs:20}]); assert.equal(summarizeDevice(dir,{commit}).lateComplete,false);
});
test('no eligible samples produce null frame statistics',t=>{
 const {dir}=fixture(t,[{tick:2,deltaMs:16}]); const m=summarizeDevice(dir,{commit}); assert.equal(m.frameP95Ms,null); assert.equal(m.frameMaxMs,null); assert.equal(m.lateComplete,false);
});
test('rejects corrupt sequence and numeric values',t=>{
 const {dir,rows}=fixture(t,[{tick:75,deltaMs:16}]); rows[0].frame=3; fs.writeFileSync(path.join(dir,'frames.csv'),formatCsv(headers,rows)); assert.throws(()=>summarizeDevice(dir,{commit}),/sequence/); rows[0].frame=0;rows[0].deltaMs='NaN';fs.writeFileSync(path.join(dir,'frames.csv'),formatCsv(headers,rows)); assert.throws(()=>summarizeDevice(dir,{commit}),/numeric/);
});
test('rejects build mismatch rather than relabeling evidence',t=>{
 const {dir}=fixture(t,[]);assert.throws(()=>summarizeDevice(dir,{commit:'c'.repeat(40)}),/identity/);
});

test('anonymous sessions share same-build measurement configuration',t=>{
 const {dir}=fixture(t,[{tick:75,deltaMs:16}]); const first=summarizeDevice(dir,{commit});
 for(const name of ['recording.json','frame-summary.json']){const file=path.join(dir,name),value=JSON.parse(fs.readFileSync(file,'utf8'));value.sessionId='another-anonymous-session';fs.writeFileSync(file,JSON.stringify(value));}
 const second=summarizeDevice(dir,{commit});assert.deepEqual(first.config,second.config);assert.notEqual(first.sessionId,second.sessionId);
});

test('rejects mismatched source identity sidecars',t=>{
 const {dir}=fixture(t,[]);const file=path.join(dir,'device.json'),facts=JSON.parse(fs.readFileSync(file,'utf8'));facts.sourceDirty=false;fs.writeFileSync(file,JSON.stringify(facts));assert.throws(()=>summarizeDevice(dir,{commit}),/source provenance/);facts.sourceDirty=true;facts.sourceHash='c'.repeat(64);fs.writeFileSync(file,JSON.stringify(facts));assert.throws(()=>summarizeDevice(dir,{commit}),/source provenance/);
});

test('suspension and partial terminal remain raw but cannot inflate complete-frame performance',t=>{
 const {dir}=fixture(t,[{tick:74,deltaMs:16},{tick:80,deltaMs:9000,suspended:1},{tick:100,deltaMs:500,partial:1}]);
 const m=summarizeDevice(dir,{commit});assert.equal(m.totalFrameCount,3);assert.equal(m.suspendedFrameCount,1);assert.equal(m.partialFrameCount,1);assert.equal(m.sampleCount,0);assert.equal(m.lateComplete,false);assert.equal(m.frameP95Ms,null);
});

for (const runtime of [
  { backend: 'Mono', os: 'Mac OS X 15.7' },
  { backend: 'IL2CPP', os: 'Mac OS X 15.7' },
  { backend: 'Mono', os: 'Android OS 16 / API-36' },
  { backend: 'IL2CPP', os: 'iOS 18.0' },
]) {
  test(`rejects unsupported Phase1A device runtime ${runtime.backend}/${runtime.os}`, t => {
    const { dir } = fixture(t, [{ tick: 74, deltaMs: 16 }, { tick: 100, deltaMs: 16 }]);
    const file = path.join(dir, 'device.json');
    const facts = JSON.parse(fs.readFileSync(file, 'utf8'));
    fs.writeFileSync(file, JSON.stringify({ ...facts, ...runtime }));
    assert.throws(() => summarizeDevice(dir, { commit }), /Android IL2CPP/);
  });
}

test('accepts versioned Android IL2CPP identity without changing full-window statistics', t => {
  const { dir } = fixture(t, [{ tick: 74, deltaMs: 16 }, { tick: 80, deltaMs: 18 }, { tick: 100, deltaMs: 20 }]);
  const file = path.join(dir, 'device.json');
  const facts = JSON.parse(fs.readFileSync(file, 'utf8'));
  const android = 'Android OS 16 / API-36 (BP4A.251205.006/F966NKSSCBZH3)';
  fs.writeFileSync(file, JSON.stringify({ ...facts, os: android }));
  const result = summarizeDevice(dir, { commit });
  assert.equal(result.runtime.os, android);
  assert.equal(result.runtime.backend, 'IL2CPP');
  assert.equal(result.lateComplete, true);
  assert.equal(result.sampleCount, 2);
  assert.equal(result.frameP95Ms, 20);
  assert.equal(result.config.sourceHash, hash);
});
