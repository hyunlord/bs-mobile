import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const spec = JSON.parse(fs.readFileSync(new URL('./estate-art.json', import.meta.url)));
test('approved work loop frames keep native dimensions and foot anchors', () => {
  for (const [kind, width, height, x, y] of [['hoe',96,96,30,83],['hammer',64,76,22,66],['sack',66,66,31.453125,59.5546875]]) {
    for (let i=0;i<8;i++) {
      const a = spec.assets.find(a => a.id === `worker-${kind}-${String(i).padStart(2,'0')}`);
      assert.ok(a, `${kind} ${i}`);
      assert.equal(a.release,'m3-estate-work-loops-444-20261027');
      assert.equal(a.displayWidth,width);
      assert.equal(a.maxSize,Math.max(width,height));
      assert.deepEqual(a.pivot,[x/width,1-y/height]);
      if(kind==='hoe') assert.match(a.file,/r2/);
    }
  }
});
