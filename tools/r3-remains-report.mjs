import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { load } from './s4b-input.mjs';
import { canonical, sha256 } from './s4b-contract.mjs';
import { formatCsv } from './csv.mjs';

export const COUNTERS = ['created', 'absorbed', 'expired', 'dropped', 'active', 'fertilityTransferred', 'fertilityConsumed', 'growthBonusApplied', 'fertilizedHarvests'];
const need = (condition, message) => { if (!condition) throw new Error(`R3 remains: ${message}`); };
function counters(row) {
  need(row && typeof row === 'object' && !Array.isArray(row), 'ledger object required');
  for (const key of COUNTERS) need(Number.isSafeInteger(row[key]) && row[key] >= 0, `invalid ${key}`);
  need(row.created === row.absorbed + row.expired + row.active, 'conservation violated');
  need(row.fertilityConsumed <= row.fertilityTransferred, 'credited fertility consumption exceeds transfer');
}
export function verifyLedger(results, run, timeline) {
  need(Array.isArray(results) && results.length === 3, 'exactly three raw repeats required');
  const first = results[0];
  need(first.remains !== undefined && first.remains !== null, 'remains not instrumented');
  counters(first.remains);
  for (const result of results) {
    need(result.hash === run.hash && result.ticks === run.ticks && result.policy === run.policy && result.peopleRule === run.peopleRule && result.seed === run.seed && result.survived === run.survived && result.endReason === run.endReason, 'raw run identity mismatch');
    need(canonical(result.remains) === canonical(first.remains), 'repeat remains ledger mismatch');
  }
  const samples = first.remains.samples;
  need(Array.isArray(samples) && samples.length === timeline.length && samples.length >= 2, 'sample grid missing');
  samples.forEach((sample, i) => {
    counters(sample);
    need(sample.tick === timeline[i].tick, 'sample grid mismatch');
    for (const key of COUNTERS) {
      if (i === 0) need(sample[key] === 0, 'initial ledger must be zero');
      if (i > 0 && key !== 'active') need(sample[key] >= samples[i - 1][key], `counter regressed: ${key}`);
      if (i === samples.length - 1) need(sample[key] === first.remains[key], `terminal mismatch: ${key}`);
    }
  });
  need(samples[0].tick === 0 && samples.at(-1).tick === run.ticks, 'initial/terminal samples required');
  need(first.remains.fertilizedHarvests <= run.harvests, 'fertilized harvest count exceeds all harvests');
  return Object.fromEntries(COUNTERS.map(key => [key, first.remains[key]]));
}
export function aggregate(records) {
  const groups = new Map();
  for (const record of records) {
    const key = `${record.policy}/${record.peopleRule}/${record.movementMode}`;
    if (!groups.has(key)) groups.set(key, { policy: record.policy, peopleRule: record.peopleRule, movementMode: record.movementMode, distinctCases: 0, ...Object.fromEntries(COUNTERS.map(field => [field, 0])) });
    const group = groups.get(key); group.distinctCases++;
    for (const field of COUNTERS) { group[field] += record[field]; need(Number.isSafeInteger(group[field]), `aggregate overflow: ${field}`); }
  }
  return [...groups.values()];
}
async function checkedJson(input, relative, expectedHash) {
  need(relative.startsWith('raw/') && relative.split('/').every(part => part && part !== '.' && part !== '..') && !relative.includes('\\'), 'unsafe raw path');
  const bytes = await fs.readFile(path.join(input, relative));
  need(sha256(bytes) === expectedHash.toLowerCase(), 'raw hash mismatch');
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
}
export function checkBaselineResults(results, run) {
  need(Array.isArray(results) && results.length === 3, 'baseline repeat count');
  for (const result of results) {
    need(result.hash === run.hash && result.ticks === run.ticks && result.policy === run.policy && result.peopleRule === run.peopleRule && result.seed === run.seed, 'baseline raw identity mismatch');
    need(!Object.hasOwn(result, 'remains'), 'baseline unexpectedly has remains instrumentation');
  }
}
export async function collect(input, baselineInput) {
  const league = await load(input);
  need(['A', 'B', 'smoke-A', 'smoke-B'].includes(league.metadata.mode), 'A/B league required');
  const records = []; let remainsProvenance;
  for (const run of league.data.runs) {
    const results = await checkedJson(input, run.sourceResultsPath, run.sourceResultsSha256);
    const metrics = await checkedJson(input, run.sourceMetricsPath, run.sourceMetricsSha256);
    const source = metrics.sourceMetadata;
    need(source && source.commit === league.metadata.sourceCommit && source.profileId === league.metadata.profileId && source.profileSha256 === league.metadata.profileSha256, 'source/profile mismatch');
    const provenance = { remainsLoop: source.remainsLoop, fertilityPerKill: source.fertilityPerKill, fertilityGrowthBonus: source.fertilityGrowthBonus };
    need(provenance.remainsLoop && typeof provenance.remainsLoop === 'object', 'remains tuning provenance missing');
    for (const key of ['capacity', 'lifetimeTicks', 'absorptionRadius']) need(Number.isSafeInteger(provenance.remainsLoop[key]) && provenance.remainsLoop[key] > 0 && provenance.remainsLoop[key] <= 1000000, `invalid remainsLoop ${key}`);
    for (const key of ['fertilityPerKill', 'fertilityGrowthBonus']) need(Number.isSafeInteger(provenance[key]) && provenance[key] >= 0, `missing ${key} provenance`);
    if (remainsProvenance === undefined) remainsProvenance = provenance;
    else need(canonical(remainsProvenance) === canonical(provenance), 'remains tuning changed across cases');
    const movementMode = league.cases.find(info => info.caseId === run.caseId).movementMode;
    records.push({ policy: run.policy, peopleRule: run.peopleRule, movementMode, ...verifyLedger(results, run, league.data.timeline.filter(row => row.caseId === run.caseId)) });
  }
  let baseline = { status: 'not-inspected', distinctCases: 0, sourceCommit: '', profileId: '' };
  if (baselineInput) {
    const previous = await load(baselineInput);
    need(previous.metadata.mode === league.metadata.mode && canonical(previous.cases) === canonical(league.cases), 'baseline matrix mismatch');
    for (const run of previous.data.runs) checkBaselineResults(await checkedJson(baselineInput, run.sourceResultsPath, run.sourceResultsSha256), run);
    baseline = { status: 'not-instrumented', distinctCases: previous.data.runs.length, sourceCommit: previous.metadata.sourceCommit, profileId: previous.metadata.profileId };
  }
  return { metadata: league.metadata, groups: aggregate(records), remainsProvenance, baseline, inputManifest: league.manifest };
}
export async function writeReport(evidence, output) {
  const destination = path.resolve(output);
  try { await fs.access(destination); throw new Error('output already exists'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  await fs.mkdir(path.dirname(destination), { recursive: true });
  const staging = await fs.mkdtemp(destination + '.incomplete-');
  const { metadata: m, groups, baseline, remainsProvenance } = evidence;
  const totals = Object.fromEntries(COUNTERS.map(key => [key, groups.reduce((n, row) => n + row[key], 0)])); counters(totals);
  const provenance = { sourceCommit: m.sourceCommit, sourceTreeSha256: m.sourceTreeSha256, profileId: m.profileId, profileSha256: m.profileSha256, contentSha256: m.contentSha256, mode: m.mode, distinctCases: m.distinctCaseCount, seedBlocks: m.seedBlockCount, repeats: 3, remainsTuningJson: JSON.stringify(remainsProvenance), baselineStatus: baseline.status, baselineCases: baseline.distinctCases, baselineSourceCommit: baseline.sourceCommit, baselineProfileId: baseline.profileId };
  await fs.writeFile(path.join(staging, 'remains-summary.csv'), formatCsv(Object.keys(groups[0]), groups));
  await fs.writeFile(path.join(staging, 'provenance.csv'), formatCsv(['key', 'value'], Object.entries(provenance).map(([key, value]) => ({ key, value }))));
  await fs.writeFile(path.join(staging, 'source-manifest.csv'), formatCsv(['file', 'bytes', 'sha256'], evidence.inputManifest));
  const smoke = m.mode.startsWith('smoke-');
  await fs.writeFile(path.join(staging, 'report.md'), `관문: ${smoke ? '부분' : '통과'} — ${m.distinctCaseCount}사례·3반복 잔재 원장 일치, ${totals.created}개 생성 보존식 검증${smoke ? '; 900틱 기술 검증' : ''}.\n변경: 실제 잔재·비옥도 계측을 정책/사람/이동별로 집계했다.\n사용자 결정 필요: 없음. 이 관문은 잔재 계측 검증이며 리그 균형 관문과 별개다.\n\n# R3 잔재 계측\n\nsource ${m.sourceCommit}, profile ${m.profileId} (${m.profileSha256}). ${m.seedBlockCount}개 대응 seed 블록이며 반복3은 독립 사례로 합산하지 않았다.\n\n생성(admitted) ${totals.created} = 흡수 ${totals.absorbed} + 만료 ${totals.expired} + 종료시 활성 ${totals.active}. 용량 초과 거절 ${totals.dropped}는 생성과 별도다. 초기·일정 표본·종료 원장의 보존식과 누적 단조성, 세 반복 원장 전체의 동일성을 검증했다.\n\n비옥도 전달 ${totals.fertilityTransferred}, 실제 소비 ${totals.fertilityConsumed}, 적용 성장 보너스 ${totals.growthBonusApplied}, 비옥도를 소비한 작물 주기의 수확 ${totals.fertilizedHarvests}. 이 수확의 모든 식량·XP·생산량이 비옥도 때문에 추가됐다고 해석하지 않는다. 소비 계측은 잔재에서 전달된 비옥도 credit만 추적하며 기존 초기 비옥도 소비를 섞지 않는다.\n\nR2 대조 원장 상태: ${baseline.status}, 확인 사례 ${baseline.distinctCases}, source ${baseline.sourceCommit || '미검사'}. not-instrumented는 원본 JSON에 잔재 계측 필드가 없다는 뜻이며 0개라는 뜻이 아니다. ID 교체와 잔재 고리가 함께 달라진 비교에서 순수한 인과 효과를 추정하지 않는다. 수치 동결·프로필 변경 근거는 별도 검토 대상이다.\n\n정확한 그룹 합은 remains-summary.csv, 실행 출처·잔재 설정은 provenance.csv, 입력10CSV 해시는 source-manifest.csv에 있다. 이 보고서는 원본 JSON 해시도 확인하므로 CSV-only 재생성 보고서라고 주장하지 않는다. 모바일 조작·재미·전체 수확의 인과적 증가는 검증하지 않았다.\n`);
  await fs.rename(staging, destination); return { totals, groups: groups.length, baseline };
}
export async function run(args) {
  need(args.length === 2 || (args.length === 4 && args[2] === '--baseline'), 'usage: node tools/r3-remains-report.mjs INPUT OUTPUT [--baseline R2_INPUT]');
  return writeReport(await collect(args[0], args[3]), args[1]);
}
if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) run(process.argv.slice(2)).then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
