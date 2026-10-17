# ADR0064: 어둠 속 영지 — 영구 밝힘 구역과 잠든 건물의 Core 계약

- 상태: **결정** — 2026-10-17 사용자가 방향과 미정 11건을 결정했다([설계 8절](../design/m2-dark-estate-buildings.md)). 구현은 아직 없다. 무기 스택(#267~)과 영웅 PR(#290)이 정리된 뒤 별도 지시로 시작한다. 수치는 모두 [가설]이며 #126 보류를 유지한다.
- 날짜: 2026-10-17
- 연결: [#292](https://github.com/hyunlord/bs-mobile/issues/292), [설계](../design/m2-dark-estate-buildings.md)
- 관련: [ADR0042](0042-wave1a-runtime-isolation.md), [ADR0047](0047-state-driven-fallow-kingdom.md), [ADR0049](0049-deterministic-wave-enemy-separation.md), [ADR0051](0051-versioned-endless-fallow-field.md), [ADR0053](0053-explicit-meta-claims-and-wave-accounting.md), [ADR0054](0054-opt-in-host-rewards-and-local-bm-slots.md)
- 번호: 2026-10-17 origin/main `046fbea`의 최대 번호는 0058이다. 열린 PR이 0058–0063을 쓴다(#255 0058, #257 0059, #261 0060, #285 0061, #286 0062, #287·#289 0063). 그래서 0064를 쓴다. 결정 반영 PR 병합 직전(2026-10-17) 다시 확인했다: main은 여전히 0058까지이고 다른 열린 PR은 0064를 쓰지 않는다.

## 맥락

사용자는 M2 첫 실험 후보로 "밝혀서 깨우는 건물"을 정했다. 매 판 건물이 잠든 채 놓여 있고, 플레이어가 버티기나 자원 소비로 깨우면 그 판에서 돕는다. 밝힘은 영구적이다. 건물을 깨울수록 쉬워지지만 강한 플레이어는 무시할 수 있어야 한다.

현재 wave-1a의 사실(정적 읽기, 상세는 [설계 1절](../design/m2-dark-estate-buildings.md)):

- 회색→복원색은 View만 소유한다. `WorldRenderer.UpdateFallowMask`가 0.1초마다 현재 `Work`(밭·건물·물)와 수확 사건으로 마스크를 다시 칠한다(`unity/Assets/Game/View/WorldFallow.cs:87-147`). 영웅은 원천이 아니다. 땅 칸의 기억이 없고, 수확한 밭은 꺼진다.
- 판 시작에 Core 건물은 없다. 집은 `Field.Home`에 그리는 장식이다(`unity/Assets/Game/View/WorldField.cs:84-90`). 밭·건물·웅덩이는 도구 발동으로만 생긴다(`core/src/SowSiege.Core/WaveWorkSystem.cs:36-129`).
- 건물은 영주가 작업 반경 안에 있을 때만 진행한다(`core/src/SowSiege.Core/WaveGrowthPrimitives.cs:11-19`). 완성 뒤 목재를 예약해 출고하고(`WaveWorkSystem.cs:160-176`), 완성 건물 공격이 있다(`WaveWorkSystem.cs:178-195`).
- 적은 정해진 주기·상한으로 반경16200 고리에서 무작위 방향으로 생긴다(`core/src/SowSiege.Core/WaveEnemySystem.cs:140-176`).
- 선택 기능은 두 방식으로 해시를 보존한다. 데이터 쪽은 `[OmitWhenNull]` 정의와 있을 때만 쓰는 태그 블록이다(`WaveEnemySeparationDefinition`, `core/src/SowSiege.Core/WaveRuntimeContract.cs:28`, `WaveRuntimeCodec.cs:13-16`). 실행 옵션 쪽은 nullable `InteractiveOptions`와 재생 형식 번호1–5다(`core/src/SowSiege.Core/InteractiveContract.cs:26`, `ReplayCodec.cs:18-22,61-64`).
- 계정→wave의 힘 투영은 없다(ADR0053 `AccountOnlyWave`).

밝힘이 위협 규칙에 영향을 주므로 더 이상 표현만이 아니다. 따라서 ADR0047의 "Core에 색·마스크·새 상태를 추가하지 않는다"를 이 선택 프로필에 한해 좁혀야 한다.

## 결정

M2 범위 [결정]: 망루·우물·사당·곡창 3종, 어둠 위협 T1 하나, 항상 켜진 시작점의 집, 데이터 고정 건물 목록 L1·L3. 경보·건물 피격·T2·계정 연결은 M2 밖이다.

### 1. 두 종류의 빛

| 빛 | 소유 | 수명 | 규칙 영향 |
| --- | --- | --- | --- |
| 영구 밝힘 구역 | Core | 건물이 깨어난 틱부터 판 끝까지. 줄지 않는다 | 어둠 위협, 건물 효과 범위 |
| 영웅 임시 시야 | View | 매 프레임 영웅 위치 | 없음 |
| 돌봄 복원색(기존) | View | 기존 그대로 | 없음(ADR0047 유지) |

- 밝힘 구역은 활성 건물의 `(중심, 반지름)`이다. 판정은 `long` 거리 제곱 비교다. 땅 칸 격자나 텍스처를 Core에 두지 않는다. 건물 수가 작으므로(초안 상한 8) 매 질의를 선형 비교한다.
- M2에서 건물은 공격받지 않는다 [결정]. 체력 필드는 후속 P3 연결을 위해 계약에만 둔다. 후속에서도 건물이 쓰러져 밝힘이 꺼지지 않게 하는 것을 기본안으로 둔다 [권고].
- 시작점의 집은 항상 켜진 건물로 판 시작 틱에 `Active` 상태로 만든다 [결정]. 켜기 조건·비용이 없다.
- 영웅 시야 반지름은 정의의 상수 하나로 둔다. View가 그 값으로 그린다. Core 규칙은 읽지 않으므로 상태·해시에 영향이 없다. 시야가 규칙에 들어오면(예: 시야 안 적 약화) 이 ADR을 다시 연다.

### 2. 어둠 위협 규칙 (수치 [가설])

- **T1 어둠 강화 [결정]:** 공격 순간의 적 위치가 모든 밝힘 구역 밖이면 영주에게 주는 피해를 `(1000 + darkDamagePermille) / 1000`배로 한다. 이동 속도 가산은 선택이다. 정수만 쓰고 난수를 뽑지 않는다.
- **T2 어둠 생성 증가 [채택 안 함, 후속 후보]:** 잠든 건물 수에 따라 생성 주기를 줄인다. 다시 다룬다면 난수 사용 순서가 바뀌므로 새 프로필에서만 허용한다.
- **깨우는 중 경보 [M2 없음, 후속 후보]:** 버티기 시작 때 주변 적을 추가로 부르지 않는다.
- 정예는 wave에 없다. 봉화의 "정예 공개"는 정예 콘텐츠가 생길 때 다룬다.
- 수치는 #126 보류 범위에서 실험용 [가설]로만 둔다. production 기본값이 아니다.

### 3. 건물 상태와 켜기 2종

```
Asleep ──(조건 충족 시작)──> Waking ──(진행 ≥ 필요)──> Active
  ^                            │
  └────(정지 규칙: 진행 유지)────┘
```

- 개체: `WaveEstateBuilding { Id, DefinitionId, Position, Phase, Progress, Required, Paid, LitRadius, Health, ReadyTick }`. 새 목록 `WaveRuntimeState.Estate`에 둔다. 기존 `WaveWork`에 섞지 않는다. 이유: `WaveWorkSystem.Tick`이 모든 작업물의 `Source`를 장비로 찾는다(`Gear(w.Source)`). 장비가 아닌 건물이 들어가면 기존 경로가 바뀐다.
- **버티기 `channel`:** 영주가 켜기 반지름 안에 있는 틱마다 `Progress++`. 밖이면 진행을 유지한 채 멈춘다 [결정]. 감쇠·초기화는 하지 않는다. 기존 `workGate: within-work-radius`와 같은 판정이다.
- **소비 `spend`:** 영주가 켜기 반지름 안에 `confirmTicks` [가설] 동안 머물고 비용이 충분하면 한 번 지불한다. `Paid = true`가 되고 즉시 `Active`가 된다. 지불은 기존 목재 예약처럼 상태 값을 줄이고 사건을 남긴다. 부족하면 아무것도 쓰지 않는다. 자원은 판 안 곡물(`world.Food`)과 목재(`State.Timber`)다.
- **둘 다 `both`:** 먼저 지불하고 그 뒤 버티기를 한다. 지불은 한 번뿐이다. 버티기 중 이탈해도 환불·재지불하지 않는다.
- 활성화는 건물마다 판당 한 번이다. 완료 원장 키는 `estate-wake:<id>`다. 저장·재생·수리가 활성화를 다시 일으키지 않는다.
- 효과는 데이터의 `effect.kind`로 고른다. 초안 종류는 `building-attack`, `area-heal`, `zone-magnet`, `harvest-bonus`, `guard-group`, `global-damage`, `palisade`, `reveal`이다. Core에 건물 ID를 박지 않는다(P8). 기본과 다른 검사용 더미 건물을 같은 경로로 검사한다.
- 배치는 정의의 슬롯(집 중심의 고리 반경 범위)과 `(seed, 챕터, 슬롯)` 정수 해시 각도로 정한다. 새 난수를 뽑지 않는다. 적 재배치·화면 캐시가 건물을 옮기거나 지우지 않는다.

### 4. 결정론과 해시

- 정의: `WaveRuntimeDefinition`에 `[property: OmitWhenNull] WaveEstateLightDefinition? EstateLight = null`을 더한다. catalog codec은 값이 있을 때만 태그 `wave-estate-light-v1` 블록을 쓴다. `EnemySeparation`과 같은 방식이다.
- 상태: `EstateLight`가 있을 때만 `Estate` 목록과 계수기를 상태 codec에 덧붙인다. 없으면 한 바이트도 더하지 않는다(ADR0042의 "null 계약에 표식 금지").
- **기존 해시 유지:** wave-1a 데이터에는 이 블록이 없다. 따라서 catalog 해시와 `WavePrimitiveTests.OriginalNormalInputGoldenHashesRemainUnchanged`(`core/tests/SowSiege.Tests/WavePrimitiveTests.cs:100-112`)의 기준 상태 해시가 그대로여야 한다. 재생 형식1–5와 헤더도 바꾸지 않는다.
- **M2 실험:** 별도 실험 프로필(초안 `wave-2a-dark`)의 데이터에만 블록을 넣는다. 건물 목록은 데이터에 고정한다(L1·L3 두 벌). 그러면 data hash가 차이를 담으므로 재생 형식을 올리지 않는다.
- **계정 연결 단계 [방향 결정, M2 이후 구현]:** 계정이 고른 건물 목록·레벨은 실행 입력이다. 그러므로 재생 헤더가 담아야 한다. nullable `InteractiveOptions.Estate`와 새 재생 형식6을 추가한다. 앱의 현재 계정 상태로 옛 재생을 덮어쓰지 않는다. 이 단계는 ADR0053 경계를 건물 목록에 한해 바꾸며, 사용자가 방향을 승인했다. 효력은 그 구현 PR부터다.
- net8.0과 netstandard2.1에서 같은 정상 입력 재생 해시를 비교한다. Unity/IL2CPP 실기 일치로 확대 보고하지 않는다.

### 5. 재사용과 신규

| 필요한 것 | 기존에서 재사용 | 새로 만들 것 | 바꿀 것 |
| --- | --- | --- | --- |
| 잠든 건물 개체 | `WaveWork` 필드 모양, 사건 `State.Emit`, `world.AllocateId` | `WaveEstateBuilding`·`WaveEstateView`·정의 레코드 | `WaveRuntimeFrame`에 선택 필드 추가 |
| 버티기 켜기 | `AdvanceTime`의 작업 반경 판정(`WaveGrowthPrimitives.cs:11-19`) | 건물용 진행 함수 | — |
| 소비 켜기 | 목재 예약·출고 패턴(`WaveWorkSystem.cs:171-172`), `world.Food` | 머무름 확인 틱, 지불 사건 | — |
| 중복 방지 | `WaveCompletionLedger`, `State.Completed` 키 | `estate-wake:` 키 | — |
| 영구 밝힘 | — | 활성 건물 원 목록, `IsLit(position)` | — |
| 어둠 위협 T1 | 적 공격 피해 적용 지점 | 밝힘 판정에 따른 정수 배율 | 적→영주 피해 계산(옵션 있을 때만) |
| 망루 | 완성 건물 공격 `Brace`(`WaveWorkSystem.cs:178-195`), 적 거리 질의, 투사체 | 건물 기준 원거리 모양 | — |
| 우물·사당 | 반경 판정 | 영주 회복 경로(wave 파일 정적 검색에서 찾지 못함) | — |
| 곡창 | 경험치 회수 `Collect`(`WaveWorkSystem.cs:255`), `Harvest`의 곡물 증가 | 구역 자석, 구역 안 수확 보너스 | — |
| 막사 | `WaveGroup`·`TickGroups`(`WaveWorkSystem.cs:291`) | 건물 기준 수비 임무, 별도 상한 | — |
| 대장간 | — | 피해 적용 지점 전체의 배율 | 무기 피해 경로 여러 곳 |
| 목책 | 적 분리 격자([ADR0049](0049-deterministic-wave-enemy-separation.md)) | 정적 장애물·우회 | 적 이동 |
| 봉화 | 프레임의 적·건물 위치 | View 표시만(규칙 없음) | — |
| 화면 | `UpdateFallowMask`, `FallowWorldSprite`, 수확 퍼짐 | 어둠 단계 셰이더, 밝힘·시야 원 상수 배열, 가장자리 표시, 진행 고리 | 셰이더 색 단계(옵션 있을 때만) |
| 결산 | `MetaRunAdapter.FromWave`(`core/src/SowSiege.Core/MetaRunAdapter.cs:78-90`) | 잠든 건물 수 사실 | 결산 보너스(수치 #126) |
| first-playable 영지 | `EstateSystem`은 재사용하지 않는다(역사 프로필) | — | — |

### 6. 기존 결정과의 관계

- **ADR0047:** 돌봄 복원색은 계속 View만 소유하고 규칙이 없다. 사용자는 밝힘 구역에 한해서만 색과 규칙을 잇는 것을 승인했다. `EstateLight`가 있는 프로필에서만 밝힘 구역이 Core 상태가 되고 위협 규칙과 이어진다. ADR0047을 대체하지 않고 범위를 좁힌다.
- **ADR0051:** 건물은 집 기준 고정 좌표다. 적 재배치·화면 캐시와 무관하다. 보스 경기장 반경6000 안팎의 건물 효과는 그대로 둔다 [가설].
- **ADR0053:** M2 실험(데이터 고정 목록 L1·L3)은 경계를 바꾸지 않는다. 계정 연결 단계는 건물 목록에 한해 투영을 연다(방향 결정, M2 이후 구현). 가신 능력 투영은 닫힌 채다.
- **ADR0054·#277:** 판매는 건물 외형뿐이다(영지 테마 자리). 외형 데이터는 그림 참조만 가진다(P6). 사용자 결정에 따라 방치 2배 광고는 영지 자재에 적용하지 않는다. 광고 정책 코드 변경은 구현 이슈에서 한다.

## 결과·비용

- Core에 새 상태 하나(건물 목록)와 질의 하나(밝힘 판정)가 생긴다. 선택 프로필에서만 적→영주 피해 계산이 밝힘 판정에 의존한다.
- 효과 7종 중 망루·곡창은 기존 경로 재사용이 크다. 우물은 작은 신규다. 막사는 무리 상한을 새로 정해야 한다. 대장간·목책은 여러 기존 경로를 건드리므로 M2 첫 범위에서 뺀다.
- 화면은 셰이더 상수 배열 9개 이하와 가장자리 표시를 더한다. 새 렌더 타깃·전체 화면 패스는 없다. 실제 비용은 측정 전 미확인이다.

## 거절한 대안

- **땅 칸 격자에 밝힘 기록:** 끝없는 들판에서 칸 수가 무한히 늘고 해시 비용이 커진다. 건물 원 목록으로 충분하다.
- **영웅이 걸은 자리를 영구히 밝힘:** 사용자가 원한 "건물을 깨워 밝힌다"와 다르다. 모든 판이 같은 산책 전략으로 수렴한다.
- **기존 `WaveWork`에 `estate` 종류 추가:** 장비 기반 작업 순환과 마스크 원천이 섞여 기존 해시 경로를 바꿀 위험이 있다.
- **전체 화면 어둠 후처리:** 적 표식·영주를 지운다(ADR0047과 같은 이유, P5).
- **wave-1a 데이터에 바로 넣기:** 역사 해시와 M1 비교 기준을 잃는다.
- **켜기 비용을 유료로 줄이기·즉시 완성 판매:** 힘 판매 금지 위반.

## 검증 (구현 PR이 증명할 것)

- wave-1a catalog 해시와 기준 상태 해시 불변. 재생 형식1–5의 기존 고정 기록 재생 불변.
- 새 프로필의 정상 입력 재생이 두 Core 대상에서 같은 해시를 낸다.
- 활성화 판당1회, 밝힘이 줄지 않음, 수리·저장 복구가 재활성화를 만들지 않음, 소비 부족 시 미지불, 정지 규칙, 검사용 더미 건물이 같은 경로로 실행됨을 xUnit으로 검사한다.
- 셰이더 전후 C05 프레임 비용 비교, 정상 속도 Mac 영상. 사용자만 화면·재미 관문을 판정한다.
- 이 ADR 자체는 구현·CI·화면 관문의 통과 증거가 아니다.
