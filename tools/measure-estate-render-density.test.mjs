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

test('approved metric requires fixed masks, reads RED rather than alpha, and preserves strict exclusions', async()=>{
  const {approvedMeasurement,screenReferenceMask}=await import('./measure-estate-render-density.mjs');
  const base={width:2,height:2,items:[],water:[],land:[]};
  const texture=red=>({width:2,height:2,pixels:Uint8Array.from(Array.from({length:4},()=>[red,0,0,255]).flat())});
  assert.equal(approvedMeasurement(base,()=>null,null,null).status,'UNKNOWN');
  assert.deepEqual([...screenReferenceMask(base,texture(0))],[0,0,0,0]);
  const result=approvedMeasurement(base,()=>null,texture(255),texture(0));
  assert.equal(result.status,'MEASURED');assert.equal(result.values.dryLandPixels,4);assert.equal(result.values.emptyDryLandPercent,100);
  assert.throws(()=>screenReferenceMask(base,{...texture(0),width:3}),/width/);
  const quad={name:'soil',source:'soil',category:'ground',order:1,active:true,texture:'soil',filter:'Point',vertices:[{x:0,y:0,u:0,v:1,a:1},{x:2,y:0,u:1,v:1,a:1},{x:2,y:2,u:1,v:0,a:1},{x:0,y:2,u:0,v:0,a:1}],triangles:[0,1,2,0,2,3]};
  const soil=approvedMeasurement({...base,items:[quad,{...quad,name:'prop',category:'prop',order:0}]},()=>texture(255),texture(255),texture(0));
  assert.equal(soil.values.visibleTerrainPropCount,0);assert.equal(soil.values.emptyDryLandPercent,100);
});
