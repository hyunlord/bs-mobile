// Exact approved crop replacement for #475; preserve every historical alias in the same texture.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
import { encodeRgbaPng } from '../../estate-art.mjs';
import { replaceCells } from '../sprout-emblems/replace.mjs';
import { crop, sha, unaffectedContract, outsideHash } from './contracts.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export const crops = [
  ['iron-edge-candidate-r1.png', 'attack.core.iron_blade.sector90'],
  ['harvest-edge-candidate-r1.png', 'attack.core.harvest_scythe.sector180'],
  ['ember-candidate-r4-final.png', 'attack.core.ember_wand.projectile'],
  ['ward-candidate-r4-final.png', 'attack.core.ward_orbit.orbit'],
  ['canal-edge-candidate-r1.png', 'attack.core.canal_bow.piercing'],
  ['spore-combined-effective-alpha-r4-final.png', 'attack.core.spore_fan.field'],
];
const approved=new Set(crops.map(([,id])=>id));
export function install(source, baseline) {
  const manifests=['first-playable-manifest.json','wave1a-manifest.json'].map(name=>({name,value:JSON.parse(fs.readFileSync(path.join(baseline,name)))}));
  const original=fs.readFileSync(path.join(baseline,'fx.png')),before=decodeRgbaPng(original);
  const cells=crops.map(([png,id])=>{
    const role=manifests[0].value.roles.find(r=>r.id===id);
    assert.equal(role.atlas,'fx');assert.equal(role.frames.length,1);
    const frame=role.frames[0],bytes=fs.readFileSync(path.join(source,png)),image=decodeRgbaPng(bytes);
    return {png,role:id,sha256:sha(bytes),rgbaSha256:sha(image.pixels),rect:[frame.x,frame.y,frame.width,frame.height],image};
  });
  const blocked=new Uint8Array(before.width*before.height);
  const mark=(x,y,w,h)=>{for(let row=Math.max(0,y-2);row<Math.min(before.height,y+h+2);row++)blocked.fill(1,row*before.width+Math.max(0,x-2),row*before.width+Math.min(before.width,x+w+2));};
  // Reserve every role rectangle and every opaque pixel, including atlas art not currently bound to a role.
  for(const {value} of manifests)for(const r of value.roles)if(r.atlas==='fx')for(const f of r.frames)mark(f.x,f.y,f.width,f.height);
  for(let y=0;y<before.height;y++)for(let x=0;x<before.width;x++)if(before.pixels[(y*before.width+x)*4+3])mark(x,y,1,1);
  const copies=[],contracts=manifests.map(({name,value})=>({name,before:unaffectedContract(value,before,approved)}));
  for(const cell of cells){
    const [x,y,w,h]=cell.rect,aliases=manifests.flatMap(({name,value})=>value.roles.filter(r=>r.atlas==='fx'&&!approved.has(r.id)&&r.frames.some(f=>f.x===x&&f.y===y&&f.width===w&&f.height===h)).map(r=>({name,role:r})));
    if(!aliases.length)continue;
    let at=null;
    for(let py=2;py+h+2<before.height&&!at;py++)for(let px=2;px+w+2<before.width;px++){
      let free=true;for(let row=py-2;row<py+h+2&&free;row++)for(let col=px-2;col<px+w+2;col++)if(blocked[row*before.width+col]){free=false;break;}
      if(free){at=[px,py,w,h];break;}
    }
    assert(at,`no vacant same-texture slot for ${cell.role}`);mark(...at);
    copies.push({rect:at,image:crop(before,{x,y,width:w,height:h}),sourceRect:cell.rect,aliases:aliases.map(a=>({manifest:a.name,role:a.role.id}))});
    for(const {role} of aliases)for(const f of role.frames)if(f.x===x&&f.y===y&&f.width===w&&f.height===h){f.x=at[0];f.y=at[1];}
  }
  const after=replaceCells(before,[...cells,...copies]),bytes=encodeRgbaPng(after);
  assert.deepEqual(decodeRgbaPng(bytes).pixels,after.pixels);
  for(let i=0;i<manifests.length;i++)assert.deepEqual(unaffectedContract(manifests[i].value,after,approved),contracts[i].before,'unapproved role rendering changed');
  const rects=[...cells,...copies].map(c=>c.rect);
  assert.equal(outsideHash(before,rects),outsideHash(after,rects));
  fs.writeFileSync(path.join(root,'unity/Assets/Art/fx.png'),bytes);
  for(const {name,value} of manifests)fs.writeFileSync(path.join(root,'unity/Assets/Art',name),JSON.stringify(value,null,2)+'\n');
  const report={release:'m3-combat-fx-audit-456-20261028',sourceAtlasSha256:sha(original),outputAtlasSha256:sha(bytes),dimensions:[before.width,before.height],outsideChanged:after.outsideChanged,outsideRgbaSha256:outsideHash(before,rects),changedPixels:after.changedPixels,
    assets:cells.map(({image,...record})=>record),preservedAliases:copies.map(({image,...record})=>({...record,rgbaSha256:sha(image.pixels)})),unapprovedContracts:contracts};
  fs.writeFileSync(path.join(root,'tools/art-authoring/combat-fx/installed.json'),JSON.stringify(report,null,2)+'\n');return report;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
  assert.equal(process.argv.length,4,'usage: install.mjs RELEASE_DIRECTORY BASELINE_DIRECTORY');
  console.log(JSON.stringify(install(path.resolve(process.argv[2]),path.resolve(process.argv[3])),null,2));
}
