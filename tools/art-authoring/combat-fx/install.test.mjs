import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { crops } from './install.mjs';
const record = JSON.parse(fs.readFileSync(new URL('./installed.json', import.meta.url)));
const current = decodeRgbaPng(fs.readFileSync(new URL('../../../unity/Assets/Art/fx.png', import.meta.url)));
test('approved six source crops preserve atlas dimensions and all other pixels', () => {
  assert.equal(record.assets.length,crops.length);
  const outside=Buffer.from(current.pixels);
  for(const {rect:[x,y,w,h]} of record.assets)for(let row=y;row<y+h;row++)outside.fill(0,(row*current.width+x)*4,(row*current.width+x+w)*4);
  assert.equal(createHash('sha256').update(outside).digest('hex'),record.outsideRgbaSha256);
  assert.equal(record.outsideChanged,0);
});
test('spore combined PNG keeps approved effective alpha while other role contracts stay unchanged', () => {
  const manifest=JSON.parse(fs.readFileSync(new URL('../../../unity/Assets/Art/first-playable-manifest.json',import.meta.url)));
  for(const [,id] of crops){const r=manifest.roles.find(x=>x.id===id);assert.deepEqual(r.pivot,{x:.5,y:.5});assert.equal(r.frames.length,1);assert.equal(r.frameMs,120);}
  const {rect:[x,y,w,h]}=record.assets.find(a=>a.role==='attack.core.spore_fan.field');let max=0;
  for(let row=y;row<y+h;row++)for(let col=x;col<x+w;col++)max=Math.max(max,current.pixels[(row*current.width+col)*4+3]);
  assert(max>=85&&max<=90,`baked effective alpha: ${max}`);
});
