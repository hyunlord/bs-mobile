import { readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';
import { parseCsv } from './csv.mjs';

const panels=[['Mean level',['meanLevel']],['Cumulative damage: weapon / tool',['meanWeaponDamage','meanToolDamage']],['Interval DPS: weapon / tool',['meanWeaponDps','meanToolDps']],['Observed / alive cohort',['nObserved','nAlive']]];
const palette=['#1f77b4','#ff7f0e','#2ca02c','#d62728','#9467bd','#8c564b'];
const fields=['seconds',...panels.flatMap(p=>p[1])];
const optional=new Set(['meanWeaponDps','meanToolDps']);
const escape=value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
export function makeScene(input) {
  if(!input.length)throw new Error('Empty plot input');
  const seen=new Set();
  const rows=input.map(row=>{
    if(!['A','B','C'].includes(row.peopleRule)||!['building','land','mixed','people','random','weapon'].includes(row.policy))throw new Error('Invalid plot identity');
    const result={...row};
    for(const field of fields){const value=row[field];if(value===''&&optional.has(field)){result[field]=null;continue;}if(typeof value!=='string'||!value.trim()||!Number.isFinite(Number(value))||Number(value)<0)throw new Error(`Invalid plot ${field}`);result[field]=Number(value);}
    if(!Number.isInteger(result.nObserved)||!Number.isInteger(result.nAlive)||result.nAlive>result.nObserved)throw new Error('Invalid cohort');
    const key=`${row.policy}/${row.peopleRule}/${result.seconds}`;if(seen.has(key))throw new Error('Duplicate plot point');seen.add(key);return result;
  });
  const rules=[...new Set(rows.map(r=>r.peopleRule))].sort();const policies=[...new Set(rows.map(r=>r.policy))].sort();
  const scene={width:rules.length*600,height:1480,lines:[],texts:[],series:[]};
  const line=(points,color='#444444',style='solid')=>scene.lines.push({points,color,style});
  const text=(x,y,value,size=14,color='#222222')=>scene.texts.push({x,y,value,size,color});
  text(30,28,'S4 OBSERVED COHORTS | NO FORWARD-FILL | ALLY DAMAGE EXCLUDED',18);
  policies.forEach((policy,i)=>{const x=30+(i%3)*190,y=58+Math.floor(i/3)*24;line([[x,y],[x+25,y]],palette[i]);text(x+32,y+5,policy);});
  text(30,115,'SOLID: WEAPON / OBSERVED    DASH: TOOL ACTIVATION + GROWTH    DOT: ALIVE',12);
  for(let col=0;col<rules.length;col++)for(let panel=0;panel<panels.length;panel++){
    const rule=rules[col],subset=rows.filter(r=>r.peopleRule===rule),[title,metrics]=panels[panel];
    const left=col*600+75,top=160+panel*310,w=500,h=225;
    const xmax=Math.max(1,...subset.map(r=>r.seconds)),ymax=Math.max(1,...subset.flatMap(r=>metrics.map(f=>r[f]??0)))*1.05;
    text(left-25,top-20,`RULE ${rule} | ${title}`,14);
    for(let i=0;i<=5;i++){const x=left+w*i/5,y=top+h-h*i/5;line([[x,top],[x,top+h]],'#dddddd');line([[left,y],[left+w,y]],'#dddddd');text(x-12,top+h+20,String(Number((xmax*i/5).toPrecision(3))),12);text(left-65,y+4,String(Number((ymax*i/5).toPrecision(3))),12);}
    line([[left,top],[left,top+h],[left+w,top+h]]);text(left+90,top+h+45,'OBSERVED SIMULATION SECONDS',12);
    policies.forEach((policy,index)=>{const selected=subset.filter(r=>r.policy===policy).sort((a,b)=>a.seconds-b.seconds);
      metrics.forEach((field,metricIndex)=>{
        const style=metricIndex?(panel===3?'dot':'dash'):'solid';const segments=[];let segment=[];
        for(const row of selected){if(row[field]===null){if(segment.length)segments.push(segment);segment=[];}else segment.push([row.seconds,row[field]]);}if(segment.length)segments.push(segment);
        const values=segments.flat();scene.series.push({policy,rule,field,values});
        for(const points of segments)line(points.map(([x,y])=>[left+x/xmax*w,top+h-y/ymax*h]),palette[index],style);
      });
    });
  }
  text(30,1440,'LATER MEANS DESCRIBE OBSERVED CASES, NOT ALL ORIGINAL CASES.',14);
  text(30,1465,'SOURCE: damage-curves.csv | LINE SEGMENTS CONNECT OBSERVATIONS; NO DATA INTERPOLATION.',12);
  return scene;
}
export function encodeSvg(scene){return `<svg xmlns="http://www.w3.org/2000/svg" width="${scene.width}" height="${scene.height}" viewBox="0 0 ${scene.width} ${scene.height}"><title>S4 observed-only cohort curves</title><rect width="100%" height="100%" fill="white"/>${scene.lines.map(l=>`<polyline points="${l.points.map(p=>p.map(n=>n.toFixed(3)).join(',')).join(' ')}" fill="none" stroke="${l.color}" stroke-width="2"${l.style==='solid'?'':` stroke-dasharray="${l.style==='dash'?'8 5':'2 4'}"`}/>`).join('')}${scene.texts.map(t=>`<text x="${t.x}" y="${t.y}" font-family="monospace" font-size="${t.size}" fill="${t.color}">${escape(t.value)}</text>`).join('')}</svg>\n`;}

// Original 5x7 bitmap alphabet keeps PNG labels portable without native font or graphics dependencies.
const alphabet={
 A:'01110 10001 10001 11111 10001 10001 10001',B:'11110 10001 10001 11110 10001 10001 11110',C:'01111 10000 10000 10000 10000 10000 01111',D:'11110 10001 10001 10001 10001 10001 11110',E:'11111 10000 10000 11110 10000 10000 11111',F:'11111 10000 10000 11110 10000 10000 10000',G:'01111 10000 10000 10111 10001 10001 01111',H:'10001 10001 10001 11111 10001 10001 10001',I:'11111 00100 00100 00100 00100 00100 11111',J:'00111 00010 00010 00010 10010 10010 01100',K:'10001 10010 10100 11000 10100 10010 10001',L:'10000 10000 10000 10000 10000 10000 11111',M:'10001 11011 10101 10101 10001 10001 10001',N:'10001 11001 10101 10011 10001 10001 10001',O:'01110 10001 10001 10001 10001 10001 01110',P:'11110 10001 10001 11110 10000 10000 10000',Q:'01110 10001 10001 10001 10101 10010 01101',R:'11110 10001 10001 11110 10100 10010 10001',S:'01111 10000 10000 01110 00001 00001 11110',T:'11111 00100 00100 00100 00100 00100 00100',U:'10001 10001 10001 10001 10001 10001 01110',V:'10001 10001 10001 10001 10001 01010 00100',W:'10001 10001 10001 10101 10101 11011 10001',X:'10001 10001 01010 00100 01010 10001 10001',Y:'10001 10001 01010 00100 00100 00100 00100',Z:'11111 00001 00010 00100 01000 10000 11111',
 '0':'01110 10001 10011 10101 11001 10001 01110','1':'00100 01100 00100 00100 00100 00100 01110','2':'01110 10001 00001 00010 00100 01000 11111','3':'11110 00001 00001 01110 00001 00001 11110','4':'00010 00110 01010 10010 11111 00010 00010','5':'11111 10000 10000 11110 00001 00001 11110','6':'01110 10000 10000 11110 10001 10001 01110','7':'11111 00001 00010 00100 01000 01000 01000','8':'01110 10001 10001 01110 10001 10001 01110','9':'01110 10001 10001 01111 00001 00001 01110',
 '-':'00000 00000 00000 11111 00000 00000 00000','.':'00000 00000 00000 00000 00000 00110 00110',':':'00000 00110 00110 00000 00110 00110 00000','/':'00001 00010 00010 00100 01000 01000 10000','|':'00100 00100 00100 00100 00100 00100 00100','+':'00000 00100 00100 11111 00100 00100 00000',',':'00000 00000 00000 00000 00110 00100 01000',';':'00000 00110 00110 00000 00110 00100 01000',' ':'00000 00000 00000 00000 00000 00000 00000'};
export function crc32(bytes){let crc=0xffffffff;for(const byte of bytes){crc^=byte;for(let bit=0;bit<8;bit++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return (crc^0xffffffff)>>>0;}
export function encodePng(scene){
 const {width,height}=scene,pixels=Buffer.alloc(width*height*3,255);
 const color=hex=>[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16));
 const pixel=(x,y,rgb)=>{x=Math.round(x);y=Math.round(y);if(x>=0&&x<width&&y>=0&&y<height)for(let c=0;c<3;c++)pixels[(y*width+x)*3+c]=rgb[c];};
 for(const line of scene.lines){const rgb=color(line.color);let distance=0;for(let i=1;i<line.points.length;i++){const [x,y]=line.points[i-1],[endX,endY]=line.points[i],length=Math.hypot(endX-x,endY-y),steps=Math.max(1,Math.ceil(length));for(let j=0;j<=steps;j++){const phase=distance+length*j/steps;if(line.style==='dash'&&phase%13>=8||line.style==='dot'&&phase%6>=2)continue;pixel(x+(endX-x)*j/steps,y+(endY-y)*j/steps,rgb);pixel(x+(endX-x)*j/steps,y+(endY-y)*j/steps+1,rgb);}distance+=length;}if(line.points.length===1)pixel(...line.points[0],rgb);}
 for(const text of scene.texts){const scale=Math.max(1,Math.floor(text.size/7)),rgb=color(text.color);let x=text.x;for(const letter of text.value.toUpperCase()){const glyph=alphabet[letter];if(!glyph)throw new Error(`Unsupported PNG label ${letter}`);glyph.split(' ').forEach((row,y)=>[...row].forEach((value,col)=>{if(value==='1')for(let dy=0;dy<scale;dy++)for(let dx=0;dx<scale;dx++)pixel(x+col*scale+dx,text.y-7*scale+y*scale+dy,rgb);}));x+=6*scale;}}
 const scan=Buffer.alloc((width*3+1)*height);for(let y=0;y<height;y++)pixels.copy(scan,y*(width*3+1)+1,y*width*3,(y+1)*width*3);
 const chunk=(name,data)=>{const type=Buffer.from(name),header=Buffer.alloc(4),tail=Buffer.alloc(4);header.writeUInt32BE(data.length);tail.writeUInt32BE(crc32(Buffer.concat([type,data])));return Buffer.concat([header,type,data,tail]);};
 const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(width);ihdr.writeUInt32BE(height,4);ihdr[8]=8;ihdr[9]=2;
 return Buffer.concat([Buffer.from('89504e470d0a1a0a','hex'),chunk('IHDR',ihdr),chunk('IDAT',deflateSync(scan,{level:9})),chunk('IEND',Buffer.alloc(0))]);
}
export async function render(directory){const scene=makeScene(parseCsv(await readFile(join(directory,'damage-curves.csv'),'utf8')));await writeFile(join(directory,'S4-curves.svg'),encodeSvg(scene));await writeFile(join(directory,'S4-curves.png'),encodePng(scene));}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){if(process.argv.length!==3)throw new Error('usage: node tools/s4-plot.mjs OUTPUT_DIRECTORY');await render(resolve(process.argv[2]));}
