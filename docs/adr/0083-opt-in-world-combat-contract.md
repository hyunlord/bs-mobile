# ADR0083 · 월드 전투의 목표·상비 병력·리플레이 opt-in 계약

- 상태: **승인 — 2026-11-02 사용자 승인 및 아래 두 조건 반영. 구현·측정 완료 아님.**
- 버전·날짜: 승인 v2, 2026-11-02. 변경: 입력 지연 허용 경계와 병상 배분 가설·서버 소유 명시.
- 연결: [#624](https://github.com/hyunlord/bs-mobile/issues/624), [GDD15](../design/gdd/15-slg-troops-proposal.md), [GDD16](../design/gdd/16-slg-heroes-proposal.md), [GDD18](../design/gdd/18-slg-world-map-proposal.md).
- 선행: [ADR0077](0077-authoritative-slg-server.md), [ADR0078](0078-server-domain-command-transactions.md), [가신 format8 ADR0075](0075-independent-vassal-replay-format-eight.md).
- 번호: 작성 기준 main의 마지막 번호는 0081. 병행 월드 지도 PR이 0082를 사용하므로 이 제안은 0083이며 병합 전 다시 확인한다.

## 1. 문제와 현재 사실

월드 직접 PvE는 지정 목표를 격파하고 표식을 지키며, 출정한 상비 병력의 전투 불능 수를 서버가 검증해야 한다. 현재 [RunVerifier](../../server/src/SowSiege.Server.Infrastructure/RunVerifier.cs)는 기본 영웅·영지와 프로필의 일반 입력을 [ReplayRunner.Verify](../../core/src/SowSiege.Core/ReplayCodec.cs)로 재실행한다. 반환값은 seed·종료 틱·해시·종료 종류다. `SubmitAsync`는 개인 개척전의 research 분야에 결합되어 있다. #595가 병합됐다는 사실은 월드 목표·병력 검증 완료를 뜻하지 않는다.

ADR0077 §13과 GDD17 §4.1은 개인 시제품의 목표를 생존 종료로 제한하고, 목표별 판정은 Core에 목표 개념이 생긴 뒤 별도 결정하도록 한다. [콘텐츠 초안의 worldEncounters](../../data/opt-in/slg/content-draft-v1.json)는 `defeat-and-hold`, 격파 수, 유지 시간, 제한 시간을 요구한다. 생존 종료·일반 적 처치 수를 이 목표의 달성으로 치환하지 않는다.

현행 [InteractiveOptions](../../core/src/SowSiege.Core/InteractiveContract.cs)는 가신 옵션까지 지원하지만 월드 목표·상비 병력 슬롯 계약은 없다. [format8 헤더](../../core/src/SowSiege.Core/ReplayCodec.Vassals.cs)는 envelope schema 1과 필수 가신 블록을 사용한다. [VassalRunSnapshot](../../core/src/SowSiege.Core/VassalRunContract.cs)은 규칙/콘텐츠·산술/나머지 버전, 슬롯·레벨 상한의 근거, roster, 해석 효과를 고정한다. [VassalRecoveryCodec](../../core/src/SowSiege.Core/VassalRecoveryCodec.cs)은 완전한 명령 prefix를 재실행해 해시·가신 나머지를 대조한다. 이 사실을 새 월드 계약의 설계 기준으로 삼되 기존 블록의 의미는 바꾸지 않는다.

## 2. 승인된 결정과 구현 경계

**승인된 결정: 직접 PvE 목표/상비 병력과 자동 PvE·도시 PvP 산술을 순수 Core의 별도 opt-in 모듈로 구현한다.** 서버가 계약을 발급하고 Core를 실행·검증하며, 영구 원장은 서버만 변경한다. 도시 자동 전투도 신규 전투 규칙이므로 이 결정에 따라 Core 변경과 C05 관문이 필요하다. 단순히 서버에서 실행한다는 이유로 규칙을 `server/`에 중복 작성하지 않는다. 이는 ADR0077 §5의 신규 순수 Core 모듈 방향을 유지한다.

| 영역 | 소유와 승인 경계 |
|---|---|
| 지형·배정·AOI·행군·채집·보호막·관계·목표 예약 | 서버. 이 ADR의 Core 승인 대기와 독립적으로 진행 가능 |
| 직접 PvE의 목표 진행·상비 병력 전투 불능 | 신규 Core opt-in. 이 ADR 승인 뒤 별도 코드 PR |
| 자동 PvE·도시 PvP 승패·병종별 K | 신규 순수 Core 자동 계산. 직접 PvE와 같은 결과 계약을 사용하되 다른 전투 모드/규칙 버전 |
| R/D/W/Q, 병상·전사, 보상·약탈·탐사 자격 | 서버 공통 writer와 월드 저장소의 원자 정산. Core는 DB·계정·연맹·UTC를 읽지 않음 |
| Unity의 목표/상비 부대 표현·새 형식 입력 기록 | ③ 담당 별도 PR. 이 세션은 `unity/`를 수정하지 않음 |

이번 PR은 문서뿐이다. 자동 전투 계수·지도 밸런스·승률은 확정하지 않는다. 수치·정책 값은 AG003에 따라 opt-in 데이터와 스키마로 제출하고 새 kind를 `tools/validate-content.mjs`에 등록한다. 형식 번호·버전·단위 분모·직렬화 상한은 프로토콜 상수이지 게임 수치를 코드에 숨기는 수단이 아니다.

## 3. 서버 계약과 Core 입력

서버는 발령 시 march snapshot을 저장하고 도착 재검사 뒤 encounter를 잠근다. 직접/자동 선택을 한 번 확정한 뒤 아래 전투 입력을 생성한다. 이후 모집·연구·가신 강화·콘텐츠 배포는 이미 발급한 입력을 바꾸지 않는다.

| 입력 | 계약 |
|---|---|
| 서버 바인딩 | accountId/marchId/encounterId/runId, targetId+generation, 계약 revision, 모드, 발급/만료 UTC, 보상 정책 hash. 서버 저장소 소유 |
| Core 바인딩 | 서버 계약의 canonical SHA-256 `contractHash`. Core는 이 값을 보존·해시하지만 계정 UUID로 게임 규칙을 분기하지 않음 |
| 규칙 provenance | profile ID/hash, rules ID/version, 콘텐츠 hash, 산술/나머지 버전, tickRate, seed와 RNG ID |
| 목표 | ordered objective 목록, 지정 spawn cohort/표식의 안정 키, 좌표·범위·수치, 정수 제한 틱 |
| 상비 병력 | 병종 ID ordinal 순의 `N[type]`, 병종별 해석된 전투 값, 병사별 안정 슬롯 `(type, ordinal)` |
| 영웅·가신·연구 | 발령 때 고정한 영웅/스킬 계약, 기존 가신 snapshot과 동종 효과의 최종 합산 값·상한·해석 근거 |
| 적·전장 | 허용된 encounter 데이터에서 해석한 적 roster/spawn 규칙·표식과 전장 경계. 클라이언트 제공 카탈로그는 사용하지 않음 |

서버는 소유권·자격·행군 상한을 검사하고 R→D와 영웅/슬롯 잠금을 같은 명령으로 저장한다. Core는 양수/범위·중복·정렬·합계와 해석 결과의 내부 일관성을 검사하나 계정의 실제 병력 보유를 승인하지 않는다. 서버가 저장한 canonical 입력과 제출 헤더가 정확히 같아야 한다. 해시가 맞다는 것만으로 클라이언트 입력을 신뢰하지 않는다.

병력 0의 개인 기초 개척전은 별도 계약이다. 월드 채집/PvE 최소 인원 조건을 개인 판에 전파하지 않는다. 동일 영웅의 중복 잠금 방지는 서버에서 개인 판에도 적용한다.

가신 효과를 기존 `vassal-run-v1`로 먼저 적용하고 영웅/연구를 다시 곱하면 공통 cap을 넘을 수 있다. **월드 모드에서는 출처별 raw 값을 검증한 뒤 동종 합산 → 공통 cap 1회 → 적용**으로 한다. 가신 블록은 provenance와 기존 계산 검증에 재사용하며 같은 효과를 두 번 적용하지 않는다. 월드 옵션이 없는 기존 가신 실행의 계산·cap·바이트는 그대로다. 상비/임시 아군의 효과 적격성도 데이터 계약에 별도 명시하고 가신의 Allies 효과를 임의로 전체 상비 병력 수에 더하지 않는다.

## 4. 목표 평가 계약

첫 버전의 목표 종류를 다음 세 가지로 제한한다. 목적지의 월드 타일 좌표와 전투 내부 좌표는 별도 공간이며 서버가 발급한 전장 계약으로 연결한다.

1. **`defeat-designated`:** 계약이 지정한 spawn cohort에 속한 살아 있는 적의 전투 불능 사건만 센다. 적의 안정 entityId를 한 번만 소비한다. 비관련 일반 적·이미 제거된 적·화면 효과·클라이언트 kill 수는 세지 않는다. 목표 수와 생성 가능한 cohort 수가 맞지 않는 데이터는 발급 전에 거절한다.
2. **`hold-marker`:** 지정 표식의 유효 상태, 살아 있는 조작 영웅이 표식 반경 안에 있음, 표식 반경 안에 유효한 적이 없음이 모두 참인 연속 틱을 센다. 하나라도 거짓이면 연속 유지 수를 0으로 되돌린다. 경계는 정수 제곱 거리 `<= radius²`로 포함한다. 유지 시간은 `holdSeconds × tickRate`로 정수 변환하고 overflow/표현 불가능 값을 거절한다. 이 세부 유지 조건과 연속 방식은 이번 구조 승인에 포함되며 별도 Core PR에서 구현한다.
3. **`time-limit`:** 게임 틱 시작을 0, 마지막 허용 진행 틱을 `limitTicks`로 한다. 시뮬레이션 일시정지나 클라이언트 시계가 서버 UTC 만료를 연장하지 않는다. 제한 틱 이후 입력은 거절한다.

`defeat-and-hold`는 격파 수와 연속 유지 수가 같은 판에서 모두 충족되어야 성공한다. 유지 누적은 판 시작부터 가능하고 격파 수 달성 전에도 조건을 만족하면 진행한다. 성공한 뒤 목표를 다시 취소하거나 같은 run의 성공 사건을 두 번 내지 않는다. 별도 “모든 적 제거 뒤부터 유지” 동작은 첫 버전에 몰래 추가하지 않는다.

틱의 확정 순서는 기존 허용 입력 적용 → 해당 틱 전투/이동·사망 사건 → 목표 집계 → 종료 판정이다. 동일 틱에서 영웅 패배 또는 필수 표식 파괴가 생기면 실패, 그다음 목표 완료면 성공, 그다음 제한 틱이면 timeout 실패다. 따라서 마지막 허용 틱에서 영웅·표식이 유효한 채 목표를 완료하면 성공한다. 숫자 반올림·순서 버전은 계약과 runtime hash에 포함한다. 직접/자동 결과의 `mode`를 구별하며 자동전투에 조작 영웅의 반경 유지 행동을 있었다고 기록하지 않는다.

## 5. 상비 병력 N/K와 결과

직접 PvE에서는 N명을 안정 슬롯으로 생성하고 각 슬롯의 병종·체력·전투 불능 여부를 Core가 소유한다. 묶음 그림/최적화된 배열은 가능하지만 슬롯을 병합·분할해 원천 식별을 잃지 않는다. 서버 밖에서 받은 인원 집계로 상태를 덮어쓰지 않는다.

피해 처리에서 체력 0에 도달한 상비 슬롯은 해당 판에서 전투 불능으로 확정한다. `K[type]`는 그 슬롯 집합의 크기다. 전투 불능 슬롯은 판 안 임시 소환·치유·구출로 새 상비 병사가 되거나 K에서 빠지지 않는다. 뿔나팔 임시 징집·구출 주민·일반 동료 그룹은 별도 출처로 추적하며 N/K에 넣지 않는다. 영웅 패배·quit·timeout 자체를 `K=N`으로 치환하지 않는다. 그 시점까지 확정된 상비 슬롯의 K만 반환한다.

공통 결과 계약은 `contractHash`, mode, rules/content hash, terminal reason, terminal tick/round, 목표별 진행·완료 상태, `N[type]`, `K[type]`, 결과 state hash다. `0 <= K[type] <= N[type]` 및 입력 N과의 정확한 일치를 검증한다. 클라이언트가 제출하는 종료 레코드는 검증 대상일 뿐 정산 원천이 아니다. 서버가 재실행해서 얻은 결과만 내부 정산 경로로 전달한다.

Core 결과에는 지갑·병상·입원·영구 전사·보상 지급·연맹 관계를 넣지 않는다. 전투 불능 K와 영구 전사 L은 다른 수량이다. 서버는 다음 두 커밋을 구분한다.

- **전투 커밋:** 대상 세대 상태, 확정 N/K, 증명/개방 자격, 유한 보상 자격, 회군을 원자 확정한다. N은 아직 D에 남는다. 승리 물질 후보는 생존 S의 적재 한도와 후보 원천을 적용해 운반물로만 확정한다.
- **귀환 커밋:** 병종 ID ordinal 순으로 `S=N-K`, `I=floor(K×injuryPercent/100)`, `A=min(I,남은 병상)`, `L=K-A`를 정수 계산한다. 중간 곱은 checked 64-bit로 계산한다. 병상은 동일 월드 사건 `(dueAt, sequence, entityId)` 순의 귀환이 먼저 사용한다. `D -= N; R += S; W += A`, 실제 지갑/공통 Credits 지급과 영웅/슬롯 해제를 함께 확정한다.

현재 PvE 80/PvP 60 부상률과 병상 수는 [가설] 데이터다. 병종별 내림을 먼저 하며 전체 K를 합산해 내린 뒤 임의 재배분하지 않는다. 병종 ID ordinal 순 병상 배분은 **[가설]**이다. 병종별 비례 배분·플레이어 지정 우선순위는 GDD15의 비교 대안으로 남긴다. 배분 함수는 Core가 아니라 서버 귀환 정산이 소유한다. 정책을 나중에 변경해도 확정 N/K와 리플레이 bytes·해시에는 영향이 없다. 서버는 적용 정책 버전과 정산 결과를 원장에 기록하며 이미 끝난 귀환을 새 정책으로 재정산하지 않는다. 각 병종·전체에 `N=S+A+L`이 성립하고 계정 점유량은 L만 감소해야 한다. 방어자 B는 R의 잠긴 부분집합이며, 도시전 defenseId로 전투 커밋과 즉시 귀환을 같은 거래에서 처리한다. 공격자는 일반 회군을 유지한다.

## 6. 리플레이·해시·복구 확장

**새 replay format9 + 독립 `world-combat-v1` 블록을 제안한다.** 기존 format8 envelope schema 1, `vassal-run-v1`, format7 estate schema 1/2와 format1~6의 바이트를 수정하지 않는다. 실제 구현 직전 main의 형식 사용 현황을 다시 확인하고 충돌하면 이 제안을 갱신한다.

형식 선택은 `WorldCombat != null`이면 새 형식, 없으면 **현재 Header 선택·직렬화 경로를 그대로** 사용한다. 옵션 없음의 헤더·명령·종료 바이트, runtime state codec bytes, 해시 입력에 빈 월드 필드·0·tag를 추가하지 않는다. 명시적 월드 옵션의 N=0 개인 계약은 absent가 아니며 새 형식으로 기록한다.

| 새 envelope의 순서 | 내용 |
|---|---|
| 공통 preamble | 기존 magic, format9, rules/state codec version, RNG ID와 기본 RunOptions/aim/dataHash |
| envelope schema | 새 envelope schema 1, 선택 필드 존재 flag를 고정 순서로 직렬화 |
| 기존 옵션 | TargetMaterial, Feel, Field, HostRewards, Estate, MeleeAim의 명시적 존재 여부와 해당 버전 값 |
| 가신 옵션 | 존재 flag, 있으면 길이 + 기존 `VassalRunCodec.Encode`의 bytes. 기존 payload를 재해석하지 않음 |
| 월드 옵션 | 필수 길이 + canonical `world-combat-v1` 입력 bytes. 목표·병종·슬롯·효과·규칙의 정렬과 단위 포함 |

이 형식은 format8에 알 수 없는 꼬리 데이터를 붙이는 방식이 아니다. 새 reader만 새 envelope를 읽는다. 구 reader는 unknown-version으로 실패하며 월드/병력을 버리고 과거 전투로 실행하지 않는다. 새 reader는 이전 형식을 계속 정확히 읽는다. 알 수 없는 schema·flag·mode·목표, 중복/역순 ID, 음수/overflow, 비정규 문자열/NaN 같은 허용하지 않는 표현, 과대 length·잘린 블록·trailing bytes·해시 불일치는 fail-closed한다. 문자열·배열·블록의 프로토콜 상한은 구현 PR에서 메모리 할당 전에 검사한다.

`world-combat-v1`은 account UUID의 의미를 해석하지 않고 contractHash·규칙/콘텐츠·전장·정렬된 N/효과를 저장한다. 발급 snapshot과 mutable runtime은 분리한다. runtime에는 목표별 카운터/연속 유지 수, 처리된 목표 적 ID, 상비 슬롯 체력/전투 불능, 이벤트 순번, 효과 정수 나머지, terminal latch를 담는다. 월드 옵션이 있을 때만 별도 state hash 확장을 활성화한다. 새 결과 포맷으로 기존 ReplayVerification의 역사 의미를 변경하지 않고 월드 결과를 별도 반환 계약으로 확장한다.

새 형식의 종료 레코드는 전용 `world-end-v1` tag/schema와 길이 경계를 둔다. 기존 `ReplayEndKind`의 Duration/Death/Quit를 성공/목표 실패로 재정의하지 않는다. 새 레코드는 최종 sequence/tick, 월드 terminal reason, 목표 결과, 정렬된 N/K와 최종 state hash를 담고 재실행 결과와 모두 대조한다. format1~8 reader의 종료 tag·필드 순서는 바꾸지 않는다. 새 입력/종료 블록과 관련 opt-in 데이터가 기존 data tree hash에 혼입되어 역사 replay를 바꾸지 않는지도 별도로 검사한다.

복구는 기존 가신 방식과 같은 **검증된 명령 prefix 재실행 + frontier 대조**를 우선 사용한다. 새 recovery magic/version 아래 완전한 새 헤더, 소비 catalog hash, 연속 sequence/tick prefix, frontier state hash와 월드 runtime bytes를 검증한다. 가신이 있으면 가신 runtime 나머지도 대조한다. 기존 `VassalRecoveryCodec`이 새 형식을 조용히 받게 하지 않고 새 recovery 경로를 둔다. prefix 누락·중복·순서 변경·중간 목표 카운터 위조·군단 슬롯 변경은 복원 거절이다.

## 7. 직접 참전의 서버 입력 수신·만료·불명확 결과

#595의 최종 파일 제출만으로는 연결이 끊긴 구간을 서버의 무입력으로 이어 계산할 수 없다. 월드 직접 판은 **짧은 입력 prefix를 서버에 영구 수신·승인하는 경로를 추가**한다. API 인증·입력 상한·승인 sequence·빈 구간 규칙은 서버 PR 소유이며 Unity 연결은 ③과 맞춘다. 최종 파일만 받을 수 있는 클라이언트에는 이 기능을 완료 상태로 열지 않는다.

서버는 수신한 연속 prefix와 해당 revision, 마지막 유효 신규 prefix 수신 시각을 영구 저장한다. **무입력 확정 frontier는 마지막 수신 시각 + 입력 지연 허용 구간까지 늦춘다.** 허용 구간은 버전 있는 데이터 값 `inputGraceMilliseconds`로 두고 시작 계약에 고정한다([가설] 수 초, 정확한 값은 opt-in 데이터 PR에서 제시). 클라이언트 시각이 아니라 서버 UTC highwater를 사용한다. 최초 prefix 전에는 서버의 run 시작 시각을 마지막 수신 시각으로 사용한다. highwater가 이 마감 시각 이하이면 아직 확정하지 않은 틱의 유효한 늦은 입력을 받으며, 초과한 뒤에는 해당 누락 틱을 무입력으로 확정하고 그 틱의 늦은 입력만 거절한다. 경계와 입력 수신·무입력 확정은 같은 run revision 잠금 아래 직렬화한다. 중복·무효 입력은 마지막 수신 시각을 갱신하지 않으며, 재시작도 허용 구간을 새로 시작하지 않는다. 이미 확정된 틱은 어떤 새 prefix도 되돌리지 않는다. 허용 구간은 게임 제한 틱이나 입력 가능한 게임 시간을 늘리지 않는다. UTC 만료 직전의 미확정 입력은 고정된 허용 마감까지 수신한 뒤 최초 terminal을 정산한다. 카드 선택 등 중단 상태가 발생하면 계약에 고정한 안정 ID 순 기본 선택을 서버 명령으로 기록한다. 이 선택도 리플레이 prefix에 포함하며 검증 시 임의 선택하지 않는다. 입력 지연 허용 데이터·chunk 상한은 시작 계약에 고정한다. 무입력 상태가 게임 시간을 영원히 멈추게 해서는 안 된다.

UTC 만료 시 승인된 prefix와 확정 무입력을 **최초 terminal 또는 제한 틱 중 먼저 도달한 시점까지** 재실행해 결과와 실제 K를 산출한다. 승인 prefix에서 이미 성립한 성공이나 무입력 중 제한 틱 전에 성립한 성공은 검증 완료가 늦었다는 이유로 실패로 덮지 않는다. 사망·quit 역시 최초 terminal을 보존한다. 제한 틱까지 앞선 terminal이 없을 때만 §4의 동일 틱 우선순위에 따라 timeout을 확정한다. 서버에 필요한 prefix나 해당 규칙/콘텐츠가 없거나 결과 검증이 불명확하면 `recovery-pending`으로 남기고 D·영웅·대상 잠금을 유지한다. 실패를 K=0으로 덮어써 무료 병력을 반환하거나 같은 세대에 자동 재발급하지 않는다. 운영 취소는 별도 감사 사건이며 이미 확정한 성공을 취소해 다시 보상하지 않는다.

검증은 월드/계정 DB 잠금 밖에서 수행하고 #595의 크기·실제 경과시간·동시 검증/시간 상한을 유지한다. 결과 커밋 때 account/run 소유권, encounter mode, targetId+generation, contractHash/revision, terminal 미확정 여부를 다시 검사한다. 검증 중 관계/삭제/운영 취소 상태가 바뀐 경우 승인된 서버 상태기계로 처리한다. 늦은 결과가 이미 확정한 취소/귀환을 덮어쓰지 않는다. 직접/자동 선택 경합과 두 verifier의 동시 완료는 unique settlement 키와 transaction으로 하나만 확정한다.

## 8. 자동 PvE와 도시 PvP

자동 전투는 직접 survivor simulation을 대충 빠르게 실행하거나 생존 결과를 복사하지 않는다. **별도 `army-auto-v1` 순수 Core 계산**을 제안한다. 서버가 입력 snapshot을 만들고 결과를 받아 정산한다. 서버에 같은 산술을 다시 구현하지 않는다. API가 Core를 호출하는 사실은 실시간 PvP 조작 기능을 추가하는 것이 아니다.

자동 계산은 정렬된 양측 병사 슬롯과 해석된 병종/영웅/가신/연구 값, 전장/목표 수비 값, 최대 round를 받는다. 첫 산술안은 정수 동시 round 방식이다: round 시작의 살아 있는 슬롯으로 표적을 안정 우선순위/슬롯 ID 순 선택 → 모든 피해를 시작 상태에서 산출 → 합산 피해를 동시에 적용 → K와 승패를 판정한다. 먼저 반복한 진영이 상대의 같은 round 공격을 지우지 않는다. 피해 식·병종 상성·영웅 기여·기본 값은 전투 데이터/규칙 버전으로 고정하며 구현 전에 데이터와 손계산 예제를 같은 PR에 제시한다. 승인되지 않은 임의 계수로 승률을 만들지 않는다.

round 상한까지 양측이 남거나 양측이 같은 round 전투 불능이면 공격 실패로 반환한다. 무병력 수비는 전투 시작 시 확인한 방어 snapshot의 정상 사례이며 건물/계정 상태를 파괴하지 않는다. 난수 없는 첫 산술안을 권고한다. 이후 난수 도입은 새 산술 버전·명시적 seed/RNG 소비 계약이 필요하며 기존 결과를 재해석하지 않는다.

자동 PvE의 적 roster는 같은 encounter 데이터의 자동 계산용 값이며, 직접과 자동의 성공 조건을 모드별 정책에 명시한다. 자동 결과는 “표식 반경에서 실제 몇 초 조작했다”는 주장을 하지 않고 `auto-objective-satisfied`를 반환한다. 같은 대상의 같은 세대에 정산 기회는 하나이며 보상 후보·생존 적재 상한은 공통이다. 자동이 직접보다 높은 보상이나 같은 승률을 보장하지 않는다.

도시 PvP는 공격 발령 snapshot과 **도착 직전** 잠근 방어 R의 부분집합 B를 사용한다. 지정 수비 영웅이 바쁘면 영웅 기여 0이고 임의 대체 영웅을 고르지 않는다. 보호막·자기/동맹 관계·수비 가용성 검사는 Core 호출 전 서버 책임이다. 약탈과 방어자 즉시 귀환, 공격자 회군은 Core 결과 뒤 같은 서버 거래로 처리한다. PvP에는 개인 탐사 증명/구역 개방을 지급하지 않는다.

판정: **이 제안에서는 ⑤의 도시 자동 전투도 Core 선행이 필요하다.** 정찰·보호막·발령/도착 관계 검사는 독립 서버 작업으로 계속할 수 있다. Core 승인을 기다리는 동안 임시 서버 전투 산식을 공개 API에 넣거나 항상 승리/무손실 결과로 대체하지 않는다.

## 9. 검증과 출시 관문

| 검사 축 | 필수 관측 |
|---|---|
| 역사 호환 | 월드 옵션 없음의 모든 기존 format1~8 헤더/전체 리플레이 bytes, state bytes, golden, crowd-411 해시 동일. 가신 absent/있음의 두 경로 각각 비교 |
| 새 형식 | 월드만/월드+가신/estate 옵션 조합, enabled N=0 개인 계약과 absent 구별, unknown version·length·중복·역순·조작 hash·trailing bytes 거절 |
| 목표 | 지정/비지정 적, 중복 kill, 경계 반경 안팎, 연속 hold 중단/재개, 마지막 허용 틱, 같은 틱 hero death·표식 파괴·성공·timeout 우선순위 |
| 병력 | N=0/1/상한, 병종 혼합, 동일 슬롯 중복 사망, 임시 징집/치유 비혼입, K=0/N, hero 패배와 K 독립, 음수·overflow 거절 |
| 효과 | raw 합산→cap 1회, 가신 중복 적용 없음, 판 중 계정 강화 무영향, 나머지 보존, 직접/자동 snapshot provenance 일치 |
| 정산 | 병종별 내림·병상 동시 귀환, N=S+A+L, D 유지, 지갑 overflow Credits, S=0 운반물0, 동일 run/세대 중복 보상0 |
| 자동 계산 | 손계산 소규모 fixture, 동시 전멸·무수비·round 상한, 입력 배열 순서 정규화, 같은 입력 반복 동일 결과, 다른 실행 타깃 동일 결과 |
| 복구 | hold/피해/효과 나머지 중간 prefix 복원, 재시작 뒤 동일 해시, 누락/중복 입력 거절, 무입력 frontier 이후 늦은 입력 거절 |
| 입력 지연 경계 | 허용 마감 직전/정확히 마감 입력 수락, 직후 이미 무입력 확정된 틱 입력 거절, 중복·무효 입력으로 유예 연장 불가, 재시작·UTC 역행·수신/확정 경합, 만료 직전 prefix와 최초 terminal 보존 |
| 동시성 | 직접/자동 경합, 중복 verifier, 검증 중 만료/취소/삭제, commit 전 실패 rollback, 재시작 재정산, DB 잠금 밖 검증 |
| 실제 실행 | 서버 발급→Unity 입력 기록→서버 prefix/최종 재실행→전투 커밋→귀환의 동일 계약 확인. 정적 검사나 봇 통과로 Unity 실행/재미를 대체하지 않음 |

구현 순서: 이 ADR 사용자 승인 → opt-in 데이터/codec/Core 목표·병력 및 자동 산술 PR → 서버 검증/복구·정산 PR → ③ Unity 연동 관문. Core PR은 기존 기본 경로의 바이트/골든/crowd-411 불변과 양쪽 실제 Core 대상 비교를 검증하고 **C05 3:3 측정 전 병합하지 않는다**. ① 측정 담당 요청은 Core PR 댓글에 기준·후보 SHA와 C05 3:3 조건을 남겨 사용자 중계로 전달한다. 측정 기준·후보 SHA가 바뀌면 이전 결과로 대체하지 않는다.

밤 23:00~07:00에는 빌드·Docker·dotnet test를 실행하지 않는다. 낮 검증은 공유 Mac의 자기 잠금, 독립 PostgreSQL 55443/Compose `codex-world`에서 진행한다. 기존 SDK 10.0.403은 읽기 전용이다. Core 테스트·서버 DB 테스트·C05·Unity 입력 연동은 각각 결과를 구분한다.

## 10. 대안과 승인 범위

- **생존 종료를 격파/유지로 취급:** 목표가 없는 판으로 월드 보상을 얻으므로 거절한다.
- **클라이언트 K·목표 완료 숫자만 검증:** 상태 위조와 응답 재전송으로 손실을 회피하므로 거절한다.
- **기존 format8/schema에 필드 추가 또는 absent에도 빈 확장 해시:** 역사 바이트와 해시를 바꾸므로 거절한다.
- **자동 전투를 server 전용 산식으로 우선 구현:** 공유 Core 규칙 경계와 직접/자동의 병력 결과 계약이 갈라지므로 권고하지 않는다. 이 대안을 선택하려면 ADR0077 §5의 명시적 대체 결정이 필요하다.
- **연결 끊김에 즉시 K=0 반환:** 전투를 유리하게 취소할 수 있으므로 거절한다.
- **ADR 승인 전에 Core 파일부터 수정:** 사용자가 지정한 선행 승인 순서에 어긋나므로 하지 않는다.

2026-11-02 사용자가 승인한 묶음은 (1) format9/독립 월드 블록, (2) 연속 표식 유지·동일 틱 종료 순서, (3) 상비 슬롯별 K와 서버 귀환 정산, (4) 입력 prefix+무입력 frontier 복구, (5) 자동 PvE·도시 PvP의 순수 Core 모듈이다. 자동 전투 계수와 밸런스 수치는 이 구조 승인이 확정하지 않는다. 병영 R/D/W/Q API는 #590 병합 후 정본에 맞추며 중복 장부를 만들지 않는다.

## 11. 이번 PR의 검증 상태

문서와 현행 코드의 읽기 정합성 검토만 수행했다. 코드·데이터·리플레이·골든 변경은 없다. 문서 링크/형식 및 PR 정책 검사 결과는 PR 본문에 기록한다. Core·서버 테스트, 자동 전투 실행, C05, Unity/기기 연동은 **NOT_RUN**이며 이 ADR만으로 구현·성능·재미를 통과했다고 보고하지 않는다.

## 12. PR-A 구현 경계 (v1, 2026-11-02, #629)

상태: **codec·스키마 구현, 실행 검증 진행 중**. §11은 원래 설계 승인 PR의 역사 기록이다. 이번 변경의 실제 검증 결과는 [PR-A 기록](../review/world-combat-codec-629.md)과 구현 PR 본문으로 구별한다.

`WorldCombatOptions`는 provenance·전장·영웅/스킬·입력 정책·목표·병종/N/안정 슬롯·spawn cohort·표식·효과 출처를 고정한다. `WorldCombatCodec`은 canonical 입력의 깊은 복사와 구조 검증을 제공한다. 병종과 슬롯은 각각 ID ordinal/0부터 연속 ordinal, 목표·cohort·표식은 ID ordinal, 효과는 MetaAbility 순서, 효과 출처는 종류→ID ordinal 순이다. 역순 입력을 decoder가 자동 정렬하지 않는다. PR-C의 입력 정규화는 별도 자동 계산 API의 책임이다.

format9의 공통 preamble은 §6의 기존 순서를 따른다. envelope schema는 i32 `1`, 뒤 순서는 TargetMaterial 문자열 → Feel 존재/flags → Field 존재/version → HostRewards 존재/payload → Estate 존재/version/ID/hash → MeleeAim 존재/version → 가신 존재/길이/기존 payload → 필수 월드 길이/payload다. 존재 표시는 u8 0/1, 정수는 little-endian이다. 공통/envelope 문자열은 기존 nullable 문자열 framing을 유지한다. 월드 payload 내부 문자열은 i32 UTF-8 길이와 bytes이며 잘못된 UTF-8을 거절한다.

월드 payload는 `world-combat-v1`, schema i32 `1`, flags u32 `1`, snapshot, 본문 SHA-256 32 bytes 순이다. checksum은 우발적/미재계산 변조 검출이며 서명이나 서버 발급 증명이 아니다. 서버는 여전히 authoritative 입력과 정확히 대조해야 한다. 새 hash는 uppercase hex canonical 표현을 사용하고 기존 형식의 hash 허용 범위는 바꾸지 않는다. payload 1MiB, 문자열 512 bytes/식별자 128 characters, collection 및 전체 상비 슬롯·cohort 적 각각 4096개는 할당 방어용 프로토콜 상한이다. 실제 출정·전투 수치는 데이터 계약으로 별도 제한한다.

`world-end-v1`은 tag i32 `4`, i32 길이, nullable Text tag, schema i32 `1`, i64 적용 명령 수, contractHash/mode/rulesHash/contentHash, terminal/tick 또는 round, 목표별 ID/진행/완료, 병종별 ID/N/K, stateHash 순이다. payload 1MiB와 각 결과 배열 4096개를 제한한다. 직접 replay의 시간 목표 진행은 현재 frontier tick이며 제한 틱에 도달했을 때만 완료 표시한다. 격파·hold 진행은 요구량에서 포화시키며 완료는 요구량 도달이다. 성공에는 시간 목표를 제외한 모든 목표 완료가 필요하다. 실패·quit을 K=N으로 바꾸지 않는다. `ReadWorld`는 전용 결과 문서를 반환하며 기존 `Read`/`ReplayVerification`에 월드 의미를 섞지 않는다.

새 `WorldRecoveryCodec` magic은 `SowSiege-world-recovery-v1`, schema i32 `1`이다. 길이로 경계가 나뉜 format9 header, 소비 catalog hash, 연속 command prefix, sequence/tick/state hash frontier, 월드 runtime bytes, 가신 runtime 존재/bytes를 운반한다. 전체 128MiB, header 2MiB, runtime 각 16MiB를 제한한다. **PR-A의 Decode 결과는 미검증 후보이며 Restore나 정산 증명이 아니다.** 실제 prefix 재실행, runtime bytes·나머지·최초 terminal 대조는 PR-B의 실행기와 함께 추가한다. 이를 구현하기 전 `InteractiveSession`은 명시적 월드 옵션을 거절한다. 월드 옵션을 버리고 기존 생존 전투로 실행하지 않는다.

opt-in [스키마](../../data/opt-in/schema/world-combat.schema.json)와 [예제](../../data/opt-in/world-combat/world-combat-v1.json)는 기존 data tree hash 밖에 둔다. 예제 수치·inputGraceMilliseconds·chunk 상한은 **[가설]**, 반복 문자 provenance hash는 **검사용 더미**이며 서버 발급/출시 설정이 아니다. 자동 모드 식별자를 표현할 수 있다는 것은 army-auto 산술 구현을 뜻하지 않는다. PR-C는 별도 규칙 입력과 손계산 fixture를 제공하며 직접 전투의 제한 틱을 자동 round 상한으로 재해석하지 않는다. 서버·Unity 연동과 C05 3:3은 별도 관문으로 유지한다.

## 13. PR-B 실행 구현 (v1, 2026-11-02, #656)

상태: **구현 작성 중, 실행 검증 NOT_RUN**. §12의 실행 거절은 PR-A의 역사 경계이며 이 단계는 명시적 `DirectPve` 입력에 한해 `InteractiveSession`을 연결한다. 자동 모드는 PR-C 이전에 계속 거절한다. Core 테스트·두 실제 대상·Unity·C05는 구현 사실과 별개 관문이다.

`WorldCombatState`는 발급 snapshot을 동결하고, `profileHash`를 실제 소비 catalog의 portable hash, `contentHash`를 입력 data hash와 대조한다. cohort의 enemy ID는 catalog에 존재해야 한다. 직접 실행의 전장은 소비 catalog map의 `[0,width] × [0,height]` 안에 전부 들어가고 catalog 영웅 시작점을 포함해야 한다. format9 codec은 전체 Int32 프로토콜 범위를 표현하지만, 기존 공간 쿼리의 지원 범위를 벗어나는 직접 입력은 생성 단계에서 거절한다. 직접 runtime v1의 `limitTicks`는 소비 `catalog.Tuning.DurationTicks` 이하여야 한다. 이 역시 codec의 Int32 표현 범위와 별개인 실행기 지원시간 제약이며, 상한 값은 기존 catalog 데이터에서 가져온다. 종료는 여전히 발급 world limit과 §4 순서를 따르며 더 긴 실행은 기존 장비/상태 타이머의 산술 안전성을 입증한 뒤 확장한다. 일반 wave ambient spawn·보스 이동·화면 밖 재활용 대신 발급 cohort만 생성하며 `(spawnTick, cohort ID, ordinal)` 순서로 안정 entity ID를 할당한다. 영웅의 기존 무기·성장·처치 사건은 같은 실제 적을 소비한다. 표식과 상비 슬롯은 일반 임시 `WaveGroup`과 별도 상태다.

가정: 직접 전투 산술 v1의 적은 발급된 속도·사거리·공격·방어·cooldown을 쓰며 최근접 살아 있는 대상을 정수 제곱 거리로 고른다. 동거리 우선순위는 영웅 → 병종/ordinal 순 상비 슬롯 → ID 순 표식이다. 상비 슬롯은 최근접 적, 동거리 entity ID 순으로 선택한다. 이동은 기존 정수 `MoveToward`, 전장은 입력 경계로 제한한다. 기본 피해는 `max(0, 공격-방어)`이고 표식의 방어는 입력 필드가 없으므로 공격 값을 그대로 받는다. 영웅 피해는 기존 Feel/estate 피해 처리·진단 사건을 재사용한다. 젖음은 소비 catalog의 기존 `WetSpeedDivisor`로 양수 속도를 줄이고, 기존 `StopUntil` 동안 직접 적의 이동/공격을 정지한다. 어떤 게임 계수도 새 Core 상수로 추가하지 않는다.

상비 슬롯은 체력이 0이 된 최초 사건을 영구 latch한다. 임시 모집·구출·음식 치유는 상비 슬롯에 접근하지 않으며 quit·영웅 패배도 K를 덮지 않는다. cohort 처치 ID를 한 번만 집계한 뒤, 실제 틱 frontier를 증가시키고 연속 hold 및 실패 → 성공 → timeout을 판정한다. terminal 뒤 모든 입력을 거절하고 별도 quit 호출도 기존 terminal을 바꾸지 않는다. `CaptureWorldCombat`은 목표/상비/표식 표시 계약, `GetWorldCombatResult`는 정산 전 검증용 결과 계약이다. 기존 `GetSummary`는 월드 모드를 거절해 목표 성공/quit를 legacy duration 결과로 오인하지 않게 한다.

효과는 기존 가신 적용 위치에서 월드 공통 capped 효과를 우선 선택한다. 영웅 Health/Attack/Movement/Growth/Experience와 임시 Allies, 상비 Allies/Movement의 적격성은 snapshot에 따른다. 가신 runtime은 provenance 대조용으로 보존하되 이중 적용하지 않는다. 월드 옵션·목표/슬롯/표식/나머지/terminal bytes는 월드 옵션이 있을 때만 state codec 말미에 쓴다. absent 경로에는 tag나 빈 필드가 없다.

`WorldReplayRunner.Verify`는 canonical format9 명령을 실제 세션으로 실행하고 checkpoint와 종료 레코드 전체를 대조한다. `WorldRecoveryCodec.Restore`는 완전한 prefix를 재실행한 뒤 sequence/tick/state hash 및 월드·가신 runtime bytes를 비교한다. checkpoint는 항상 해당 command prefix 직후 상태를 검증한다. 명시적 quit는 새 명령을 추가하지 않으므로 초기/최종 prefix checkpoint를 먼저 대조한 뒤 quit latch를 적용하고 `world-end-v1`의 종료 hash를 대조한다. 같은 frontier의 checkpoint에 quit 이후 hash를 제출하면 거절한다. 명시적 quit는 `world-end-v1`으로 검증하며, quit 완료 세션을 재개 가능한 recovery로 내보내지 않는다. 입력 grace/chunk 정책은 발급 snapshot에 그대로 고정하며 UTC 판정·무입력 확정·카드 기본 선택 기록은 §7대로 서버가 소유한다.

직접 모드 Stats 소비 표: cohort Health는 초기 적 체력, Attack은 적의 단발 피해, Defense는 영웅 무기·건물·임시 아군·상비 아군의 모든 적 피해에서 공통 차감, Speed는 정수 이동, Range는 공격 거리, CooldownTicks는 다음 공격 틱이다. 상비 Stats도 같은 여섯 필드를 각각 체력/공격/피해 방어/이동/사거리/cooldown에 사용한다. 영웅 자체의 체력·무기·이동은 snapshot의 영웅 ID와 고정 catalog에서 가져오고 공통 효과를 적용한다. 표식은 별도 Health/Radius 필드만 가지며 공격·방어·이동 Stats가 없다. 적의 원래 catalog Health/Attack/Speed를 cohort Stats 대신 쓰지 않는다. 킬 경험치·전리품·무기의 기존 판정/성장 primitive는 소비 catalog로 유지한다.

산술 실패 정책: snapshot의 합계·정렬·거리 제곱·정수 범위, 초기 상비/영웅 체력과 cooldown frontier는 실행 전에 검사한다. 동적 장비/효과 조합의 checked 연산이 실행 중 overflow하면 해당 검증은 실패하고 결과·정산 근거를 반환하지 않는다. 호출자는 예외가 난 세션의 부분 상태를 재사용하지 않고 마지막 검증된 prefix부터 다시 생성해야 한다. world building knockback은 long으로 더한 뒤 발급 경계로 제한하며, 기존 absent 경로 산술은 바꾸지 않는다. 형식에서 Int32 범위를 표현할 수 있다는 것은 임의의 catalog 조합이 안전한 전투 수치라는 승인이 아니다.

## PR-C 구현 계약 (#658, 2026-11-02)

[가설] [별도 자동 규칙·손계산 fixture](../../data/opt-in/army-auto/hand-fixtures-v1.json)와 [스키마](../../data/opt-in/schema/army-auto.schema.json)를 산술 구현 전에 제시한다. 출시 밸런스 승인이 아니다. 매 생존 슬롯은 round당 한 번 공격하며 피해는 `max(minDamage, floor(attack × matchup / 1000) - defense)`다. ArmyAuto 전용 raw 보정은 직접 전투 permille 효과와 달리 flat 가산 단위다. raw 보정은 영웅·가신·연구 출처별 정수 가산값을 모두 합한 후 능력별 데이터 cap을 한 번 적용한다. 상성 누락은 오류이고 기본 배율을 추측하지 않는다. 전장·목표 수비는 수비측 defense에 가산한다. 영웅은 해석된 병력 보정만 주고 독립 공격하지 않는다. 공격 대상은 낮은 데이터 우선순위 → 병종 ID ordinal 비교 → 슬롯 ordinal 순이다. 같은 round의 초과 피해는 다른 슬롯에 재분배하지 않는다.

[확정] `army-auto-v1`은 직접 전투 format9/WorldCombatSnapshot 운반 계약과 별개의 순수 계산 입력이다. seed와 RNG를 사용하지 않는다. 입력 배열을 복사·정규화하며 서버 발급 opaque contractHash를 그대로 반환하며 전체 입력과 규칙의 canonical SHA-256을 별도 inputHash/rulesHash로 계산해 stateHash에 바인딩한다. 해시는 인증이 아니며 서버는 authoritative 입력과 계약을 대조한다. 결과의 WorldCombatResult는 공격자 N/K이고 명시적 wrapper가 수비자 N/K와 양측 최종 슬롯 상태를 담는다. 빈 ordinal 목록인 병종도 N=0/K=0으로 남는다. 자동 목표는 모드별 데이터 정책의 `auto-objective-satisfied`이며 직접 hold tick을 만들지 않는다. 수비 영웅 가용성은 서버가 발급한 snapshot이고 비가용 영웅의 기여는 0이어야 한다. Core는 대체 영웅을 선택하지 않는다.

[가설] 살아 있는 공격자/빈 수비는 round0에 무수비 정책으로 판정한다. 빈 공격자는 양쪽이 비어도 실패한다. round 마지막 공격으로 적이 전멸하면 cap 판정보다 승리가 앞선다. 동시 전멸은 공격 실패이며 cap까지 양쪽이 살아도 실패한다. 서버 귀환 정산·보상·계정 변경은 포함하지 않는다.

## PR-D 서버 소비 진입점 (v1, 2026-11-03, #665)

상태: **구현 초안, 실행 검증 NOT_RUN**. [공개 API와 오류 계약](../review/world-combat-api-665.md)을 추가한다. `WorldCombatApi`는 발급 format9 헤더를 고정하고 입력 chunk 상한·서버 보관 헤더 bytes·승인 prefix를 검증한다. 기존 `WorldRecoveryCodec`과 `WorldReplayRunner`를 재사용하며 format1~8 및 기존 state codec을 수정하지 않는다. 신규 서버 도우미에서는 HostRewards 옵션 및 host/review 명령을 거절하고 기존 저수준 reader의 허용 범위는 유지한다.

§7의 입력 수신 시각·grace/highwater·revision 직렬화·무입력 확정 권한은 서버 소유다. Core는 서버가 결정한 targetTick까지 무입력 명령을 생성·재실행하는 도우미만 제공한다. `OrderVersion=1` 기본 카드 선택은 ordinal 최소 안정 ID이며 모든 선택을 반환 prefix에 포함한다. 이미 terminal이면 전진하지 않고 최초 결과를 보존한다. `inputGraceMilliseconds`와 chunk 상한을 코드 기본값으로 만들지 않는다. 결과 codec은 기존 world-end framing을 사용하며 decode만으로 정산을 승인하지 않는다.

자동 계수는 기존 army-auto 입력 데이터에 남고 병상·부상·영구 전사·보상·UTC는 Core에 추가하지 않는다. 서버는 저장한 승인 recovery만 넘기며 클라이언트 checksum을 승인 증명으로 사용하지 않는다. 검증 불명확 시 recovery-pending 및 잠금 유지는 서버 상태기계 책임이다. 새 Core head의 역사 호환·Unity·C05 관문과 사용자 병합 승인은 별도로 필요하다.

## 능동 영웅 효과 승인 후속 계약 (#674, 2026-11-03)

상태: **사용자 승인 (2026-11-03)**, 구현 없음. #675 계약을 승인했으며 능동 효과 구현은 내일 이 세션 병합 창에서 #673 병합 후 돌진부터 별도 PR로 착수한다. [능동 효과 설계 메모](../review/active-hero-effects-contract-674.md)에 기존 기능 재사용 조사, 돌진 변위·도발 우선권·능동 피해 감소 자체 cap·장판/둔화 인스턴스의 정수 단위·동시 상한·만료/사망 순서와 format9 확장안을 정리했다.

권고안은 기존 format9 envelope1/world-combat-v1 및 역사 absent 바이트를 고정하고 능동 옵션에만 envelope2/필수 hero-active-v1 블록·별도 runtime·recovery-v2를 사용하는 것이다. 구 reader는 새 버전을 거절하며 발급 영웅/스킬 참조·전체 헤더·승인 prefix·재실행 state를 대조한다. 상시 raw 합산→cap 1회와 army-auto-v1은 그대로다. 돌진 충돌·경로 피격 상세는 Core에 위임됐다. 기존 충돌·피격 함수를 재사용하는 틱 내 이동 선분 vs 벽·적 구간 검사를 첫 구현 PR 설계 절에 기록하고, 얇은 장애물 통과·타격 누락·중복 방지 경계 fixture로 고정한다. bounds clamp만을 완료 구현으로 간주하지 않는다.

새 효과 실행은 위 착수 순서에 따라 분할 Core PR에서 수행한다. 각 PR은 역사 바이트 불변, 두 DLL, Unity 및 C05 3:3 관문을 충족해야 한다. 이번 문서 변경은 runtime·밸런스·성능 검증이 아니다.

## 농로 돌파 첫 구현 (#678, 2026-11-04)

상태: **구현·검증 진행 중**. 사용자 위임에 따라 [돌진 구현 계약](../review/hero-dash-678.md)에 틱 내 이동 선분 vs 벽/적 몸체와 기존 공격 사거리의 구간 검사, 정수 최초 접촉·반올림·경계 탈출, target/cooldown/피해 사건 중복 방지, schema2/active runtime/recovery-v2를 구체화했다. 기존 format1~8 absent 및 format9 schema1 bytes는 고정한다. 도발·장판은 별도 PR이며 미지원 능력을 무효 효과로 발급하지 않는다.

2026-11-04 최신 사용자 지시에 따라 문서 #673/#675의 오늘 병합 창 확인과 돌진 구현을 병행한다. 문서 병합 순서는 월드맵 종료 후 #673→#675, 구현 PR의 측정 준비 마감은 22:00이다. 실행/Unity/C05 상태는 각 exact head의 PR 기록이 정본이며 이 절 자체는 통과 증거가 아니다.
