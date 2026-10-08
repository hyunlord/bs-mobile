import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { parseCsv, formatCsv } from './csv.mjs';

const POLICIES = ['weapon', 'land', 'building', 'people', 'mixed', 'random'];
const SPECIALIZED = POLICIES.slice(0, 4);
const RULES = ['A', 'B', 'C'];
const HEADERS = {
  'outcomes.csv': 'policy,peopleRule,movementMode,caseCount,seedBlockCount,survived,survivalRate,truncatedAlive,meanReachedSeconds,meanLevel,meanCombatDamage'.split(','),
  'condition-ranks.csv': ['peopleRule', 'seed', 'cofirstPoliciesJson'],
  'gates.csv': ['gate', 'pass', 'applicable'],
};
const need = (condition, message) => { if (!condition) throw new Error(`c-prime: ${message}`); };
function integer(value, label) {
  need(typeof value === 'string' && /^(0|[1-9][0-9]*)$/.test(value) && Number.isSafeInteger(Number(value)), `${label} integer required`);
  return Number(value);
}
export function ratioVerdict(policySurvived, policyCases, randomSurvived, randomCases) {
  for (const value of [policySurvived, policyCases, randomSurvived, randomCases]) need(Number.isSafeInteger(value) && value >= 0, 'nonnegative safe counts required');
  need(policyCases > 0 && randomCases > 0 && policySurvived <= policyCases && randomSurvived <= randomCases, 'invalid survival counts');
  const numerator = BigInt(policySurvived) * BigInt(randomCases);
  const denominator = BigInt(randomSurvived) * BigInt(policyCases);
  if (denominator === 0n) return { status: 'NOT_EVALUABLE', ratio: null, crossNumerator: numerator.toString(), crossDenominator: '0' };
  const pass = 5n * numerator >= 3n * denominator && 2n * numerator <= 3n * denominator;
  return { status: pass ? 'PASS' : 'FAIL', ratio: Number(numerator) / Number(denominator), crossNumerator: numerator.toString(), crossDenominator: denominator.toString() };
}
export function adjudicate(stage, tables) {
  need(['R2', 'R3'].includes(stage), 'unknown stage');
  const outcomes = new Map();
  for (const row of tables['outcomes.csv']) {
    need(POLICIES.includes(row.policy) && RULES.includes(row.peopleRule) && row.movementMode === 'circuit', 'unexpected outcome cohort');
    const key = `${row.policy}/${row.peopleRule}`;
    need(!outcomes.has(key), 'duplicate outcome cohort');
    const cases = integer(row.caseCount, 'caseCount'), blocks = integer(row.seedBlockCount, 'seedBlockCount'), survived = integer(row.survived, 'survived');
    need(cases === 32 && blocks === 32 && survived <= cases && integer(row.truncatedAlive, 'truncatedAlive') === 0, 'full matched 32-seed cohort required');
    need(/^(?:0(?:\.[0-9]+)?|1(?:\.0+)?)$/.test(row.survivalRate) && Number(row.survivalRate) === survived / cases, 'survival rate/count mismatch');
    outcomes.set(key, { cases, survived });
  }
  need(outcomes.size === 18, 'all 18 policy/people cohorts required');
  const conditions = new Set(); let dominant = [...POLICIES];
  for (const row of tables['condition-ranks.csv']) {
    const seed = integer(row.seed, 'seed');
    need(RULES.includes(row.peopleRule) && seed >= 42 && seed <= 73, 'invalid matched condition');
    const key = `${row.peopleRule}/${seed}`; need(!conditions.has(key), 'duplicate matched condition'); conditions.add(key);
    const cofirst = JSON.parse(row.cofirstPoliciesJson);
    need(Array.isArray(cofirst) && cofirst.length > 0 && cofirst.every(policy => POLICIES.includes(policy)) && new Set(cofirst).size === cofirst.length, 'invalid cofirst set');
    dominant = dominant.filter(policy => cofirst.includes(policy));
  }
  need(conditions.size === 96, 'all 96 matched conditions required');
  const gates = new Map();
  for (const row of tables['gates.csv']) {
    need(['validity', 'a', 'b', 'c', 'd', 'XP', 'mixed-ledger'].includes(row.gate) && !gates.has(row.gate), 'invalid/duplicate historical gate');
    need(['true', 'false'].includes(row.pass) && row.applicable === 'true', 'full historical gate booleans required'); gates.set(row.gate, row.pass === 'true');
  }
  need(gates.size === 7 && gates.get('validity'), 'complete valid historical assessment required');
  const bPass = dominant.length === 0;
  need(bPass === gates.get('b'), 'recomputed b contradicts historical b');
  const cells = SPECIALIZED.flatMap(policy => RULES.map(peopleRule => {
    const p = outcomes.get(`${policy}/${peopleRule}`), r = outcomes.get(`random/${peopleRule}`);
    return { stage, policy, peopleRule, policySurvived: p.survived, policyCases: p.cases, randomSurvived: r.survived, randomCases: r.cases, ...ratioVerdict(p.survived, p.cases, r.survived, r.cases) };
  }));
  const passCells = cells.filter(row => row.status === 'PASS').length, evaluableCells = cells.filter(row => row.status !== 'NOT_EVALUABLE').length;
  return { cells, summary: { stage, retrospective: true, requiredCells: 12, evaluableCells, passCells, cPrimePass: passCells === 12, bPass, retrospectiveReviewPass: passCells === 12 && bPass, historicalCPass: gates.get('c'), dominantPoliciesJson: JSON.stringify(dominant) } };
}
async function readStage(stage, directory) {
  const tables = {}, manifest = [];
  for (const [file, headers] of Object.entries(HEADERS)) {
    const bytes = await fs.readFile(path.join(directory, file));
    tables[file] = parseCsv(new TextDecoder('utf-8', { fatal: true }).decode(bytes), headers);
    manifest.push({ source: `${stage}/${file}`, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
  }
  return { ...adjudicate(stage, tables), manifest };
}
export async function review(r2, r3, output) {
  const destination = path.resolve(output);
  try { await fs.access(destination); throw new Error('output already exists'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const stages = await Promise.all([readStage('R2', r2), readStage('R3', r3)]);
  const cells = stages.flatMap(stage => stage.cells), summaries = stages.map(stage => stage.summary), manifest = stages.flatMap(stage => stage.manifest);
  await fs.mkdir(path.dirname(destination), { recursive: true }); const staging = await fs.mkdtemp(destination + '.incomplete-');
  for (const [name, rows] of [['c-prime.csv', cells], ['summary.csv', summaries], ['source-manifest.csv', manifest]]) await fs.writeFile(path.join(staging, name), formatCsv(Object.keys(rows[0]), rows));
  const pass = summaries.every(row => row.retrospectiveReviewPass);
  const markdown = `관문: ${pass ? '통과' : '실패'} — 후향 c′ R2 ${summaries[0].passCells}/12, R3 ${summaries[1].passCells}/12; 기존 (b) 각각 ${summaries.map(row => row.bPass ? '통과' : '실패').join('/')}.\n변경: #57 결정의 새 생존율 범위를 이미 완료된 R2·R3 A 집계에 적용했다.\n사용자 결정 필요: 없음. 과거 사전 선언 판정·보고서·원자료는 변경하지 않는다.\n\n# c′ 후향 재판정\n\n이 결과는 결과를 관측한 뒤 선택한 기준의 **후향 재판정**이다. 사전 등록된 새 실험의 통과나 과거 실패의 취소가 아니다. 새 시뮬레이션·수치 조정·표본 추가는 하지 않았다. 원래 (a)·(d)·XP·원장 검증을 새로 판정하지 않으며, 아래 결론은 c′와 변경하지 않은 (b)에만 해당한다.\n\n각 전문 정책 weapon/land/building/people을 같은 사람 규칙의 random과 비교한다. 네 정책×세 사람 규칙 **12셀 모두** random 생존율의 0.6–1.5배(양 경계 포함)에 들어야 c′ 통과다. 모든 셀은 같은32개 seed 블록을 공유하며 독립 표본수를 늘리지 않는다.\n\n|단계|정책|사람|정책 생존|random 생존|비율|c′|\n|---|---|---|---|---|---|---|\n${cells.map(row => `|${row.stage}|${row.policy}|${row.peopleRule}|${row.policySurvived}/${row.policyCases}|${row.randomSurvived}/${row.randomCases}|${row.ratio === null ? '판정 불가' : row.ratio.toFixed(6)}|${row.status}|`).join('\n')}\n\n## 정확한 판정과 한계\n\nx=정책 생존수×random 사례수, y=random 생존수×정책 사례수로 두고 BigInt 정수 비교 **5x≥3y 및 2x≤3y**를 모두 요구한다. 표시용 반올림 비율은 판정에 쓰지 않는다. random 생존수가0이면 0/0도 NOT_EVALUABLE이며 해당 셀·전체 c′ 통과로 처리하지 않는다.\n\n(b)는 원래96개 사람×seed 조건의 공동1위 정책 집합 교집합으로 다시 계산하고 기존 gates.csv와 일치함을 확인했다. 기존 순위와 동률 정의를 바꾸지 않았다. 원래 조건별 순위 CSV를 입력으로 쓰며 원시 시뮬레이션을 재실행했다고 주장하지 않는다.\n\n집계 입력6파일의 SHA-256은 source-manifest.csv, 정확한 교차곱과 셀별 결과는 c-prime.csv에 있다. source 표기는 단계별 상대 이름이라 디렉터리를 옮겨도 재생성 바이트가 같다. R2 원본은 ../S4b/A/report.md, R3 원본은 ../R3/A/report.md에 그대로 남는다. 이 비율 구간은 신뢰구간이나 전역 균형·인과 효과의 증명이 아니다.\n`;
  await fs.writeFile(path.join(staging, 'report.md'), markdown);
  await fs.rename(staging, destination); return summaries;
}
if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  try { need(process.argv.length === 5, 'usage: node tools/retrospective-c-prime.mjs R2_A_REPORT R3_A_REPORT NEW_OUTPUT'); console.log(JSON.stringify(await review(...process.argv.slice(2)))); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
