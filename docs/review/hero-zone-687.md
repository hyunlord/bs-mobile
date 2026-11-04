# 봉인의 서고 Core 구현 계약 (#687)

2026-11-04. 상태: **구현·검증 진행 중**. 승인된 [능동 효과 계약](active-hero-effects-contract-674.md)과 [GDD16 §10](../design/gdd/16-slg-heroes-proposal.md)을 장판/둔화 상태로 구체화한다. 실행·Unity·C05 결과는 PR의 exact head 기록을 따른다. 콘텐츠 수치는 계속 [가설]이며 실제 영웅 데이터·아트·동등 전투 예산 검증은 이 변경에 포함하지 않는다.

## 발급과 수치

`HeroActiveKind.ZoneSlow`와 `HeroZoneParameters`를 명시적으로 발급한다. 사거리·반경·양의 최소 속도는 기존 정수 월드 거리, 예고·지속·재사용은 30Hz 정수 tick, raw/cap은 permille다. 기존 Dash/Taunt 값에 Zone payload를 섞거나 필수 payload를 누락하면 발급을 거절한다. 영웅/스킬/시작 슬롯과 신뢰한 catalog canonical bytes 결속은 기존 경로를 재사용한다. 상시 영웅/가신/연구 효과에 능동 스킬 ID를 넣지 않으며 army-auto에는 발급하지 않는다.

같은 owner의 장판 정의는 동시 인스턴스 상한·둔화 cap·최소 양의 속도에 합의해야 한다. 정의 개수로 동시 저장량을 곱하지 않는다. 모든 효과의 합산 동시 인스턴스는 프로토콜 방어 상한4096, 도발 claim과 장판 source는 각각65536 이내이며 최악 직렬화 runtime 합계는1MiB 이하다. 숫자는 콘텐츠 밸런스 상한이 아닌 안전한 입력 크기 경계다. 각 정의의 실제 동시 개수·대상 수·범위·시간·둔화 수치는 발급 데이터가 정한다. 배열 생성 전에 합산 상한과 tick 덧셈 오버플로를 거절한다.

## 틱 순서와 중첩

1. 틱 시작에 `tick >= EndTick` 인스턴스와 source를 만료시킨다.
2. 영웅 이동 뒤 자동 발동한다. 살아 있는 사거리 안 적을 거리→안정 ID로 고르고 그 위치를 고정한다. 예고 중 대상이 이동/사망해도 중심은 바뀌지 않는다. 타깃 없음/동시 상한 도달은 cooldown을 소비하지 않는다. 예고 인스턴스도 동시 상한을 사용한다.
3. `ActiveStartTick = StartTick + TellTicks`, `EndTick = ActiveStartTick + DurationTicks`이며 활성 구간은 시작 포함·끝 제외다. 안정 정의 ID 순으로 발동하고 돌진·도발과 owner sequence를 공유한다.
4. cohort 생성 뒤 적 이동 전 전역 membership 단계를 틱당 한 번 실행한다. 각 장판의 유효 적을 거리→ID로 **매번 재선정**한다. 도발의 기존 claim 유지 방식과 구별된다.
5. 적 이동·넉백·사망 직후 무효 source를 즉시 제거한다. 같은 틱에 빈자리를 보충하지 않는다. 다음 전역 단계에서 다시 선택한다.
6. 동일 적의 모든 source를 보존하고 raw 최댓값만 한 번 cap한다. 약한 source가 남아 있으면 강한 source 종료 뒤 그것이 적용된다. source는 자기 장판 수명을 넘지 않는다. 원속도에서 wet과 능동 둔화를 각각 계산한 뒤 더 느린 값을 적용한다. 양의 최소 속도는 데이터이며 원속도 이하로 검증한다. 원속도0은 계속0이다.
7. owner 이동은 고정 장판을 지우지 않는다. owner 사망·quit·terminal은 즉시 장판과 source를 모두 지운다. hold-marker는 살아 있는 실제 적 위치만 사용하고 장판/source를 점유자로 추가하지 않는다.

현재 직접 월드 전투는 단일 제어 owner를 지원한다. owner ID와 공유 sequence를 기록하지만 복수 영웅 세션 지원으로 표현하지 않는다. 스폰이 발동 단계 뒤에 일어나므로 tick0 생성 적은 tick1부터 자동 발동 타깃이 된다.

## 바이트와 공개 API

기존 format9 envelope2의 필수 `hero-active-v1` 블록에 ZoneSlow tagged variant를 추가한다. payload 순서는 TargetRange, TellTicks, Radius, DurationTicks, CooldownTicks, MaximumInstances, MaximumTargets, SlowPermille, SlowCapPermille, MinimumPositiveSpeed, policyCount, `(EnemyId, Eligible)`다. bool은0/1만 허용한다. 기존 Dash/Taunt writer와 null omission을 유지하여 absent format1~8, format9 schema1, 기존 돌진·도발 계약 바이트를 보존한다.

장판이 있는 경우에만 내부 runtime `hero-active-runtime-v3`/schema3를 사용한다. 기존 돌진 필드 뒤 taunt-present bool과 존재하는 도발 block, 장판 block을 쓴다. 장판 block은 cooldown 정의 ID 순, 인스턴스 sequence 순, source target ID 순이다. `PortableStateCodec`의 기존 외부 섹션명 `hero-active-runtime-v1`은 바꾸지 않는다. 구 reader는 미지원 ZoneSlow kind를 명시적으로 거절한다. recovery-v2와 승인 prefix 재실행/바이트 비교를 재사용한다.

`InteractiveSession.CaptureHeroZones()`는 정의별 cooldown, 예고/활성 zone(owner/정의/sequence/시작/활성 시작/만료/중심), source(owner/zone sequence/target/raw/만료)를 반환한다. 기존 frame 형식을 바꾸지 않는다. UI가 이 정보를 그릴 수 있지만 Core 검증은 화면 품질을 증명하지 않는다.

| 경계 | 오류 계약 |
| --- | --- |
| 잘못된 typed union·수치·정책·공유 상한·catalog 불일치 | 발급/생성 `ArgumentException` |
| 절단·미지 kind·잘못된 bool·변조된 header | decode `InvalidDataException` |
| runtime/source 변조 | recovery 재실행 바이트 불일치 `InvalidDataException` |
| 승인 prefix 교체 | 기존 `WorldCombatApiException.Code = frontier-conflict` |
| no-input frontier·최종 결과 | 기존 `WorldCombatApi` 승인 이력 대조 및 오류 매핑 재사용 |

## 가정과 검증 경계

- 발동 anchor는 살아 있는 적이며 boss/면역 적도 포함한다. 실제 slow membership은 명시적 `Eligible=false` 및 boss를 제외한다. 이는 시각적 고정 장판의 발동과 피해 상태 적용을 분리한다.
- 현재 동적 면역 입력/state는 없다. 정책과 boss 배제만 구현하며 동적 면역 변경 지원을 주장하지 않는다.
- 병상·보상·UTC·무료 해금 조건은 서버/콘텐츠 소유다. Core는 유료 성장이나 무료 해금 경로를 추가하지 않는다. 개척 기사 기본 콘텐츠 완주 및 같은 성장 단계 총 전투 예산 원칙은 그대로다.
- 경계 fixture는 tell/expiry, 진입/이탈/재선정, 약한 source 잔존, cap/min/wet, 사망/quit/hold, 무입력 frontier/복구, malformed union, 합산 저장량 거절을 포함한다. 이전 실제 도발 DLL에서 생성한25개 raw 파일을 고정했으며 후보에서 기대값을 재생성하지 않는다.
- C05 무상한 기준3회:후보3회 통과 전 병합하지 않는다. 기존 absent C05와 신규 활성 장판의 대표 장면 비용을 구별한다.
