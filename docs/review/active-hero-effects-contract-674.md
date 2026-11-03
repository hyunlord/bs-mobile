# 능동 영웅 효과 계약 설계 메모 — ADR0083 승인 후속 계약

- 날짜: 2026-11-03 / 추적: [#674](https://github.com/hyunlord/bs-mobile/issues/674) / 상태: **승인 (2026-11-03), 구현 없음**.
- 제품 요구사항: [GDD16 §10 r2](../design/gdd/16-slg-heroes-proposal.md) 사용자 승인. 전투 수치는 계속 [가설]이며 이 메모는 수치를 확정하지 않는다.
- 소스 확인 기준: `feat/665-world-combat-api` @ `709869e654df948bec2a1a158a190244cc9ec6ce`. 아래 경로는 `core/src/SowSiege.Core/` 기준이다. 설계 메모 PR의 기준 가지는 승인 GDD 문서 PR #673이며, 실행 소스는 이 고정 SHA로 별도 확인했다.
- 승인 적용 범위: 명시적으로 새 능동 효과 계약을 포함한 직접 월드 PvE format9만. 기존 자유 PvE·army-auto-v1에는 적용하지 않는다. 자유 PvE 횟수로 얻는 해금은 서버 정책이며 자유 PvE 실행 규칙을 변경하지 않는다.
- 이 문서의 자료형·필드·틱 순서는 **승인된 설계**다. 기존 구현으로 표현하지 않는다. Core 코드·실행 데이터·스키마 변경은 별도 구현 PR과 관문을 요구한다.

## 1. 경계와 불변 조건

영웅별 기본 공격 하나와 자동 고유 능력 하나를 데이터로 구성한다. 기본 공격은 기존 시작 공격 슬롯을 교체하며 무료 추가 무기가 아니다. 영웅 ID를 Core 분기로 넣지 않고 범용 능력 종류와 설정으로 처리한다. 수동 버튼·새 입력 명령은 추가하지 않는다.

패시브 영웅·가신·연구 효과는 기존 raw 합산 → 기존 cap 한 번을 유지한다. 능동 피해 감소와 능동 slow는 별도 규칙 및 자체 cap을 갖는다. 유료 성장·해금도 능동 cap, 재사용 대기시간, 플레이 요구조건을 우회하지 못한다. 계정·UTC·해금 횟수·병상 배분은 서버에 남긴다. Core는 서버가 확정한 정수 계약만 소비한다.

게임 거리·시간·속도·대상 수·배율·동시 instance 상한은 데이터 값이다. 형식 버전·30Hz·정수 배율 분모·바이트 읽기 안전 한계만 프로토콜 상수로 둔다. 게임 밸런스 cap과 역직렬화 안전 한계를 구분한다.

`hold-marker`는 살아 있는 영웅의 실제 위치와 살아 있는 적 점유를 사용한다. 도발·장판 자체는 점유자가 아니며 slow/taunt가 걸린 적도 살아 있는 적이다. 실패 → 성공 → 시간 초과 우선순위는 변경하지 않는다.

## 2. 기존 구현과 재사용 범위

| 근거 파일·심볼 | 확인된 동작 | 설계에서의 사용/제약 |
| --- | --- | --- |
| `WorldState.cs:10–34`, `Position.DistanceSquared`, `MoveToward` | 거리 제곱은 long; 이동은 max(abs(dx), abs(dy)) 정규화와 정수 나눗셈 | 거리 정렬 재사용. MoveToward를 유클리드 정속 돌진으로 오인하지 않는다. |
| `Simulation.cs:113–127`, `Advance` | 영웅 이동 → Wave tick → progression → tick 증가 → 월드 목표 판정 | 신규 능동 단계를 명시적으로 삽입하되 absent 경로 순서는 보존한다. |
| `Simulation.cs:160–172`, `MoveManual` | 입력 이동·패시브 이동 효과 적용 후 월드 경계 clamp | 돌진은 일반 변위 대체. 이 경로에는 검증된 장애물 swept 충돌 해결기가 없다. |
| `WaveRuntimeSystem.cs:86–168`, `Tick` | 적 행동 후 공격 준비/발사·기존 투사체·상비군·사망 정리 | 시작 공격 발사 억제 훅만 추가; 기존 투사체·상비군을 함께 멈추지 않는다. |
| `WaveEnemySystem.cs:184–196`, `Lured` | 유인 중 `Advance`를 건너뛰며 이동 | 도발 대체로 직접 재사용 불가. 기존 확정 공격/예고 보존 요구와 다르다. |
| `WaveEnemySystem.cs:198–388`, `Advance`; `Tell`/`Recover` | 공격 예고·돌진·회복·타깃·접근 이동을 소유 | 신규 도발은 접근 단계 타깃/정지 반경만 바꾼다. |
| `WorldCombatState.cs:87–131`, `TickEnemies` | 월드 적은 별도 cohort 타깃 선택과 이동/공격 경로 | 직접 월드의 실제 적용 지점. 일반 Wave 훅만으로 적용되었다고 판단하면 안 된다. |
| `WaveEnemySystem.cs:461–479`, `WorldMovementSpeed`, `DamageWorldHero`, `Hurt` | wet 속도, 공통 영웅 피해·기존 면역·사망 처리 | 속도 감쇠/피해 감소의 단일 적용 위치 후보. 기존 면역을 돌진 무적으로 확대하지 않는다. |
| `WaveRuntimeState.cs:25–49`, `WaveLure`, `WaveEnemyAction` | lure 위치/만료, 단일 WetUntil·StopUntil·공격 phase 보유 | 소유자별 slow를 WetUntil 하나에 얹지 않는다. 별도 정규화 instance 상태 필요. |
| `WorldCombatState.cs:218–229`, `Encode` | `world-runtime-v1`에 목표·cohort·상비군·효과 상태 인코딩 | 기존 runtime 바이트 고정, 신규 능동 상태 별도 버전 블록. |
| `PortableStateCodec.cs:812–820` | 월드 옵션 있을 때만 월드 상태 블록 기록 | absent에 새 0 필드도 쓰지 않는다. |
| `ReplayCodec.World.cs:27–85`, `WriteWorldHeader`, `ReadWorldHeader` | format9 월드 envelope schema1, 다른 schema 거절, 길이 제한 world 블록 | schema1 바이트 고정; 새 schema2 명시적 opt-in 후보. |

`WaveEnemySystem.Tick`은 월드 전투에서 `battle.TickEnemies` 호출 후 early return하므로 기존 일반 유인·detour·slow 관련 경로가 월드에도 적용된다고 가정하면 안 된다. 위 월드 전용 이동/피해 지점에 실제로 연결하는 검증이 필요하다. 기존 투사체/공격 lifecycle은 재사용하되 새 기본 공격의 형상·속도·관통 수를 현재 데이터 operation으로 모두 표현할 수 있는지는 구현 전에 별도 확인한다. 시작 슬롯과 진화 대체 공격의 관계도 현 영웅 JSON만으로 확정하지 않는다.

## 3. 발급 계약과 정수 단위

제안하는 `hero-active-v1` 블록은 정의 ID/버전, 콘텐츠 해시, 시작 공격 슬롯 바인딩, 범용 능력 종류, 모든 정수 파라미터, 자기 상한 및 동시 수를 포함한다. 수치 변경도 발급 계약·재생 해시에 반영한다. 결과에 사용한 정의를 서버가 나중에 최신 데이터로 바꾸어 해석하지 않는다.

- Core 좌표 단위는 기존 정수 좌표다. GDD의 U→정수 좌표 배율은 콘텐츠 데이터에 둔다. 로더가 반경·속도·누적 거리를 정수로 확정하며 부동소수점은 실행 계약에 들어오지 않는다.
- duration/cooldown/preparation은 틱, 배율은 permille, 거리 제곱은 checked long이다. 양수/영 허용 필드를 구별하고 곱셈·누적·제곱 overflow를 발급 전에 거절한다.
- 정렬은 거리 제곱 → 안정 entity ID. instance는 `(ownerId, activationSequence)`이며 활성화 순서는 `(startTick, ownerId, activationSequence)`로 완전 정렬한다. sequence overflow를 거절하고 재사용하지 않는다.
- 게임 동시 수는 데이터 검증 대상이다. 직렬화 count 최대치·중복 ID·잘못된 순서·범위 밖 좌표·알 수 없는 종류는 별도 엄격한 형식 오류다.

## 4. 돌진: 변위·충돌·준비

발동 시 입력 방향을 고정하고 일반 이동 변위를 대체한다. 패시브 속도를 돌진 속도에 다시 곱하지 않는다. 무적·피해 감소·접촉 피해를 주지 않는다. 이동 입력 해제는 다음 틱의 돌진 이동 전에 종료한다. 충돌 종료도 소비한 cooldown을 돌려주지 않는다.

정수 이동 제안은 기존 입력과 같은 max-axis 방향 정규화를 명시적으로 채택하고, 축별 나눗셈 잔여를 instance에 누적하는 방식이다. 매틱 truncation 손실을 보정하며 잔여도 상태 codec에 포함한다. 이 경우 데이터 속도/총 이동 거리는 **max-axis 거리**이고 대각선 유클리드 거리는 더 길어진다. 기존 이동과 같은 metric을 재사용하는 권고안이다. GDD의 U는 아직 정수 변환 전 설계 단위이므로 이 해석을 계약 승인 항목으로 명시한다. 대각선 이득을 포함해 기사 대비 예산을 비교하며, 방향 무관 유클리드 길이로 바꾸려면 정수 sqrt와 오차 한계를 별도 계약 변경으로 검토한다.

최대 지속 틱과 최대 누적 이동 거리 중 먼저 도달한 조건에서 끝난다. 마지막 틱은 남은 거리에 맞춰 변위를 줄인다. 벽을 넘지 않도록 시작점→제안 끝점의 swept 충돌을 수행하고 최초 접촉점까지 이동 후 종료한다. 단순 끝점 clamp만으로 좁은 벽 통과 방지를 주장하지 않는다. 승인 GDD16은 이동 경로의 타격도 건너뛰지 않을 것을 요구한다. 경로와 접촉/공격 판정 형상의 교차, 틱 안 공격 사건의 유효 시점, 충돌·피격 동률 순서, 같은 공격의 경로/끝점 중복 피격 방지가 함께 필요하다. 이 판정 계약은 아래 충돌 정본 보완 범위에 포함하며 끝점에서 기존 Hurt만 실행하는 것으로 완료 처리하지 않는다.

현재 확인된 재사용은 월드 bounds clamp뿐이다. 장애물 형상·영웅 충돌 반경·통행 차단 데이터의 정본 및 정수 최초 접촉 계산은 첫 구현 PR에서 Core가 확정한다(§9 사용자 위임). 충돌 서비스와 도형 계약 없이 충돌 보장을 구현 완료로 표시하지 않는다. 동률 접촉은 장애물 안정 ID로 해결한다.

준비는 일반 이동의 실제 위치 변화가 연속된 틱만 센다. 정지·막힘·발동에서 초기화하며 cooldown/돌진 중에는 누적하지 않는다. 준비 완료 후 적 조건 미충족 상태에서 실제 이동을 계속하면 준비 완료를 유지한다. 신규 기본 공격 발사만 억제하고 공격 시계는 진행한다. 종료 후 밀린 공격을 일괄 발사하지 않는다. 시작 슬롯의 진화 후 대체 공격도 같은 슬롯 계보로 추적해 억제하고, 다른 슬롯의 자동 공격/투사체는 억제하지 않는 안을 권고한다. 슬롯 계보를 확실히 연결할 수 없는 조합은 발급 시 거절한다.

## 5. 도발과 방패

준비는 연속 실제 정지 틱이다. 이동·발동에서 초기화하고 cooldown 중 누적하지 않는다. 발동 중심은 고정한다. 이동불가·보스·면역 적을 제외하고 거리→ID로 대상 상한까지 선택한다. 기존 유효 대상을 유지하고 빈자리만 같은 순서로 채운다. 이탈·사망·면역 획득 즉시 그 instance의 주장만 해제한다.

동일 적을 여러 도발이 잡으면 가장 오래된 유효 instance가 우선한다. 같은 시작 틱에는 owner ID, 이어 instance sequence를 사용한다. 패배한 유효 주장도 유지해 승자 종료 시 즉시 다음 주장으로 전환하며, 각 instance의 대상 수에는 그 instance가 보유한 주장 수를 센다. 이 수 계산 방식은 **승인 설계**이며 실제 지배 대상 수만 세는 방식과 혼용하지 않는다.

도발은 공격 사이의 접근 타깃만 영웅으로 바꾸고 데이터 정지 반경까지 자기 속도/경로/충돌로 이동시킨다. 원거리 적도 이 접근 반경을 사용한다. 이미 확정된 공격의 표적·예고·공격 시점·사거리·주기는 보존한다. 새 접근이 기존 회복/정지/충돌 제약을 무시하지 않는다. 기존 `Lured` 우회는 사용하지 않는다.

방패 방향은 발동 시 가장 가까운 도발 가능 적의 방향, 동위치라면 현재 조준 방향으로 고정한다. 유효한 다음 양수 피격 한 번만 해당 방패를 소비한다. 기존 면역으로 0이 된 타격은 소비하지 않는다. 범위 밖 방향 타격도 소비하지 않는다. 모든 피해 원인의 입사 방향을 산출할 수 있도록 공격 origin을 넘겨야 하며, origin 없는/동위치 피해는 방향을 추측하지 않고 방향 방패 부적격(원래 피해 적용, charge 미소비)으로 처리하는 안을 권고한다. 이는 모든 피해를 막는 방패로 확대하지 않기 위한 경계다.

여러 능동 DR이 같은 피격에 적용되면 적용 가능한 raw permille을 먼저 합산하고 능동 자체 cap을 한 번 적용한다. 기존 패시브 피해 계산 후 능동 감소를 한 번 적용하는 순서를 제안한다. 순차 shield 곱연산은 하지 않는다. 해당 피격에 기여한 일회 방패는 모두 소비한다는 제안이며, 소비 순서를 달리하려면 별도 결정이 필요하다. 순서는 기존 면역 거절 → 기존 피해 보정(어둠 포함) → 능동 DR → HP 차감이다. 입력 피해 D가 양수일 때 남은 피해는 floor(D × (1000 - cappedDR) / 1000), 0을 허용하고 계산 전 양수였던 유효 타격은 charge를 소비하는 안을 권고한다. 감소가 0 피해를 만들면 새 피격 면역/넉백을 부여하지 않으며 기존 관찰 사건에 실제 감소량을 구별한다. 정수 나눗셈의 작은 피해 편향은 손계산/밸런스 관문에 포함한다.

영웅 실제 이동에는 입력뿐 아니라 넉백도 포함한다. 현재 Contact/Hurt 이후 넉백 경로가 있으므로 이동 단계에서만 취소를 검사하면 안 된다. 각 실제 위치 변경 직후 도발/방패를 취소해 같은 틱 뒤쪽 사건에 남지 않게 한다.

## 6. 지속 장판과 slow

발동 시 선택한 적 위치를 고정하고 예고 틱을 거쳐 장판을 연다. 예고 중 표적이 이동/사망해도 위치를 추적하지 않는다. 매틱 장판 중심 거리→ID로 적을 다시 선택한다. 동시 대상 수는 데이터 상한이다. 제외·이탈·면역·장판 종료 즉시 해당 zone instance의 slow만 해제하고 다른 소유자의 효과는 유지한다.

slow source 상태는 `(zoneInstance, targetId, magnitude, validUntilTick)`를 정규 순서로 보존한다. 같은 종류는 가장 강한 유효 값 하나만 적용하며 합산·연속 곱하지 않는다. 동률 강도의 표시 만료는 유효 source들의 최대 만료지만 source들을 합쳐 버리지는 않는다. 약한 source도 개별 만료를 유지해 강한 source가 없어졌을 때 복원한다. zone membership 취소는 만료 연장보다 우선한다.

선택된 slow raw에 능동 자체 cap을 한 번 적용한다. 기존 wet와 능동 slow를 순차 곱하지 않는 안을 권고한다. 같은 원래 이동속도에서 기존 wet 결과와 능동 slow 결과를 각각 계산해 더 느린 하나만 채택한다. 능동 결과는 max(데이터 최소 양수 속도, floor(원속도 × (1000 - cappedSlow) / 1000))이며 원속도0은0이다. 최소속도는 원속도보다 클 수 없다. wet의 기존 정수 제수/최소1 연산은 그대로 재사용하고 능동 상한으로 wet를 다시 제한하지 않는다. 이 후보는 속도 감소 계열의 중복 곱연산을 막으며, 다른 이동 제어 종류는 자동 합성하지 않고 별도 승인한다. 공격 주기·예고 시간은 늦추지 않는다.

장판/도발/돌진의 owner별 동시 수는 GDD의 데이터 상한을 따른다. 상한 도달 시 기존 instance 교체 대신 신규 발동을 보류하고 cooldown을 소비하지 않는 방식을 제안한다. 상한 보류 시 준비는 유지하되 돌진의 일반 이동 중단, 도발의 정지 중단 등 해당 준비 조건을 잃으면 초기화한다. 장판은 별도 준비 카운터가 없고 다음 틱에 다시 조건을 검사한다. 현재 가설 cooldown이 지속보다 길다는 이유로 상한 처리 규칙을 생략하지 않는다.

## 7. 결정론적 틱 경계와 수명

신규 opt-in 경로에 한해서 다음 순서를 제안한다. 기존 absent 순서는 그대로 둔다.

1. 틱 시작에 `[startTick, endTick)` 만료를 제거하고 사망한 owner의 준비·예고·효과·source를 제거한다.
2. 입력 해제/이동 취소 검사 후 일반 이동 또는 돌진을 해결한다. 실제 위치 변화로 취소해야 하는 효과를 즉시 해제한다.
3. 실제 이동/정지 준비와 cooldown 조건을 평가하고 자동 발동한다. cooldown은 발동 틱부터 계산한다. 도발은 발동 틱이 첫 지속 틱이며 장판은 activeStart=start+tellTicks다. 돌진은 이 단계에서 예약하고 첫 실제 변위는 다음 틱 단계2부터다(motionStart=start+1, motionEnd=motionStart+duration). 따라서 첫 이동 전 준비 단계가 지속을 한 틱 소모하지 않는다. 발동 틱부터 시작하는 재사용과 실제 이동 구간을 구별한다.
4. 예고 완료 zone을 열고 현재 위치/생존/면역으로 도발·장판 membership을 갱신한다. 적 이동에 쓸 slow/도발 우선권을 계산한다.
5. 기존 적 이동/공격을 수행한다. 각 이동 후 membership 이탈을 즉시 반영한다. 이동에 영향을 주는 재진입/신규 선택은 다음 틱 단계 4부터 적용한다. 이 단계 비대칭은 순환 이동 계산을 피하기 위한 제안이다.
6. 기본 공격 발사·기존 투사체·도구·상비군·사망 처리를 기존 순서로 수행한다. 피해로 owner가 사망하면 즉시 자체 효과를 지워 이후 사건에 남지 않게 한다. 기존 발사체 lifecycle은 기존 규칙을 따른다.
7. 적/owner 사망에 따른 남은 주장을 정리하고 tick 증가 후 기존 월드 목표·terminal 판정을 수행한다. terminal에는 다음 전투로 전달할 능동 상태를 만들지 않는다.

장판 활성화 틱, 만료 틱, 이동 후 선택 갱신은 fixture로 고정해야 한다. 특히 “매틱 동시 대상 선택”은 단계 4에서 한 번 전역 선택하고 이동 후 이탈은 즉시 해제하는 의미라는 제안이다. 이동 후 같은 틱 빈자리 재충원은 하지 않는 안을 권고한다. GDD의 매틱 선택을 이 순서로 구체화하며 다음 틱까지 늦어지는 진입을 손계산 fixture로 공개한다.

## 8. format9 확장과 복구

기존 format9 envelope schema1 및 `world-combat-v1`·`world-runtime-v1`의 바이트를 수정하지 않는다. 새 능동 옵션이 있는 경우에만 **format9 envelope schema2**를 기록한다. 기존 공통 헤더/월드 블록을 같은 순서로 쓴 뒤 길이 제한 `hero-active-v1` 블록을 필수로 추가하는 방안을 제안한다. `world-combat-v1`의 schema1/flags1은 그대로이며 능동 의미를 flags에 숨기지 않는다. 기존 월드 옵션 계약을 재정의할 필요가 없고 독립 필수 블록으로 기능을 식별할 수 있으므로 이 초안은 world-combat-v2 대신 envelope2를 선택한다. 새 reader는 schema1/2만 알고, 기존 reader는 schema2를 헤더 단계에서 거절한다. 알 수 없는 필드/flags/종류/버전도 조용히 무시하지 않는다.

활성 설정이 없는 기존9는 계속 schema1이며 bytes·state bytes·결과 해시가 동일해야 한다. schema2의 빈/불완전 블록을 schema1이나 무효 옵션으로 자동 정규화하지 않는다. format1–8에는 신규 헤더/state 필드를 쓰지 않는다. 그 형식에 능동 설정을 붙이는 요청은 거절한다.

신규 active runtime 블록은 준비 연속 카운터, cooldown ready tick, instance sequence, 고정 방향/중심, 정수 변위 잔여, 시작/종료 tick, 방패 charge, 도발 주장, zone source를 정규 순서로 인코딩한다. 기존 world runtime 바로 뒤에 신규 명시적 태그/버전 블록을 붙이는 방안이며 hash 입력에도 포함한다. 설정만 기록하고 runtime을 생략하면 중간 복구 검증이 불충분하다.

복구는 기존 `SowSiege-world-recovery-v1` magic/schema1 바이트를 보존하고 능동 옵션에는 **새 `SowSiege-world-recovery-v2` magic/schema2** 및 필수 active runtime 비교 블록을 제안한다. 구 reader는 magic 단계에서 거절한다. 새 reader도 magic/schema/헤더 옵션 불일치를 거절한다. accepted prefix와 발급 헤더를 검증하고 원 입력으로 재실행한 runtime/state hash 전체를 비교한다. no-input frontier는 기존 0 입력으로 전진하므로 돌진 해제와 자동 능력 조건도 같은 규칙으로 처리한다. UTC 유예는 서버만 판단한다.

기존 `world-end-v1`의 승패/손실 및 결과 필드 의미는 유지하는 안이다. 신규 상태를 포함한 state hash와 발급 헤더 결속을 `VerifyResult` fixture로 검증한다. 구 실행기는 schema2 발급 헤더를 거절하므로 end만 따로 decode한 성공을 정산 증명으로 사용할 수 없다. 결과 JSON만으로 헤더의 새 능력 버전을 추론하지 않는다.

### 8.1 canonical 블록과 실패 정책 제안

schema2는 schema1과 같은 공통 옵션/가신/world-combat-v1 payload를 쓴 뒤 필수 i32 길이와 active bytes를 덧붙인다. envelope 버전 숫자 자체만2이며 schema1 encoder는 수정하지 않는다. active bytes 순서는 tag(hero-active-v1) → schema i32(1) → flags u32(1) → rules/content SHA-256 식별자 → owner/hero ID → 시작 슬롯 ID/공격 정의 ID → 능력 정의 배열 → 마지막 SHA-256 본문 digest다. 정의는 안정 definition ID ordinal 순, 중복/역순 거절이다. 각 정의는 ID·버전·kind·명시적 대상/면역 정책·고정 스키마의 정수 parameter record 순이다. 종류별 record는 준비/위협거리/이동속도/총거리/지속/재사용, 도발반경/정지반경/동시대상/방패방향원뿔/DR, 예고/장판반경/지속/동시대상/slow 등 해당 종류에 필요한 값만 갖고 자유 key-value map을 쓰지 않는다. 적용 파라미터 전체와 cap/최소속도/동시상한을 해시에 결속한다.

두 블록의 교차 일치도 발급·decode·recovery마다 검사한다. active의 hero ID는 기존 world snapshot Hero.Id 및 공통 Run.HeroId와 같아야 한다. owner는 해당 영웅 instance의 안정 ID와 같으며 active 정의 ID/버전/콘텐츠 hash는 발급 catalog에서 능동으로 분류된 snapshot skill 참조 목록과 정확히 대응해야 한다. 그 목록의 누락·추가·중복·서로 다른 버전/해시를 거절한다. 기존 패시브 참조는 별도로 검증하며 능동 정의로 중복 실행하지 않는다. 시작 공격 바인딩도 발급 catalog의 해당 영웅 슬롯/정의 및 진화 계보와 일치해야 하며 블록 자체 checksum이 맞는다는 이유로 통과시키지 않는다. 기존 WorldHeroSkill은 ID/Version/ContentHash를 이미 갖는다. 능동/패시브 구분은 동일 hash의 발급 정의 종류로 해석하고 임의 문자열 접두사로 추정하지 않는다.

수치는 signed i32, tick/좌표도 기존 i32 범위, activationSequence는 i64다. 산술 중간값은 checked i64이며 overflow를 거절한다. 문자열은 기존 길이+strict UTF8 형식, 블록과 collection 안전 한계는 기존 WorldCombatCodec의1MiB/4096을 상한으로 재사용한다. 실제 게임 허용 수는 데이터가 더 작게 제한한다. SHA는 인증이 아니며 서버 저장 계약 bytes와 비교한다.

state hash의 기존 world-runtime-v1 뒤에 태그 hero-active-runtime-v1/schema1/정규화 runtime을 schema2일 때만 기록한다. recovery는 magic SowSiege-world-recovery-v2/schema2와 필수 active runtime 길이 블록으로 분리하고 header도 schema2여야 한다. schema1/header1은 기존 recovery-v1만 허용한다. 새 설정과 runtime hash·recovery bytes를 서로 다른 버전으로 조합하면 거절한다. runtime의 instance/source 정렬과 카운터/잔여를 모두 검증하되 bytes를 직접 신뢰해 세션을 복원하지 않고 원 prefix 재실행과 비교한다.

새 형식 미지원·정의 hash 불일치·상한/overflow·부정합은 승인 실패다. 현재 공개 API의 invalid-contract/contract-mismatch/invalid-prefix/invalid-result 분류를 재사용하고 raw codec은 InvalidDataException으로 거절하는 방향이다. 새 공개 오류 코드를 추가한다면 같은 PR의 API 가이드에 명시한다. 미지원 능력을 무효 효과나 기본 기사로 대체하지 않는다.

## 9. 승인 및 구현 관문

2026-11-03 사용자가 승인한 계약 묶음은 다음과 같다: 기존 max-axis 이동 metric, 진화 포함 시작 슬롯 억제, origin 불명/동위치 방패 부적격, DR 잔여 피해 floor·0 허용·기여 charge 동시 소비, wet/slow의 더 느린 결과 하나, 도발 유효 주장 수 상한, 틱당 한 번 전역 membership 선택.

**돌진 충돌·경로 피격 상세는 사용자 위임에 따라 Core가 결정한다.** 기존 충돌·피격 판정 함수를 재사용한 틱 내 구간 검사(이동 선분 vs 벽·적 판정)를 첫 구현 PR의 설계 절에 명시한다. 기존 bounds clamp만으로 swept 충돌 해결기가 있다고 간주하지 않으며, 없는 구간 교차 계산은 기존 형상·피격 규칙을 보존하는 정수 계산으로 보강한다. 장애물/행위자 반경·안정 ID·형상·콘텐츠 hash, 최초 접촉점과 사건 시점/동률 순서, 경로/끝점 중복 피격 방지를 그 PR에서 확정한다. 시각적 지형을 통행 불가 데이터로 추정하지 않는다.

얇은 장애물을 한 틱 변위가 가로지르는 경우, 양 끝점 밖의 적/유효 공격 형상을 이동 선분이 통과하는 경우, 접선·끝점·벽/적 동률·경로/끝점 중복을 경계 fixture로 고정한다. 얇은 장애물 통과와 타격 누락이 없음을 두 Core DLL 및 재실행/복구에서 확인한다. 구간 검사와 fixture가 없는 bounds clamp·끝점 피격만으로 완료를 주장하지 않는다. 추가 사용자 결정을 기다리는 항목이 아니라 첫 구현 PR에서 Core가 설계·검증할 책임이다.

착수 순서: 오늘(2026-11-03)은 구현하지 않는다. 내일 이 세션의 병합 창에서 #673 병합 후 첫 능동 효과 구현 PR을 돌진부터 시작하고, 다른 능력은 하나씩 분리한다.

구현 검증은 각 능력의 발동/중단/만료/사망 경계 및 동률 ID, 여러 owner, 겹치는 강·약 slow, 다중 DR cap 한 번, 방향/충돌/overflow, no-input frontier, prefix 변조 거절, 중간 복구를 손계산 fixture로 포함한다. frame별 runtime과 두 Core DLL 결과를 비교한다. 피해 없는 돌진·공격 중 도발·hold-marker·army-auto 미적용도 별도 검증한다.

호환 관문은 기존 format1–8 absent 헤더/리플레이/state/golden/crowd-411과 기존 format9 schema1 바이트 불변이다. 변경이 감지되면 멈추고 보고한다. 신규 schema2를 구 reader가 확실히 거절하는 fixture를 둔다. Core 구현 PR에는 기존 Unity 네 묶음·보충 두 건·crowd-411 및 exact-head C05 3:3을 별도로 요구한다. 이 설계 문서는 실행/성능 통과 증거가 아니다.

조사 범위: 위 exact git ref의 정적 조회. 신규 효과 테스트·빌드·Unity·C05는 NOT_RUN. 구현된 기능이나 성능으로 보고하지 않는다.

## 10. 승인된 후속 구현 순서

| 순서 | 범위 | 해당 PR 완료 조건 |
| --- | --- | --- |
| 첫 PR: 돌진과 필수 계약/codec | opt-in 정의·schema2·active state/recovery, 틱 내 선분 충돌/경로 피격, 시작 슬롯 억제·정수 이동·준비/수명 | 역사 바이트·구 reader 거절·교차 참조, 얇은 장애물/타격 누락/중복 경계 fixture, 방향·거리·중단·사망·prefix 복구 및 두 DLL 비교. 미구현 능력 발급은 거절 |
| 도발/DR | 타깃 우선권·정지반경·중복 주장·방향별 단발 DR | 확정 공격 보존·다중 사건·넉백 즉시 해제·cap/rounding/charge fixture |
| 장판/slow 및 합성 | 예고·동시 membership·강약source·wet 경쟁·세 능력 통합 | 범위 이탈/사망·틱 경계·상한·hold-marker·각 영웅 동일 예산 플레이 비교 계획 |

각 Core PR은 exact-head 역사 호환, Unity 네 묶음/보충/crowd-411, C05 3:3이 필요하다. 기능이 일부만 준비된 상태에서 서버가 전체 영웅 계약을 발급하지 않는다. 승인된 착수 순서와 관문을 지키며 실행 증거 없는 측정 준비 주장·병합은 하지 않는다. 이번 문서 전용 PR에는 Core 실행이나 C05를 대신하는 PASS를 기록하지 않는다.

정적 확인 원본: [월드 cohort AI](https://github.com/hyunlord/bs-mobile/blob/709869e654df948bec2a1a158a190244cc9ec6ce/core/src/SowSiege.Core/WorldCombatState.cs), [일반 AI·wet·Hurt](https://github.com/hyunlord/bs-mobile/blob/709869e654df948bec2a1a158a190244cc9ec6ce/core/src/SowSiege.Core/WaveEnemySystem.cs), [format9 envelope](https://github.com/hyunlord/bs-mobile/blob/709869e654df948bec2a1a158a190244cc9ec6ce/core/src/SowSiege.Core/ReplayCodec.World.cs), [월드 recovery](https://github.com/hyunlord/bs-mobile/blob/709869e654df948bec2a1a158a190244cc9ec6ce/core/src/SowSiege.Core/WorldRecoveryCodec.cs), [영웅/skill 참조](https://github.com/hyunlord/bs-mobile/blob/709869e654df948bec2a1a158a190244cc9ec6ce/core/src/SowSiege.Core/WorldCombatContract.cs).
