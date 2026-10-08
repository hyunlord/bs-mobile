관문: 부분 — A 576·B 288사례 계약 고정, 실제 720초 평가 관문은 측정 전이다.
변경: 기존 S4를 보존하며 S4b 전용 러너·CSV·보고서를 추가한다.
사용자 결정 필요: 없음. 사람 묶음 공동1위의 엄격한 해석은 기존 needs-decision을 따른다.

# S4b 데이터 계약

## CLI와 사례

`node tools/s4b-league.mjs MODE --profile STEM --output DIR --workers 2`

MODE는 `calibration`, `A`, `B`, `smoke-A`, `smoke-B`다. 프로필·출력 경로는 필수이며 workers는 1–4다. 프로필이 정확한 tuning JSON을 참조하므로 별도 임의 수치 CLI는 없다. 출력은 저장소 밖 또는 무시되는 `artifacts/` 안이어야 한다. 기존 출력 디렉터리를 덮어쓰지 않는다. 실패 도중 원자료는 `.incomplete-*`에 남고 완료 CSV로 게시하지 않는다.

|모드|카드 정책|사람|seed|이동|서로 다른 사례|틱|
|---|---|---|---|---|---:|---:|
|calibration|random|C|1000–1031|circuit|32|21600|
|A|6정책|A/B/C|42–73|circuit|576|21600|
|B|random|A/B/C|42–73|circuit/harvest/evade|288|21600|
|smoke-A|6정책|A/B/C|9000–9002|circuit|54|900|
|smoke-B|random|A/B/C|9000–9002|3이동|27|900|

각 사례의 반복3은 해시·결과·실험 원장이 일치해야 하며 통계는 repeat0만 사용한다. A/B는 같은 32개 seed 블록의 대응 설계다. 사례576개나 실행1728개를 독립 표본수라고 하지 않는다. 보정 후보 최대6개 순서·첫 적격 후보 고정은 루트 실행 원장으로 통제하며 러너가 자동 수치 조정하지 않는다. smoke는 기술 검증만 하며 보정·평가 seed를 보지 않는다.

보정 wrapper의 전체 `tuning`은 기본 `data/tuning.json`과 비교한다. 차이는 `world.map.lordHealth`와 `world.threat.*`만 허용하며 Node와 Sim 양쪽에서 검사한다. 적별 위협은 선택된 적의 speed/damage/health/attackCooldownTicks 오버라이드로만 바꾼다. 정책 가중·농지·사람·건물 경제·슬롯·계절·나머지 지도 설정을 wrapper 안에서 몰래 바꾸면 거부한다. 별도 experiment의 XP 계수는 보정 대상이며 이동·mixed 범주 순서는 후보 1부터 동결한다. 후보별 실제 스칼라 diff와 tuning 바이트 해시를 외부 증거에 남긴다.

## 원자료와 출처

기존 S4의 `runs,timeline,cards,loot,effects,determinism,metadata.csv` 열 이름을 유지하고 다음 세 파일을 추가한다. 정확한 헤더는 `tools/s4b-contract.mjs`의 HEADERS가 정의한다. CSV는 UTF-8, 모든 셀 인용, 표준 이중 따옴표 escaping, JSON 셀은 `Json` 접미사다. 원래 대량 JSON은 `raw/<caseId>.results.json` 및 `.metrics.json`에만 두며 docs/Git에 넣지 않는다.

- `caseId`: `policy__peopleRule__seed__movement`.
- `movement-samples.csv`: caseId,tick,lordX,lordY,estateX,estateY,distanceSquared,movementMode. 실제 0틱·일정 표본 간격·종료틱이며 timeline과 정확히 대응한다.
- `farm-wait-events.csv`: caseId,farmId,episode,ripeTick,endTick,endKind,observedWaitTicks,censored. observedWaitTicks=endTick-ripeTick. 동일 밭의 에피소드가 중복·겹치지 않는다. harvest는 수확 완료, destroyed는 경쟁 사건, death-censored/duration-censored는 실제 종료의 우측 검열이다.
- `movement-equality.csv`: peopleRule,seed,movementMode,leftCaseId,rightCaseId,leftSourceResultsPath,rightSourceResultsPath,leftTraceSha256,rightTraceSha256,commonEndTick,comparedTickCount,comparedCoordinateValues,mismatchCount. A 1440행(96조건×15정책쌍), smoke-A 135행. B·보정은 헤더만 있다.

Sim의 experiment는 movementMode,deathTick,movementSamples,farmWaitEvents 및 `movementTraceEncoding=int32le-xy-v1`,movementTraceCount,movementTraceBase64를 제공한다. 추적은 실제 0틱부터 종료까지 little-endian int32 XY를 담는다. 각 정책쌍에서 commonEndTick=min(leftTicks,rightTicks), 비교 틱수=commonEndTick+1, 좌표수=2×틱수다. 모든 해당 틱의 각 XY 값을 실제 비교하며 mismatchCount는 다른 좌표 값 수다. 추적 SHA-256은 Base64 문자열이 아니라 디코드한 바이트의 해시다. 비교 범위를 여섯 정책의 최소 생존틱으로 줄이지 않는다.

메타데이터는 `stage=S4b`, mode,distinctCaseCount,seedBlockCount,totalExecutions와 원본 source/profile/content/assembly 해시를 기록한다. `identityJson`은 공통 실행 환경, 선택 카탈로그, 시즌·전체기간을 보존한다. `experimentJson`은 Sim의 experimentContractVersion,tuningFile,tuningSha256,experimentDefinition,experienceCurve,mixedCategoryOrder,worldUnit,threatTuning,enemyTuning를 그대로 담는다. 튜닝 해시는 정확한 JSON 파일 바이트 SHA-256이며 XP Base/Linear/Quadratic과 전역 위협·적별 수치를 재검토할 수 있다. B에서 달라지는 movementMode는 공통 identity에 섞지 않는다.

## 보고서와 관문

`node tools/s4b-report.mjs INPUT_DIR OUTPUT_DIR`는 통계를 열 개 CSV로 계산한다. 원자료 `raw/`가 있으면 원본 JSON 해시를 추가 검증한다. 같은 CSV를 다른 디렉터리로 복사해 재생성해도 결과 바이트가 동일하다. 보고서 source-manifest.csv는 입력 CSV의 바이트 해시다. 원래 JSON 해시는 러너가 추출 당시 검증한다. CSV만 복사한 재생성은 원본 JSON을 재검증했다고 주장하지 않으며 기록된 출처를 신뢰하는 재현이다.

CSV 파손·불완전 행렬·반복 불일치·잘못된 생존 종료·격자 누락·원장 모순은 실패한다. 실제 이동 불일치 및 본 평가A의 mixed/random 원장 동일쌍은 유효성 실패이고 보고서를 남긴 뒤 종료코드1이다. 유효한 실험의 밸런스/XP FAIL은 보존해야 하는 결과이므로 종료코드0이다. smoke는 SMOKE_ONLY, B는 REFERENCE_ONLY이며 전체 A 관문 PASS를 주장하지 않는다.

A의 순위 피해는 weaponDamage+toolActivationDamage+toolGrowthDamage+allyDamage다. (a) random/C32사례 생존율30–70%, Wilson95%(z=1.959963984540054); (b)96대응조건 전체 공동1위 이상 정책 없음; (c)세 사람묶음에서 weapon/land/building/people 각각 하나 이상 공동1위; (d)식량·자재 배제다. 그룹 순위는 같은32개 seed의 생존수·틱합·레벨합·피해합을 사전식 비교하며 오차 허용으로 동률을 늘리지 않는다. 전체 통과는 유효성+(a–d)+모든 정책×사람 그룹의720초 생존자 레벨 중앙값25–45+mixed/random 동일 원장0이 모두 필요하다. 생존자0은 XP FAIL이다.

거리 통계는 sqrt(distanceSquared)의 world-unit 값이다. 중앙값은 짝수 표본의 가운데 두 값 평균, p95는 nearest-rank다. 표본수와 거리 구간 분포를 함께 기록한다. 사망 후 좌표를 채우지 않는다. 대기 통계는 harvest/destroyed/검열종류를 분리하며 검열 하한을 완료 대기로 평균내지 않는다. 반복·시점·정책 사례를 독립 seed로 늘리지 않는다. 모바일 재미·조작 검증은 범위 밖이다.
