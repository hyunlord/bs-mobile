# bs-mobile 지도 렌즈

`lens.yaml`은 실행 정본(`runtime`), 이전 가신 후보 풀(`candidate`), D3 카탈로그(`designed-v1`)를 분리한다. 설계 그래프 키는 원본 `revision`을 읽은 `개정:원본ID`이며(예: `designed-v1.1:core:iron_blade`), 층 필터는 `designed-v1` 계열로 유지한다. 실행 그래프 키는 `runtime:원본ID`다. 원본 ID는 `attributes.originalId`에 보존한다. 콘텐츠 수는 층별로 읽는다. 같은 ID의 대응 간선은 구현·실행·승인 증거가 아니다.

설계 콘텐츠는 `data/system-design-v1.json#/content`의 `kind` 필드로 분류한다. 시스템과 웨이브도 원본 배열에서 읽으며 수량을 고정하지 않는다. 시스템 영향 간선은 `influenceMatrix.cells[row][column]`의 행→열 방향이다. `none`은 직접 간선이 없다는 뜻이다. 카탈로그 전체를 담은 숨김 레코드는 표현식의 원천일 뿐 콘텐츠 집계에 포함하지 않는다.

설계 참조는 설계 층 안에서만 해결한다. `sourceId`는 계보이며 실행 연결로 해석하지 않는다. 실행 레코드에 `designRef`가 있으면 카탈로그 경로·개정·같은 ID·종류를 확인해 우선 연결하고, 잘못된 명시적 참조를 같은 ID 추정으로 대체하지 않는다. `designRef`가 없을 때만 같은 종류·원본 ID의 설계 노드에 대응한다.

발견 규칙은 모두 warning이며 gate가 없다. `cardText`는 필드가 있을 때 길이를 검사하고, `primitives` 누락은 해당 필드를 사용하는 설계 콘텐츠가 나타난 뒤 계산한다. bespoke는 `{id, reason}` 배열을 가진 레코드 수와 배열 항목 수를 따로 집계한다. primitive 참조 수는 설계 문법 사용량이며 구현률이 아니다. `itemScope: universal`로 명시된 물품만 장비별 연결 물품 수에서 제외한다. 해당 필드가 없던 역사 카탈로그의 물품 연결은 유지한다(`not(eq(..., "universal"))`). 데이터 판정 규칙은 이 렌즈에만 둔다.

`linkedItems`는 설계 물품의 `linkedToolIds`·`linkedWeaponIds`에서 대상 종류에 맞는 목록을 읽고, 대상 그래프 ID 또는 원본 ID에 연결된 물품 ID를 중복 없이 센다. `item-tool`·`item-weapon` 간선과 같은 종류·층·범용 물품 제외 조건을 적용하므로 전체 간선의 관련 없는 출처를 가져오지 않는다. 카탈로그 추출에서 원본 `id`와 `originalId`가 같은 계약을 따르며, 알 수 없거나 모호한 대상은 기존 간선 검증이 계속 거부한다. 표현식이 읽은 레코드와 필드의 출처는 보존한다.

```sh
node .lattice/verify-designed.mjs <graph.json>
node .lattice/verify-designed.mjs <historical-de2a571-graph.json> --baseline
```

`--baseline`은 요청된 역사 커밋 `de2a571`에만 사용하는 재현 오라클이다. 무진화 무기6·연결 물품0 무기15·씨앗 자루11·목수 망치7·xp 반환 없는 도구12·첫 웨이브 진화0을 확인한다. 현재 데이터에는 이 수량을 강제하지 않는다. 미래 수정은 같은 렌즈로 읽은 역사 그래프와 비교하며, 과거에 이 렌즈가 존재했던 것처럼 표현하지 않는다.

## 원본 시제품과 실행 근거

YAML 렌즈는 8개 원본 발견을 저자 해석으로 구별하고, 원본 `runtimeProjection`과 프로필 우선 적용 `effectiveEffects`를 함께 표시한다. `codeSupport:runtime-operations`는 실제 `RuntimeSystem.Apply`, `Modify`, `PlantingPosition`의 정적 처리기 근거이며 실행 관측이 아니다. 비용·보상 행렬은 숫자 열을 가진 범용 표 뷰로 표현한다. 시제품 정답 파일은 검증기에만 주며 렌즈 입력으로 사용하지 않는다.

```sh
node .lattice/verify-runtime.mjs <graph.json> <source-checkout> [prototype-oracle.json]
```

검증기는 각 ID·원본/프로필 우선 깊이·8개 발견·정적 코드 위치·비용/보상 행렬을 실제 소스와 독립 비교한다. 선택적 시제품 오라클 인자는 고정 시제품의 216개 ID 분류와 지정 수치를 추가 검증한다. 현재 CI는 소스와 추출 결과의 일치를 검증하며 미래 콘텐츠 수량을 고정하지 않는다.

`linkedItems`는 designed-v1에만 붙이고 `waveIndex`도 designed-v1에서만 계산한다. 다른 층의 의미 없는 빈 투영을 만들지 않으며, 각 층의 `designIntent`와 원본 `effect` 속성은 그대로 보존한다.

## 공통 동작 단위 지원

`docs/design/runtime-primitive-support.json`은 별도 `runtime-support` 층이다. 설계 등록 단위마다 정적 지원 경계·정확한 Core 허용 파라미터 조합·처리기·작성된 테스트·제한을 표시한다. 설계 콘텐츠에서 단위로 향하는 간선은 문법 사용 관계이며 실행 증거가 아니다. `partial-runtime`, `projection-boundary`, `loader-boundary`, `unsupported`를 구별하고 실행 관측·의미 승인·화면 관찰은 별도 미판정 값으로 보존한다. 기존 `codeSupport:runtime-operations`와 역사 14-operation 계약은 유지한다.

```sh
node .lattice/verify-primitive-support.mjs <graph.json> [source-root]
```

실제 그래프의 노드·facet·지원 표·설계 사용 간선을 원장과 대조하고, 원장의 exact tuple을 Core 허용 조합과 대조한다. 코드 링크/테스트 심벌의 존재를 실행 통과로 보고하지 않는다. 현재 그래프에만 새 원장 검증을 적용하며 역사 스냅샷에 오늘의 지원 근거를 소급하지 않는다.
