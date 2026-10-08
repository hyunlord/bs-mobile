import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const key = b => `${b.kind}|${b.contentId}|${b.state}`;
export const tweens = ['none', 'walk', 'work', 'pulse', 'recoil'];
export function requiredBindings(root) {
  const p = read(path.join(root, 'data/profiles/first-playable.json'));
  const result = [];
  const add = (kind, contentId, states) => { for (const state of states) result.push({ kind, contentId, state }); };
  for (const ids of [...Object.values(p.selection), p.runtime.charters, p.runtime.items, p.runtime.evolutions])
    for (const id of ids) add('icon', id, ['default']);
  for (const id of p.selection.enemies) add('enemy', id, ['idle', 'walk', 'hit', 'death']);
  for (const id of p.selection.heroes) add('hero', id, ['idle', 'walk', 'hit', 'death']);
  for (const role of ['peasant', 'militia', 'guard', 'returning', 'vassal']) add('person', role, ['idle', 'move', 'work', 'muster', 'return']);
  for (const id of p.selection.weapons) add('attack', id, [p.firstPlayable.weapons[id].form]);
  for (const id of p.selection.tools) {
    const tool = read(path.join(root, 'data/tools', id.split(':')[1] + '.json'));
    add('attack', id, [tool.activation.shape]);
    const target = tool.growth.target;
    if (target === 'land') add('crop', id, ['stage0', 'stage1', 'stage2', 'stage3']);
    if (target === 'building') add('building', id, ['site', 'constructing', 'complete', 'damaged', 'ruin']);
    if (target === 'building' || target === 'people') add('attack', id, ['projectile']);
  }
  add('building', '', ['site']); add('attack', '', ['projectile']);
  for (const id of [...p.runtime.items, ...p.runtime.evolutions]) add('attack', id, ['sector180', 'disk']);
  for (const event of p.firstPlayable.mapEvents) {
    add('mapEvent', event.id, ['present', 'claimed', 'damaged', 'broken']);
    add('icon', event.id, ['default']);
  }
  for (const id of p.selection.estates) add('terrain', id, ['spring', 'summer', 'autumn', 'winter']);
  for (const id of ['loot', 'remains']) add('object', id, ['default']);
  for (const id of ['edge', 'corner']) add('boundary', id, ['default']);
  for (const id of ['hit', 'death', 'kill-experience', 'harvest', 'harvest-experience', 'tax', 'building-start', 'building-complete', 'evolution', 'level-up', 'lord-hit', 'event-spawn', 'event-claim', 'cart-broken', 'boss-warning', 'elite-warning', 'threat', 'threat-cluster']) add('feedback', id, ['default']);
  return [...new Map(result.map(b => [key(b), b])).values()];
}
function check(condition, message) { if (!condition) throw new Error(message); }
function fields(value, names, label) {
  check(value && typeof value === 'object' && !Array.isArray(value), `${label}: expected object`);
  for (const name of Object.keys(value)) check(names.includes(name), `${label}: unknown or gameplay field ${name}`);
  for (const name of names) check(Object.hasOwn(value, name), `${label}: missing ${name}`);
}
const text = value => typeof value === 'string' && value.trim().length > 0;
const finite = value => typeof value === 'number' && Number.isFinite(value);
export function validateManifest(m, required = []) {
  fields(m, ['schemaVersion', 'licenses', 'atlases', 'roles', 'bindings'], 'manifest');
  check(m.schemaVersion === 1, 'schemaVersion must be 1');
  for (const k of ['licenses', 'atlases', 'roles', 'bindings']) check(Array.isArray(m[k]) && m[k].length, `${k}: empty/missing array`);
  const licenses = new Set();
  for (const l of m.licenses) {
    fields(l, ['id', 'origin', 'source', 'prompt', 'commercialUse'], 'license');
    check(text(l.id) && !licenses.has(l.id), `duplicate/empty license: ${l.id}`);
    check(text(l.origin) && text(l.source) && text(l.prompt) && text(l.commercialUse), `license ${l.id}: incomplete provenance`);
    check(!/cc0|public.domain/i.test([l.origin,l.commercialUse].join(' ')), `license ${l.id}: generated art must not claim CC0/public domain`);
    licenses.add(l.id);
  }
  const atlases = new Map();
  for (const a of m.atlases) {
    fields(a, ['id', 'path', 'width', 'height', 'padding', 'license'], 'atlas');
    check(text(a.id) && !atlases.has(a.id), `duplicate/empty atlas: ${a.id}`);
    check(/^Assets\/Art\/[\w./-]+\.png$/.test(a.path) && !a.path.includes('..'), `atlas ${a.id}: unsafe path`);
    check(Number.isInteger(a.width) && a.width > 0 && Number.isInteger(a.height) && a.height > 0 && Number.isInteger(a.padding) && a.padding >= 2, `atlas ${a.id}: dimensions/padding`);
    check(licenses.has(a.license), `atlas ${a.id}: missing license ${a.license}`);
    atlases.set(a.id, a);
  }
  const roles = new Set();
  for (const r of m.roles) {
    fields(r, ['id','atlas','frames','pivot','worldSize','frameMs','tween'], 'role');
    check(text(r.id) && !roles.has(r.id), `duplicate/empty role: ${r.id}`); roles.add(r.id);
    const a = atlases.get(r.atlas); check(a, `role ${r.id}: missing atlas ${r.atlas}`);
    check(Array.isArray(r.frames) && r.frames.length, `role ${r.id}: no frames`);
    fields(r.pivot, ['x','y'], r.id + ' pivot'); fields(r.worldSize, ['x','y'], r.id + ' worldSize');
    check([r.pivot.x,r.pivot.y].every(v => finite(v) && v >= 0 && v <= 1), `role ${r.id}: pivot`);
    check([r.worldSize.x,r.worldSize.y].every(v => finite(v) && v > 0), `role ${r.id}: worldSize`);
    check(Number.isInteger(r.frameMs) && r.frameMs > 0 && tweens.includes(r.tween), `role ${r.id}: animation`);
    for (const f of r.frames) {
      fields(f, ['x','y','width','height'], r.id + ' frame');
      check(Object.values(f).every(Number.isInteger) && f.width > 0 && f.height > 0 && f.x >= a.padding && f.y >= a.padding && f.x + f.width + a.padding <= a.width && f.y + f.height + a.padding <= a.height, `role ${r.id}: frame bounds/padding`);
    }
  }
  const bindings = new Set();
  for (const b of m.bindings) {
    fields(b, ['kind','contentId','state','roleId'], 'binding'); const k = key(b);
    check(text(b.kind) && typeof b.contentId === 'string' && text(b.state) && ![b.kind,b.contentId,b.state].some(v => v.includes('|')), `binding ${k}: invalid key`);
    check(!bindings.has(k), `duplicate binding ${k}`); bindings.add(k);
    check(roles.has(b.roleId), `binding ${k}: missing role ${b.roleId}`);
  }
  for (const b of required) check(bindings.has(key(b)), `missing required binding ${key(b)}`);
  return { atlases: atlases.size, roles: roles.size, bindings: bindings.size, required: required.length };
}
export function validateRepository(root) {
  const m = read(path.join(root, 'unity/Assets/Art/first-playable-manifest.json'));
  const result = validateManifest(m, requiredBindings(root));
  for (const a of m.atlases) {
    const png = fs.readFileSync(path.join(root, 'unity', a.path));
    check(png.length >= 33 && png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])), `atlas ${a.id}: invalid PNG`);
    check(png.readUInt32BE(16) === a.width && png.readUInt32BE(20) === a.height, `atlas ${a.id}: PNG dimensions differ`);
    check(png[25] === 6 || png[25] === 4 || (png[25] === 3 && png.includes(Buffer.from('tRNS'))), `atlas ${a.id}: no alpha channel`);
  }
  return result;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
  if (process.argv.includes('--requirements')) console.log(JSON.stringify(requiredBindings(root), null, 2));
  else console.log(JSON.stringify(validateRepository(root)));
}
