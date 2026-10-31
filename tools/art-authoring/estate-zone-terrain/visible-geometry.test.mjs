import test from 'node:test';
import assert from 'node:assert/strict';
import { visibleGeometry } from './visible-geometry.mjs';

test('transparent canvas centre never decides river restoration', () => {
  const image={width:64,height:32,pixels:Buffer.alloc(64*32*4)};
  image.pixels[3]=255;
  const result=visibleGeometry(image,[0,0],[{id:'visible',x:0,y:0,width:.1,height:.1}]);
  assert.deepEqual(result.occupiedZones,['visible']);
  assert.deepEqual([result.left,result.top,result.width,result.height],[0,0,1/64,1/32]);
});
test('occupancy uses visible pixels and sorted stable zone IDs only', () => {
  const image={width:2,height:1,pixels:Buffer.from([1,2,3,8,4,5,6,255])};
  const zones=[{id:'z',x:-1,y:-1,width:2,height:2},{id:'a',x:-1,y:-1,width:2,height:2}];
  assert.deepEqual(visibleGeometry(image,[0,0],zones).occupiedZones,['a','z']);
  assert.equal(visibleGeometry(image,null,[]).left,.5);
  assert.throws(()=>visibleGeometry({width:1,height:1,pixels:Buffer.alloc(4)},null,[]),/Empty/);
});
