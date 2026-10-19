# ADR 0058: wave 진화 해금과 카드 제시를 데이터로 정한다

- 상태: 승인 — 2026-10-15 사용자 검토에서 미정 4건을 결정했다(아래 「사용자 결정」). 구현은 「구현 순서」 1부터 진행한다.
- 날짜: 2026-10-15
- 관련: [#276](https://github.com/hyunlord/bs-mobile/issues/276), [ADR0029](0029-first-playable-contract-and-historical-isolation.md), [ADR0054](0054-opt-in-host-rewards-and-local-bm-slots.md), [#126](https://github.com/hyunlord/bs-mobile/issues/126)(수치 보류 유지)
- 출처: 참고작(VS·Capybara Go!·Brotato) 정적 분석은 **구조 원리만** 근거로 쓴다. 이름·수치·에셋은 가져오지 않는다. 아래 "현재 구조"는 main `da26edf` 코드를 직접 읽은 사실이다. 실행 검증은 하지 않았다.

## 맥락

### 현재 구조 (정적 확인)

- **진화 해금은 두 경로로 나뉘어 있다.** 실제 카드 제시는 `ProgressionSystem.WaveCards()`(`core/src/SowSiege.Core/ProgressionSystem.cs:122-126`)가 `e.Kind switch`로 진화마다 플래그 하나를 고른다. 데이터의 `programs[id].params["unit:event-gate"].condition`을 읽는 `WaveRuntimeSystem.EligibleEvolution`(`WaveRuntimeSystem.cs:48-50`)은 `WaveRuntimeIntegrationTests`·`WavePrimitiveTests`에서만 호출된다. **데이터의 조건을 바꿔도 실제 제시는 바뀌지 않는다.**
- 해금 조건은 (1) 미활성, (2) 모든 재료 보유(레벨 무관), (3) 진화 종류별 플래그다. 플래그 `BladePlotKill`·`RepairCompleted`·`HarvestNearBuilding`(`WaveRuntimeState.cs:60`)은 한 번 `true`가 되면 유지되는 **횟수 없는 사건 표시**이며 상태 codec(`WaveRuntimeCodec.cs:81-83`)에 들어가 상태 해시에 반영된다.
- 카드 제시(`ProgressionSystem.Deal`, 44-64): 무기·도구·`WaveCards()`를 ID 서수 정렬 → `CanOffer`·금지 목록 필터 → 첫 제시의 보장 재료 카드(추첨 없음) → 잠금 카드(추첨 없음) → 3장이 될 때까지 `random.Next(pool.Count)` 균등 추첨. wave는 `catalog.Runtime`이 없어 가중치 경로(`WeightedOffer`)를 쓰지 않는다. 진화 우선 제시·가중치·카드별 상한은 없다. 보유 장비는 최대 레벨(12) 전까지 다른 카드와 같은 확률로 다시 나온다. 희귀도는 선택 시점에 1회 추첨한다(69행).
- RNG는 `TrackedRandom` 하나다(`WorldState.cs:92`). 적 출현 위치와 전투도 같은 흐름에서 뽑으므로 **카드 추첨 횟수나 범위가 한 번만 달라져도 이후 모든 상태가 달라진다.**
- 진화 종류 enum은 Core(`WaveRuntimeContract.cs:13`, `WaveLegacyCompiler.cs:138-165`, `WavePrimitiveSupport.cs`), Sim 로더(`WaveContentLoader.cs:64` 정확한 개수, 226-228·261-271 종류별 재료), 도구(`tools/wave1a-content.mjs`), Unity(`WorldWave.cs`, `WavePresentation.cs`, `UiRunPanels.cs`)에 흩어져 있다.
- first-playable은 이미 데이터 방식이다. 프로필의 `evolutionRequirements`(재료별 `minimumLevel`)와 `evolutionGrowthRequirements`(성장 횟수)를 `FirstPlayableRuntime`이 읽는다. 이 ADR은 이 방식을 wave에 맞춰 가져오며 first-playable 동작은 바꾸지 않는다.

### 고정된 해시

- `benchmarks/wave-history`(27000틱 2개)와 `benchmarks/wave-c05`의 끝 상태 해시. 데이터는 `5118e2c`로 고정하고 **현재 Core**로 다시 실행한다(`tools/verify-wave-history-parity.mjs`). Core 동작이 바뀌면 깨지고 데이터 변경에는 영향받지 않는다.
- `core/tests/SowSiege.Tests/Fixtures/wave-primitive-baseline/hashes.json`(seed 30000–30004, 3000틱)과 catalog 해시 `D108BA8D…`(`WavePrimitiveTests.cs:99-113`, `WaveEnemySeparationTests.cs:141-149`).
- `tools/verify-wave1a-target-parity.mjs`는 새로 만든 재생을 두 Core 대상에서 비교한다. 고정 해시는 없다.
- `data/` 파일 바이트가 바뀌면 데이터 출처 해시가 바뀐다. 이것은 상태 해시 변경이 아니다.

## 결정

### (a) 진화 해금 조건을 진화 정의의 데이터로 옮긴다

wave 진화 정의에 `unlock`을 둔다. 해금 판정은 **하나의 평가기**가 이 필드만 읽는다. `ProgressionSystem.WaveCards()`의 `Kind switch`와 테스트 전용 `EligibleEvolution` 경로를 이 평가기로 합친다.

| 필드 | 뜻 | 기본값(필드가 없을 때) |
|---|---|---|
| `unlock.inputs[].id` | 보유해야 하는 재료 | 정의의 `inputIds` 전부 |
| `unlock.inputs[].minLevel` | 재료 최소 레벨 | `1`(보유만) |
| `unlock.inputs[].requireMaxLevel` | 재료가 최대 레벨이어야 함 | `false` |
| `unlock.events[].event` | Core가 감지하는 이름 있는 사건 | `programs[id]`의 `unit:event-gate.condition` |
| `unlock.events[].minCount` | 그 사건의 최소 횟수 | `1` |

- **경계:** 데이터는 "어떤 사건이 몇 번"을 고른다. 사건의 의미(예: "성장물 근처 처치")는 Core의 사건 생산 코드가 정한다. 새 사건 이름은 Core 코드와 스키마 enum을 함께 추가하는 일이며 진화별 분기가 아니다(P8). 사건 이름은 의미 enum이며 콘텐츠 ID 참조로 위장하지 않는다.
- `minCount: 1`은 지금의 유지형 플래그와 같다. 지금의 세 플래그를 그대로 쓰므로 상태 표현이 바뀌지 않는다. `minCount > 1`이 데이터에 처음 나올 때만 사건별 횟수 상태를 추가한다.
- **예외 훅:** 데이터로 표현할 수 없는 해금 조건만 `unlock.hook`(이름 enum)으로 Core 함수 하나에 연결한다. 현재 3종은 모두 데이터로 표현되므로 훅은 **0개**로 시작한다. 처음 필요한 훅이 생길 때 스키마 enum과 함께 추가한다.
- 진화 선택 **뒤의 효과**(아래 표의 "해금 밖 종류별 분기")는 해금 조건이 아니므로 이 ADR에서 옮기지 않는다. 후속 항목으로만 기록한다.

#### 현재 3종의 분류

| 진화(종류) | 재료 보유 | 재료 레벨 | 사건 조건 → 데이터 | 사건을 감지하는 Core 코드 | 해금 예외 훅 | 해금 밖 종류별 분기(이 ADR 범위 밖) |
|---|---|---|---|---|---|---|
| `core:sowing_sworddance` (PlantingArc) | `iron_blade`, `seed_bag`(둘 다 시작 장비라 0틱부터 보유) | 조건 없음 → `minLevel: 1` | `attack-kill-near-growth` ≥ 1 | `WaveRuntimeSystem.cs:345-348`: `melee-fan`이고 `unit:remnant-create`·`unit:harvest-contact`가 없는 장비의 처치가 grain 작업물 근처에서 일어남 | 없음 | View 공격 출처 규칙(`WorldWave.cs:51`) |
| `core:warded_masonry` (RepairOrbit) | `ward_orbit`, `carpenter_hammer` | 조건 없음 → `minLevel: 1` | `repair-completed` ≥ 1 | `WaveWorkSystem.cs:168`: `ReadyTick == -1`(피해 또는 폐허 재건)인 건물의 완성 | 없음 | 선택 시 기존 궤도 공격의 출처 이전(`ProgressionSystem.cs:134-140`), View 수리 기준점 표시(`WorldWave.cs:279`) |
| `core:sheltered_sowing` (ShelteredPlot) | `seed_bag`, `carpenter_hammer` | 조건 없음 → `minLevel: 1` | `harvest-near-building` ≥ 1 | `WaveWorkSystem.cs:250`: 살아 있는 완성 건물의 `WorkRadius` 안에서 grain 수확 보상 | 없음 | 두 번째 재료의 도구 칸 반환(`ProgressionSystem.cs:154`의 `InputIds[1]`), View 공격 출처 규칙(`WorldWave.cs:51`) |

결론: 세 해금 조건은 모두 `inputs`(레벨 1) + `events`(횟수 1)로 표현되며 지금 동작과 같다.

### (b) 카드 제시를 데이터로 보강한다

wave 런타임 정의에 선택 블록 `cardOffer`를 둔다. 필드가 없으면 지금 동작과 같다.

| 필드 | 규칙 | 기본값 |
|---|---|---|
| `evolutionPriority` | `none`: 지금처럼 같은 풀에서 추첨. `first-slot`: 해금된 진화가 있으면 보장·잠금 카드 다음 첫 빈 칸을 해금된 진화 중에서 먼저 뽑고, 나머지 칸은 원래 풀에서 뽑는다. | `none` |
| `ownedGearWeight`, `unownedGearWeight` | 보유 장비 재제시 비율. 별도 확률 추첨을 더하지 않고 같은 한 번의 가중 추첨에서 상대 가중치로 표현한다. `ownedGearWeight: 0`이면 보유 장비를 다시 내지 않는다. | `1`, `1` |
| `cards.<id>.weight` | 카드별 가중치(정수, 0 허용). 0이면 그 카드를 제시하지 않는다. 장비의 실제 가중치는 `weight × 보유/미보유 가중치`. | `1` |
| `cards.<id>.maxPicks` | 판당 선택 상한. 기존 `world.Cards` 선택 기록에서 계산하므로 새 상태가 없다. 상한에 이르면 풀에서 뺀다. | 없음(무제한) |

- 가중치 0인 카드는 추첨 전에 풀에서 뺀다. 남은 가중치는 **최대공약수로 나눈 뒤** `random.Next(합)`으로 한 번 뽑는다. 가중치가 모두 같으면 합이 `pool.Count`가 되어 `random.Next(pool.Count)`와 같은 값·같은 인덱스가 나온다. 그래서 기본값에서는 추첨 횟수와 범위가 바뀌지 않는다.
- 보장 재료 카드, 잠금, 다시 뽑기·금지·잠금 예산, 선택 시점의 희귀도 추첨 순서는 바꾸지 않는다.
- `maxPicks`는 "선택" 횟수 기준이다. 다시 뽑기로 버려진 제시는 기록이 없어 "등장" 횟수 상한은 새 상태가 필요하다. 등장 상한은 채택하지 않는다(사용자 결정 2).
- 수치는 정하지 않는다. 가중치·상한 값은 #126 보류가 풀린 뒤 별도 결정한다.

### (c) 결정성

- 모든 추첨은 지금처럼 Core의 `TrackedRandom` 하나에서 한다. 시스템 시각·`System.Random` 별도 인스턴스·Unity RNG를 쓰지 않는다.
- 새 필드의 기본값은 지금 동작과 같다. 기존 데이터(필드 없음)로는 상태 해시와 catalog 해시가 바뀌지 않아야 한다.
- catalog codec은 `WaveRuntimeCodec.cs:6-16`의 선례(`programs`는 레거시 컴파일과 다를 때만, `enemySeparation`은 있을 때만 기록)를 따른다. `unlock`·`cardOffer`는 **기본값과 다를 때만** 앞쪽 선택 블록(`wave-evolution-unlock-v1`, `wave-card-offer-v1`)으로 기록한다.

| 변경 | 기존 데이터의 상태 해시 | catalog 해시 | 비고 |
|---|---|---|---|
| `Kind switch` → `unlock` 평가기 통합(기본값 사용) | **유지** | 유지 | 같은 플래그·같은 풀·같은 순서. wave-history·baseline으로 증명 |
| `unlock` 필드를 기본값과 같게 명시 | 유지 | 유지 | codec이 기본값과 같으면 기록하지 않음 |
| `minLevel > 1`, `requireMaxLevel` 사용 | **변경**(해금 시점이 바뀔 때) | 변경 | 상태 구조는 그대로 |
| `minCount > 1` 사용 | **변경** | 변경 | 사건 횟수 상태 추가. 쓰는 데이터에서만 codec에 기록 |
| 가중 추첨으로 교체(모든 가중치 같음) | **유지** | 유지 | 최대공약수 정규화로 `Next(pool.Count)`와 같음 |
| 가중치·보유 비율 기본값 외 사용 | **변경** | 변경 | |
| `evolutionPriority: first-slot` | **변경**(해금된 진화가 있는 제시부터) | 변경 | 해금 진화가 없으면 추첨 순서 동일 |
| `maxPicks` 사용 | **변경**(상한 도달 후) | 변경 | 새 상태 없음 |
| `wave-1a.json`에 필드 추가 | 영향 없음 | — | **데이터 출처 해시**만 바뀜. wave1a 대상 일치 재생은 새로 생성 |

"변경" 항목은 기존 wave-1a 데이터에서 쓰지 않는다. 쓰려면 새 프로필 또는 새 데이터 리비전으로 두고, 기존 고정 해시는 역사 기록으로 보존한다.

### (d) 스키마 초안

`data/schema/evolution.schema.json`에 공용 정의를 두고 `wave-runtime.schema.json`이 참조한다(검사기 `tools/validate-content.mjs`는 ajv 8.20 Draft 2020 strict 모드로 스키마를 `kind` 이름으로 등록한다. 같은 설정의 최소 예제에서 `evolution#/$defs/unlock` 참조가 해석되고 `minCount: 0`을 거부함을 확인했다. 실제 스키마 적용은 구현 PR에서 검사한다). first-playable 진화 레코드에는 이 필드를 아직 넣지 않는다. first-playable은 프로필의 `evolutionRequirements`를 계속 쓴다.

`evolution.schema.json`에 추가:

```json
"$defs": {
  "unlock": {
    "type": "object",
    "additionalProperties": false,
    "required": ["inputs", "events"],
    "properties": {
      "inputs": {
        "type": "array", "minItems": 1, "maxItems": 4, "uniqueItems": true,
        "items": {
          "type": "object", "additionalProperties": false, "required": ["id"],
          "properties": {
            "id": { "type": "string", "pattern": "^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$" },
            "minLevel": { "type": "integer", "minimum": 1, "maximum": 12, "default": 1 },
            "requireMaxLevel": { "type": "boolean", "default": false }
          }
        }
      },
      "events": {
        "type": "array", "minItems": 0, "maxItems": 4, "uniqueItems": true,
        "items": {
          "type": "object", "additionalProperties": false, "required": ["event", "minCount"],
          "properties": {
            "event": { "enum": ["attack-kill-near-growth", "repair-completed", "harvest-near-building"] },
            "minCount": { "type": "integer", "minimum": 1, "maximum": 1000 }
          }
        }
      }
    }
  }
}
```

`wave-runtime.schema.json`의 `definition.evolutions.*`에 선택 필드 추가(기존 필수 목록은 그대로):

```json
"unlock": { "$ref": "evolution#/$defs/unlock" }
```

`wave-runtime.schema.json`의 `definition`에 선택 필드 추가:

```json
"cardOffer": {
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "evolutionPriority": { "enum": ["none", "first-slot"], "default": "none" },
    "ownedGearWeight": { "type": "integer", "minimum": 0, "maximum": 1000, "default": 1 },
    "unownedGearWeight": { "type": "integer", "minimum": 1, "maximum": 1000, "default": 1 },
    "cards": {
      "type": "object",
      "propertyNames": { "pattern": "^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$" },
      "additionalProperties": {
        "type": "object", "additionalProperties": false,
        "properties": {
          "weight": { "type": "integer", "minimum": 0, "maximum": 1000, "default": 1 },
          "maxPicks": { "type": "integer", "minimum": 1, "maximum": 100 }
        }
      }
    }
  }
}
```

검사기 추가 규칙(스키마 밖): `uniqueItems`는 객체 전체를 비교하므로 같은 `id`에 다른 `minLevel`을 준 항목을 막지 못한다. 따라서 `unlock.inputs[].id`와 `unlock.events[].event`는 각각 중복 없이 한 번만 나와야 한다. `unlock.inputs[].id`는 그 진화의 `inputIds`와 같은 집합이어야 한다. `unlock.events`와 `unit:event-gate.condition`이 함께 있으면 서로 같아야 한다(전환 기간). `cardOffer.cards`의 키는 그 런타임의 장비·물품·진화 ID여야 한다. 사건 enum은 Core의 사건 생산자 목록과 같아야 한다.

## 결과·비용

- 진화를 추가할 때 해금 조건을 Core 분기 없이 데이터로 정한다. 단, Sim 로더의 정확한 개수(3)·종류별 재료·설계 서명 고정(`WaveContentLoader.cs:64,148-149,226-228,261-271`)과 `tools/wave1a-content.mjs`의 고정 목록은 그대로 남아 있다. 새 진화를 넣는 일은 이 ADR 구현과 별도 작업이다.
- 해금 경로가 하나가 되어 "데이터를 바꿨는데 제시가 그대로"인 상태가 사라진다.
- 카드 제시 규칙이 데이터가 되므로 수치 결정(#126)이 코드 변경 없이 가능해진다. 결정 자체는 여전히 보류다.
- 진화 선택 뒤 종류별 효과 분기는 남는다(표의 마지막 열).

## 거절한 대안

- **조건식 문자열(작은 식 언어):** 표현력은 크지만 검증·결정성·리뷰 비용이 크다. 지금 필요한 것은 재료·레벨·사건 횟수뿐이다.
- **`Kind`를 유지하고 옆에 데이터 추가:** 판정 출처가 둘이 되어 지금의 문제(테스트 전용 경로)가 반복된다.
- **보유 장비 재제시를 별도 확률 추첨으로:** 카드마다 추첨이 하나 늘어 기본값에서도 RNG 순서가 바뀐다.
- **카드 전용 RNG 흐름 분리:** 이후 변경이 다른 상태를 흔들지 않게 하는 장점이 있지만, 도입 순간 모든 기존 해시가 바뀐다. 새 재생 형식과 함께 별도 ADR로 다룬다.
- **wave에도 제시 시점 희귀도 추첨:** first-playable과 맞출 수 있지만 추첨 순서가 바뀐다. 이 ADR 범위 밖이다.

## 구현 순서(승인 후)

1. 평가기 통합: `unlock` 기본값을 기존 `inputIds`·`unit:event-gate`에서 만들고 `WaveCards()`·`EligibleEvolution`을 한 경로로 합친다. 데이터·스키마 변경 없음. wave-history 2개, `wave-c05`, `wave-primitive-baseline` 5개, catalog 해시 `D108BA8D…`가 그대로임을 두 Core 대상에서 확인한다.
2. 스키마·로더: `unlock`·`cardOffer` 읽기, 기본값과 다를 때만 codec 기록. 기본값 명시 데이터로 1의 해시가 그대로인지 확인한다. 기본값 외 값을 쓰는 검사용 더미(`data/test/`)로 해금 시점·추첨 변화와 결정론(두 대상 일치)을 확인한다.
3. 제시 규칙: 가중 추첨·`first-slot`·`maxPicks`. 같은 해시 관문과 더미 검사. `first-slot`은 기존 wave-1a 데이터가 아니라 **새 데이터 리비전(wave-1a r2)**에서 켠다. 기존 wave-1a 해시는 역사 기록으로 보존한다.

각 PR은 해시 유지 증거(wave-history, `wave-c05`, `wave-primitive-baseline`, catalog 해시)와 AGENTS의 #200 C05 비용 비교를 함께 보고한다. View(진화 카드 표시)가 바뀌면 [#274](https://github.com/hyunlord/bs-mobile/issues/274)의 Core↔View 대응 검사를 함께 갱신한다.

## 구현 기록

- 순서 1 — [#280](https://github.com/hyunlord/bs-mobile/issues/280)/[PR #281](https://github.com/hyunlord/bs-mobile/pull/281): 카드 제시가 `WaveRuntimeSystem.ExtraCards()`의 데이터 판정을 쓴다. 재생 바이트가 main과 같다.
- 순서 2 — [#282](https://github.com/hyunlord/bs-mobile/issues/282): `unlock`·`cardOffer` 로더와 `WaveEvolutionUnlocks` 평가기. 구현하면서 정한 세부 사항:
  - 스키마에는 `default` 키워드를 넣지 않았다. 기본값은 이 ADR의 표와 Core 평가기가 정한다.
  - `cardOffer`는 순서 3 전까지 기본값만 받는다. 기본값과 다른 값은 로더가 거부해, 데이터가 조용히 무시되지 않게 한다.
  - `unlock.events`는 전환 기간 규칙대로 event-gate 조건 하나와 같아야 한다. 따라서 지금 데이터로 바꿀 수 있는 것은 재료 레벨과 사건 횟수다.
  - 스키마 파일 바이트도 데이터 출처 해시에 들어간다. 그래서 `data/schema`의 `unlock`·`cardOffer` 정의는 이 순서에서 빼고 별도 데이터 개정으로 미뤘다. 이 순서는 데이터 바이트를 바꾸지 않으므로 데이터 해시(`51DA41A2…`, crowd-411 고정값)와 고정 해시(wave-history·C05·baseline·catalog)가 그대로다. 데이터 개정 전까지 스키마는 두 필드를 모르므로, 출하 데이터는 두 필드를 쓰지 않는다.
- C05 비용 비교는 사용자 지시에 따라 순서 1·2를 합쳐 한 번에 측정한다.

## 후속 항목

### 적 스폰 그룹 확장

현재 wave 적은 `firstSpawnTick` 하나만 데이터로 갖고, 전역 `SpawnPeriodTicks`(90)·적 상한(1000)·보스 단일 출현(25200틱)으로 출현한다(`WaveEnemySystem.cs:140-178`). 출현마다 위치 추첨이 2회(들판 모드 1회)다. 후속 ADR에서 다음을 다룬다.

- 그룹별 등장 시작·**종료 시점**
- 반복 간격의 감소 곡선과 **최솟값**
- **무리 표시**(View 예고. Core↔View 대응 검사 대상에 넣는다)
- **정예와 동시 출현 금지**(wave-1a에는 정예 종류가 아직 없다. 정예 표시 필드부터 필요하다)
- 결정성: 지금 규칙을 기본값으로 표현해 기존 해시를 유지한다. 그룹 크기나 순서가 바뀌면 위치 추첨 횟수가 바뀌므로 해시 변경 항목으로 따로 표시한다.

### RewardedAdsPolicy 보상 분류

`RewardedAdsPolicy`(`unity/Assets/Game/App/RewardedAdsPolicy.cs`)는 Unity host 쪽 로컬 원장이며 슬롯은 `Result`·`Revive`·`Idle`·`Reroll`이다(`RewardedAdsContracts.cs:6`). 후속 작업에서 각 슬롯에 보상 분류(**힘 / 선택 폭 / 편의 / 외형**)를 필수로 붙이고, **힘** 분류는 검사에서 거부한다. 광고 SDK 추가는 계속 금지다.

2026-10-15 사용자 결정으로 분류를 정했다. ADR0054의 슬롯 변경은 이 ADR이 아니라 **별도 후속 PR**에서 다룬다. 그 PR 전까지 ADR0054의 현재 동작은 그대로다.

| 슬롯 | 지금 주는 것(ADR0054) | 결정된 분류 | 후속 PR에서 할 일 |
|---|---|---|---|
| `Reroll` | 카드 다시 뽑기 1회, 판당 2회까지 | 선택 폭 — 허용 | 변경 없음 |
| `Revive` | 판 안 부활 1회 | 편의로 기록 — 판당 1회 허용(구제 성격) | 판당 1회 한도 유지 확인 |
| `Result` | 결산 2배 | **힘 — 금지** | 2배 보상을 없애고 외형 또는 편의 보상으로 대체하는 안을 제시 |
| `Idle` | 방치 보상 수령(하루 3회는 가설) | 편의 — 재정의 | 재화 증량이 아니라 **수령 대기 시간 단축**으로 바꿈 |

광고 SDK 추가 금지는 그대로다.

## 사용자 결정 (2026-10-15)

1. `evolutionPriority: first-slot`을 채택한다. 기존 wave-1a 해시는 역사 기록으로 보존하고, 새 데이터 리비전(wave-1a r2)에서 켠다. 구현 순서 3에서 적용한다.
2. 카드 상한은 선택 기준 `maxPicks`로 충분하다. 등장 기준 상한은 채택하지 않는다.
3. 보유 장비 재제시는 가중치 비율 방식으로 표현한다.
4. 광고 슬롯 분류는 위 표와 같다. `Result` 2배는 힘이므로 금지하고, `Idle`은 수령 대기 시간 단축으로 재정의한다. ADR0054 변경은 별도 후속 PR로 한다.

## 검증

- 제안 PR #277과 결정 반영: 문서만 바뀌므로 `node tools/check-docs.mjs`와 docs-only CI.
- 이 ADR은 런타임 관문을 통과했다고 주장하지 않는다. 위 "현재 구조"는 정적 코드 확인이다. 해시 유지 주장은 구현 순서 1~3의 재생 검증으로 증명한다.
