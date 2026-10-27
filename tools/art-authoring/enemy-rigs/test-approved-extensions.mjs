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
assert.equal(stone.frames.length, 16); assert.equal(stone.frameMs, 45); assert.equal(stone.tween, 'none');
assert.deepEqual(stone.pivot, { x: .5, y: .12109375 });
assert.equal(stone.worldSize.x, .559006211093322);
for (const [i, f] of stone.frames.entries()) assert.deepEqual(f, { x: (i % 8) * 260 + 2, y: Math.floor(i / 8) * 260 + 2, width: 256, height: 256 });
for (const kind of ['raider', 'ram_runner', 'shield_raider'])
  assert.equal(role(first, `enemy.core.${kind}.walk`).frames.length, 1, 'first-playable identity remains separate');
assert.equal(role(wave, 'enemy.core.stone_breaker.walk').frames.length, 1, 'unselected wave role unchanged');
for (const manifest of [wave, first]) for (const a of manifest.atlases) {
  assert.ok(a.width <= 4096 && a.height <= 4096, a.id);
}
console.log('PASS: raider 16x26ms / stone 16x45ms, approved crop/pivot/size, profile isolation, max4096');
