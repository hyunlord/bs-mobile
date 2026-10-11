import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {decodeRgbaPng} from '../../wave1a-art.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..');
const out=path.resolve(root,process.argv[2]||'artifacts/grounded-motion/enemy-new');
const frames=JSON.parse(fs.readFileSync(path.join(out,'baked-frames.json')));
const manifest=JSON.parse(fs.readFileSync(path.join(out,'manifest-fragment.json')));
const atlas=decodeRgbaPng(fs.readFileSync(path.join(out,'fallow-enemy-rigs.png')));
assert.equal(frames.rigs.length,6);assert.equal(manifest.roles.length,18);assert.equal(atlas.width,2048);assert.equal(atlas.height,2304);
let outlines=0;
for(const rig of frames.rigs){
assert.equal(rig.frames.length,11);assert.equal(new Set(rig.frames.slice(0,8).map(f=>f.sha256)).size,8,rig.slug+' needs 8 distinct walking keys');
assert.notEqual(rig.frames[8].sha256,rig.frames[9].sha256,rig.slug+' distinct tells');
for(const frame of rig.frames){
assert.deepEqual(frame.root,rig.frames[0].root,'no whole-body bob/rotation');
assert.ok(frame.bounds.x>=4&&frame.bounds.y>=4&&frame.bounds.x+frame.bounds.width<=252&&frame.bounds.y+frame.bounds.height<=252,rig.slug+' transparent safe margins');
assert.ok(frame.bounds.pixels>4000,rig.slug+' plausible occupied silhouette');
const x0=frame.rect.x-4,y0=frame.rect.y-4;
for(let y=0;y<256;y++)for(let x=0;x<256;x++){const i=((y0+y)*atlas.width+x0+x)*4;if(x<4||y<4||x>=252||y>=252)assert.equal(atlas.pixels[i+3],0,rig.slug+' actual transparent perimeter');if(atlas.pixels[i+3]>20&&atlas.pixels[i]<30&&atlas.pixels[i+1]<30&&atlas.pixels[i+2]<33)outlines++;}
}
const roles=manifest.roles.filter(r=>r.id.startsWith('enemy.rig.'+rig.slug+'.'));
assert.equal(roles.length,3);assert.equal(roles.find(r=>r.id.endsWith('.walk')).frames.length,8);assert.equal(roles.find(r=>r.id.endsWith('.tell')).frames.length,2);assert.equal(roles.find(r=>r.id.endsWith('.idle')).frames.length,1);
assert.ok(roles.every(r=>r.atlas==='fallow-enemy-rigs'&&r.tween==='none'));
assert.equal(new Set(roles.map(r=>JSON.stringify([r.pivot,r.worldSize]))).size,1,'stable pivot/scale between states');
}
assert.ok(outlines>2000,'baked ink outline present');
const charger=frames.rigs.find(r=>r.slug==='ram_runner');let brightRed=0,opaque=0;
for(const f of charger.frames)for(let y=0;y<f.rect.height;y++)for(let x=0;x<f.rect.width;x++){const i=((f.rect.y+y)*atlas.width+f.rect.x+x)*4;if(atlas.pixels[i+3]<128)continue;opaque++;if(atlas.pixels[i]>150&&atlas.pixels[i]>atlas.pixels[i+1]*1.7&&atlas.pixels[i]>atlas.pixels[i+2]*1.7)brightRed++;}
assert.ok(brightRed/opaque<.002,'normal charger is dark, not red');
console.log(JSON.stringify({rigs:6,walkKeys:48,tellKeys:12,idleKeys:6,rootFixed:true,padding:4,uniqueWalkKeys:true,inkOutlinePixels:outlines,chargerBrightRedFraction:brightRed/opaque,runtime:'one baked SpriteBatch quad; no live skeleton',status:'pass'},null,2));
