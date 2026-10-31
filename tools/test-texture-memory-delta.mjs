import test from 'node:test';
import assert from 'node:assert/strict';
import { texturePaths, expectedBytes, validateRecord, parsePlatforms } from './texture-memory-delta.mjs';
const meta = `  enableMipMap: 0\n  maxTextureSize: 4096\n  platformSettings:\n  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 4096\n    textureFormat: -1\n    textureCompression: 0\n    overridden: 0\n  - serializedVersion: 4\n    buildTarget: Android\n    maxTextureSize: 4096\n    textureFormat: 48\n    textureCompression: 1\n    overridden: 1\n  - serializedVersion: 4\n    buildTarget: iOS\n    maxTextureSize: 4096\n    textureFormat: 48\n    textureCompression: 1\n    overridden: 1\n`;
const state={width:17,height:9,mipmaps:false,formats:{mac:'RGBA32',android:'ASTC4',ios:'ASTC4'},bytes:{mac:612,android:240,ios:240}};
const oid='a'.repeat(64), identity={sha256:oid,fileBytes:123};
const record=()=>structuredClone({path:'unity/Assets/Art/a.png',before:{identity,...state,status:'ESTIMATE'},after:{identity,...state,status:'ESTIMATE'},reason:'approved mobile trial'});
test('only current PR runtime texture changes are covered, meta deduplicated',()=>assert.deepEqual(texturePaths(['docs/a.png','unity/Assets/Game/Editor/x.png','unity/Assets/Art/a.png.meta','unity/Assets/Art/a.png']),['unity/Assets/Art/a.png']));
test('NPOT block bytes round each dimension and mip chain is explicit',()=>{assert.equal(expectedBytes(17,9,'ASTC4',false),240);assert.equal(expectedBytes(4,4,'RGBA32',true),84);});
test('missing record, stale identity and wrong formula fail',()=>{assert.throws(()=>validateRecord(null,identity,identity,state),/Missing/);let r=record();r.before.identity.sha256='b'.repeat(64);assert.throws(()=>validateRecord(r,identity,identity,state),/identity/);r=record();r.after.bytes.android++;assert.throws(()=>validateRecord(r,identity,identity,state),/bytes/);});
test('correct record preserves Mac while mobile changes are scoped',()=>{validateRecord(record(),identity,identity,state);assert.deepEqual(parsePlatforms(meta,17,9),state);});
test('before values are labelled estimates, not measured claims',()=>{let r=record();r.before.status='MEASURED';assert.throws(()=>validateRecord(r,identity,identity,state),/ESTIMATE/);});

test('real PR diff requires changed record and ignores unrelated history',async()=>{
  const fs=await import('node:fs'),path=await import('node:path'),os=await import('node:os');
  const {execFileSync}=await import('node:child_process');const {createHash}=await import('node:crypto');
  const {checkTextureMemoryDelta}=await import('./texture-memory-delta.mjs');const {prChangedFiles}=await import('./pr-changes.mjs');const {encodeRgbaPng}=await import('./estate-art.mjs');
  const cwd=fs.mkdtempSync(path.join(os.tmpdir(),'texture-delta-'));const git=(...args)=>execFileSync('git',args,{cwd,encoding:'utf8'}).trim();
  try{
    git('init','-q');git('config','user.name','Test');git('config','user.email','test@example.invalid');
    const p='unity/Assets/Art/a.png';fs.mkdirSync(path.join(cwd,'unity/Assets/Art'),{recursive:true});const raw=Buffer.concat([encodeRgbaPng({width:17,height:9,pixels:Buffer.alloc(17*9*4,255)}),Buffer.alloc(1024*1024+1)]);fs.writeFileSync(path.join(cwd,p),raw);
    const beforeMeta=meta.replaceAll('overridden: 1','overridden: 0');fs.writeFileSync(path.join(cwd,p+'.meta'),beforeMeta);git('add','.');git('commit','-qm','base');const base=git('rev-parse','HEAD');
    fs.writeFileSync(path.join(cwd,p+'.meta'),meta);git('add','.');git('commit','-qm','override');let head=git('rev-parse','HEAD'),changed=prChangedFiles({base,head,cwd});assert.throws(()=>checkTextureMemoryDelta({changed,base,head,cwd}),/Missing/);
    const id={sha256:createHash('sha256').update(raw).digest('hex'),fileBytes:raw.length},record={path:p,before:{identity:id,status:'ESTIMATE',...parsePlatforms(beforeMeta,17,9)},after:{identity:id,status:'ESTIMATE',...state},reason:'test'};
    fs.mkdirSync(path.join(cwd,'docs/metrics/texture-memory-deltas'),{recursive:true});fs.writeFileSync(path.join(cwd,'docs/metrics/texture-memory-deltas/test.json'),JSON.stringify({records:[record]}));git('add','.');git('commit','-qm','record');head=git('rev-parse','HEAD');changed=prChangedFiles({base,head,cwd});assert.doesNotThrow(()=>checkTextureMemoryDelta({changed,base,head,cwd}));
    const validHead=head;git('checkout','-qb','base-new',base);fs.writeFileSync(path.join(cwd,p),encodeRgbaPng({width:21,height:11,pixels:Buffer.alloc(21*11*4,128)}));git('add','.');git('commit','-qm','independent base art');const newerBase=git('rev-parse','HEAD');git('merge','--no-edit',validHead);assert.doesNotThrow(()=>checkTextureMemoryDelta({changed:prChangedFiles({base:newerBase,head:validHead,cwd}),base:newerBase,head:validHead,cwd}));git('checkout','--detach',validHead);
    for(const mutation of [r=>r.after.identity.sha256='f'.repeat(64),r=>r.after.bytes.ios++]){const bad=structuredClone(record);mutation(bad);fs.writeFileSync(path.join(cwd,'docs/metrics/texture-memory-deltas/test.json'),JSON.stringify({records:[bad]}));git('add','.');git('commit','-qm','invalid record');head=git('rev-parse','HEAD');changed=prChangedFiles({base,head,cwd});assert.throws(()=>checkTextureMemoryDelta({changed,base,head,cwd}),/identity|bytes/);}
  }finally{fs.rmSync(cwd,{recursive:true,force:true});}
});
