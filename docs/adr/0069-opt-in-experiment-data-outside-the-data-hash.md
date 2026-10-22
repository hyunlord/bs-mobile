# ADR0069: opt-in 실험 데이터는 데이터 트리 해시 밖에 두고, 봇 실행도 프로필로 어둠 속 영지를 켠다

- 상태: 승인 — 2026-10-19 사용자 지시(M2 두 번째 조각: 데이터 수치, 봇 시뮬레이션에서 프로필로 켜기)
- 날짜: 2026-10-19
- 연결: [ADR0064](0064-dark-estate-awakening-buildings.md) 4절(역사 재생 확인 의무), [설계](../design/m2-dark-estate-buildings.md) 10절, #315(첫 조각)
- 번호: main 최대 0068(무기 묶음 0065·0066 포함)이라 0069. 2026-10-22 main `e719918`에서 다시 확인했다.

## 맥락

- `ContentLoader.Hash`는 `data/` 아래 모든 JSON(`test/` 제외)을 하나의 트리 해시로 만든다. 이 값은 재생 헤더의 `DataHash`이고, Unity의 `CanonicalContent.DataHash`가 된다.
- 따라서 실험 파일을 하나만 더해도 모든 프로필의 `DataHash`가 바뀐다. 기본 데이터 해시가 옵트인 콘텐츠 추가로 바뀌지 않아야 새 기록의 헤더와 C05 출처가 안정적이다. 군중 411 벤치마크는 #341부터 기록 당시 데이터 해시와 리플레이·종결 고정값을 그대로 두고 실제 종결 상태 해시로 판정하며, 현재 데이터 해시가 다르면 경고만 남긴다([ADR0058](0058-data-driven-evolution-unlocks-and-card-offers.md) 보충). 그래서 이 결정의 근거는 그 벤치마크가 아니라 기본 데이터 해시의 안정성이다(2026-10-21 사용자 결정으로 다시 씀). ADR0064 4절은 데이터를 더하는 PR이 이 위험을 같은 PR에서 정하라고 했다.
- 사용자는 어둠 속 영지를 봇 시뮬레이션에서도 프로필로 켜서(기본 꺼짐) 난이도 완화를 봇으로 재기를 요구했다. 지금 영지는 대화형 세션(`InteractiveOptions.Estate`)에서만 켜진다.

## 결정

1. **opt-in 하위 트리:** `data/opt-in/` 아래 파일은 `ContentLoader.Hash`, Unity 내보내기 스냅샷(번들·해시), 같은 해시를 JS로 다시 계산하는 `tools/diagnostic-provenance.mjs`에서 뺀다. 기존 파일은 그대로이므로 모든 기존 `DataHash`·Unity 번들·재생이 그대로다.
2. **opt-in 프로필:** `data/opt-in/profiles/<이름>.json`. 프로필 이름은 `profiles/`와 `opt-in/profiles/` 중 한 곳에만 둘 수 있고, 두 곳에 있으면 거부한다. 첫 프로필은 `wave-2a-dark`다. wave-1a 프로필과 같고 `estateLightFile`·`estateLoadout`만 더한다.
3. **영지 빛 파일:** `data/opt-in/estate-light/<이름>.json` = `{ contractVersion: 1, status: "experiment", light: WaveEstateLightDefinition }`. 프로필의 `estateLightFile`이 가리키고, 로더가 wave-1a 정의에 `EstateLight`를 붙인다. 건물 수치와 효과(`effect`)는 이 파일에 있다. 스키마는 `data/schema/estate-light.schema.json`이고, `validate-content`는 `opt-in/` 아래 파일을 원래 종류로 검사한다.
4. **무결성:** opt-in 파일은 `DataHash`에 들어가지 않는다. 그 대신 다음이 실행을 고정한다.
   - 대화형 실행: 재생 형식 7이 `LoadoutCatalogHash`(영지 블록을 포함한 catalog 해시)로 고정한다.
   - 봇 실행: 결과 해시가 catalog 전체와 `RunOptions`를 포함하므로 영지 수치가 바뀌면 결과 해시가 바뀐다.
5. **봇에서 켜기:**
   - `RunOptions.EstateLoadout`(`OmitWhenNull`)를 더했다. Sim은 영지 빛이 있는 프로필이면 프로필의 `estateLoadout`을 기본으로 쓰고, `--estate-loadout off|<id>`로 바꾼다.
   - 이동 `estate-wake`는 깨울 수 있는 가장 가까운 잠든 건물로 걸어가 깨어날 때까지 머문다. 깨울 수 있다는 것은 버티기형이거나, 이미 지불했거나, 자원이 충분한 경우다. 남은 건물이 없으면 기본 이동으로 돌아간다. 영지 목록이 있을 때만 허용한다.
   - 대화형 실행은 계속 `InteractiveOptions.Estate`만 쓴다. 재생 형식 7이 기록하지 않는 `RunOptions.EstateLoadout`는 대화형에서 거부한다.

## 결과·비용

- 기존 프로필의 데이터 트리 해시, catalog·상태 해시, wave-1a 봇 결과 해시가 그대로다. 테스트로 고정했다.
  - opt-in 폴더를 지운 사본과 해시가 같다.
  - `wave-2a-dark` catalog에서 영지 블록을 빼면 wave-1a catalog 해시와 같다.
  - wave-1a 봇 seed 7·900틱 해시가 영지 코드와 무관하게 main과 같다. 처음에는 ac78982의 `5FAA2C4D…`로 고정했다.
  - **재고정(2026-10-22, 사용자 사전 승인 조건 충족):** main의 상태 인코딩 변경으로 이 값이 바뀌어 `E7963B7C…`(전체 `E7963B7C0D511AA263EE56A5C1F2197D69E06BBEFE87B6135B9EEE827BBDC570`)로 다시 고정했다. 이전 값 `5FAA2C4D…` → `F5674D58…`(ADR0058 진화 판정 데이터화, `132eb0d`, #282) → `D7661E80…`(`92d55b6`, #257) → `E7963B7C…`(`9f2e31f`, #287). 세 커밋 모두 코드 변경이 원인이고(데이터만 이전 것으로 되돌려도 같은 값), 봇 결과의 다른 필드 152개(처치·경험치·체력 등)는 바뀌지 않았다. #323 없이 main `e719918`만으로 돌린 값과 #323 병합본의 값이 같아, 영지 코드가 결과를 바꾸지 않았다.
- `DataHash`만으로는 opt-in 실험 데이터가 같은지 알 수 없다. 실험 실행을 비교하거나 재생할 때는 catalog 해시(형식 7 헤더, 봇 결과 해시)를 본다.
- `opt-in/`은 실험용 칸이다. 출시 프로필로 승격하면 일반 폴더로 옮긴다. 그때 트리 해시가 바뀌므로 그 PR이 역사 재생 영향을 다시 확인한다.

## 거절한 대안

- **프로필 단위 닫힌 파일 집합 해시로 바꾸기:** 모든 기존 `DataHash`와 기록이 한꺼번에 바뀐다.
- **고정 재생·벤치마크를 새 해시로 다시 기록하기:** 역사 기준을 잃는다. 실험 하나 때문에 C05·411 기준을 바꾸지 않는다.
- **`data/test/`에 두기:** `test/`는 출시 콘텐츠가 아닌 검사용 더미 칸이다. 실험 데이터와 뜻이 다르다. wave 로더는 `includeTest`를 거부한다.
- **wave-1a 실행 파일에 영지 블록 넣기:** wave-1a catalog 해시와 M1 비교 기준을 잃는다(ADR0064).

## 검증

- xUnit:
  - opt-in 파일이 트리 해시 밖에 있음
  - opt-in 프로필 = wave-1a + 영지 빛
  - wave-1a 봇 골든 해시
  - Unity 내보내기에서 opt-in 제외
  - 봇 `estate-wake`가 건물을 깨우고 같은 입력에 같은 해시를 냄
  - 잘못된 조합 거부
- `node tools/validate-content.mjs` 통과.
- 필수 CI.
