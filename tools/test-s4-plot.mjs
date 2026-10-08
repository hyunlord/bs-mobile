import test from 'node:test';
import assert from 'node:assert/strict';
import { inflateSync } from 'node:zlib';
import { makeScene, encodePng, crc32 } from './s4-plot.mjs';
const rows=[0,10].map(seconds=>({policy:'mixed',peopleRule:'A',seconds:String(seconds),nObserved:'2',nAlive:seconds?'1':'2',meanLevel:seconds?'3':'1',meanWeaponDamage:seconds?'8':'0',meanToolDamage:seconds?'12':'0',meanWeaponDps:seconds?'0.8':'',meanToolDps:seconds?'1.2':''}));
test('series retain observed CSV values and omit unavailable initial DPS',()=>{
 const scene=makeScene(rows); const series=scene.series;
 assert.deepEqual(series.find(s=>s.field==='meanToolDamage').values,[[0,0],[10,12]]);
 assert.deepEqual(series.find(s=>s.field==='meanWeaponDps').values,[[10,0.8]]);
 assert.deepEqual(series.find(s=>s.field==='nAlive').values,[[0,2],[10,1]]);
 assert.equal(series.some(s=>s.field==='meanAllyDamage'),false);
});
test('plot rejects absent, nonfinite and contradictory cohort inputs',()=>{
 for(const change of [{meanLevel:''},{meanLevel:'Infinity'},{nAlive:'3'},{seconds:'-1'}])assert.throws(()=>makeScene([{...rows[0],...change}]));
 assert.throws(()=>makeScene([]));
});
test('PNG chunks have valid CRC and contain the full RGB raster deterministically',()=>{
 const scene=makeScene(rows);const png=encodePng(scene);assert.deepEqual(png,encodePng(scene));
 assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');let offset=8;const compressed=[];
 while(offset<png.length){const size=png.readUInt32BE(offset);const type=png.toString('ascii',offset+4,offset+8);const body=png.subarray(offset+4,offset+8+size);assert.equal(crc32(body),png.readUInt32BE(offset+8+size));if(type==='IDAT')compressed.push(body.subarray(4));offset+=size+12;}
 const raw=inflateSync(Buffer.concat(compressed));assert.equal(raw.length,(scene.width*3+1)*scene.height);assert.ok(raw.some(n=>n!==255&&n!==0));
});
