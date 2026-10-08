import { mkdtemp, mkdir, readFile, writeFile, lstat, readdir, rm, copyFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join, dirname, isAbsolute } from 'node:path';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
function run(command,args,cwd){const result=spawnSync(command,args,{cwd,encoding:'utf8',maxBuffer:32*1024*1024});if(result.status!==0)throw new Error(`${command} failed: ${result.error?.message??result.stderr}`);return result.stdout;}
export async function packageStage(root,stage,date,destination){
 if(!/^(?:S[0-5]|R[1-3]|S4b)$/.test(stage)||!/^\d{8}$/.test(date))throw new Error('Invalid stage or YYYYMMDD');
 root=resolve(root);destination=resolve(destination);const temporary=await mkdtemp(join(tmpdir(),'bs-mobile-package-'));
 try{
  const snapshot=join(temporary,'snapshot'),extracted=join(temporary,'verify');await mkdir(snapshot);await mkdir(extracted);
  const files=run('git',['ls-files','-z'],root).split('\0').filter(Boolean).sort();const rows=[];
  for(const name of files){if(name==='MANIFEST.json'||isAbsolute(name)||name.split('/').some(part=>part==='..')||/[\r\n]/.test(name))throw new Error(`Unsafe archive path ${name}`);
   const path=join(root,name),stat=await lstat(path);if(!stat.isFile()||stat.isSymbolicLink())throw new Error(`Not a regular file: ${name}`);
   const bytes=await readFile(path);rows.push({path:name,bytes:bytes.length,sha256:hash(bytes)});await mkdir(dirname(join(snapshot,name)),{recursive:true});await writeFile(join(snapshot,name),bytes,{mode:stat.mode});
  }
  const manifest={stage,date,commit:run('git',['rev-parse','HEAD'],root).trim(),files:rows,excludes:['.git','ignored build/cache files','external credentials']};
  await writeFile(join(snapshot,'MANIFEST.json'),`${JSON.stringify(manifest,null,2)}\n`);
  const zip=join(temporary,'stage.zip');run('zip',['-q','-r',zip,'.'],snapshot);run('unzip',['-tq',zip],temporary);run('unzip',['-q',zip,'-d',extracted],temporary);
  const actual=[];async function walk(folder,prefix=''){for(const entry of await readdir(folder,{withFileTypes:true})){const name=prefix+entry.name;if(entry.isDirectory())await walk(join(folder,entry.name),name+'/');else if(entry.isFile())actual.push(name);else throw new Error(`Unexpected extracted entry ${name}`);}}await walk(extracted);
  const expected=[...files,'MANIFEST.json'].sort();if(JSON.stringify(actual.sort())!==JSON.stringify(expected))throw new Error('Archive file set mismatch');
  for(const row of rows){const bytes=await readFile(join(extracted,row.path));if(bytes.length!==row.bytes||hash(bytes)!==row.sha256)throw new Error(`Archive hash mismatch ${row.path}`);}
  if(!(await readFile(join(extracted,'MANIFEST.json'))).equals(await readFile(join(snapshot,'MANIFEST.json'))))throw new Error('Manifest mismatch');
  await mkdir(destination,{recursive:true});const output=join(destination,`bs-mobile-${stage}-${date}.zip`);await copyFile(zip,output);
  return {zip:output,fileCount:rows.length,sha256:hash(await readFile(output)),crc:'PASS',cleanExtractionHashes:'PASS',exactFileSet:'PASS'};
 }finally{await rm(temporary,{recursive:true,force:true});}
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){const [stage,date,destination]=process.argv.slice(2);if(!destination||process.argv.length!==5)throw new Error('usage: node tools/package-stage.mjs STAGE YYYYMMDD DESTINATION');console.log(JSON.stringify(await packageStage(process.cwd(),stage,date,destination)));}
