# bs-mobile 지도 렌즈

`lens.yaml`은 실행 정본(`runtime`), 이전 가신 후보 풀(`candidate`), D3 카탈로그(`designed-v1`)를 분리한다. 설계 그래프 키는 원본 `revision`을 읽은 `개정:원본ID`이며(예: `designed-v1.1:core:iron_blade`), 층 필터는 `designed-v1` 계열로 유지한다. 실행 그래프 키는 `runtime:원본ID`다. 원본 ID는 `attributes.originalId`에 보존한다. 콘텐츠 수는 층별로 읽는다. 같은 ID의 대응 간선은 구현·실행·승인 증거가 아니다.

설계 콘텐츠는 `data/system-design-v1.json#/content`의 `kind` 필드로 분류한다. 시스템과 웨이브도 원본 배열에서 읽으며 수량을 고정하지 않는다. 시스템 영향 간선은 `influenceMatrix.cells[row][column]`의 행→열 방향이다. `none`은 직접 간선이 없다는 뜻이다. 카탈로그 전체를 담은 숨김 레코드는 표현식의 원천일 뿐 콘텐츠 집계에 포함하지 않는다.

설계 참조는 설계 층 안에서만 해결한다. `sourceId`는 계보이며 실행 연결로 해석하지 않는다. 실행 레코드에 `designRef`가 있으면 카탈로그 경로·개정·같은 ID·종류를 확인해 우선 연결하고, 잘못된 명시적 참조를 같은 ID 추정으로 대체하지 않는다. `designRef`가 없을 때만 같은 종류·원본 ID의 설계 노드에 대응한다.

발견 규칙은 모두 warning이며 gate가 없다. `cardText`는 필드가 있을 때 길이를 검사하고, `primitives` 누락은 해당 필드를 사용하는 설계 콘텐츠가 나타난 뒤 계산한다. bespoke는 `{id, reason}` 배열을 가진 레코드 수와 배열 항목 수를 따로 집계한다. primitive 참조 수는 설계 문법 사용량이며 구현률이 아니다. `itemScope: universal`로 명시된 물품만 장비별 연결 물품 수에서 제외한다. 해당 필드가 없던 역사 카탈로그의 물품 연결은 유지한다(`not(eq(..., "universal"))`). 데이터 판정 규칙은 이 렌즈에만 둔다.

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
