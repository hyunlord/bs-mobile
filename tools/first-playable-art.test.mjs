import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { requiredBindings, validateManifest } from './first-playable-art.mjs';
const root = fileURLToPath(new URL('../', import.meta.url));
function fixture() {
  return { schemaVersion: 1,
    licenses: [{ id: 'generated', origin: 'built-in imagegen', source: 'output reference', prompt: 'paint a knight', commercialUse: 'OpenAI output terms' }],
    atlases: [{ id: 'actors', path: 'Assets/Art/actors.png', width: 64, height: 64, padding: 2, license: 'generated' }],
    roles: [{ id: 'hero', atlas: 'actors', frames: [{ x: 2, y: 2, width: 28, height: 28 }], pivot: { x: 0.5, y: 0 }, worldSize: { x: 0.48, y: 0.6 }, frameMs: 350, tween: 'walk' }],
    bindings: [{ kind: 'hero', contentId: 'core:frontier_knight', state: 'walk', roleId: 'hero' }] };
}
test('presentation contract accepts one authored frame and deliberate tween', () => assert.equal(validateManifest(fixture()).roles, 1));
for (const [name, mutate, error] of [
  ['gameplay metadata', m => { m.roles[0].damage = 9; }, /unknown or gameplay field damage/],
  ['duplicate roles', m => m.roles.push(m.roles[0]), /duplicate\/empty role/],
  ['duplicate binding', m => m.bindings.push(m.bindings[0]), /duplicate binding hero\|core:frontier_knight\|walk/],
  ['missing bound role', m => { m.bindings[0].roleId = 'absent'; }, /missing role absent/],
  ['out of bounds frame', m => { m.roles[0].frames[0].width = 64; }, /bounds\/padding/],
  ['missing padding', m => { m.roles[0].frames[0].x = 0; }, /bounds\/padding/],
  ['unresolved license', m => { m.atlases[0].license = 'unknown'; }, /missing license/],
  ['invented CC0', m => { m.licenses[0].commercialUse = 'CC0'; }, /must not claim/],
  ['unknown tween', m => { m.roles[0].tween = 'simulate'; }, /animation/],
]) test(`fails closed: ${name}`, () => { const m = fixture(); mutate(m); assert.throws(() => validateManifest(m), error); });
test('required inventory derives all selected identities and explicit transitional people states', () => {
  const required = requiredBindings(root);
  assert.equal(required.filter(b => b.kind === 'icon').length, 82);
  assert.equal(required.filter(b => b.kind === 'enemy').length, 52);
  assert.equal(required.filter(b => b.kind === 'crop').length, 12);
  assert.equal(required.filter(b => b.kind === 'building').length, 16);
  assert.equal(required.filter(b => b.kind === 'person').length, 25);
  assert.ok(required.some(b => b.kind === 'attack' && b.contentId === '' && b.state === 'projectile'));
  assert.ok(required.some(b => b.kind === 'person' && b.contentId === 'returning' && b.state === 'muster'));
  assert.ok(!required.some(b => b.contentId.startsWith('test:')));
  assert.throws(() => validateManifest(fixture(), required), /missing required binding icon\|core:iron_blade\|default/);
});
test('full selected inventory can bind intentionally shared presentation and rejects one omitted state', () => {
  const required = requiredBindings(root); const m = fixture();
  m.bindings = required.map(b => ({ ...b, roleId: 'hero' }));
  assert.equal(validateManifest(m, required).required, required.length);
  m.bindings = m.bindings.filter(b => !(b.kind === 'crop' && b.contentId === 'core:seed_bag' && b.state === 'stage3'));
  assert.throws(() => validateManifest(m, required), /missing required binding crop\|core:seed_bag\|stage3/);
});
