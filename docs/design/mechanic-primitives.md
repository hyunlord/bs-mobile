# v1.1 공통 동작 문법과 실행 증거 경계

확인일: 2026-10-10 · 설계 전용 문법 `primitiveContract.version: 1.1.0`. 기존 Core·Unity·실행 콘텐츠·프로필은 변경하지 않는다.

## 구성

각 레코드는 `primitives: string[]`, `params: { [unitId]: object }`, `bespoke: [{ id, reason }]`를 가진다. primitive 집합과 params 키 집합은 정확히 같아야 한다. 레지스트리의 각 `paramSchema`는 필수 필드와 enum/const/참조 형태를 명시하며 `additionalProperties: false`다. 문장을 불투명 params로 넣지 않는다. 콘텐츠 ID는 입력·출처 참조에만 사용하고 동작 ID는 `unit:` 의미 이름을 사용한다. 수치 미정은 허용된 `null`만 사용하며 게임플레이 수치를 이 설계 문법으로 확정하지 않는다.

레코드의 공통 부분만 primitive로 표현하며 현재 문법이 표현하지 못하는 결합 상태·순서는 bespoke 이유로 별도 표시한다. `bespoke: []`도 코드 구현 또는 의미 검증 완료를 뜻하지 않는다. 새 primitive를 추가할 때는 재사용되는 의미·엄격한 스키마·실제 적용 레코드를 함께 검토한다.

`completion-ledger.channels`는 이번 문법에서 `xp`만 허용한다. 지속 전투·버프는 완료 보상으로 계산하지 않는다. `event`는 해당 작업의 완료 사건, `claimGate`는 보상이 실제 생성되는 조건이다. 씨앗 자루는 성숙만으로 경험치를 지급하지 않고 `ripe-contact` 수확에서 원본 성장 주기당 한 번만 지급한다. 재시도·수리·복구·저장 재개는 원본 식별자를 유지한다. 금화 판매와 메타 반출은 `stock-cycle`의 배타적 소비로 분리한다.

## 실행 operation 연결

`operationSupportIds`는 기존 연산과 대조할 위치를 가리킨다. primitive 전체를 그 연산이 지원한다는 선언이 아니다. 예를 들어 `stat-modifier`의 이동속도/최대체력은 legacy stat-add의 허용 subject가 아니며, `harvest-contact.sourceIds`는 현재 harvest-near 처리기가 소비하지 않는다.

- syntax: Sim이 허용하는 subject와 modifier/비modifier 구분. amount는 0 금지이며 stat-add만 음수 허용. 양수 duration 요구 연산을 별도 기록한다.
- handler: 실제 C# 경로·함수 및 문맥/필드 소비 제한. 처리기 존재와 설계 단위 전체의 의미 지원은 다르다.
- profiles: production 14종/first-playable 12종의 유효 선택 존재. damage-pulse/rally-returners는 FP 선택에 없으며 처리기 부재가 아니다.
- observed: 모두 `not-assessed`. 이 작업에서 게임·리그·기기 실행을 수행하지 않았다.
- semantic: 모두 `not-established`. 카운터·처리기·문법만으로 콘텐츠 고유 동작 승인을 만들지 않는다.

소스 대조: [RuntimeContentLoader.ValidateEffect](../../core/src/SowSiege.Sim/RuntimeContentLoader.cs), [RuntimeSystem.Modify/PlantingPosition/Apply](../../core/src/SowSiege.Core/RuntimeSystem.cs), [기존 operation 경계](runtime-operations.md). Graft-first 함수 탐색 후 실제 Sim 검증과 Core 분기를 읽었다. growthActions construct/garrison, 기본 무기 기하·도구 성장 및 MetaEngine은 14 effects operation과 별개다.

## 재생성 보고 계약

```sh
node tools/design-rules-report.mjs
```

보고의 `D11-10-primitives`는 단위 목록/레코드 수/operationSupport/단위별 사용 수/support/bespokeRecords를 출력한다. Lattice는 같은 카탈로그 revision의 이 규칙 결과를 비교해야 한다. viewer·lens 구현은 이번 범위가 아니다. `unitCoverage`는 문법 사용량이며 구현률로 표시하면 안 된다. selected/owned/eligible/observedNonzeroEffect/semanticReview를 하나의 implemented 플래그로 합치지 않는다.

현재 스냅샷: 163레코드, 38등록 단위, 400primitive 참조, 44bespoke 행위/레코드. 이 수는 위 명령의 실제 통합 카탈로그로 재생성한다.

| 공통 단위 | 참조 수 |
|---|---:|
| `unit:attack-shape` | 57 |
| `unit:event-gate` | 12 |
| `unit:attack-variant` | 3 |
| `unit:remnant-create` | 17 |
| `unit:growth-cycle` | 16 |
| `unit:completion-ledger` | 15 |
| `unit:ally-task` | 19 |
| `unit:visible-marker` | 33 |
| `unit:evolution-replace` | 24 |
| `unit:growth-protect` | 1 |
| `unit:enemy-tell` | 22 |
| `unit:boss-phases` | 1 |
| `unit:meta-route` | 12 |
| `unit:material-guarantee` | 4 |
| `unit:stat-modifier` | 6 |
| `unit:harvest-contact` | 1 |
| `unit:equipment-scope` | 42 |
| `unit:chapter-route` | 10 |
| `unit:status-apply` | 2 |
| `unit:stock-cycle` | 2 |
| `unit:completed-structure-attack` | 1 |
| `unit:paired-growth` | 1 |
| `unit:attack-anchor` | 1 |
| `unit:projectile-lifecycle` | 1 |
| `unit:mission-cycle` | 1 |
| `unit:target-routing` | 10 |
| `unit:resource-routing` | 17 |
| `unit:prevent-transition` | 8 |
| `unit:triggered-geometry` | 3 |
| `unit:meta-unlock` | 8 |
| `unit:enemy-pressure` | 17 |
| `unit:terrain-routing` | 6 |
| `unit:contact-reaction` | 7 |
| `unit:fixed-placement` | 2 |
| `unit:group-formation` | 6 |
| `unit:map-route-trace` | 1 |
| `unit:growth-conditioning` | 4 |
| `unit:geometry-modifier` | 7 |

## 현재 문법 밖의 정확한 행위

아래의 공통 공격·입력 교체·작업·메타 경로 부분은 이미 primitive에 분해했다. 표는 남은 결합 상태만 열거하며 전체 레코드를 미분해 문장으로 대신하지 않는다.

| 레코드 | 별도 계약이 필요한 행위 |
|---|---|
| `core:compost_sack` | 잔재 소비→숙성→다음 파종 소비의 재고별 상태 전이와 새 주기 재무장 조건이 현재 growth-cycle에 없다. |
| `core:kiln_bellows` | 점토/고철 화물별 소성→냉각→출고와 냉각 중 불씨 차단의 공정 상태기계가 없다. |
| `core:cart_yoke` | 인접 수확지·폐허의 동적 운반망 편입/제거와 화물별 목적지 분기가 없다. |
| `core:lantern_staff` | 같은 왕복로 완료 이력에 따른 잔재 묶음 운반의 경로 학습 상태가 없다. |
| `core:herb_basket` | 건조한 약초 꾸러미를 소모해 치료 임무를 완료하고 살아 있는 작업자를 복귀시키는 상태 전이가 없다. |
| `core:rain_chime` | 계절 역할 전환을 현재 화물 인도 뒤로 미루는 작업 인계 상태가 없다. |
| `core:granary_lock` | 겨울 전용 봉인 재고와 건물 파괴 유실을 동시에 묶는 저장 구획 규칙이 없다. |
| `core:measured_volley` | 표적 부재 중 발사를 보류하고 재진입에 보류 탄을 한 번 방출하는 준비 탄 상태가 없다. |
| `core:rally_boundary` | 최근 경계 통과 좌표 기억과 귀환 그룹 재정렬 완료 뒤 해산의 순서가 없다. |
| `core:ruin_salvage` | 폐허 해체를 현행 건설보다 우선하고 회수 부재를 다음 재건 터에 귀속시키는 작업 큐가 없다. |
| `core:harpoon_plough` | 붙잡은 적과 덫을 같은 고랑 경로로 함께 끄는 결합 이동 및 보스 예외가 없다. |
| `core:marching_banner` | 공격 전진로와 귀환로를 분리하고 박자마다 예비대를 인계하는 이중 통로 교대가 없다. |
| `core:rain_channel_shot` | 물길을 지속 관통 사격선으로 바꾸고 그동안 관개를 잠그는 동적 경로 모드가 없다. |
| `core:compost_rain` | 유한 거름·물 재고를 이동 구름에 결합해 구름 경로의 밭에 작업하는 운반체가 없다. |
| `core:winter_seed_stove` | 가마 열과 종자함을 결속해 다음 파종 재고를 보호하는 열 차폐 조건이 없다. |
| `core:reaping_seed_bridge` | 낫의 수확 접촉에서 보관 종자를 소비해 건너편 빈 터까지 파종 호를 만드는 합성 기하가 없다. |
| `core:burial_spore_courier` | 한 운반체의 적 충돌 폭발/밭 도착 배송을 상호 배타적으로 확정하는 종결 상태가 없다. |
| `core:storm_chime_relay` | 이동 아군을 번개 중계 노드로 사용하고 옆가지 적만 타격하는 이종 노드 연결이 없다. |
| `core:kiln_chain_perimeter` | 가마 외곽 공전의 충돌 벡터를 화구 쪽으로 정하는 경계면 밀침이 없다. |
| `core:grain_sling_convoy` | 운반 화물의 일부를 미끼로 소비하고 빈 용기로 추격자를 타격하는 화물-공격 전환이 없다. |
| `core:ox_herbal_rescue` | 부상 아군을 실제 태우고 치료 지점까지 운반하는 탑승 구조 임무가 없다. |
| `core:grave_shadow_corridor` | 말뚝 사이 이동 공격체가 바깥 경계에서만 공격하고 안쪽 성장물을 가리는 면별 판정이 없다. |
| `core:mandrake_muster_ring` | 수확한 구근 위치에 기존 인원을 원형으로 재배치하고 방사 방향으로 밀치는 진형 생성이 없다. |
| `core:lantern_rank_gates` | 귀환 아군의 통과에만 열고 닫는 동적 아군 문 판정이 없다. |
| `core:furrow_convoy_rail` | 고랑을 운송 궤도로 전환하고 정차한 밭에서만 적재하는 경로-정차 작업이 없다. |
| `core:root_choir` | 파괴 가능한 뿌리 닻이 소환 통로·장벽·본체 노출을 함께 제어하는 연결 상태가 없다. |
| `core:cinder_crane` | 바람 전환이 불씨 확산 경로와 잿속 본체 노출을 함께 바꾸는 단계 전이가 없다. |
| `core:toll_regent` | 미회수 보상 봉인 깃발과 본체 방패 방향을 결속하고 파괴 때 해제하는 종속 상태가 없다. |
| `core:winter_hart` | 긴 몸체의 회전 접촉과 익은 밭 서리 봉인을 함께 처리하는 단계별 기하가 없다. |
| `meta:chapter_2` | 범람 통로/상시 높은 우회로 구분과 물빠짐 때 어린 밭 휴면 해제의 장치 상태가 없다. |
| `meta:chapter_3` | 이동 그늘 경계에 의존하는 달꽃 성숙 일시 정지와 경계 예고가 없다. |
| `meta:chapter_4` | 공격 적중으로 기억한 균열 방향을 다음 울림 낙석으로 소비하는 장치 기억이 없다. |
| `meta:chapter_5` | 바람으로 개폐되는 갈대문이 경로만 가리고 적 머리·공격 예고는 보존하는 표현 계약이 없다. |
| `meta:chapter_6` | 도구 작업/불길 통과가 잿더미와 비옥 새싹자리를 왕복 전환하는 장치 상태가 없다. |
| `meta:chapter_7` | 예고된 돌풍이 미회수 보상만 바람막이로 이동시키는 선택적 환경 이동이 없다. |
| `meta:chapter_8` | 양쪽 여울의 교대 개폐와 중앙 상시 통로를 유지하는 이중 수문 상태가 없다. |
| `meta:chapter_9` | 바람막이 안팎의 결빙 면역과 도구 작업 해빙을 결합하는 눈보라 상태가 없다. |
| `meta:chapter_10` | 계절 문양별 이전 구역 환류 잠금과 다음 계절 작업에 의한 해제 원장이 없다. |
| `core:ditch_root_bastion` | 깨어난 구근을 중심으로 바깥 방향 장창을 생성하는 방사 찌르기 배열이 없다. |
| `core:seed_bolt_granary_burst` | 익은 작물의 종자 소비와 다음 적중 부채탄 방출의 저장 탄 상태가 없다. |
| `core:stone_masonry_rampart` | 충격 경계돌을 살아 있는 건물 앞 짧은 밀침 벽으로 전환하는 지형 생성이 없다. |
| `core:ember_herbal_wisp` | 유한 약초를 실은 공격체가 경로 적을 태우고 부상 아군에게 배송하는 복합 표적 종결이 없다. |
| `core:mill_shadow_reel` | 말뚝 쌍을 고정 왕복 종점으로 삼고 그늘 경계 적만 베는 두 기준점 판정이 없다. |
| `core:watchtower_signal_bolt` | 귀환 경로 앞 관통 사격이 실제 아군 안내 경로를 생성하는 연결 이벤트가 없다. |

## 확인 범위

엄격한 Ajv paramSchema 검증, primitive/params 키 일치, 유한 unit 참조를 검사했다. 이는 설계 문법 검사이며 Core 동작·렌더러·성능·재미 검증이 아니다. 동작 구현 단계에서는 같은 seed/입력의 실제 상태 변화·원장 중복 방지·진화 입력 대체·화면 표현을 각각 검증해야 한다.

## #167 런타임 구현 상태 읽기

위 v1.1 문법과 bespoke 44개 목록은 설계 원본을 보존한 기록이다. 공통 단위 실행기의 현재 **부분 지원**은 [단위 지원 원장](runtime-primitive-support.json)과 [runtime-operations.md의 #167 경계](runtime-operations.md#167-공통-단위-런타임의-별도-지원-원장)에서 읽는다. Lattice는 이 원장의 전체 단위 목록을 `runtime-support` 층에 표시하고 설계 사용 간선으로 연결한다. 기존 `operationSupport` 14개를 새 단위 구현률로 치환하지 않는다.

설계의 `unitCoverage`·`selectedReferences`는 여전히 문법 사용 수다. 실행기의 엄격한 파라미터 조합, 처리기 경계, 작성된 테스트 경로, 실행 관측, 의미 검토, 화면 검토는 서로 다른 필드다. 설계에 등록된 단위라도 현재 조합을 처리하지 못하면 `unsupported`, 일부만 처리하면 `partial-runtime`이며 `implemented` 하나로 뭉치지 않는다. 실행 레코드에 별도로 명시한 사건 조건은 설계 참조 수를 임의로 늘리지 않는다.
