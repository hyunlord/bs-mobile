# 런타임 operation 계약과 구현 경계

확인일: 2026-10-10 · 기준: `90c8ca34f9de6755afca01d28c8eff4f2eae20be`와 D1 공통 필드 정규화 · 추적: #133.

`runtimeProjection.effects[].operation`은 **14종**이다. 아래는 소스·선택 데이터의 정적 대조이며 실행 빈도나 설계 의도 달성의 판정이 아니다. `production`의 유효 선택에는 14종, `first-playable`에는 12종이 있다. 후자의 `damage-pulse`, `rally-returners`는 처리기 미구현이 아니라 현재 유효 선택에서 제외된 연산이다. 이 문서는 Lattice 렌즈의 근거이며 뷰어·새 런타임을 구현하지 않는다.

## 읽는 순서

1. [스키마의 operation enum](../../data/schema/weapon.schema.json#L198)은 문법 허용 목록이다. `subject`는 스키마에서 비어 있지 않은 문자열이며, 실제 조합은 [Sim `ValidateEffect`](../../core/src/SowSiege.Sim/RuntimeContentLoader.cs#L101)가 제한한다.
2. [Sim `LoadRuntime`](../../core/src/SowSiege.Sim/RuntimeContentLoader.cs#L7)는 프로필 선택과 `runtimeOverrides`를 적용한다. override는 해당 레코드의 projection을 **교체**한다. 원본 효과에 추가하는 것이 아니다. [first-playable override](../../data/profiles/first-playable.json#L285)가 네 기존 특허장의 동작을 바꾼다.
3. [Core `OwnedEffects`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L95)는 소유 장비·특허장·물품·해금 진화만 순회한다. first-playable 물품은 현재 장비 태그 자격도 다시 검사한다. [진화 자격](../../core/src/SowSiege.Core/FirstPlayableRuntime.cs#L8)은 입력 장비·레벨·성장 조건을 별도로 확인한다.
4. [`Emit`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L142)은 트리거·조건·식량을 확인하고, 효과가 0이면 비용을 환불한다. 같은 트리거의 재진입은 막는다. modifier는 `Emit` 대신 `Modify`/`PlantingPosition`에서 소비한다.
5. [`Record`와 `Result`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L322)의 효과 카운터는 0이 아닌 상태 변화의 증거다. 0은 미소유·미해금·조건 불충족·대상 부재일 수도 있다. 카운터가 증가해도 화면 표현이나 레코드의 `concept` 전체를 구현했다는 뜻은 아니다.

## operation → 허용 subject → 실제 처리기

`P/FP`는 각각 production/first-playable **유효 선택에 해당 연산을 가진 효과가 존재함**을 뜻한다. `있음`은 특정 플레이에서 반드시 실행됨을 뜻하지 않는다. 두 프로필 모두 공통 Core 처리기를 사용한다.

| operation | 허용 subject | Core 처리와 소비 위치 | P / FP 선택 및 조건 |
|---|---|---|---|
| `stat-add` | `attack-damage`, `attack-knockback`, `attack-cooldown`, `repair-amount`, `draft-min-rest`, `draft-speed`, `worker-speed`, `worker-incoming-damage`, `building-front-damage`, `building-rear-damage` | [`RuntimeSystem.Modify`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L118): 스택×amount 가산과 하한 제한. 공격은 [`CombatSystem.Activate`](../../core/src/SowSiege.Core/CombatSystem.cs#L123), [`FirstPlayableCombat`](../../core/src/SowSiege.Core/FirstPlayableCombat.cs#L167), 수리·이동은 [`EstateSystem`](../../core/src/SowSiege.Core/EstateSystem.cs#L133), 피해·징집은 [`BuildingDamage`/`CanDraft`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L284) | 있음 / 있음. 각 subject의 호출 경로와 조건을 별도 확인해야 함. |
| `planting-bias` | `existing-edge`, `estate-inward` | [`PlantingPosition`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L129) → [`EstateSystem.Plant`](../../core/src/SowSiege.Core/EstateSystem.cs#L77): 최근접 밭 또는 영지 방향으로 파종 위치 이동 | 있음 / 있음. 밭 간격·수용량 제한으로 실제 파종 실패 가능. |
| `damage-pulse` | `weapon-front` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L164): melee 태그 무기 소유 시 전방 적에 피해 | 있음 / 없음. FP의 `core:guarded_harvest` override는 수치 modifier로 교체됨. |
| `repair-nearest` | `building` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L182): 반경 내 완성된 손상 건물 수리, `repair` 발행 | 있음 / 있음(`core:warded_masonry`). FP는 체력 0 건물을 제외하므로 이 효과로 폐허 복구 불가. |
| `worker-buff` | `people` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L191): 최대 amount명의 농민 `DutyUntil` 연장 → [`TickFarms`](../../core/src/SowSiege.Core/EstateSystem.cs#L185)의 노동 보너스 | 있음 / 있음(`core:soup_ladle`). 이동속도 증가가 아니라 농사 노동 보너스. |
| `rally-returners` | `people` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L198): 귀환자 정지 기한·경유지 → [`TickPeople`](../../core/src/SowSiege.Core/EstateSystem.cs#L282) 이동·공격 | 있음 / 없음. FP `core:rally_boundary` override가 이 효과를 제거함. |
| `plant-path` | 선택된 **도구 id** | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L205) → `PlantFarm` → [`EstateSystem.Plant`](../../core/src/SowSiege.Core/EstateSystem.cs#L77) | 있음 / 있음. 일반 경로는 Enemy 문맥 필요. FP는 Farm 문맥도 허용하여 수확 후 파종 가능. amount는 파종 시도 수이며 공간 제한 적용. |
| `shield-farms` | `land` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L224): 밭 보호 횟수·기한 → [`ShieldFarm`](../../core/src/SowSiege.Core/RuntimeSystem.cs#L276) → [`ResolveEnemyAttacks`](../../core/src/SowSiege.Core/CombatSystem.cs#L198) | 있음 / 있음. 작은/같은 보호 횟수로 기존 보호를 갱신하지 않음. |
| `harvest-near` | 선택된 **도구 또는 무기 id** | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L233) → `HarvestFarm` → [`EstateSystem.Harvest`](../../core/src/SowSiege.Core/EstateSystem.cs#L217) | 있음 / 있음. 반경 내 익은 밭을 최대 amount개 수확. **subject id는 검사만 하며 처리기는 대상 필터나 수확 귀속에 사용하지 않는다.** |
| `damage-young-plots` | `land` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L238): 미성숙 밭 최대 amount개의 Progress를 0으로 | 있음 / 있음. 밭 삭제·체력 피해·성장 단계 초기화가 아니라 현재 단계 진행 손실. |
| `extend-duty` | `people` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L242) → [`TickPeople`](../../core/src/SowSiege.Core/EstateSystem.cs#L252)의 복무 종료 | 있음 / 있음. Person 문맥의 복무 기한 연장. FP에서는 Person 부재 시 반경 내 militia/guard에 적용. |
| `guard-return` | `people` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L254) → [`TickPeople`](../../core/src/SowSiege.Core/EstateSystem.cs#L296), [`ResolveEnemyAttacks`](../../core/src/SowSiege.Core/CombatSystem.cs#L178) | 있음 / 있음. Person 필요, 귀환당 한 번 정지 기한 연장. 귀환자 공격·방어 소비 경로가 있음. |
| `pause-neighbor-growth` | `land` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L261): 문맥 밭 외 주변 밭 PauseUntil → [`TickFarms`](../../core/src/SowSiege.Core/EstateSystem.cs#L193) | 있음 / 있음. amount는 양수 검증되지만 적용 강도/대상 수에 사용하지 않음. duration과 radius가 실제 작동값. |
| `return-via-building` | `building` | [`Apply` case](../../core/src/SowSiege.Core/RuntimeSystem.cs#L268): 완성된 살아 있는 최근접 건물 경유 → [`TickPeople`](../../core/src/SowSiege.Core/EstateSystem.cs#L283) | 있음 / 있음. Person 필요, 자기 위치·영지와 같은 경유지 제외. amount는 양수 검증되지만 경유 횟수에 사용하지 않음. |

`stat-add`만 음수 amount가 가능하며 모든 효과는 0을 금지한다. `stat-add`와 `planting-bias`만 `trigger: modifier`이고 식량 비용은 0이다. `worker-buff`, `rally-returners`, `shield-farms`, `extend-duty`, `guard-return`, `pause-neighbor-growth`는 양수 duration이 필수다. 나머지 연산에 duration 필드가 있다고 소비한다고 해석하지 않는다.

트리거는 `attack`, `harvest`, `repair`, `plant`, `farm-hit`, `draft`, `return-start`, `return-arrival`, `estate-cross`, `kill`, `modifier`다. 공격·처치는 [CombatSystem](../../core/src/SowSiege.Core/CombatSystem.cs#L162), 성장·귀환은 [EstateSystem](../../core/src/SowSiege.Core/EstateSystem.cs#L87), 경계 통과는 [Simulation.Advance](../../core/src/SowSiege.Core/Simulation.cs#L110)가 발행한다. FP 무기 attack는 [실제 적/지도 이벤트 명중](../../core/src/SowSiege.Core/FirstPlayableCombat.cs#L163) 때 발행된다. 검증기가 허용하는 임의 trigger/operation 조합이 필요한 Person/Farm/Enemy 문맥까지 보장하지는 않는다.

## effects 밖에서 이미 작동하는 시스템

- 도구 `activation`의 공격은 [`CombatSystem.Activate`](../../core/src/SowSiege.Core/CombatSystem.cs#L123), 기본 성장 `growth.target`의 land/building/people은 [`EstateSystem.ApplyGrowth`](../../core/src/SowSiege.Core/EstateSystem.cs#L49)가 처리한다. effects 배열이 없다고 도구 전체를 설계 전용으로 분류하면 안 된다.
- `runtimeProjection.growthActions[].operation`의 **`construct` / `garrison`은 위 14종과 다른 문법**이다. [Sim 검사](../../core/src/SowSiege.Sim/RuntimeContentLoader.cs#L87)와 [`ApplyGrowth`](../../core/src/SowSiege.Core/EstateSystem.cs#L49)가 각각 building/people에 연결한다. growthActions가 있으면 기본 growth 경로를 대체한다.
- 무기 형태·레벨은 [`WeaponCombatSystem.ActivateWeapon`](../../core/src/SowSiege.Core/WeaponCombatSystem.cs#L10)과 [`FirstPlayableCombat`](../../core/src/SowSiege.Core/FirstPlayableCombat.cs#L14), 적 표적은 [`CombatSystem.Target`](../../core/src/SowSiege.Core/CombatSystem.cs#L75), 지도 이벤트는 [`TickMapEvents`](../../core/src/SowSiege.Core/FirstPlayableRuntime.cs#L20)가 처리한다. 판 밖 성장은 [`MetaEngine`](../../core/src/SowSiege.Core/MetaEngine.cs) 영역이다.
- 따라서 `operation 존재 → 레코드 고유 메커니즘 구현` 추론은 금지한다. 예를 들어 `core:ash_gathering_charm`의 stat-add는 공격력 modifier의 근거일 뿐 처치 흔적을 파종에 전달하는 설계 의도의 근거가 아니다. first-playable에서 선택되지 않은 기록과 선택되었지만 단순 수치로 투영된 기록도 구별한다.

## id와 메타데이터 예외

콘텐츠 참조(`inputIds`, `linkedToolIds`, `antiSynergy`, `result.baseId`, 프로필 선택)는 콘텐츠 id로 연결한다. 반면 `growth.target: land/building/people`, 적 `target: seed/ripe/building/lord/people`, 위 표의 비-id subject, 태그·역할·시즌·카운트 selector는 **분류 값**이다. 이를 콘텐츠 id로 강제 변환하지 않는다. effect id와 loot channel id는 네임스페이스 식별자지만 최상위 콘텐츠 레코드 참조와 다르다. `conditions.value`도 `equipment-owned`/`equipment-id`는 콘텐츠 참조이고 `equipment-tag`/`owned-tag`는 태그다.

D1 정규화에서 최상위 `kind`는 콘텐츠 종류(예: `evolution`)로 통일하고 종전 진화 조합 종류는 `evolutionKind`로 분리한다. `weapon-tool`/`tool-tool`/`tool-growth`의 의미는 유지하며 [Sim 진화 로딩](../../core/src/SowSiege.Sim/RuntimeContentLoader.cs#L44)에서 Core의 진화 종류로 전달한다. 중첩 조건의 `kind`, 이벤트 종류 등 서로 다른 의미의 필드를 일괄 치환하지 않는다. `designStatus`는 설계/제작 상태이지 실행 증명 플래그가 아니다.

## Lattice용 기계 판정 계약

D3 v1.1부터 [설계 카탈로그](../../data/system-design-v1.json)의 `primitiveContract.operationSupport`가 아래 14종의 정적 지원 근거를 저장한다. [공통 동작 단위](mechanic-primitives.md)의 `operationSupportIds`로 연결하며, 이는 설계 메커니즘 전체 구현이나 실행 관찰 판정이 아니다. 기존 콘텐츠에 추정 `implemented: true`를 추가하지 않는다.

- `operationSupport`: `id`, `operation`, `syntax`, `handler`, `profiles`, `observed`, `semantic`을 저장한다. 문법 허용, 처리기 존재, 프로필 선택을 분리한다. 설계 v1.1의 관찰은 `not-assessed`, 의미 구현은 `not-established`이다. 단위가 여러 기존 연산을 참조해도 단위 전체를 지원한다고 셈하지 않는다.
다음 세 모델은 여전히 향후 렌즈의 권고이며 이번 변경으로 실행 추적 필드가 추가되지는 않는다.

- `effectiveProjection`: profile id와 콘텐츠 id를 키로 원본/override 출처를 남긴다. `selected`, `owned`, `eligible`, `observedNonzeroEffect`를 서로 다른 사실로 관리한다. 소유·관찰은 실행 provenance 없이는 미확인이다.
- `semanticReview`: concept/effect의 주장과 실제 작동을 사람이 대조한 별도 판정으로 둔다. handler 존재·카운터·`designed-v1`만으로 자동 승인하지 않는다.
- `referenceRole`: 콘텐츠 참조, 태그, 분류 enum, 내부 effect/channel id를 필드 경로별로 구별한다. raw 문자열에 콜론이 있는지만으로 간선을 만들지 않는다.

이 문서의 검증 범위는 14종 enum·Sim 허용 조합·Core 분기/소비자·두 프로필의 선택과 override 정적 대조다. 플레이 실행·Unity 시각성·실기 성능·설계 고유성 승인은 증명하지 않는다.

규칙 비교는 `node tools/design-rules-report.mjs`의 안정된 rule ID별 `expected`·`actual`·`violatingIds`를 기준으로 한다. 카탈로그 개정과 원본 파일이 같을 때만 Lattice 렌즈 값과 비교한다. 생성 가능한 보고서 사본은 커밋하지 않는다.
