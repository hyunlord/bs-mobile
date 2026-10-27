import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const spec = JSON.parse(fs.readFileSync(new URL('./estate-art.json', import.meta.url)));
test('approved construction and carrying assets preserve their authored ground anchors', () => {
  const get = id => { const asset = spec.assets.find(a => a.id === id); assert.ok(asset, id); return asset; };
  assert.deepEqual(get('construction-building-3-lit').pivot, [.5, .225]);
  assert.equal(get('construction-building-3-lit').file, 'building-stage3-lit-r2-1x.png');
  assert.deepEqual(get('worker-carry-se-00').pivot, [122 / 256, 25 / 256]);
  assert.deepEqual(get('worker-carry-sw-15').pivot, [134 / 256, 25 / 256]);
  assert.equal(get('worker-carry-sw-15').displayWidth, 66);
  for (const direction of ['se', 'sw']) for (let i = 0; i < 16; i++)
    assert.equal(get(`worker-carry-${direction}-${String(i).padStart(2, '0')}`).maxSize, 256);
});
test('only approved dark bank and tree revisions enter the runtime atlas', () => {
  const relevant = spec.assets.filter(a => a.id.startsWith('bank-') || a.id.startsWith('estate-tree-'));
  assert.equal(relevant.length, 28);
  assert.ok(relevant.filter(a => a.id.endsWith('-dark')).every(a => a.file.includes('-dark-r2-')));
});
