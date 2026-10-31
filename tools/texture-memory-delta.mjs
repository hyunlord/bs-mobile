import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';

export const texturePaths = changed => [...new Set(changed.filter(p => /^unity\/Assets\/(Art\/|Resources\/|Game\/View\/Resources\/).+\.png(?:\.meta)?$/.test(p)).map(p => p.replace(/\.meta$/, '')))].sort();
export function expectedBytes(width, height, format, mipmaps) {
  assert(Number.isInteger(width) && width > 0 && Number.isInteger(height) && height > 0, 'Invalid dimensions');
  assert(['RGBA32','RGB24','ASTC4','ASTC6'].includes(format), 'Unsupported format; report and extend explicit estimator');
  let total=0;
  do { total += format.startsWith('ASTC') ? Math.ceil(width/Number(format.slice(4)))*Math.ceil(height/Number(format.slice(4)))*16 : width*height*(format==='RGBA32'?4:3);
    if (!mipmaps || (width===1 && height===1)) break;
    width=Math.max(1,Math.floor(width/2));height=Math.max(1,Math.floor(height/2));
  } while(true);
  return total;
}
export function parsePlatforms(meta,width,height,colorType=6) {
  const blocks=[...meta.matchAll(/    buildTarget: ([^\n]+)\n([\s\S]*?)(?=  - serializedVersion:|  spriteSheet:|$)/g)];
  const integer=(text,key,fallback)=>{const m=text.match(new RegExp('^\\s*'+key+': (-?\\d+)','m'));return m?Number(m[1]):fallback;};
  const settings=new Map(blocks.map(m=>[m[1],m[2]]));const fallback=settings.get('DefaultTexturePlatform');assert(fallback,'Missing default texture platform');
  const mipmaps=integer(meta,'enableMipMap',0)===1,formats={},bytes={};
  for(const [key,name] of [['mac','Standalone'],['android','Android'],['ios','iOS']]) {
    const candidate=settings.get(name),s=candidate&&integer(candidate,'overridden',0)===1?candidate:fallback;
    assert(integer(s,'maxTextureSize',4096)>=Math.max(width,height),'Resized textures need explicit estimator support');
    const f=integer(s,'textureFormat',-1),compression=integer(s,'textureCompression',0);
    formats[key]=f===48?'ASTC4':f===50?'ASTC6':f===4?'RGBA32':f===3?'RGB24':f===-1&&compression===0?(colorType===2?'RGB24':'RGBA32'):null;
    assert(formats[key],`Unsupported ${name} format/compression ${f}/${compression}`);
    bytes[key]=expectedBytes(width,height,formats[key],mipmaps);
  }
  return {width,height,mipmaps,formats,bytes};
}
export function validateRecord(record,beforeIdentity,afterIdentity,actualAfter,actualBefore=null) {
  assert(record,'Missing texture memory delta record');assert(typeof record.reason==='string'&&record.reason.trim(),'Missing reason');
  for(const [key,identity] of [['before',beforeIdentity],['after',afterIdentity]]) {
    const value=record[key];if(identity===null){assert.equal(value,null,`${key} must be null for absent texture`);continue;}
    assert(value && value.status==='ESTIMATE',`${key} must explicitly be ESTIMATE, not device measurement`);
    assert.deepEqual(value.identity,identity,`${key} identity must match PR PNG blob`);
    assert.equal(typeof value.mipmaps,'boolean');
    for(const p of ['mac','android','ios'])assert.equal(value.bytes[p],expectedBytes(value.width,value.height,value.formats[p],value.mipmaps),`${key}.${p} bytes formula mismatch`);
  }
  for(const [key,actual] of [['after',actualAfter],['before',actualBefore]])if(actual)for(const field of ['width','height','mipmaps','formats','bytes'])assert.deepEqual(record[key][field],actual[field],`${key}.${field} differs from PNG/meta`);
}
function identity(blob){if(blob===null)return null;const pointer=blob.toString('utf8').match(/^version https:\/\/git-lfs.github.com\/spec\/v1\noid sha256:([a-f0-9]{64})\nsize (\d+)\n?$/);return pointer?{sha256:pointer[1],fileBytes:Number(pointer[2])}:{sha256:createHash('sha256').update(blob).digest('hex'),fileBytes:blob.length};}
export function checkTextureMemoryDelta({changed,base,head,cwd=process.cwd()}) {
  const textures=texturePaths(changed);if(!textures.length)return;
  const git=args=>execFileSync('git',args,{cwd,maxBuffer:128*1024*1024});const ancestor=git(['merge-base',base,head]).toString().trim();
  const blob=(ref,p)=>{try{return git(['show',`${ref}:${p}`]);}catch(error){if(error.status===128)return null;throw error;}};
  const records=changed.filter(p=>/^docs\/metrics\/texture-memory-deltas\/[^/]+\.json$/.test(p)).flatMap(p=>JSON.parse(blob(head,p)).records);
  for(const p of textures){const matches=records.filter(r=>r.path===p);assert.equal(matches.length,1,`Missing or duplicate texture memory record: ${p}`);
    const before=blob(ancestor,p),after=blob(head,p),bi=identity(before),ai=identity(after);let current=null,colorType=6;
    if(ai){let raw=after;
      if(!raw.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))){const working=fs.existsSync(path.join(cwd,p))?fs.readFileSync(path.join(cwd,p)):null;
        if(working&&identity(working).sha256===ai.sha256)raw=working;
        else {const common=git(['rev-parse','--git-common-dir']).toString().trim();const object=path.resolve(cwd,common,'lfs','objects',ai.sha256.slice(0,2),ai.sha256.slice(2,4),ai.sha256);if(!fs.existsSync(object))git(['lfs','fetch','--include='+p,'--exclude=','origin',head]);assert(fs.existsSync(object),`PR head LFS object missing after scoped fetch: ${p}`);raw=fs.readFileSync(object);}}
      assert.deepEqual(identity(raw),ai,`Decoded PR head PNG identity differs: ${p}`);assert(raw.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])),'Expected decoded LFS PNG');colorType=raw[25];current=parsePlatforms(blob(head,p+'.meta').toString(),raw.readUInt32BE(16),raw.readUInt32BE(20),colorType);}
    // A changed LFS base may not be hydrated in CI. Its oid/size anchors the explicitly declared before estimate.
    const oldMeta=blob(ancestor,p+'.meta');
    const previous=bi&&ai&&oldMeta&&bi.sha256===ai.sha256?parsePlatforms(oldMeta.toString(),current.width,current.height,colorType):null;
    validateRecord(matches[0],bi,ai,current,previous);
  }
}
