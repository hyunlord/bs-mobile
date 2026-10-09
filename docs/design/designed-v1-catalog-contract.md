# designed-v1 카탈로그와 실행 연결 계약

상태: 설계 데이터 형식 승인, 로스터는 사용자 검토 전. [D3 의뢰서](09_ASTRA_GOAL_bs-mobile_phase2b-system-design_v2.md), [#135](https://github.com/hyunlord/bs-mobile/issues/135).

## 정본 위치와 읽기

- 제안 설계 정본: [`data/system-design-v1.json`](../../data/system-design-v1.json).
- 형식: UTF-8 JSON 객체. `contractVersion: 1`, `revision: "designed-v1"`, `approvalStatus: "needs-decision"`.
- JSON Schema: [`data/schema/system-design-v1.schema.json`](../../data/schema/system-design-v1.schema.json). 스키마와 의미 검사는 기존 콘텐츠 검사의 일부이며 수량·분포·참조도 검사한다.
- `content` 배열의 각 레코드는 `id`, `name`, `kind`, `tags`, `designStatus: "designed-v1"`, `origin`, `concept`, `effect.trigger/benefit/cost`, `researchRefs`를 가진다. 종류별 행동 계약을 같은 레코드에 둔다.
- 물품의 `linkedToolIds`와 `linkedWeaponIds`, 진화·장원의 `inputIds`는 종류까지 검사하는 콘텐츠 참조다. 구현 웨이브는 이 선행 콘텐츠가 같은 웨이브 또는 이전 웨이브에 있도록 검사한다.
- `systems`는 목적·입력·출력·플레이어 결정을 담는다. `influenceMatrix.systemIds`의 순서가 행과 열 양쪽에 적용된다. `cells[row][column]`은 **행 시스템이 열 시스템에 주는 영향**이다. `none`은 제안된 직접 영향 없음이며 전이적 영향 부재를 뜻하지 않는다.
- `visualLanguage`는 표현 규칙, `implementationWaves`는 승인 후 구현 순서 제안이다. 순서에 들어갔다고 구현되거나 승인된 것은 아니다.

## ID와 개정 구분

기존 콘텐츠는 동일한 ID를 유지한다. 예: `core:iron_blade`, `meta:chapter_1`. 새 콘텐츠도 기존 `core:`·`meta:` 네임스페이스를 사용한다. 별도 `design:` ID로 복제하지 않는다. `origin: "revised"`는 동일 ID의 현재 레코드를 `sourceId`로 가리킨다. `origin: "new"`는 기존 정본에 없는 ID이며 `sourceId`를 갖지 않는다. 새 ID라고 기존 콘텐츠와 충돌해도 되는 것은 아니다.

설계 레코드의 식별자는 **(revision, id)**다. 같은 콘텐츠 ID의 현재 실행 레코드와 새 설계 레코드를 하나로 덮어쓰거나 두 개의 새 콘텐츠로 집계하지 않는다. 현재 실행 정본은 기존 종류별 JSON과 `data/meta/progression.json`, 선택된 실행 프로필이다. D3 카탈로그의 진화 입력·도구 연결·보스·자재 출처/소비처는 **D3 content 내부**의 ID로 해석한다. 현재 풀에만 있는 ID로 제안 로스터의 누락을 메우지 않는다.

`sourceId`는 개정 계보이고 `researchRefs`는 [조사 카탈로그](../research/mechanics-catalog.md)의 근거 행이다. 둘 다 실행 증거가 아니다. 기존 설계 풀에서 v1에 채택하지 않은 항목은 삭제되거나 실패 판정을 받은 것이 아니며 이번 로스터 집계 밖이다.

## 향후 실행 레코드의 designRef

후속 구현 PR은 자신이 구현 대상으로 삼은 설계 레코드를 다음 모양으로 가리켜야 한다. **이번 D3에서는 현재 strict 실행 로더에 필드를 추가하지 않는다.** 아래는 후속 실행 스키마·로더 변경 시 함께 적용할 규칙이다.

```json
{
  "designRef": {
    "catalog": "data/system-design-v1.json",
    "revision": "designed-v1",
    "id": "core:iron_blade"
  }
}
```

- `catalog`는 저장소 상대 경로이며 임의 URL이나 상위 경로 탈출을 허용하지 않는다.
- `revision`은 대상 카탈로그의 개정과 일치하고 `id`는 그 content 안의 동일 kind 레코드로 해석돼야 한다. 기존·신규 콘텐츠 모두 실행 레코드의 id와도 일치해야 한다.
- 구현 변경 PR에서 스키마·로더·참조 검사를 함께 추가한다. 해결되지 않는 링크는 오류로 처리하며, 자동으로 최신 설계로 바꾸지 않는다.
- `designRef` 존재는 **설계 연결만** 증명한다. operation 처리기 존재, 프로필 선택, 실제 발동, 고유 행동의 일치, 사용자 승인은 각각 별도 증거다. 일부만 구현했다면 남은 계약과 검증 범위를 함께 기록한다.
- 설계의 다음 개정은 새 revision으로 식별하고 과거 구현의 연결 대상을 소급해서 갈아끼우지 않는다.

## Lattice 소비 경계

Lattice는 이 문서와 스키마를 통해 카탈로그를 읽을 수 있다. 이 저장소의 설계 작업은 뷰어·MCP·렌즈를 구현하지 않는다. 설계 노드와 실행 노드를 revision 및 원천별로 구분한 뒤 콘텐츠 id로 연결한다. `designed-v1`은 실행 상태가 아니며 `runtimeProjection`으로 변환하지 않는다. 시스템 행렬은 명시된 방향으로 표시하고, 제안된 영향과 현재 Core 호출 관계를 같은 사실로 표시하지 않는다.

기존 Lattice 초기 지도에서 제외된 중첩 메타 레코드와 개정 식별 관련 리뷰는 [#141](https://github.com/hyunlord/bs-mobile/issues/141)에 남겼다. 새 카탈로그가 만들어졌다는 이유로 기존 렌즈가 이미 지원한다고 보고하지 않는다.

## 보존과 검증

현재 실행 데이터·프로필·계수·Core/Unity 동작은 보존한다. 다만 현재 출처 해시는 모든 data JSON을 포함하므로 새 카탈로그·스키마를 추가하면 데이터 출처 해시는 달라진다. 이를 숨기려고 역사 해시 알고리즘을 바꾸지 않는다. 설계 검사 통과는 게임 재미·가독성·실기 성능을 증명하지 않는다. 사용자 로스터 검토 후 별도 구현 이슈→PR→CI에서 각 동작을 검증한다.
