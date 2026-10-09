import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export function summarizeEconomy(csv, meta) {
  const [header, ...lines] = csv.trim().split(/\r?\n/).map(line => line.split(','));
  const rows = lines.map(values => { assert.equal(values.length, header.length, 'CSV field count'); return Object.fromEntries(header.map((k,i) => [k,values[i]])); });
  assert.ok(rows.length > 0, 'empty observation');
  const output = [];
  for (const type of [...new Set(rows.map(r => r.archetype))]) {
    const selected = rows.filter(r => r.archetype === type), runs = selected.filter(r => r.event === 'run');
    const balances = Object.fromEntries(meta.materials.map(m => [m.id,0]));
    const flows = Object.fromEntries(meta.materials.map(m => [m.id,{ credited:0, spent:0, overflow:0, peak:0, final:0 }]));
    for (const r of selected) {
      assert.match(r.state_hash,/^[A-F0-9]{64}$/); assert.match(r.data_hash,/^[A-F0-9]{64}$/);
      if (r.event === 'run') assert.match(r.run_hash,/^[A-F0-9]{64}$/);
      for (const m of meta.materials) {
        const values = Object.fromEntries(['wallet','credited','spent','overflow'].map(k => [k,Number(r[m.id+'_'+k])]));
        assert.ok(Object.values(values).every(v => Number.isSafeInteger(v) && v >= 0), 'finite nonnegative flow');
        assert.equal(balances[m.id]+values.credited-values.spent,values.wallet,'material conservation');
        assert.ok(values.wallet <= m.walletCap,'wallet cap'); balances[m.id]=values.wallet;
        for (const k of ['credited','spent','overflow']) flows[m.id][k]+=values[k];
        flows[m.id].peak=Math.max(flows[m.id].peak,values.wallet); flows[m.id].final=values.wallet;
      }
    }
    const dayReached = chapter => { const r=runs.find(r => Number(r.chapter)>=chapter); return r ? Number(r.day) : null; };
    const dayCleared = chapter => { const r=runs.find(r => Number(r.highest_cleared)>=chapter); return r ? Number(r.day) : null; };
    const blockers = new Map();
    for (const r of runs) for (const reason of r.bottleneck.split(';').filter(Boolean)) blockers.set(reason,(blockers.get(reason)||0)+1);
    const final=selected.at(-1);
    output.push({ archetype:type, days:Math.max(...selected.map(r=>Number(r.day))), runs:runs.length, clears:runs.filter(r=>r.cleared==='1').length,
      highestCleared:Number(final.highest_cleared), reachDays:Object.fromEntries([3,6,10].map(c=>[c,dayReached(c)])), clearDays:Object.fromEntries([3,6,10].map(c=>[c,dayCleared(c)])),
      final:{ vassalLevel:Number(final.vassal_level),rank:Number(final.vassal_rank),forge:Number(final.forge),research:Number(final.research),granary:Number(final.granary),barracks:Number(final.barracks),challenges:Number(final.challenges),unlocked:Number(final.unlocked_content)},
      blockers:[...blockers].sort((a,b)=>b[1]-a[1]),flows });
  }
  const normal=output.find(x=>x.archetype==='normal');
  const targetGates=normal ? { chapter3:normal.reachDays[3]>=1&&normal.reachDays[3]<=2,chapter6:normal.reachDays[6]>=5&&normal.reachDays[6]<=9,chapter10:normal.reachDays[10]>=14 } : null;
  const observationComplete = ['light','normal','heavy'].every((type,i) => { const x=output.find(x=>x.archetype===type); return x && x.days>=21 && x.runs===x.days*[2,6,12][i]; });
  return { observationComplete, definition:'reach = first actual attempt, clear reported separately; chapter6 around a week = days5..9 hypothesis', boundedWallets:true,targetGates,archetypes:output };
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  const file=process.argv[2];if(!file)throw new Error('usage: node tools/meta-economy-report.mjs <csv>');
  const root=path.resolve(path.dirname(new URL(import.meta.url).pathname),'..');
  console.log(JSON.stringify(summarizeEconomy(fs.readFileSync(file,'utf8'),JSON.parse(fs.readFileSync(path.join(root,'data/meta/progression.json'),'utf8'))),null,2));
}
