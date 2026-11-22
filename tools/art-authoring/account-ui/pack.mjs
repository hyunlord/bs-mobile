import fs from 'node:fs';
import crypto from 'node:crypto';
import { pack, encodeRgbaPng } from '../../estate-art.mjs';
import { decodeRgbaPng } from '../../wave1a-art.mjs';
const root = process.argv[2] ?? 'artifacts/account-ui';
const output = 'unity/Assets/Resources/AccountUI';
const read = name => JSON.parse(fs.readFileSync(`${root}/${name}`));
const wanted = new Set(['recruit-once-button-active','recruit-once-button-disabled','recruit-probability-button-active','recruit-probability-button-disabled','recruit-pity-panel','recruit-result-frame-common','recruit-result-frame-rare','recruit-result-frame-epic','recruit-grade-common','recruit-grade-rare','recruit-grade-epic','hero-list-card']);
const recruit = read('recruit/MANIFEST-r1.json').assets.filter(a => (a.file.endsWith('-2x.png') && wanted.has(a.id)) || a.file.endsWith('-324x383-r1.png'));
const research = read('research/MANIFEST-r2.json').assets.filter(a => a.file?.startsWith('research-') && a.file.endsWith('-2x.png'));
for (const [kind, assets, release] of [['recruit',recruit,'hero-recruit-assets-592-20261030'],['research',research,'slg-screen-assets-581-20261030']]) {
 const seen = new Set();
 const entries = assets.filter(a => { if(seen.has(a.id))return false;seen.add(a.id);return true; });
 if(!entries.length)throw Error(`No ${kind} sprites`);
 const images = entries.map(a => {const bytes=fs.readFileSync(`${root}/${kind}/${a.file}`);const hash=crypto.createHash('sha256').update(bytes).digest('hex');if(hash!==a.sha256)throw Error(`Source hash mismatch: ${a.file}`);return decodeRgbaPng(bytes);});
 const packed = pack(images, kind === 'recruit' ? 2048 : 512, 4);
 fs.writeFileSync(`${output}/${kind}.png`, encodeRgbaPng(packed.atlas));
 fs.writeFileSync(`${output}/${kind}.json`, JSON.stringify({release,approval:'Integration requested; source review approval remains as published',width:packed.atlas.width,height:packed.atlas.height,assets:entries.map((a,i)=>({id:a.id,file:a.file,sha256:a.sha256,rect:packed.rects[i],border:a.nineSliceInsetsLBRT??[0,0,0,0],scale:a.scale??(a.file.endsWith('-2x.png')?2:1)}))},null,2)+'\n');
 console.log(`${kind}: ${entries.length} sprites, ${packed.atlas.width}x${packed.atlas.height}`);
}
