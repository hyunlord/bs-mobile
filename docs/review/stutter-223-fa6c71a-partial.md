# #223 — 사용자 보류 시점의 부분 결과

관문: 부분 — 사용자 요청에 따라 2026-10-12 11:35:25 KST 측정을 중단했다. A/B 각3회 완료가 아니며 통과·개선 확정을 하지 않는다. 추가 실행 없이 기존 결과만 집계했다.

`artifacts/stutter-223/abba-fa6c71a`의 완료9실행(A1/B1/B2/A2 각411·C05, A3의411만), 표시13행이다. C05-all은 하네스의 고정 측정 창 전체이며 판 전체의 모든 틱을 뜻하지 않는다. `tools/summarize-stutter50.mjs`의 기존 `frameStats`를 사용했고 원자료·엄격한 완주 검사·관문 수식은 바꾸지 않았다. 평균 합산·이상치 삭제·초과 프레임 제외는 하지 않았다.

|실행|범위|프레임|p95 ms|p99 ms|최대 ms|>25ms 비율 %|해당 행 수치 기준만|
|---|---|---:|---:|---:|---:|---:|---|
|A1|411|1453|16.8928|17.3544|28.9474|0.4818|미달|
|A1|C05-all|13093|17.1568|17.6291|164.2788|0.1833|미달|
|A1|C05-800+|2386|17.0295|17.5676|32.8201|0.2515|미달|
|B1|411|1441|17.6285|26.6806|30.6820|1.1797|미달|
|B1|C05-all|13099|17.2032|17.6282|28.7815|0.1603|만족|
|B1|C05-800+|2387|17.0187|17.4990|27.5393|0.3770|미달|
|B2|411|1372|24.8837|25.7150|30.5716|3.4985|미달|
|B2|C05-all|13044|17.1572|17.6125|841.7649|0.2223|미달|
|B2|C05-800+|2383|17.1516|17.6178|27.5698|0.2518|미달|
|A2|411|1460|16.7456|16.8146|29.2773|0.0685|만족|
|A2|C05-all|13105|17.1603|17.6166|33.0311|0.2289|미달|
|A2|C05-800+|2383|17.1575|17.6252|27.1686|0.3357|미달|
|A3|411|1459|16.7628|17.1162|24.9111|0.0000|만족|

행 기준은 최대≤50ms와 >25ms 비율≤0.2%를 동시에 적용했다. 행 하나의 만족은 전체 ABBA 합격이 아니다. 특히 B1/B2 411은1.180%/3.499%로 미달, B2 C05 최대841.7649ms도 미달이다. B1 C05-all은 수치상 만족하지만800+ 구간0.3770%는 미달이다. A도 실행별 차이가 크므로 현재 표로 원인이나 개선을 단정하지 않는다.

## 상태·해시·전경

완료9실행은 모두 `verified=true`, `performanceEligible=true`, `developmentBuild=false`, `profilerEnabled=false`, `sourceDirty=false`, `interrupted=false`. CSV에 모든 행의 commit/source/state/expected hash를 그대로 남겼다. 각 완료 실행의 frames.csv에서 focused!=1 행0개다. B는 focusEvents/excludedIntervals0개이며, 구버전 A에는 그 필드가 없어 CSV의0은 미기록 기본값이다. A는 interrupted=false와 모든 측정 행의 focused=1로 확인하며 새 lifecycle join을 적용했다고 주장하지 않는다. 이는 해당 하네스 기록의 상태이며 OS 지연 원인 판정은 아니다.

- A: `8f446e5f46273a7f6001f6e09411e9bdd0c9f534`, source `EBAA94181C9B2A4989E4448DB781B828E971F27D497ADD52230BA53F15EA94F5`.
- B: `fa6c71a03bd42810e13c66fb44f81d561ec1a53e`, source `2A31903EFFC0CECD00C6B2F5F5CEA3A3DE691BB6DF638A63010E2DBAEFF381B1`.
- 411 완료 실행 공통 최종 해시: `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619`; expected hash와 모두 일치.
- C05-all 완료 실행 공통 최종 해시: `B056B090575693A13CEEAB514E909F5C2C346FF2B1007598A4EE6E9B2DE8AEE8`; expected hash와 모두 일치.

외부 전경 관측 원자료는 `foreground-apps-part{1,2,3}.jsonl`, 합본은`foreground-apps.jsonl`, 실행 순서는`launch-order.tsv`이다. 합본은 여러 initial/end 세션이 이어진 파일이므로 한 연속 관측으로 간주하지 않는다. 완료 실행 사이 Orca 복귀는 앱 종료/다음 실행 전환과 구분해야 하며, 초 단위 launch 로그만으로 특정 측정 프레임에 앱 이름을 억지 연결하지 않았다.

## 누락·실패 보존

- A3 C05 첫 시도: 11:23:05 KST Orca 전환 이후 Google Chrome·카카오톡; `c05capped-focus-lost-incomplete`와 로그 보존. benchmark.json 없음. 기존 A는 중단 후 재개할 수 없어 완료로 세지 않는다.
- A3 C05 두 번째 시도: 외부 관측 part2는 Google Chrome·OpenUsage 전환을 기록한다. `c05capped-focus-lost-incomplete-2-player.log` 보존, benchmark.json 없음.
- B3 411: 일시정지 상태에서 사용자가 측정 보류를 요청했고 최종 benchmark.json 없음. B3 C05도 없음. 누락 결과를0 또는 통과로 채우지 않는다.
- 사용자가 요청한 UI·빌드 전달을 먼저 진행한다. 이 자료를 근거로 측정을 자동 재개하지 않는다.

작은 정본 CSV 제안: [정본 CSV](../metrics/stutter-223-fa6c71a-partial.csv). 개발진 진단 기록과 Release 측정은 별개이며 새 진단 추정으로 이 표의 실패를 삭제하지 않는다. pass2 진단 raw의 추가 대용량 분석은 전달 우선순위에 따라 이번 집계에서 생략했다.

## Release 실패 표본

사전 선택 기준: 완료된 B 실행 전체에서 wallMs가 가장 큰 프레임과 직전/직후3개. [7프레임 CSV](../metrics/stutter-223-fa6c71a-failure-window.csv)는 B2 C05의 frame5118–5124다. 최고 frame5121/tick9587/enemies564는841.7649ms, Advance0·Core0·snapshot0·GC0, 화면 구성63.5259ms·HUD0.0072ms다. 해당 시점 전경은 유지됐다. FrameTimingManager 값은 관측 프레임에 붙은 지연 표본이며 정확히 같은 렌더 프레임의 비용으로 합산하지 않는다. 원인은 미확정이며 사용자 보류 후 추가 실행하지 않았다.

## 부분 통계 재생성

다음 명령은 로컬에 보존한 완료 표본에서만 부분 CSV를 재생성한다. 전체 관문 명령 `node tools/summarize-stutter50.mjs artifacts/stutter-223/abba-fa6c71a`는 누락이 있으므로 성공할 수 없다. 이 부분 명령으로 그 검사를 대체하지 않는다.

```sh
node --input-type=module <<'JS'
import fs from 'node:fs';
import path from 'node:path';
import { frameStats } from './tools/summarize-stutter50.mjs';
import { parseCsv } from './tools/csv.mjs';
const root = 'artifacts/stutter-223/abba-fa6c71a';
const output = [];
for (const run of ['A1', 'B1', 'B2', 'A2', 'A3']) for (const fixture of ['crowd411', 'c05capped']) {
  const dir = path.join(root, run, fixture), file = path.join(dir, 'benchmark.json');
  if (!fs.existsSync(file)) continue;
  const id = JSON.parse(fs.readFileSync(file, 'utf8'));
  const rows = parseCsv(fs.readFileSync(path.join(dir, 'frames.csv'), 'utf8')).map(r => ({ ...r, wallMs: Number(r.wallMs), enemies: Number(r.enemies) }));
  for (const [scope, selected] of fixture === 'crowd411' ? [['411', rows]] : [['C05-all', rows], ['C05-800+', rows.filter(r => r.enemies >= 800)]]) {
    const stats = frameStats(selected);
    output.push({ run, scope, frames: stats.frames, p95_ms: stats.p95_ms, p99_ms: stats.p99_ms, max_ms: stats.max_ms, over25_percent: stats.over25_percent, row_thresholds_met: stats.pass, commit: id.commit, source_hash: id.sourceHash, state_hash: id.stateHash, expected_hash: id.expectedStateHash, verified: id.verified, performance_eligible: id.performanceEligible, development: id.developmentBuild, profiler: id.profilerEnabled, dirty: id.sourceDirty, interrupted: id.interrupted, unfocused_rows: selected.filter(r => r.focused !== '1').length, focus_events: id.focusEvents?.length ?? 0, excluded_intervals: id.excludedIntervals?.length ?? 0, pacing: id.pacingPolicy ?? 'legacy-unbounded-catch-up', foreground_policy: id.foregroundPolicy ?? 'legacy-uninterrupted-only' });
  }
}
const headers = Object.keys(output[0]);
fs.writeFileSync('artifacts/stutter-223/abba-fa6c71a/partial-regenerated.csv', [headers.join(','), ...output.map(r => headers.map(h => r[h]).join(','))].join('\n') + '\n');
JS
```
