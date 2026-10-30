import test from 'node:test';
import assert from 'node:assert/strict';
import { selftest, referenceWaterMask, run } from './measure-estate-render-density.mjs';

test('twelve raster and connected-empty regression fixtures', () => selftest());
test('reference water requires explicit geometry and validates actual camera polygons', () => {
  const input={width:2,height:2,land:[[[1,0],[2,1],[1,2],[0,1]]]};
  const texture={width:1,height:1,pixels:Uint8Array.of(255,255,255,255)};
  const zones=[{x:0,y:0,width:1,height:1}];
  const world={origin:[-1,0],size:[2,2],cell:[2,2]};
  assert.deepEqual([...referenceWaterMask(input,texture,zones,world)],[1,1,1,1]);
  assert.throws(()=>referenceWaterMask(input,texture,zones),/Explicit finite/);
  assert.throws(()=>referenceWaterMask(input,texture,[],world),/Config\/metadata/);
  assert.throws(()=>referenceWaterMask(input,texture,zones,{...world,size:[0,2]}),/Positive/);
});
test('CLI rejects missing capture or unknown flags rather than generating metrics',()=>{
  assert.throws(()=>run([]),/Usage:/);
  assert.throws(()=>run(['--ignore-unsupported']),/Unknown option/);
});
