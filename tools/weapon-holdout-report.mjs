import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { dominance } from './s4-report.mjs';
import { ratioVerdict } from './retrospective-c-prime.mjs';
import { formatCsv,parseCsv } from './csv.mjs';
export const POLICIES=['weapon','land','building','people','mixed','random'];
export function makeCases(mode){assert.ok(['full','smoke'].includes(mode));return (mode==='full'?Array.from({length:32},(_,i)=>20000+i):[9100]).flatMap(seed=>['A','B','C'].flatMap(peopleRule=>POLICIES.map(policy=>({caseId:`${policy}__${peopleRule}__${seed}`,policy,peopleRule,seed,arm:'control',requestedTicks:mode==='full'?21600:900}))));}
export function evaluate(runs,mode){
 const expected=makeCases(mode);assert.deepEqual(runs.map(r=>r.caseId).sort(),expected.map(r=>r.caseId).sort(),'exact holdout case grid');
 for(const e of expected){const r=runs.find(r=>r.caseId===e.caseId);assert.equal(r.repeatCount,3,'three repeats required');assert.equal(r.repeatVerified,true,'repeat verification required');for(const k of ['policy','peopleRule','seed'])assert.equal(r[k],e[k]);assert.equal(typeof r.survived,'boolean');assert.ok(Number.isSafeInteger(r.ticks)&&r.ticks>0&&r.ticks<=e.requestedTicks);if(r.survived)assert.equal(r.ticks,e.requestedTicks);for(const k of ['level','weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage'])assert.ok(Number.isSafeInteger(r[k])&&r[k]>=0);}
 const outcomes=[];for(const peopleRule of ['A','B','C'])for(const policy of POLICIES){const g=runs.filter(r=>r.peopleRule===peopleRule&&r.policy===policy);outcomes.push({policy,peopleRule,cases:g.length,survived:g.filter(r=>r.survived).length,survivalRate:g.filter(r=>r.survived).length/g.length,meanReachedTicks:g.reduce((s,r)=>s+r.ticks,0)/g.length});}
 const ratios=outcomes.filter(r=>['weapon','land','building','people'].includes(r.policy)).map(p=>{const r=outcomes.find(r=>r.policy==='random'&&r.peopleRule===p.peopleRule);const verdict=ratioVerdict(p.survived,p.cases,r.survived,r.cases);return {policy:p.policy,peopleRule:p.peopleRule,policySurvived:p.survived,randomSurvived:r.survived,cases:p.cases,...verdict,weaponLowerPass:p.policy==='weapon'?(r.survived>0&&5n*BigInt(p.survived)>=3n*BigInt(r.survived)):null};});
 const d=dominance(runs),bPass=d.dominantPolicies.length===0,cPrimePass=ratios.every(r=>r.status==='PASS'),weaponPass=ratios.filter(r=>r.policy==='weapon').every(r=>r.weaponLowerPass);
 return {overall:mode==='smoke'?'SMOKE_ONLY':bPass&&weaponPass&&cPrimePass?'PASS':'FAIL',bPass,cPrimePass,weaponPass,outcomes,ratios,conditions:d.conditions,dominantPolicies:d.dominantPolicies};
}
export function reportContents(runs,mode){const r=evaluate(runs,mode),gates=['b','weapon-lower','c-prime'].map((gate,i)=>({gate,applicable:mode==='full',pass:[r.bPass,r.weaponPass,r.cPrimePass][i]}));
 const intro=[`관문: ${mode==='smoke'?'부분':r.overall==='PASS'?'통과':'실패'} — ${runs.length}조건·3반복, ${mode==='smoke'?'900틱 구조검사; 정식 관문 미평가':`b=${r.bPass}, weapon=${r.weaponPass}, c′=${r.cPrimePass}`}.`,'CSV 데이터 하위보고서다. 커밋·PR·CI 종합 판정은 별도 검토 보고서를 따른다.',`seed ${mode==='full'?'20000–20031 (32대응블록)':'9100 (smoke)'}; 반복은 표본수에 포함하지 않는다.`,'(b)는 각 사람규칙×seed의 [생존,틱,레벨,실제전투피해] 공동1위 교집합이 비어야 한다.','무기 하한은 같은 규칙 random생존의0.6배, c′는 전문4정책×ABC의0.6–1.5배다.','random생존0은 NOT_EVALUABLE이며 통과하지 않는다.','전투피해는 무기+도구활성+도구성장+아군; 식량·재료는 제외한다.','overkill은 실제무기의 requestedDamage-appliedHpDamage 진단값이며 억제피해를 더하지 않는다.','noRadiusTargetRate는 반경후보0, trueEmptyActivationRate는 형태판정까지 명중0; 분모는 실제attackAttempts다.','실패 결과는 보존하며 후속 제안은 별도 승인 문서의 단일 제안만 따른다.'].join('\n');
 const text=intro+'\n\n|정책|사람|생존/사례|\n|---|---|---|\n'+r.outcomes.map(o=>`|${o.policy}|${o.peopleRule}|${o.survived}/${o.cases}|`).join('\n')+'\n\n|정책|사람|random대비|c′|\n|---|---|---|---|\n'+r.ratios.map(o=>`|${o.policy}|${o.peopleRule}|${o.ratio??'판정불가'}|${o.status}|`).join('\n')+'\n';
 return new Map([['outcomes.csv',formatCsv(Object.keys(r.outcomes[0]),r.outcomes)],['ratios.csv',formatCsv(Object.keys(r.ratios[0]),r.ratios)],['condition-ranks.csv',formatCsv(Object.keys(r.conditions[0]),r.conditions)],['gates.csv',formatCsv(Object.keys(gates[0]),gates)],['report.md',text]]);
}
export function parseRuns(text){return parseCsv(text).map(r=>{assert.ok(['true','false'].includes(r.survived),'invalid survival boolean');assert.equal(r.repeatCount,'3','exactly3 repeats required');assert.equal(r.repeatVerified,'true','verified repeats required');const numbers={};for(const k of ['seed','ticks','level','weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage']){assert.match(r[k],/^(0|[1-9][0-9]*)$/);numbers[k]=Number(r[k]);assert.ok(Number.isSafeInteger(numbers[k]));}return {...r,...numbers,survived:r.survived==='true',repeatCount:3,repeatVerified:true};});}
export function validateReplayInputs(rows,provenance,mode){
 assert.equal(provenance.length,1,'one provenance row required');const p=provenance[0],full=mode==='full';assert.ok(['full','smoke'].includes(mode));
 const required={mode,profile:'weapon-growth-79',profileId:'core:weapon_growth_79',seeds:full?'20000..20031':'9100',distinctCases:String(full?576:18),seedBlocks:String(full?32:1),repeatCount:'3',totalExecutions:String(full?1728:54),tickRate:'30',configuredDurationTicks:'21600',requestedTicks:String(full?21600:900)};
 for(const[k,v]of Object.entries(required))assert.equal(p[k],v,`provenance ${k}`);assert.ok(['true','false'].includes(p.sourceDirty));if(full)assert.equal(p.sourceDirty,'false','full source must be clean');assert.match(p.sourceCommit,/^[0-9a-f]{40}$/i);
 for(const key of ['sourceTreeSha256','simulationAssemblySha256','coreAssemblySha256','profileSha256','tuningSha256','contentSha256','weaponDefinitionSha256'])assert.match(p[key],/^[0-9a-f]{64}$/i);
 for(const key of ['runtime','os','architecture'])assert.ok(typeof p[key]==='string'&&p[key].length);assert.equal(p.coreTargetFramework,'.NETCoreApp,Version=v8.0');
 for(const r of rows)for(const key of ['inputHash','packetSha256','gameplayHash','gameplayDigest','diagnosticDigest','diagnosticIdentity'])assert.match(r[key],/^[0-9a-f]{64}$/i,`run ${key}`);
 evaluate(rows,mode);
}
export async function replay(input,output,mode){
 const rows=parseRuns(await fs.readFile(path.join(input,'runs.csv'),'utf8')),provenance=parseCsv(await fs.readFile(path.join(input,'provenance.csv'),'utf8'));validateReplayInputs(rows,provenance,mode);
 const contents=reportContents(rows,mode),bytes=[...contents.values()].reduce((sum,text)=>sum+Buffer.byteLength(text),0);assert.ok(bytes<4500000,'report byte budget exceeded');
 const absent=async()=>{try{await fs.lstat(output);throw Object.assign(Error('output exists'),{code:'EEXIST'});}catch(e){if(e.code!=='ENOENT')throw e;}};await absent();const staging=await fs.mkdtemp(`${path.resolve(output)}.incomplete-`);
 try{for(const[name,text]of contents)await fs.writeFile(path.join(staging,name),text);await absent();await fs.rename(staging,output);}catch(e){await fs.rm(staging,{recursive:true,force:true});throw e;}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))replay(process.argv[2],process.argv[3],process.argv[4]??'full').catch(e=>{console.error(e.message);process.exitCode=1;});
