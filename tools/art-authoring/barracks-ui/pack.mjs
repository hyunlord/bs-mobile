import fs from 'node:fs';
import crypto from 'node:crypto';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const input=process.argv[2]??'artifacts/barracks-r2';
// Wallet icons: Releases wallet-iron-20261031-r1 and hero-basic-gem-20261030-r1, 44px (2x of the 22px wallet cell).
const walletInput=process.argv[3]??'artifacts/wallet-icons';
const manifest=JSON.parse(fs.readFileSync(`${input}/TRAINING-MANIFEST-r2.json`));
const main=JSON.parse(fs.readFileSync(`${input}/MANIFEST-r2.json`));
const selected=manifest.assets.filter(a=>a.scale===2);
for(const id of ['training-progress-track','training-progress-fill'])selected.push(main.assets.find(a=>a.id===id&&a.scale===2));
for(const kind of ['levy','guard'])selected.push({id:`troop-${kind}`,file:`troop-${kind}-portrait-r1-1x.png`,scale:1,nineSliceInsetsLBRT:[0,0,0,0]});
for(const [id,file,release] of [['wallet-iron','wallet-iron-r1-44px.png','wallet-iron-20261031-r1'],['wallet-gem','wallet-gem-r1-44px.png','hero-basic-gem-20261030-r1']])selected.push({id,file,scale:2,release,dir:walletInput,nineSliceInsetsLBRT:[0,0,0,0]});
const images=selected.map(a=>{const bytes=fs.readFileSync(`${a.dir??input}/${a.file}`);const hash=crypto.createHash('sha256').update(bytes).digest('hex');if(a.sha256&&a.sha256!==hash)throw Error(a.file);a.sha256=hash;return decodeRgbaPng(bytes);});
const result=pack(images,1024,4);const out='unity/Assets/Resources/BarracksUI';
fs.writeFileSync(`${out}/training.png`,encodeRgbaPng(result.atlas));
fs.writeFileSync(`${out}/training.json`,JSON.stringify({release:'slg-screen-assets-581-20261030',width:result.atlas.width,height:result.atlas.height,assets:selected.map((a,i)=>({id:a.id,file:a.file,...(a.release?{release:a.release}:{}),sha256:a.sha256,rect:result.rects[i],border:a.nineSliceInsetsLBRT??[0,0,0,0],scale:a.scale}))},null,2)+'\n');
console.log(JSON.stringify({sprites:selected.length,width:result.atlas.width,height:result.atlas.height,rgbaBytes:result.atlas.pixels.length}));
