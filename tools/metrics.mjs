#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import assert from 'node:assert/strict';

const escape = (value) => String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const canonical = (value) => JSON.stringify(value);
function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}
function validate(input) {
  requireValue(input && typeof input === 'object' && !Array.isArray(input), 'metrics must be an object');
  const finiteTree = (value) => { if (typeof value === 'number') requireValue(Number.isFinite(value), 'all numeric fields must be finite'); else if (value && typeof value === 'object') Object.values(value).forEach(finiteTree); };
  finiteTree(input);
  requireValue(input.schemaVersion === 1 || input.schemaVersion === 2, 'schemaVersion must be 1 or 2');
  requireValue(typeof input.timestamp === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(input.timestamp) && Number.isFinite(Date.parse(input.timestamp)), 'timestamp must be an ISO date');
  requireValue(typeof input.commit === 'string' && /^[a-f0-9]{40}$/i.test(input.commit), 'commit must be a full SHA');
  for (const field of ['stage', 'model']) requireValue(typeof input[field] === 'string' && input[field].trim().length > 0, `${field} must be nonempty`);
  for (const field of ['config', 'runtime']) requireValue(input[field] !== null && typeof input[field] === 'object' && !Array.isArray(input[field]) && Object.keys(input[field]).length > 0, `${field} must be a nonempty object`);
  requireValue(Number.isSafeInteger(input.seed) && input.seed >= 0, 'seed must be a nonnegative safe integer');
  if (input.schemaVersion === 2) validateDevice(input);
  else requireValue(Number.isFinite(input.tickP95Ms) && input.tickP95Ms >= 0, 'tickP95Ms must be finite and nonnegative');
  requireValue(input.balanceDispersion === null || (Number.isFinite(input.balanceDispersion) && input.balanceDispersion >= 0), 'balanceDispersion must be null or finite and nonnegative');
  requireValue(typeof input.measurementScope === 'string' && input.measurementScope.trim().length > 0, 'measurementScope must be explicit');
  if (input.scaffoldDamageCv !== undefined) requireValue(Number.isFinite(input.scaffoldDamageCv) && input.scaffoldDamageCv >= 0, 'scaffoldDamageCv must be finite and nonnegative');
  if (input.stage === 'S0') requireValue(input.balanceDispersion === null, 'S0 scaffold must not claim balance dispersion');
  return input;
}
function validateDevice(input) {
  requireValue(input.tickP95Ms === null && input.balanceDispersion === null, 'device frames must not masquerade as Core tick or balance measurements');
  requireValue(input.device && typeof input.device === 'object' && !Array.isArray(input.device), 'device identity is required');
  for (const field of ['model', 'os', 'screen', 'posture']) requireValue(typeof input.device[field] === 'string' && input.device[field].trim().length > 0, `device.${field} must be nonempty`);
  requireValue(Number.isSafeInteger(input.sampleCount) && input.sampleCount >= 0, 'sampleCount must be nonnegative');
  requireValue(typeof input.lateComplete === 'boolean', 'lateComplete must be boolean');
  if (input.sampleCount === 0) requireValue(input.frameP95Ms === null && input.frameMaxMs === null && !input.lateComplete, 'empty late windows must remain unmeasured');
  else requireValue(Number.isFinite(input.frameP95Ms) && input.frameP95Ms >= 0 && Number.isFinite(input.frameMaxMs) && input.frameMaxMs >= input.frameP95Ms, 'frame timings must be finite, nonnegative and ordered');
}
function chart(rows, field, unit, title) {
  const measured = rows.filter((row) => row.data[field] !== null && row.data[field] !== undefined);
  if (!measured.length) return '<p>Not measured for this series.</p>';
  const max = Math.max(...measured.map((row) => row.data[field]), 0.001);
  const points = measured.map((row) => {
    const index = rows.indexOf(row);
    return { x: 48 + index * 624 / Math.max(1, rows.length - 1), y: 172 - row.data[field] / max * 136, row };
  });
  return `<svg viewBox="0 0 720 220" role="img" aria-label="${escape(title)}; chronological measurements. Exact values follow in the table."><title>${escape(title)}</title><path class="axis" d="M48 20V172H692"/><text x="0" y="30">${max.toPrecision(3)}</text><text x="0" y="176">0</text><text x="0" y="204">${escape(unit)}</text>${points.length > 1 ? `<polyline class="trend" points="${points.map((p) => `${p.x},${p.y}`).join(' ')}"/>` : ''}${points.map((p) => `<circle cx="${p.x}" cy="${p.y}" r="4"><title>${escape(p.row.data.timestamp)} ${escape(p.row.data.commit)}: ${p.row.data[field]} ${escape(unit)}</title></circle>`).join('')}<text x="48" y="204">${escape(rows[0].data.commit.slice(0, 7))}</text><text x="692" y="204" text-anchor="end">${escape(rows.at(-1).data.commit.slice(0, 7))}</text></svg>`;
}
function renderDevice(group, index) {
  const d = group[0].data;
  const value = number => number === null ? 'Not measured' : number.toPrecision(6);
  return `<section aria-labelledby="series-${index}"><h2 id="series-${index}">${escape(d.stage)} / ${escape(d.model)}</h2><p>${escape(d.measurementScope)}</p><p class="meta">Device: <code>${escape(canonical(d.device))}</code><br>Runtime: <code>${escape(canonical(d.runtime))}</code><br>Configuration: <code>${escape(canonical(d.config))}</code></p><h3>Frame p95 · milliseconds</h3>${chart(group, 'frameP95Ms', 'ms', 'Frame p95')}<p>Frame intervals include rendering and scheduling. They are not Core tick timings. Missing or incomplete late windows do not prove the full-run performance gate.</p><div class="table" tabindex="0" role="region" aria-label="Series ${index + 1} device measurements"><table><caption>Exact device measurements and source records</caption><thead><tr><th scope="col">Recorded (UTC)</th><th scope="col">Commit / raw JSON</th><th scope="col">Seed</th><th scope="col">Frame p95 (ms)</th><th scope="col">Maximum frame (ms)</th><th scope="col">Samples / coverage</th></tr></thead><tbody>${group.map(({ data, file }) => `<tr><td>${escape(data.timestamp)}</td><td><a href="history/${escape(file)}"><code>${escape(data.commit)}</code></a></td><td>${data.seed}</td><td>${value(data.frameP95Ms)}</td><td>${value(data.frameMaxMs)}</td><td>${data.sampleCount} / ${data.lateComplete ? 'Complete late window' : 'Incomplete late window'}</td></tr>`).join('')}</tbody></table></div></section>`;
}
function render(rows) {
  const groups = new Map();
  for (const row of rows) {
    const d = row.data;
    const key = canonical([d.schemaVersion, d.stage, d.model, d.runtime, d.config, d.measurementScope, d.device ?? null]);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="description" content="Measured simulation trends with commit and runtime provenance. S0 scaffold results are not gameplay balance evidence."><title>bs-mobile measurement history</title><style>
:root{--paper:#fbfbfa;--ink:#242424;--muted:#555550;--rule:#d5d5ce;--accent:#175c45;--focus:#964b00;--s2:8px;--s4:16px;--s6:24px;--s8:32px}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:16px/1.6 system-ui,sans-serif}main{max-width:1120px;margin:auto;padding:var(--s8) var(--s4)}h1{font-size:28px;line-height:1.2}h2{font-size:20px}h3{font-size:16px}p{overflow-wrap:anywhere}section{margin-block:var(--s8);padding-top:var(--s4);border-top:1px solid var(--rule)}.meta{font-size:14px;color:var(--muted)}code{font-family:ui-monospace,monospace;overflow-wrap:anywhere}a{color:var(--accent);text-underline-offset:4px}a:hover,a:active{text-decoration-thickness:3px}a:focus-visible,.table:focus-visible{outline:3px solid var(--focus);outline-offset:4px}.table{overflow:auto}table{width:100%;border-collapse:collapse;font-size:14px}th,td{padding:var(--s2);text-align:left;border-bottom:1px solid var(--rule);vertical-align:top}th{white-space:nowrap}caption{text-align:left;font-weight:700;padding-block:var(--s4)}svg{display:block;width:100%;max-width:720px;height:auto;font:14px system-ui,sans-serif;fill:var(--muted)}svg .axis{stroke:var(--rule);fill:none}svg .trend{stroke:var(--accent);stroke-width:2;fill:none}svg circle{fill:var(--accent)}@media(max-width:600px){svg{font-size:28px}}
</style></head><body><main><header><p class="meta">bs-mobile · Engineering evidence</p><h1>Measurement history</h1><p>${rows.length} recorded run${rows.length === 1 ? '' : 's'}. Each point retains its commit, seed, configuration and runtime. Different measurement scopes are separate series.</p><p><strong>S0 is scaffolding.</strong> Tick timing measures the infrastructure harness only. Balance dispersion remains unmeasured until a gameplay model exists. Core tick values do not establish mobile frame performance or game balance. Device frame series are labeled separately.</p></header>${rows.length ? [...groups.values()].map((group, index) => {
    const d = group[0].data;
    if (d.schemaVersion === 2) return renderDevice(group, index);
    return `<section aria-labelledby="series-${index}"><h2 id="series-${index}">${escape(d.stage)} / ${escape(d.model)}</h2><p>${escape(d.measurementScope)}</p><p class="meta">Runtime: <code>${escape(canonical(d.runtime))}</code><br>Configuration: <code>${escape(canonical(d.config))}</code></p><h3>Tick p95 · milliseconds</h3>${chart(group, 'tickP95Ms', 'ms', 'Tick p95')}<h3>Balance outcome dispersion</h3>${chart(group, 'balanceDispersion', 'σ', 'Balance outcome dispersion')}${group.some((r) => r.data.scaffoldDamageCv !== undefined) ? `<h3>Scaffold policy damage CV (not game balance)</h3>${chart(group, 'scaffoldDamageCv', 'CV', 'Scaffold policy damage CV')}<p class="meta">Synthetic policy damage dispersion. This is harness evidence only; exact values and league provenance are in the raw JSON.</p>` : ''}<div class="table" tabindex="0" role="region" aria-label="Series ${index + 1} measurements"><table><caption>Exact measurements and source records</caption><thead><tr><th scope="col">Recorded (UTC)</th><th scope="col">Commit / raw JSON</th><th scope="col">Seed</th><th scope="col">Tick p95 (ms)</th><th scope="col">Balance dispersion</th><th scope="col">Scaffold damage CV</th></tr></thead><tbody>${group.map(({ data, file }) => `<tr><td>${escape(data.timestamp)}</td><td><a href="history/${escape(file)}"><code>${escape(data.commit)}</code></a></td><td>${data.seed}</td><td>${data.tickP95Ms.toPrecision(6)}</td><td>${data.balanceDispersion === null ? 'Not measured' : data.balanceDispersion.toPrecision(6)}</td><td>${data.scaffoldDamageCv === undefined ? 'Not measured' : data.scaffoldDamageCv.toPrecision(6)}</td></tr>`).join('')}</tbody></table></div></section>`;
  }).join('') : '<p>No measurements recorded.</p>'}<footer><p class="meta">Static report. No external assets or network requests. Raw JSON records are adjacent in history/; keep that folder with this HTML to open source links.</p></footer></main></body></html>`;
}
async function main() {
  const [inputPath, outputDir] = process.argv.slice(2);
  if (inputPath === '--selftest') {
    const sample = { schemaVersion: 1, timestamp: '2026-01-01T00:00:00Z', commit: 'a'.repeat(40), stage: 'S0', model: 'scaffold', config: { iterations: 3 }, runtime: { version: 'test' }, seed: 42, tickP95Ms: 0.01, balanceDispersion: null, measurementScope: 'infrastructure' };
    assert.equal(validate(sample), sample);
    for (const bad of [{ tickP95Ms: Infinity }, { balanceDispersion: 1 }, { commit: '../escape' }, { runtime: {} }, { seed: -1 }]) assert.throws(() => validate({ ...sample, ...bad }));
    assert.ok(render([{ data: { ...sample, model: '<script>x</script>' }, file: 'a.json' }]).includes('&lt;script&gt;'));
    assert.ok(render([]).includes('No measurements recorded.'));
    console.log('metrics selftest PASS');
    return;
  }
  requireValue(inputPath && outputDir, 'usage: node tools/metrics.mjs INPUT.json OUTPUT_DIR');
  const raw = JSON.parse(await readFile(inputPath, 'utf8'));
  if (raw.schemaVersion === 2 && process.env.METRICS_SHA !== undefined) {
    requireValue(process.env.METRICS_SHA === raw.commit, 'METRICS_SHA must match the recorded device build');
  }
  const data = validate({ ...raw, commit: process.env.METRICS_SHA ?? raw.commit, measurementScope: raw.measurementScope ?? raw.scope, runtime: typeof raw.runtime === 'string' ? { framework: raw.runtime, os: raw.os, architecture: raw.architecture, stopwatchFrequency: raw.stopwatchFrequency } : raw.runtime, config: { ...raw.config, ...(raw.policy === undefined ? {} : { policy: raw.policy }), ...(raw.iterations === undefined ? {} : { iterations: raw.iterations }), ...(raw.ticks === undefined ? {} : { ticks: raw.ticks }) } });
  const historyDir = join(outputDir, 'history');
  await mkdir(historyDir, { recursive: true });
  const rows = [];
  for (const file of (await readdir(historyDir)).filter((name) => name.endsWith('.json'))) {
    const record = JSON.parse(await readFile(join(historyDir, file), 'utf8'));
    rows.push({ file, data: validate(record.data) });
  }
  const record = { data, source: raw };
  const digest = createHash('sha256').update(canonical(record)).digest('hex').slice(0, 16);
  const file = `${data.commit}-${digest}.json`;
  if (!rows.some((row) => row.file === file)) rows.push({ file, data });
  rows.sort((a, b) => Date.parse(a.data.timestamp) - Date.parse(b.data.timestamp) || a.file.localeCompare(b.file));
  const html = render(rows);
  await writeFile(join(historyDir, file), `${JSON.stringify(record, null, 2)}\n`);
  await writeFile(join(outputDir, 'index.html'), html);
  console.log(`metrics: ${rows.length} records; ${join(outputDir, 'index.html')}`);
}
main().catch((error) => { console.error(`metrics: ${error.message}`); process.exitCode = 1; });
