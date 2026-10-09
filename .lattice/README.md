# bs-mobile 지도 렌즈

`lens.json`은 실행 정본(`runtime`), 이전 가신 후보 풀(`candidate`), D3 카탈로그(`designed-v1`)를 분리한다. 그래프 키는 `층:원본ID`이며 원본 ID는 `attributes.originalId`에 보존한다. 콘텐츠 수는 층별로 읽는다. 같은 ID의 대응 간선은 구현·실행·승인 증거가 아니다.

설계 콘텐츠는 `data/system-design-v1.json#/content`의 `kind` 필드로 분류한다. 시스템과 웨이브도 원본 배열에서 읽으며 수량을 고정하지 않는다. 시스템 영향 간선은 `influenceMatrix.cells[row][column]`의 행→열 방향이다. `none`은 직접 간선이 없다는 뜻이다. 카탈로그 전체를 담은 숨김 레코드는 표현식의 원천일 뿐 콘텐츠 집계에 포함하지 않는다.

설계 참조는 설계 층 안에서만 해결한다. `sourceId`는 계보이며 실행 연결로 해석하지 않는다. 실행 레코드에 `designRef`가 있으면 카탈로그 경로·개정·같은 ID·종류를 확인해 우선 연결하고, 잘못된 명시적 참조를 같은 ID 추정으로 대체하지 않는다. `designRef`가 없을 때만 같은 종류·원본 ID의 설계 노드에 대응한다.

발견 규칙은 모두 warning이며 gate가 없다. `cardText`는 필드가 있을 때 길이를 검사하고, `primitives` 누락은 해당 필드를 사용하는 설계 콘텐츠가 나타난 뒤 계산한다. bespoke는 명시적 플래그 또는 primitives의 `bespoke` 항목으로 집계한다. 데이터 판정 규칙은 이 렌즈에만 둔다.

```sh
node .lattice/verify-designed.mjs <graph.json>
node .lattice/verify-designed.mjs <historical-de2a571-graph.json> --baseline
```

`--baseline`은 요청된 역사 커밋 `de2a571`에만 사용하는 재현 오라클이다. 무진화 무기6·연결 물품0 무기15·씨앗 자루11·목수 망치7·xp 반환 없는 도구12·첫 웨이브 진화0을 확인한다. 현재 데이터에는 이 수량을 강제하지 않는다. 미래 수정은 같은 렌즈로 읽은 역사 그래프와 비교하며, 과거에 이 렌즈가 존재했던 것처럼 표현하지 않는다.
