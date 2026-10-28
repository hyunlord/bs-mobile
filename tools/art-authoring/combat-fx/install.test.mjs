import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import os from 'node:os';
import path from 'node:path';
import { crops, writeChecked } from './install.mjs';
import { unaffectedContract } from './contracts.mjs';
const record = JSON.parse(fs.readFileSync(new URL('./installed.json', import.meta.url)));
const current = decodeRgbaPng(fs.readFileSync(new URL('../../../unity/Assets/Art/fx.png', import.meta.url)));
test('approved six source crops preserve atlas dimensions and all other pixels', () => {
  assert.equal(record.assets.length,crops.length);
  const outside=Buffer.from(current.pixels);
  for(const {rect:[x,y,w,h]} of [...record.assets,...record.preservedAliases])for(let row=y;row<y+h;row++)outside.fill(0,(row*current.width+x)*4,(row*current.width+x+w)*4);
  assert.equal(createHash('sha256').update(outside).digest('hex'),record.outsideRgbaSha256);
  assert.equal(record.outsideChanged,0);
  for(const {rect:[x,y,w,h],rgbaSha256} of [...record.assets,...record.preservedAliases]){
    const crop=Buffer.alloc(w*h*4);
    for(let row=0;row<h;row++)current.pixels.copy(crop,row*w*4,((y+row)*current.width+x)*4,((y+row)*current.width+x+w)*4);
    assert.equal(createHash('sha256').update(crop).digest('hex'),rgbaSha256);
  }
});
test('spore combined PNG keeps approved effective alpha while other role contracts stay unchanged', () => {
  const manifest=JSON.parse(fs.readFileSync(new URL('../../../unity/Assets/Art/first-playable-manifest.json',import.meta.url)));
  for(const [,id] of crops){const r=manifest.roles.find(x=>x.id===id);assert.deepEqual(r.pivot,{x:.5,y:.5});assert.equal(r.frames.length,1);assert.equal(r.frameMs,120);}
  const {rect:[x,y,w,h]}=record.assets.find(a=>a.role==='attack.core.spore_fan.field');let max=0;
  for(let row=y;row<y+h;row++)for(let col=x;col<x+w;col++)max=Math.max(max,current.pixels[(row*current.width+col)*4+3]);
  assert(max>=85&&max<=90,`baked effective alpha: ${max}`);
});

test('all unapproved FX aliases keep decoded frames and geometry in both profiles',()=>{
  for(const {name,before} of record.unapprovedContracts){
    const manifest=JSON.parse(fs.readFileSync(new URL('../../../unity/Assets/Art/'+name,import.meta.url)));
    assert.deepEqual(unaffectedContract(manifest,current,new Set(crops.map(([,id])=>id))),before);
  }
  assert.equal(record.preservedAliases.length,4);
  assert.deepEqual([current.width,current.height],record.dimensions);
});

for(const changedIndex of [0,1,2])test(`unrelated edit in target ${changedIndex} aborts all writes`,()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'combat-fx-preflight-'));
  try{
    const outputs=['fx.png','first-playable-manifest.json','wave1a-manifest.json'].map((name,i)=>{
      const target=path.join(dir,name),before=Buffer.from('baseline-'+name),after=Buffer.from('candidate-'+name);
      fs.writeFileSync(target,i===changedIndex?Buffer.from('foreign-'+name):before);
      return {target,before,after};
    });
    const snapshots=outputs.map(o=>fs.readFileSync(o.target));
    assert.throws(()=>writeChecked(outputs),/unrelated target changes/);
    outputs.forEach((o,i)=>assert.deepEqual(fs.readFileSync(o.target),snapshots[i]));
  }finally{fs.rmSync(dir,{recursive:true});}
});
