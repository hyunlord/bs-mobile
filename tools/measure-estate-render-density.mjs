import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { parseArgs } from 'node:util';
import { decodeRgbaPng } from './wave1a-art.mjs';

// Same contribution thresholds as DENSE-METRICS-r2. Geometry comes from actual CanvasRenderer meshes.
const threshold = 32 / 255, minimumPixels = 8;
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
function category(item) {
  if (item.category) return item.category;
  if (item.name.startsWith('EstateMap.Shadow:')) return 'shadow';
  if (item.source === 'zone-fog') return 'fog';
  if (['night', 'illumination'].includes(item.source)) return 'lighting';
  if (['terrain', 'soil', 'yard-ground'].includes(item.source)) return 'ground';
  if (item.source === 'zone-river' || item.role?.startsWith('river')) return 'water';
  if (item.source === 'road') return 'path';
  if (item.source === 'zone-exterior') return 'exterior';
  if (item.source === 'zone-cluster') return 'cluster';
  if (item.source === 'building') return 'building';
  if (['yard','roadside','empty-land','fence','peasant','zone-prop','construction','reclaimable-site','placed','landmark','wood','frontier','river-bank'].includes(item.source)) return 'prop';
  throw Error(`Unmapped source ${item.source}: ${item.name}`);
}
function inside(x, y, polygon) {
  polygon = polygon.points ? polygon.points.map(p=>[p.x,p.y]) : polygon;
  let result = false;
  for (let i=0,j=polygon.length-1; i<polygon.length;j=i++) {
    const a=polygon[i], b=polygon[j];
    if ((a[1]>y)!==(b[1]>y) && x < (b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]) result=!result;
  }
  return result;
}
function sampleAlpha(texture, u, v, filter) {
  const at=(x,y)=>texture.pixels[(Math.min(texture.height-1,Math.max(0,y))*texture.width+Math.min(texture.width-1,Math.max(0,x)))*4+3]/255;
  const x=u*texture.width-.5, y=(1-v)*texture.height-.5;
  if(filter==='Point') return at(Math.floor(x+.5),Math.floor(y+.5));
  if(filter!=='Bilinear') throw Error(`Unsupported filter ${filter}`);
  const ix=Math.floor(x),iy=Math.floor(y),fx=x-ix,fy=y-iy;
  return (at(ix,iy)*(1-fx)+at(ix+1,iy)*fx)*(1-fy)+(at(ix,iy+1)*(1-fx)+at(ix+1,iy+1)*fx)*fy;
}
function raster(item, width, height, clip, texture) {
  if(item.unsupported) throw Error(`${item.name}: ${item.unsupported}`);
  const alpha=new Float32Array(width*height), vertices=item.vertices;
  if(!vertices?.length || !item.triangles?.length) throw Error(`${item.name}: missing rendered mesh`);
  const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
  for(let i=0;i<item.triangles.length;i+=3) {
    const [a,b,c]=item.triangles.slice(i,i+3).map(index=>vertices[index]);
    const denominator=cross(a,b,c); if(Math.abs(denominator)<1e-8)continue;
    const left=Math.max(0,Math.ceil(clip[0]-.5),Math.floor(Math.min(a.x,b.x,c.x))), right=Math.min(width,Math.ceil(clip[2]-.5),Math.ceil(Math.max(a.x,b.x,c.x)));
    const top=Math.max(0,Math.ceil(clip[1]-.5),Math.floor(Math.min(a.y,b.y,c.y))), bottom=Math.min(height,Math.ceil(clip[3]-.5),Math.ceil(Math.max(a.y,b.y,c.y)));
    for(let y=top;y<bottom;y++)for(let x=left;x<right;x++){
      const p={x:x+.5,y:y+.5},wa=cross(b,c,p)/denominator,wb=cross(c,a,p)/denominator,wc=1-wa-wb;
      if(wa< -1e-6 || wb< -1e-6 || wc< -1e-6)continue;
      const u=a.u*wa+b.u*wb+c.u*wc,v=a.v*wa+b.v*wb+c.v*wc;
      const value=sampleAlpha(texture,u,v,item.filter)*(a.a*wa+b.a*wb+c.a*wc)*(item.inheritedAlpha??1);
      alpha[y*width+x]=Math.max(alpha[y*width+x],value); // Shared triangle edges are one Graphic, never double blended.
    }
  }
  return alpha;
}
export function largestEmptyComponent(mask, width, height) {
  const seen=new Uint8Array(mask.length),queue=new Int32Array(mask.length);let largest=0;
  for(let start=0;start<mask.length;start++){
    if(!mask[start]||seen[start])continue;
    let head=0,tail=1;queue[0]=start;seen[start]=1;
    while(head<tail){
      const p=queue[head++],x=p%width,y=Math.floor(p/width);
      const visit=n=>{if(mask[n]&&!seen[n]){seen[n]=1;queue[tail++]=n;}};
      if(x>0)visit(p-1);if(x+1<width)visit(p+1);if(y>0)visit(p-width);if(y+1<height)visit(p+width);
    }
    largest=Math.max(largest,tail);
  }
  return largest;
}
export function measure(input, loadTexture) {
  const errors=[...(input.errors??[])], {width,height}=input;
  assert(Number.isInteger(width)&&Number.isInteger(height)&&width>0&&height>0&&width*height<=4000000);
  const size=width*height, clip=input.clip??[0,0,width,height], transparency=new Float32Array(size).fill(1), coverage=new Float32Array(size), strictCoverage=new Float32Array(size),paths=new Float32Array(size), rows=[];
  for(const item of [...input.items].sort((a,b)=>b.order-a.order)) {
    if(!item.active)continue;
    try {
      const kind=category(item);
      if(kind==='lighting' || (kind==='ground' && !input.strictApprovedReference))continue; // Runtime darkness/lamps are not authoring artwork; source fog does occlude.
      const alpha=raster(item,width,height,item.clip??clip,loadTexture(item.texture));
      let visiblePixels=0,rawPixels=0;
      for(let p=0;p<size;p++){
        const contribution=alpha[p]*transparency[p];
        if(alpha[p]>=threshold)rawPixels++;
        if(contribution>=threshold)visiblePixels++;
        if(kind==='path' && contribution>=threshold){paths[p]=1;coverage[p]=1;strictCoverage[p]=1;}
        else if(kind==='exterior'){if(contribution>=128/255)coverage[p]=1;}
        else if(!['water','fog','ground'].includes(kind) && contribution>=threshold){coverage[p]=1;strictCoverage[p]=1;}
        // Water and paths remain occluders; exterior is excluded from count but participates in visibility/coverage.
        transparency[p]*=1-alpha[p];
      }
      rows.push({name:item.name,source:item.source,role:item.role,category:kind,rawPixels,visiblePixels,visible:visiblePixels>=minimumPixels,counted:!['exterior','water','path','shadow','building','fog','ground'].includes(kind)&&visiblePixels>=minimumPixels});
    } catch(error){errors.push(String(error.message));}
  }
  const emptyMask=new Uint8Array(size);
  let land=0,dry=0,bare=0,strictBare=0,dryWithoutPaths=0,bareWithoutPaths=0;
  if(input.strictApprovedReference && (!input.landReferenceMask || !input.waterAlphaMask))errors.push('Missing approved screen land/visual-water reference masks');
  else if(!input.landReferenceMask && (!input.land?.length || !Array.isArray(input.water)))errors.push('Missing logical ground/water polygons');
  else for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    if(x+.5<clip[0]||x+.5>=clip[2]||y+.5<clip[1]||y+.5>=clip[3])continue;
    if(input.landReferenceMask ? input.landReferenceMask[y*width+x]<128/255 : !input.land.some(poly=>inside(x+.5,y+.5,poly)))continue;
    land++;if(input.waterAlphaMask ? input.waterAlphaMask[y*width+x]>=128/255 : input.water.some(poly=>inside(x+.5,y+.5,poly)))continue;
    const p=y*width+x;dry++;if((input.strictApprovedReference?strictCoverage[p]:coverage[p])<threshold){bare++;emptyMask[p]=1;}if(strictCoverage[p]<threshold)strictBare++;
    if(paths[p]<threshold){dryWithoutPaths++;if(strictCoverage[p]<threshold)bareWithoutPaths++;}
  }
  const pct=(a,b)=>b?100*a/b:null;
  const largestEmptyPixels=largestEmptyComponent(emptyMask,width,height);
  const visibleCountsBySource={},visibleCountsByRole={};
  for(const row of rows.filter(r=>r.visible)){
    visibleCountsBySource[row.source]=(visibleCountsBySource[row.source]??0)+1;
    visibleCountsByRole[row.role??'']=(visibleCountsByRole[row.role??'']??0)+1;
  }
  const values={dryLandPixels:dry,largestConnectedEmptyPixels:largestEmptyPixels,largestConnectedEmptyComponentPercent:pct(largestEmptyPixels,dry),visibleCountsBySource,visibleCountsByRole,visibleTerrainPropCount:rows.filter(r=>r.counted).length,visiblePrimaryBuildings:rows.filter(r=>r.category==='building'&&r.visible).length,logicalLandScreenPercent:pct(land,size),emptyDryLandPercent:pct(bare,dry),strictEmptyDryLandPercent:pct(strictBare,dry),emptyDryLandExcludingPathsPercent:pct(bareWithoutPaths,dryWithoutPaths)};
  return {status:errors.length?'UNKNOWN':'MEASURED',schema:'estate-density-r2-actual-mesh-v2',largestEmptyMethod:'4-neighbor pixel connectivity on clipped dry logical land with post-occlusion occupancy below 32/255; denominator all dry logical land pixels. Paths count as occupied. This added metric is not an original Astra metric.',referenceParity:'PARTIAL: original occupancy32/canopy128 contribution method recovered. Reference mask reprojection available; CG.high reference vs Unity bilinear sampling remains disclosed; authored fog occludes but never fills terrain.',thresholds:{alphaContribution:32,visiblePixels:8},camera:input.camera,geometryProvenance:input.geometryProvenance,waterMethod:input.waterAlphaMask?'Recovered original world water mask, alpha128, bilinear reprojected to actual exported camera':'Runtime logical river cells',values:errors.length?null:values,diagnosticPartialValues:errors.length?values:undefined,errors,items:rows};
}
// Approved screenshot masks encode occupancy in red, not PNG alpha. No resizing or logical-cell fallback.
export function screenReferenceMask(input, texture) {
  assert.equal(texture.width,input.width,'Reference mask width differs from capture');
  assert.equal(texture.height,input.height,'Reference mask height differs from capture');
  return Float32Array.from({length:input.width*input.height},(_,i)=>texture.pixels[i*4]/255);
}
export function approvedMeasurement(input, loadTexture, land, water) {
  if(!land || !water) return {status:'UNKNOWN',values:null,errors:['Approved screen land and visual-water masks are required; logical water is not a substitute']};
  const result=measure({...input,strictApprovedReference:true,landReferenceMask:screenReferenceMask(input,land),waterAlphaMask:screenReferenceMask(input,water)},loadTexture);
  result.largestEmptyMethod='4-neighbor connectivity on approved RED land minus approved RED visual-water; strict post-occlusion alpha32 occupancy including paths, excluding exterior/ground/water';
  result.waterMethod='Approved capture-space visual-water RED >=128; land RED >=128; no resampling';
  result.referenceParity='Same fixed denominator and alpha32 contribution contract; Node bilinear sampling differs from historical CG.high; compare measured baseline error before interpreting parity.';
  return result;
}
export function referenceWaterMask(input, texture, zones, world) {
  assert(world && [...world.origin,...world.size,...world.cell].every(Number.isFinite), "Explicit finite water mask world geometry required");
  assert(world.size.every(n=>n>0)&&world.cell.every(n=>n>0), "Positive water mask size/cell required");
  const points=poly=>poly.points?poly.points.map(p=>[p.x,p.y]):poly;
  assert.equal(input.land.length,zones.length,'Config/metadata zones differ');
  const q=points(input.land[0]),z=zones[0];
  const ax=(q[1][0]-q[0][0])/z.width,ay=(q[1][1]-q[0][1])/z.width,bx=(q[3][0]-q[0][0])/z.height,by=(q[3][1]-q[0][1])/z.height;
  const origin=[q[0][0]-z.x*ax-z.y*bx,q[0][1]-z.x*ay-z.y*by],det=ax*by-ay*bx;
  assert(Math.abs(det)>1e-8,'Degenerate camera');
  zones.forEach((r,i)=>[[r.x,r.y],[r.x+r.width,r.y],[r.x+r.width,r.y+r.height],[r.x,r.y+r.height]].forEach(([u,v],j)=>{
    const actual=points(input.land[i])[j];assert(Math.abs(actual[0]-(origin[0]+u*ax+v*bx))<.02&&Math.abs(actual[1]-(origin[1]+u*ay+v*by))<.02,'Cannot infer camera affine from zone polygons');
  }));
  const mask=new Float32Array(input.width*input.height);
  for(let y=0;y<input.height;y++)for(let x=0;x<input.width;x++){
    const px=x+.5-origin[0],py=y+.5-origin[1],u=(px*by-py*bx)/det,v=(py*ax-px*ay)/det;
    const tx=((u-v)*world.cell[0]/2-world.origin[0])/world.size[0],ty=((u+v)*world.cell[1]/2-world.origin[1])/world.size[1];
    if(tx>=0&&tx<1&&ty>=0&&ty<1)mask[y*input.width+x]=sampleAlpha(texture,tx,1-ty,'Bilinear');
  }
  return mask;
}
export function selftest(){
  const tex={width:1,height:1,pixels:Uint8Array.of(255,255,255,255)};
  const quad=(name,order,x=0,w=4,a=1,category='prop')=>({name,source:'zone-prop',category,order,active:true,texture:'test',filter:'Point',vertices:[{x,y:0,u:0,v:1,a},{x:x+w,y:0,u:1,v:1,a},{x:x+w,y:4,u:1,v:0,a},{x,y:4,u:0,v:0,a}],triangles:[0,1,2,0,2,3]});
  const base={width:4,height:4,land:[[[0,0],[4,0],[4,4],[0,4]]],water:[],items:[]};
  let result=measure({...base,items:[quad('rear',0),quad('front',1)]},()=>tex);assert.equal(result.status,'MEASURED');assert.equal(result.values.visibleTerrainPropCount,1);assert.equal(result.items.find(r=>r.name==='rear').visiblePixels,0);
  result=measure({...base,items:[quad('clipped',0,-2,4)]},()=>tex);assert.equal(result.items[0].visiblePixels,8);assert.equal(result.values.emptyDryLandPercent,50);
  result=measure({...base,items:[quad('fog',1,0,4,1,'fog'),quad('prop',0)]},()=>tex);assert.equal(result.values.visibleTerrainPropCount,0);assert.equal(result.values.emptyDryLandPercent,100);
  result=measure({...base,items:[quad('exterior',0,0,4,1,'exterior')]},()=>tex);assert.equal(result.values.visibleTerrainPropCount,0);assert.equal(result.values.emptyDryLandPercent,0);assert.equal(result.values.strictEmptyDryLandPercent,100);
  result=measure({...base,items:[{...quad('unsupported',0),unsupported:'custom alpha shader'}]},()=>tex);assert.equal(result.status,'UNKNOWN');assert.equal(result.values,null);
  result=measure({...base,items:[quad('building',0,0,4,1,'building')]},()=>tex);assert.equal(result.values.visibleTerrainPropCount,0);assert.equal(result.values.visiblePrimaryBuildings,1);
  result=measure({...base,items:[quad('path',0,0,2,1,'path')]},()=>tex);assert.equal(result.values.emptyDryLandPercent,50);assert.equal(result.values.emptyDryLandExcludingPathsPercent,100);
  result=measure({...base,items:[quad('lamp',1,0,4,1,'lighting'),quad('prop',0)]},()=>tex);assert.equal(result.values.visibleTerrainPropCount,1);
  assert.equal(largestEmptyComponent(Uint8Array.of(1,0,0,1),2,2),1);
  assert.equal(largestEmptyComponent(Uint8Array.of(1,1,0,1,1,1),3,2),5);
  result=measure({...base,water:[[[1,0],[3,0],[3,4],[1,4]]]},()=>tex);assert.equal(result.values.dryLandPixels,8);assert.equal(result.values.largestConnectedEmptyPixels,4);assert.equal(result.values.largestConnectedEmptyComponentPercent,50);
  result=measure({...base,items:[quad('path',0,1,2,1,'path')]},()=>tex);assert.equal(result.values.largestConnectedEmptyComponentPercent,25);
  console.log('PASS 12 static raster fixtures: occlusion, clipping, excluded fog, exterior coverage, fail-closed unsupported. No Unity validation.');
}

function pair(text, name) {
  const result=text?.split(',').map(Number);
  assert(result?.length===2 && result.every(Number.isFinite), name+' requires x,y');
  return result;
}
export function run(argv) {
  const {values:opts,positionals}=parseArgs({args:argv,allowPositionals:true,options:{
    'approved-land-mask':{type:'string'}, 'approved-water-mask':{type:'string'},
    selftest:{type:'boolean'}, 'texture-root':{type:'string'},
    'reference-water-mask':{type:'string'}, 'water-origin':{type:'string'},
    'water-size':{type:'string'}, 'grid-cell':{type:'string'}, 'zone-config':{type:'string'}
  }});
  if(opts.selftest){assert.equal(positionals.length,0);selftest();return null;}
  assert.equal(positionals.length,1,'Usage: node tools/measure-estate-render-density.mjs capture.png.density.json [--texture-root unity] --approved-land-mask capture-land.png --approved-water-mask capture-visual-water.png [--reference-water-mask mask.png --water-origin=-256,448 --water-size=1152,1152 --grid-cell=64,32 --zone-config config.json]');
  const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
  const inputBytes=fs.readFileSync(positionals[0]),input=JSON.parse(inputBytes),cache=new Map(),textures=[];
  const textureRoot=path.resolve(opts['texture-root']??path.join(root,'unity'));
  const load=file=>{
    if(!cache.has(file)){
      const resolved=path.resolve(textureRoot,file),relative=path.relative(textureRoot,resolved);
      assert(!relative.startsWith('..')&&!path.isAbsolute(relative),'Texture outside texture root');
      const bytes=fs.readFileSync(resolved);cache.set(file,decodeRgbaPng(bytes));textures.push({path:file,sha256:sha(bytes)});
    }
    return cache.get(file);
  };
  const legacy=measure(input,load);
  const maskProvenance={};
  const readMask=key=>{if(!opts[key])return null;const bytes=fs.readFileSync(opts[key]);maskProvenance[key]={path:opts[key],sha256:sha(bytes)};return decodeRgbaPng(bytes);};
  const result=approvedMeasurement(input,load,readMask('approved-land-mask'),readMask('approved-water-mask'));
  result.primaryMetric='approved-reference-strict-visual-water';
  result.legacyLogicalWaterMeasurement=legacy;
  result.approvedMaskProvenance=maskProvenance;
  result.provenance={capture:{path:positionals[0],sha256:sha(inputBytes)},textures};
  const referenceOptions=['reference-water-mask','water-origin','water-size','grid-cell','zone-config'];
  if(referenceOptions.some(k=>opts[k]!==undefined)){
    try{
      assert(referenceOptions.every(k=>opts[k]!==undefined),'Reference water requires mask, origin, size, grid cell, and zone config together');
      const world={origin:pair(opts['water-origin'],'water-origin'),size:pair(opts['water-size'],'water-size'),cell:pair(opts['grid-cell'],'grid-cell')};
      const maskBytes=fs.readFileSync(opts['reference-water-mask']),configBytes=fs.readFileSync(opts['zone-config']);
      const zones=JSON.parse(configBytes).zones;
      const mask=referenceWaterMask(input,decodeRgbaPng(maskBytes),zones,world);
      const comparison=measure({...input,waterAlphaMask:mask},load);
      result.referenceWaterComparison={status:comparison.status,waterMethod:comparison.waterMethod,values:comparison.values,errors:comparison.errors,
        provenance:{mask:{path:opts['reference-water-mask'],sha256:sha(maskBytes)},config:{path:opts['zone-config'],sha256:sha(configBytes)},world}};
    }catch(error){result.referenceWaterComparison={status:'UNKNOWN',values:null,errors:[String(error.message)]};}
  }
  return result;
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  try{
    const result=run(process.argv.slice(2));
    if(result){console.log(JSON.stringify(result,null,2));process.exitCode=result.status==='UNKNOWN'||result.referenceWaterComparison?.status==='UNKNOWN'?2:0;}
  }catch(error){console.log(JSON.stringify({status:'UNKNOWN',values:null,errors:[String(error.message)]}));process.exitCode=2;}
}
