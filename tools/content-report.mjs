import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const kinds = [
  ['weapons', '무기'], ['tools', '도구'], ['charters', '특허장'], ['items', '물품'],
  ['heroes', '영웅'], ['estates', '영지 전통'], ['vassals', '가신'],
  ['enemies', '적'], ['evolutions', '진화'], ['skins', '외형'],
];
const escape = (value) => String(value ?? '').replaceAll('|', '\\|').replaceAll('\n', '<br>');
const table = (headers, rows) => [
  `| ${headers.join(' | ')} |`, `| ${headers.map(() => '---').join(' | ')} |`,
  ...rows.map((row) => `| ${row.map(escape).join(' | ')} |`), '',
].join('\n');
const records = [];
for (const [directory, label] of kinds) {
  const files = (await fs.readdir(path.join(root, 'data', directory))).filter((file) => file.endsWith('.json')).sort();
  for (const file of files) records.push({ directory, label, file, record: JSON.parse(await fs.readFile(path.join(root, 'data', directory, file), 'utf8')) });
}
const tags = new Map();
const pairs = new Map();
for (const { record } of records) {
  for (const tag of new Set(record.tags)) {
    if (!tags.has(tag)) tags.set(tag, []);
    tags.get(tag).push(record.id);
  }
  for (const note of record.antiSynergyNotes ?? []) {
    const key = [record.id, note.otherId].sort().join(' ↔ ');
    if (!pairs.has(key)) pairs.set(key, note.reason);
  }
}
const links = records.filter(({ directory, record }) => ['weapons', 'tools', 'items'].includes(directory) && record.loopLinks.length > 0);
const output = [
  `관문: 부분 — 콘텐츠 ${records.length}개 정본 표 생성. 실행·밸런스 판정은 R3 보고서를 따른다.`, '',
  '변경: 기본 영웅·영지를 개척 기사·새싹 변경으로 이관하고 유해 순환 설정을 반영했다.',
  '결정: 과거 S3 표는 보존하고 현재 데이터 표를 별도 파일로 생성한다.',
  '검증 범위: 생성 표의 데이터 일치 확인이며 실행 효과·성능·밸런스를 입증하지 않는다.', '',
  '# 현재 콘텐츠 풀', '',
  '이 문서는 `node tools/content-report.mjs`로 정본 JSON에서 생성한다. 후보 수량·태그·참조의 검증은 `node tools/validate-content.mjs`가 담당한다. 이 표는 구현 또는 밸런스 승인 증거가 아니다.', '',
  'R3 기본 영웅·영지는 개척 기사 `core:frontier_knight` / 새싹 변경 `core:sprout_march`이다. 새싹 변경은 데이터의 `remainsLoop`로 처치 유해의 보관·만료·밭 흡수를 설정한다. 역사적 [S3 후보 표](S3-pool.md), [세 후보 비교](S3-base-proposals.md), [확장 이름 목록](S3-expansion-concepts.md)은 당시 기록으로 보존한다. 실행 검증 결과는 별도 R3 보고서를 따른다.', '',
  '`s2-runtime`은 S2 당시 기본 실행 대상으로 분류된 기록이며, 이후 구현된 동작 전체를 제한하는 표시는 아니다. R3에서는 영지의 선택적 `remainsLoop`를 읽어 처치 유해의 보관·만료·밭 흡수를 실행하는 공통 기능이 구현되었다. 이 기능은 영웅의 유해 운반 능력이나 설명에 있는 모든 고유 효과의 구현을 뜻하지 않는다. `candidate`는 실행 프로필에 자동 편입되지 않는다. 발동 `damageCoefficient`는 비교 설계용이며 S2 정수 피해에 곱하지 않는다. 바닥값은 측정된 60–70% 보증이 아니다.', '',
  '## 수량', '',
  table(['종류', '레코드 수', 's2-runtime 분류 레코드'], kinds.map(([directory, label]) => [label, records.filter((r) => r.directory === directory).length, records.filter((r) => r.directory === directory && r.record.designStatus === 's2-runtime').length])),
  `기본 순환 연결을 명시한 무기·도구·물품: ${links.length}개. 의미를 가진 상호 비용은 아래 반시너지 표에서 검토한다.`, '',
];
for (const [directory, label] of kinds) {
  output.push(`## ${label}`, '');
  const selected = records.filter((r) => r.directory === directory);
  output.push(table(['ID / 이름', '개념', '태그', '상태', '순환 연결'], selected.map(({ record: r }) => [
    `${r.id}<br>${r.name}`, r.concept, r.tags.join(', '), r.designStatus,
    r.loopLinks.map((link) => `${link.stage}: ${link.reason}`).join('<br>') || '직접 연결 없음',
  ])));
  if (directory === 'tools') output.push(table(['도구', '발동 형태 / 설계 계수', '성장 대상 / 산출 / 주 경로', '바닥값 근거'], selected.map(({ record: r }) => [r.name, `${r.activation.form} / ${r.activation.damageCoefficient}`, `${r.growth.target} / ${r.growth.output} / ${r.growth.primaryRoute}`, r.floorRationale])));
  if (directory === 'items' || directory === 'charters') output.push(table(['이름', '발동 조건', '이익', '비용'], selected.map(({ record: r }) => [r.name, r.effect.trigger, r.effect.benefit, r.effect.cost])));
  if (directory === 'vassals' || directory === 'heroes') output.push(table(['이름', '조건', '능력', '비용'], selected.map(({ record: r }) => [r.name, r.ability.trigger, r.ability.effect, r.ability.cost])));
  if (directory === 'enemies') output.push(table(['이름', '목표', '압박', '대응'], selected.map(({ record: r }) => [r.name, r.target, r.behavior.pressure, r.behavior.counterplay])));
  if (directory === 'estates') output.push(table(['영지', '단일 순환', '순서와 작용'], selected.map(({ record: r }) => [r.name, r.uniqueLoop.summary, r.uniqueLoop.stages.map((stage) => `${stage.id}: ${stage.action}`).join('<br>')])));
  if (directory === 'skins') output.push(table(['외형', '대상', '색 / 실루엣 / 재질 / 연출'], selected.map(({ record: r }) => [r.name, r.targetId, Object.values(r.appearance).join('<br>')])));
  if (directory === 'evolutions') output.push(table(['이름', '유형 / 재료', '성장 조건', '변형 대상 / 결과', '비용'], selected.map(({ record: r }) => [r.name, `${r.evolutionKind} / ${r.inputIds.join(' + ')}`, r.growthCondition ? `${r.growthCondition.target}/${r.growthCondition.state}/${r.growthCondition.minimum}` : '재료 조합', `${r.result.baseId}: ${r.result.effect}`, r.result.cost])));
}
output.push('## 태그 분포', '', '테스트 레코드를 제외하고 같은 레코드 안의 중복 태그는 한 번만 센다.', '', table(['태그', '서로 다른 레코드 수', 'ID'], [...tags].sort(([a], [b]) => a.localeCompare(b)).map(([tag, ids]) => [tag, ids.length, ids.sort().join(', ')])));
output.push('## 반시너지', '', 'A↔B와 B↔A는 한 쌍으로 센다. 아래 비용은 설계 가설이며 S2에서 모든 고유 상호작용을 구현했다는 뜻이 아니다.', '', table(['무순서 쌍', '경쟁하는 자원·시간·상태'], [...pairs].sort(([a], [b]) => a.localeCompare(b))));
const rendered = `${output.join('\n').trimEnd()}\n`;
const destination = path.join(root, 'docs/content/current-pool.md');
if (process.argv.includes('--check')) {
  if (await fs.readFile(destination, 'utf8') !== rendered) throw new Error('current-pool.md differs from canonical JSON; run node tools/content-report.mjs');
  console.log('Current content tables match canonical JSON');
} else {
  await fs.mkdir(path.dirname(destination), { recursive: true });
  await fs.writeFile(destination, rendered);
  console.log(`Generated ${destination}: ${records.length} records, ${tags.size} tags, ${pairs.size} anti-synergy pairs`);
}
