// Recreate local SVG reports from the committed small evidence tables only.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCsv } from './csv.mjs';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(process.argv[2] ?? path.join(repo, 'artifacts/stutter-part1-report'));
fs.mkdirSync(output, { recursive: true });
const prefix = path.join(output, 'stutter-part1-final');
const canonical = parseCsv(fs.readFileSync(path.join(repo, 'docs/metrics/stutter-part1-abba.csv'), 'utf8'));
const sourceOutliers = parseCsv(fs.readFileSync(path.join(repo, 'docs/metrics/stutter-part1-outliers.csv'), 'utf8'));
const inputs = { crowd411: true, c05capped: true };
const runs = canonical.filter(row => row.density === 'all').map(row => ({ fixture: row.fixture, variant: row.variant, slot: Number(row.slot), p5: Number(row.p5), p50: Number(row.p50), p95: Number(row.p95), max: Number(row.max) }));
if (runs.length !== 16 || new Set(runs.map(row => row.fixture + '/' + row.slot)).size !== 16) throw new Error('Expected 16 unique per-run summaries');
for (const row of runs) {
  if (!Object.hasOwn(inputs, row.fixture) || !['A', 'B'].includes(row.variant) || ![row.slot, row.p5, row.p50, row.p95, row.max].every(Number.isFinite)) throw new Error('Invalid canonical run');
}
const outliers = sourceOutliers.map(row => ({ fixture: row.fixture, variant: row.variant, dominantScope: row.dominantScope }));
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;');
const colors={A:'#536779',B:'#cb672a'};
function svg(name,title,body,height=600){fs.writeFileSync(`${prefix}-${name}.svg`,`<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="${height}" viewBox="0 0 1280 ${height}"><rect width="1280" height="${height}" fill="#f8f7f3"/><style>text{font-family:Arial,sans-serif;fill:#17232e;font-size:15px}.title{font-size:25px;font-weight:bold}.small{font-size:12px}</style><text class="title" x="36" y="38">${esc(title)}</text>${body}</svg>`);}
let body='';
for(const [fi,fixture] of Object.keys(inputs).entries()){
 const offset=fi*620,x=offset+75,top=90,w=520,h=300,lo=15.5,hi=18;
 body+=`<text x="${x}" y="70">${fixture} — p5 / p50 / p95</text>`;
 const y=v=>top+h-(v-lo)/(hi-lo)*h;
 for(const t of [15.5,16,16.7,17.4,18])body+=`<line x1="${x}" x2="${x+w}" y1="${y(t)}" y2="${y(t)}" stroke="${t===17.4?'#ba2f30':'#d4d8da'}"/><text x="${x-44}" y="${y(t)+5}">${t}</text>`;
 for(const r of runs.filter(r=>r.fixture===fixture)){
  const xx=x+30+(r.slot-1)*65;
  body+=`<line x1="${xx}" x2="${xx}" y1="${y(r.p5)}" y2="${y(r.p95)}" stroke="${colors[r.variant]}" stroke-width="4"/><circle cx="${xx}" cy="${y(r.p50)}" r="5" fill="${colors[r.variant]}"/><text x="${xx-14}" y="420">${r.slot}${r.variant}</text><text class="small" x="${xx-22}" y="440">${r.p95.toFixed(3)}</text>`;
 }
}
body+='<text x="36" y="492">A=65983e2 (tracked MetalDisplayLink 0); B=3bbdb32 (tracked 1). Environment override unset.</text><text x="36" y="520">Red line: p95 upper cadence gate 17.4ms. Each run remains separate; these are not confidence intervals.</text><text x="36" y="548">Both baselines already pass the >25ms ratio. B does not meet the cadence band; do not call this an improvement.</text>';
svg('cadence','ABBAABBA: native Release cadence',body);
body='';
for(const [fi,fixture]of Object.keys(inputs).entries()){
 const x=75+fi*620,top=100,h=300;
 body+=`<text x="${x}" y="76">${fixture} — maximum wall interval (ms)</text>`;
 const subset=runs.filter(r=>r.fixture===fixture),max=Math.max(...subset.map(r=>r.max));
 for(const r of subset){const xx=x+20+(r.slot-1)*65,hh=r.max/max*h;body+=`<rect x="${xx}" y="${top+h-hh}" width="34" height="${hh}" fill="${colors[r.variant]}"/><text class="small" x="${xx-4}" y="${top+h-hh-8}">${r.max.toFixed(1)}</text><text x="${xx}" y="427">${r.slot}${r.variant}</text>`;}
}
body+='<text x="36" y="484">Per-panel scale follows its maximum. Counts and ratios do not excuse a visible long stall.</text><text x="36" y="512">Release clocks show scope duration, not active CPU work; root causes of remaining stalls are unresolved.</text>';
svg('max','Largest interval in each valid run — raw, no exemptions',body);
const groups=['catch-up (multiple Advances)','unresolved post-Update','view scope','Core scope'];body='';
for(const [fi,fixture]of Object.keys(inputs).entries()){
 const xx=fi*620+40;body+=`<text x="${xx}" y="80">${fixture}: every >25ms interval</text>`;
 for(const [gi,g]of groups.entries()){
 const y=125+gi*84;body+=`<text x="${xx}" y="${y}">${g}</text>`;
 for(const [vi,v]of ['A','B'].entries()){const count=outliers.filter(r=>r.fixture===fixture&&r.variant===v&&r.dominantScope===g).length;body+=`<rect x="${xx+250}" y="${y-15+vi*24}" width="${count*18}" height="18" fill="${colors[v]}"/><text x="${xx+260+count*18}" y="${y+vi*24}">${v}: ${count}</text>`;}
 }
}
body+='<text x="36" y="490">Priority: multiple Advances first; otherwise largest of Core / combined View / post-Update span.</text><text x="36" y="518">This is recorded-scope classification, NOT causes. No OS, GPU, first-appearance or GC cause is inferred.</text>';
svg('scopes','Long-frame accounting — unresolved attribution remains explicit',body);

// Final restored-setting verification is a separate attempt, never part of ABBA.
const restored = parseCsv(fs.readFileSync(path.join(repo, 'docs/metrics/stutter-part1-restored-summary.csv'), 'utf8')).filter(row => row.density === 'all');
if (restored.length !== 2 || new Set(restored.map(row => row.fixture)).size !== 2) throw new Error('Expected two restored validation fixtures');
let restoredBody = '';
const chartTop = 105, chartHeight = 270, chartLeft = 130, chartRight = 1110;
const yPercent = value => chartTop + chartHeight - value / .8 * chartHeight;
for (const value of [0, .2, .4, .6, .8]) {
  restoredBody += `<line x1="${chartLeft}" x2="${chartRight}" y1="${yPercent(value)}" y2="${yPercent(value)}" stroke="${value === .2 ? '#ba2f30' : '#d4d8da'}"/><text x="60" y="${yPercent(value)+5}">${value.toFixed(1)}%</text>`;
}
for (const [index, row] of restored.entries()) {
  const percent = Number(row.over25Percent), count = Number(row.over25), frames = Number(row.frames), maximum = Number(row.max);
  if (![percent, count, frames, maximum].every(Number.isFinite)) throw new Error('Invalid restored summary');
  const x = 290 + index * 490, height = percent / .8 * chartHeight;
  restoredBody += `<rect x="${x}" y="${yPercent(percent)}" width="160" height="${height}" fill="${percent > .2 ? '#ba2f30' : '#536779'}"/><text x="${x}" y="${yPercent(percent)-12}">${count}/${frames}: ${percent.toFixed(4)}%</text><text x="${x}" y="405">${row.fixture === 'c05' ? 'C05 capped' : '411 replay'}</text><text x="${x}" y="433">max ${maximum.toFixed(3)}ms</text>`;
}
restoredBody += '<text x="36" y="484">Restored Metal0, commit 6fe4d29. Red line: >25ms frames must be at most 0.2%.</text><text x="36" y="512">411 fails the ratio. C05 passes the ratio but retains 312ms / 149ms visible stalls; cause unresolved.</text><text x="36" y="540">Separate final validation, not ABBA repetitions; no outlier exemptions or OS attribution.</text>';
svg('restored', 'Final restored settings: long-frame failure remains visible', restoredBody);

console.log('Wrote four SVG reports from canonical CSVs to ' + output);
