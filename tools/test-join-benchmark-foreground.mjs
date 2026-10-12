import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { joinForeground, writeForegroundJoin } from './join-benchmark-foreground.mjs';
const observations = [
  {event:'initial',utcUnixMilliseconds:100,name:'Sow and Siege',pid:1},
  {event:'activated',utcUnixMilliseconds:200,name:'Godot',pid:2},
  {event:'activated',utcUnixMilliseconds:300,name:'Sow and Siege',pid:1},
  {event:'observer-end',utcUnixMilliseconds:400,name:'Sow and Siege',pid:1}
];
const benchmark = {focusEvents:[99,100,250,300,401].map(time => ({utcUnixMilliseconds:time,frame:time,timestamp:time*1000,kind:'focus',focus:time===300,paused:false}))};
test('joins latest known activation only within complete observer coverage', () => {
  assert.deepEqual(joinForeground(benchmark,observations).map(row=>row.appName),['unknown','Sow and Siege','Godot','Sow and Siege','unknown']);
});
test('missing end, initial, timestamp or app leaves unknown instead of guessing', () => {
  for (const rows of [observations.slice(0,-1),observations.slice(1),[]])
    assert(joinForeground(benchmark,rows).every(row=>row.status==='unknown'));
  assert.equal(joinForeground({focusEvents:[{}]},observations)[0].status,'unknown');
  assert.equal(joinForeground({focusEvents:[{utcUnixMilliseconds:100}]},[{...observations[0],name:'unavailable'},observations.at(-1)])[0].status,'unknown');
});
test('clock rollback fails closed', () => assert.throws(()=>joinForeground(benchmark,[observations[1],observations[0]]),/unordered/));
test('writes only derived outputs and preserves both raw sources byte for byte', t => {
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'foreground-join-')); t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));
  const source=path.join(dir,'benchmark.json'), observer=path.join(dir,'observer.jsonl'), out=path.join(dir,'derived');
  const a=JSON.stringify(benchmark),b=observations.map(row=>JSON.stringify(row)).join('\n');
  fs.writeFileSync(source,a);fs.writeFileSync(observer,b);
  assert.equal(writeForegroundJoin(source,observer,out).rows[2].appName,'Godot');
  assert.equal(fs.readFileSync(source,'utf8'),a);assert.equal(fs.readFileSync(observer,'utf8'),b);
  assert.throws(()=>writeForegroundJoin(source,observer,out),/already exists/);
});
