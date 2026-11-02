import test from 'node:test';
import assert from 'node:assert/strict';
import { extrudeGutter } from './river-gutter.mjs';
test('bilinear river gutter preserves every crop byte and changes only one padding texel', () => {
  const atlas={width:8,height:8,pixels:Buffer.alloc(8*8*4)};
  for(let y=2;y<5;y++)for(let x=2;x<5;x++)atlas.pixels.set([x*20,y*20,81,255],(y*8+x)*4);
  const before=Buffer.from(atlas.pixels);
  extrudeGutter(atlas,[2,2,3,3]);
  for(let y=0;y<8;y++)for(let x=0;x<8;x++) {
    const actual=atlas.pixels.subarray((y*8+x)*4,(y*8+x)*4+4);
    if((x>=2&&x<5&&y>=2&&y<5)||x<1||x>5||y<1||y>5)
      assert.deepEqual(actual,before.subarray((y*8+x)*4,(y*8+x)*4+4));
    else { const sx=Math.max(2,Math.min(4,x)),sy=Math.max(2,Math.min(4,y));
      assert.deepEqual(actual,before.subarray((sy*8+sx)*4,(sy*8+sx)*4+4)); }
  }
});
test('gutter refuses writes outside reserved atlas space', () => {
  assert.throws(()=>extrudeGutter({width:4,height:4,pixels:Buffer.alloc(64)},[0,0,2,2]));
});
