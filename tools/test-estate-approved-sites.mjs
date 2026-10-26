import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import { decodeRgbaPng } from './wave1a-art.mjs';

const directory = new URL('../unity/Assets/Resources/EstateArt/', import.meta.url);
const manifest = JSON.parse(fs.readFileSync(new URL('estate-art.json', directory), 'utf8'));
const atlas = decodeRgbaPng(fs.readFileSync(new URL('estate-terrain.png', directory)));
const release = 'm3-lv0-sites-review-20261026';
const names = ['stone-foundation', 'broken-fence', 'charred-frame', 'weedy-field', 'stone-corner', 'survey-stakes'];

test('six approved Lv0 pairs retain the authored floor anchor and identical alpha', () => {
  assert.equal(manifest.sprites.filter(s => s.id.startsWith('lv0-')).length, 12);
  for (const [index, name] of names.entries()) {
    const pair = ['lit', 'dark'].map(light => {
      const role = `lv0-${name}-${light}`;
      const sprite = manifest.sprites.find(s => s.id === role);
      assert.ok(sprite, role);
      assert.equal(sprite.atlas, 'terrain');
      assert.equal(sprite.source.release, release);
      assert.equal(sprite.source.file, `0${index + 1}-${name}-${light}-2x.png`);
      assert.match(sprite.source.sha256, /^[a-f0-9]{64}$/);
      assert.deepEqual(sprite.source.size, [272, 192]);
      assert.deepEqual(sprite.rect.slice(2), [272, 192], 'no resampling of authored 2x art');
      assert.deepEqual(sprite.pivot, [.5, .375], 'floor centre (136,120), bottom-left normalized');
      assert.equal(sprite.displayWidth, 136, 'logical 128x64 floor plus authored canvas margins');
      const [x, y, width, height] = sprite.rect;
      return Buffer.from(Array.from({ length: width * height }, (_, i) =>
        atlas.pixels[((y + Math.floor(i / width)) * atlas.width + x + i % width) * 4 + 3]));
    });
    assert.deepEqual(pair[0], pair[1], name + ': lighting cannot change the silhouette');
    assert.ok(pair[0].some(a => a === 0) && pair[0].some(a => a > 128), 'transparent margin and solid foundation');
  }
});

test('three approved road patches share the original terrain atlas', () => {
  const roads = manifest.sprites.filter(s => s.id.startsWith('road-'));
  assert.deepEqual(roads.map(s => s.id).sort(), ['road-bend', 'road-fork', 'road-straight']);
  for (const road of roads) {
    assert.equal(road.source.release, release);
    assert.deepEqual(road.source.size, [512, 512]);
    assert.deepEqual(road.rect.slice(2), [512, 512]);
    assert.deepEqual(road.pivot, [.5, .5]);
    assert.equal(road.atlas, 'terrain', 'roads and sites share the existing terrain texture');
  }
  const added = manifest.sprites.filter(s => s.source.release === release);
  assert.equal(added.length, 22, 'only the approved sites, roads and r4 ground enter through this Release');
});


test('approved r4 grass and six soil canvases preserve source size and middle anchors', () => {
  const sizes = [[512, 512], [96, 67], [128, 48], [160, 110], [192, 133], [224, 149], [256, 173]];
  const roles = ['ground-grass-r4', ...Array.from({ length: 6 }, (_, i) => `soil-patch-r4-${String(i + 1).padStart(2, '0')}`)];
  roles.forEach((role, i) => {
    const sprite = manifest.sprites.find(s => s.id === role);
    assert.equal(sprite.source.release, release);
    assert.deepEqual(sprite.source.size, sizes[i]);
    assert.deepEqual(sprite.rect.slice(2), sizes[i], 'approved raster is never resampled');
    assert.deepEqual(sprite.pivot, [.5, .5]);
    assert.equal(sprite.atlas, 'terrain');
    if (i > 0) {
      const [x, y, w, h] = sprite.rect;
      const alphas = Array.from({ length: w * h }, (_, p) => atlas.pixels[((y + Math.floor(p / w)) * atlas.width + x + p % w) * 4 + 3]);
      assert.ok(alphas.some(a => a === 0) && alphas.some(a => a > 0 && a < 255), 'soil fades into the continuous grass');
    }
  });
});
