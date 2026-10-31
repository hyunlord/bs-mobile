# FP01: first-playable 고정 성능 장면 (#443)

상태: 계약·하네스 초안. **Native main 기준선 3회는 미측정**. 자동 합성 테스트는 성능 수치가 아니다.

## 입력과 선정 근거

`run.ssreplay`는 #432에서 이미 비교한 seed 30000 입력 그대로다. 원자료의 출처는
`fp-five-enemy-walks-432-20261027` Release와 그 제작 source `47490004986a112a5fd10be1d07e05d3b45179cc`다.
`sourceCommit`은 이 검토 renderer/data snapshot이며 최초 recorder commit이라는 뜻은 아니다.
원본 로컬 경로는 `artifacts/day-20261027/replay-proof/first-playable-30000.ssreplay`였다.
입력 SHA·data hash·종료 27000틱/27052명령·종료 해시는 `contract.json`에 고정했다.
`SetInvulnerable`·`GrantLevel` 명령을 포함하므로 **무적 스트레스 비교**다. 정상 플레이·밸런스 판정을 대신하지 않는다.
기존 `44049CB5…` correctness fixture와는 다른 입력이며 그 역사 판정은 바꾸지 않는다.

측정 구간은 기존 #432 실제 영상과 같은 19891–20790틱이다. 이미 기록된 1800개 렌더 프레임을
900개 고유 틱으로 축약해, 성능 실행 전에 밀도와 다섯 종 존재를 확인했다. `density-selection.csv`는
실제 `RunFrame.Enemies`에서 얻은 작은 정본이며 적 중심의 화면 포함 여부나 가림 판정이 아니다.
전체 27000틱 밀도를 조사했다는 뜻은 아니다.

| 밀도 | 선정 구간 틱 수 | 비고 |
|---|---:|---|
| 0–224 | 198 | 실제 최저 199 |
| 225–249 | 353 | |
| 250+ | 349 | 실제 최고 283 |

다섯 종 `stone_breaker`, `rope_catcher`, `ember_sapper`, `bell_thief`, `whistle_hunter`는
900/900틱에 모두 존재한다. 원본 `sequence.json` SHA256은 contract의 `selectionSourceSha256`이다.
고정 스캔 재생성: `node tools/scan-first-playable-benchmark.mjs <원본 sequence.json> <출력.csv>`.
원본은 #432 로컬 증거에 보존하며 새 성공 원자료 묶음을 업로드하지 않는다.

## 측정 경계

- 원본 명령을 19891틱까지 그리기 없이 재생한다. 입력이나 Core 상태를 재작성하지 않는다.
- 원래 상태를 120프레임 그려 워밍업한다. 첫 측정 프레임은19891틱을 유지해 시작점을 반드시 기록하고, 이후 정상 시간으로 측정한다.
- 900개 틱 모두 측정 행에 있어야 한다. 같은 틱의 여러 렌더 프레임은 허용하지만 시작 누락·중간 틱 건너뛰기·끝 누락은 실패다.
- 20790틱의 마지막 프레임 간격을 기록한 뒤, 원본 나머지 명령을 그리기 없이 끝까지 적용한다.
- 종료 해시·종료 종류·27052명령이 원본과 일치해야 한다. `measurementEndTick`과 `endTick`은 다르다.
- 렌더·캡처·Profiler가 없는 Release만 성능 증거다. 전경 상실·제외 구간이 있으면 실행 무효다.
- 1600×900, Metal/Mono, 같은 quality·기기·pacing. C05처럼 uncapped는 회귀 추세용이며 절대 모바일 관문이 아니다.
- 3회씩 밀도별 pooled p95가 같은 설정의 기준선 대비 20%를 초과하면 경고한다. 누락 밀도는 미완료다.

실행과 기준선 생성 절차는 [런북](../../docs/runbooks/wave-density-benchmark.md)의 FP01 절을 따른다.
