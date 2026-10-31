import assert from 'node:assert/strict';
import fs from 'node:fs';
const read = name => JSON.parse(fs.readFileSync(new URL(`../../../unity/Assets/Art/${name}`, import.meta.url)));
const wave = read('wave1a-manifest.json'), first = read('first-playable-manifest.json');
const role = (manifest, id) => manifest.roles.find(r => r.id === id);
const raider = role(wave, 'enemy.rig.raider.walk');
assert.equal(raider.frames.length, 16); assert.equal(raider.frameMs, 26);
assert.deepEqual(raider.pivot, { x: .5383064516129032, y: .12096774193548387 });
assert.equal(raider.worldSize.x, .6453105882352942);
const stone = role(first, 'enemy.core.stone_breaker.walk');
assert.equal(stone.frames.length, 1); assert.equal(stone.frameMs, 350);
assert.equal(stone.atlas, 'actors');
assert.deepEqual(stone.pivot, { x: .5, y: .08 });
assert.deepEqual(stone.worldSize, { x: .430815, y: .42 });
assert.deepEqual(stone.frames, [{ x: 669, y: 369, width: 239, height: 233 }]);
assert.ok(!first.atlases.some(a => a.id === 'stone-breaker-approved'));
assert.equal(wave.atlases.find(a => a.id === 'fallow-enemy-rigs').height, 3840);
for (const kind of ['raider', 'ram_runner', 'shield_raider'])
  assert.equal(role(first, `enemy.core.${kind}.walk`).frames.length, 1, 'first-playable identity remains separate');
assert.equal(role(wave, 'enemy.core.stone_breaker.walk').frames.length, 1, 'unselected wave role unchanged');
for (const manifest of [wave, first]) for (const a of manifest.atlases) {
  assert.ok(a.width <= 4096 && a.height <= 4096, a.id);
}
console.log('PASS: wave raider 16x26ms / historical first-playable stone unchanged, approved crop/pivot/size, profile isolation, max4096');
