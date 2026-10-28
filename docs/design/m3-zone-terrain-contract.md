# M3 구역 지형·자동 장식 계약 초안

- 버전: v0.1 / 2026-10-28
- 상태: **DESIGN READY / IMPLEMENTATION BLOCKED #485**. 설계 준비는 런타임 완료나 그림 승인을 뜻하지 않는다.
- 추적: [#489](https://github.com/hyunlord/bs-mobile/issues/489), 선행 [#485](https://github.com/hyunlord/bs-mobile/pull/485).
- 변경 요약: r8의 구역 개방 결과를 숲·밭·폐허·언덕·강변 그림으로 읽게 하는 View 전용 역할·배치·검사 계약을 제안한다.
- 중단 기록: #485는 legacy 흐름의 높이 실패 후 fixture null 모순으로 반복 실패 중단 상태다. 사용자 재개 지시와 선행 수정·검증 전에는 이 설계의 게임 구현을 시작하지 않는다. 그림 담당자는 이 문서를 병행 참고할 수 있다.

## 1. 범위와 정본

[승인 r8 배치 설계](m3a-r8-layout-proposal.md)의 13개 사각형과 [기하 계약](m3-estate-r8/GEOMETRY.md)을 사용한다. 구역 개방 시 테마가 드러나되, 새 생산시설·자원·인구·상호작용은 생기지 않는다. 저장 형식, Core 규칙, 리플레이·골든 해시, 기본 데이터 트리는 불변이다. r3~r7 설정과 그림 배치는 보존한다. r8의 마을 생산시설 조건 2, 나머지 안쪽 7구역 조건 3은 선행 설계/구현 소유이며 여기서 바꾸지 않는다.

검토 기준 B는 승인된 m3-estate-keyarts-r2-20261024 동네 그림이다. 7/13·13/13 개방 화면을 같은 확대와 전체 보기에서 비교한다. 현재 열 채의 건물만 있는 사실을 숨기거나 B의 장기 건물 수·주민 수를 구현했다고 표현하지 않는다. [구역 아트 검토](../review/estate-zone-art-482.md)의 새 콘셉트는 사용자 승인 PENDING이다. 내부 QA PASS를 런타임 자산 사용 승인으로 간주하지 않는다.

## 2. JSON 역할 계약 [제안]

실행 시에는 View Resources의 별도 `EstateZoneThemes.json`으로 두되, **이 PR에는 실행 파일을 만들지 않는다**. 기존 EstateDecorRules의 전역 기본값을 바꾸지 않고 `m3a-prototype-r8`에만 연결한다. JsonUtility가 읽는 배열 기반 DTO를 사용한다. 아래 JSON은 문서 예제이며 수치는 [가설]이다.

```json
{
  "version": "estate-zone-themes-v1",
  "configVersions": ["m3a-prototype-r8"],
  "seed": 20261028,
  "zones": [{"id": "zone:inner-northeast-wood", "theme": "wood"}],
  "themes": [{
    "id": "wood", "candidateSpacingCells": 4, "density": 0.6,
    "maxCount": 12,
    "variants": [{
      "key": "broadleaf-a", "role": "estate-tree-broadleaf-01-lit",
      "darkRole": "estate-tree-broadleaf-01-dark",
      "width": 128, "reserveCells": [3, 3]
    }]
  }]
}
```

예제는 축약이며 실제 계약 검사는 아래 13개 ID가 정확히 한 번씩 있는지 검사한다. `key`는 교체에도 유지하는 의미 ID, `role`은 atlas 조회 ID다. `width`는 기존 EstateArt와 같은 비확대 표시 px, `reserveCells`는 투영 전 셀 단위 폭·높이다. 셀 64×32와 atlas 원판 px를 혼동하지 않는다. pivot·displayWidth·source SHA는 기존 estate-art.json을 정본으로 삼는다. 향후 Astra 그림은 승인 후 role 참조와 필요 크기만 교체하며 저장 이관은 없다. 잘못된 role·중복 ID는 검사 실패이지 조용한 fallback 대상이 아니다.

| zone: 뒤 ID | 테마 | 개방 후 읽혀야 할 것 |
|---|---|---|
| start | village | 열 채 부지·광장·길·기존 마당 |
| outskirts-north | approach | 성긴 울타리·길가 군집 |
| outskirts-east | approach | 성긴 울타리·길가 군집 |
| outskirts-south | approach | 성긴 울타리·길가 군집 |
| outskirts-west | approach | 성긴 울타리·길가 군집 |
| inner-village | village | 마당·작은 텃밭·생활 소품 |
| inner-northeast-wood | wood | 서로 다른 나무 군집·작은 공터 |
| inner-west-hill | hill | 암석·성긴 관목, 지형 높이 추가 없음 |
| inner-windmill | meadow | 초지·작은 밭, 미승인 풍차 신설 없음 |
| inner-fields | fields | 흙 패치·텃밭·밭 울타리 |
| inner-fallow | fallow | 넓은 휴경지·성긴 복원 군집 |
| inner-ruins | ruins | 폐허 벽·분류된 듯한 돌·빈 울타리 |
| inner-riverside | riverside | 기존 강둑·강변 생활 소품 |

## 3. 승인 자산 역할 목록과 출처

정본: [estate-art.json](../../unity/Assets/Resources/EstateArt/estate-art.json). 각 entry의 source.release/file/sha256, rect, pivot, displayWidth, layer를 따른다. 아래는 현재 존재하는 역할만 사용하며 원판 변경·새 그림 생성을 요구하지 않는다. `01/02`, `1..3` 표기는 명시된 범위의 실제 ID 집합이다.

| 용도 | 역할 | 승인 Release 출처 |
|---|---|---|
| 연속 바닥·흙 | ground-grass-r4, soil-patch-r4-01..06 | m3-lv0-sites-review-20261026 |
| 숲·관목 | estate-tree-conifer/broadleaf/shrub-01/02-lit 및 -dark | m3-riverbank-clusters-review-416-20261026 (dark r2) |
| 군집 | cluster-fallow/sage/restored-1..3 | m3-estate-autodecor-20261024 |
| 마당·밭 | yard-earth, yard-grass, yard-stone, garden | m3-estate-autodecor-20261024 |
| 생활 | sacks, cart, bucket, firewood, grass, rocks | m3-estate-autodecor-20261024 |
| 울타리·목장 | house-fence-kit, field-fence-segment, field-fence-corner, sheep-paddock | m3-estate-life-kits-20261024 |
| 폐허 | frontier-ruin/dead-tree/broken-fence/empty-armor/dry-well/stump-tracks | m3-estate-life-kits-r2-20261024 |
| 강변 | bank-straight/inside/outside/bridge-01/02-lit 및 -dark | m3-riverbank-clusters-review-416-20261026 (dark r2) |
| 기존 강·다리 | riverbank-patch, river-bend-patch, bridge-ne-sw/nw-se-base 및 -rail | m3-estate-life-kits-20261024 |

`garden`은 표시 폭 28인 작은 텃밭이다. 확대해서 대형 경작지로 사용하지 않고 흙 패치·울타리와 군집으로 묶는다. 현재 `rocks`는 작은 돌 소품이므로 대형 채석장처럼 확대하지 않는다. 장식 밭과 실제 생산 건물은 구별한다. 양 목장은 식량 생산을 추가하지 않는다. 기존 나무·폐허의 섬 받침 제거 수정본을 유지한다.

## 4. 안정 배치·제외 계약

- 시드 입력: `seed|themeVersion|zoneId|candidateCellX|candidateCellY|variantKey`. [AstraDecor.Hash](../../unity/Assets/Game/App/AstraDecor.cs)의 안정 해시를 재사용한다. 새 RNG 의존성·시스템 시간·GetHashCode는 사용하지 않는다.
- 카메라, 확대, 현재 열린 구역 수, 리스트 순서, 프레임 번호를 시드에 넣지 않는다. 개방 순서가 달라도 동일 상태는 동일 배치다.
- 구역별 정수 후보 격자 → 안정 jitter/variant → hash 우선순위와 ordinal key 동점 처리 → 제외 검사 → 수량 상한. 모든 좌표는 지도 기준이며 카메라 이동으로 다시 샘플링하지 않는다.
- 겹침 배제는 후보 중심점만 보지 않고 예약 footprint 및 투영된 표시 범위를 본다. 대상: 모든 건물/Lv0/공사 부지, 마당, 문과 접근부, 실제 곡선 길 띠 폭, 광장, r8 강 셀, 다리·난간·접속부, 기존 landmark.
- 경계 후보의 예약 영역은 해당 구역 안에 둔다. 이웃 개방으로 기존 구역의 후보를 밀어내지 않는다. 화면상의 수관 겹침은 Y 정렬을 따르되 문·길 가림은 검사한다.
- 테마 구역에서 기존 전역 empty-land 장식을 대체한다. 기존 마당·길가는 유지하되 중복 장식과 충돌하면 명시적 우선순위(부지/길 > 마당 > 테마 > 잔장식)로 제거한다.
- 닫힘/개방의 동일 후보 위치를 유지하며 dark/lit 또는 tint를 바꾼다. 기존 closed frontier와 이중 생성하지 않는다. 개방은 복원 완료가 아니므로 기존 fallow/sage/restored 상태를 유지한다.
- 건물 이동·공사·완공으로 장식이 겹치면 장식을 숨긴다. 장식이 Core 이동·건설을 거부하거나 저장에 장애물을 쓰지 않는다. 물 위 이동의 기존 규칙을 바꾸지 않는다.
- 기존 큰 지면과 고정 강·다리 예약이 우선이다. r8 선행 구현의 강 예약과 테마 장식 예약은 별도 검사한다.

## 5. 렌더 순서와 비용 [제안]

[EstateDecor](../../unity/Assets/Game/App/EstateDecor.cs)의 레이어·발 기준 Y 정렬을 그대로 따른다. 바닥/흙 패치는 길보다 아래, 기존 마당·그림자 아래에 둔다. 나무·돌·울타리는 Standing의 Y 정렬에 합류한다. 다리 전경 난간, 어둠, Landmark의 기존 순서를 유지한다. 모든 장식은 입력 비차단이다. atlas 묶음을 줄이려고 앞뒤 Y 순서를 뒤집지 않는다.

| 예산 [가설] | 초안 | 검증/실패 시 처리 |
|---|---:|---|
| 큰 테마 군집 | 구역당 최대 12 | 작은 주변 구역은 제외 검사로 더 적게; 억지 충원 금지 |
| 작은 추가 소품 | 구역당 최대 16 | 마당·기존 장식과 중복 계수하지 않음 |
| 신규 런타임 텍스처 | 0 | 기존 atlas 역할로만 구성 |
| 새 테마 동적 애니메이션 | 0 | 이번은 정지 환경 조각 |
| 화면 묶음 증가 | 같은 상태 기존 대비 20% 이내 목표 | Y 정렬 훼손/임계치 상향 대신 원인 보고 |

위 값은 성능 측정값·#126 경제 확정값이 아니다. 7/13·13/13 상태별 객체 수, 텍스처 전환, 실제 묶음, 계획 생성 시간과 할당량을 기록한다. 전체 지도를 매 프레임 재계획하지 않는다. 영지 화면 수치를 C05/FP01 전투 성능으로 대신하지 않는다.

## 6. 후속 Astra 그림 요청 목록 [제안·미승인]

| 역할 | 요청 단위/크기 가이드 | 기존 대체 | 승인 전 |
|---|---|---|---|
| 띠밭 | 4×2셀 바닥, 단계별 씨앗/싹/익음; 경계 alpha | 작은 garden+soil | 새 생산 기능 없이 장식만 |
| 언덕 암반 | 3×3셀 군집 2종, 바닥 기준점 | rocks+관목 | 지형 높이·충돌 변경 없음 |
| 폐허 돌 군집 | 2×2셀 2~3종, 낮은 실루엣 | frontier+rocks | 큰 벽 중복 밀도 제한 |
| 숲 공터 지면 | 4×4셀 지면 2종, 수목 분리 | ground-grass+soil | 종이색 받침 금지 |
| 강변 생활 키트 | 2×2셀 범위 소품 묶음 2종 | bucket/cart/grass | 강·다리 형태 변경 없음 |

64×32 셀 기준 크기는 접지 footprint이며 수직 캔버스 크기가 아니다. 납품 시 실제 canvas/alpha 경계/pivot/displayWidth/기초 footprint와 lit/dark 대응을 명시한다. 새로운 그림의 Release·SHA·사용자 승인을 기록한 후 교체한다. 콘셉트 비교판을 통째로 잘라 runtime 자산으로 간주하지 않는다.

## 7. 구현 후 검증 계약 (현재 NOT RUN)

1. 13 zone ID 정확히 한 번, role 존재, 양수 크기/수량, dark/lit 대응, r8 전용 routing을 검사한다.
2. 동일 상태 반복·저장 재로드·입력 배열 역순·카메라/줌 변경에서 배치 목록 동일. 인접 구역 추가 개방 시 기존 구역 위치 불변. 두 개방 순서가 같은 최종 배치를 만드는지 검사한다.
3. 건물·Lv0·공사·문·길 띠·강·다리 제외 영역과 예약 footprint 교차 0. 큰 조각의 투영 영역도 별도 검사한다.
4. 닫힌 곳 lit 장식 0, 개방 후 테마 등장, 기존 frontier 중복 0. 이동·완공으로 장식이 상호작용을 막지 않는지 확인한다.
5. r3~r7 계획 결과 및 역사 저장 바이트 유지. Core/data diff 0, 기존 리플레이·골든 해시 불변. View JSON이 save에 들어가지 않는지 검사한다.
6. 정상 명령으로 얻은 7/13·13/13 상태의 first-playable/wave-1a EditMode·PlayMode 기록. fixture의 자원 주입을 정상 진행으로 표현하지 않는다. 선행 #485의 실패를 이 PR 통과로 덮지 않는다.
7. 승인 B와 같은 확대 1배·3배 및 전체 보기. 구역 이름/열린 목록/시드/소스 SHA를 기록하고 숲·밭·폐허·언덕·강변의 판독성, 경계 이음새, 반복, 길 가림을 확인한다. 건물은 기존 열 채임을 표시한다.
8. 상태별 객체/묶음/메모리·계획 비용 표. 낮 공유 Mac 잠금과 중단 규칙을 지킨다. 같은 실패 두 번이면 중단한다.

## 8. 현재 증거와 인계

현재 증거는 main 자산 manifest 및 r8 구현 작업트리의 읽기 전용 조사다. Graft file API는 EstateDecor 정의 미색인이므로 실제 소스를 확인했다. 게임 실행·새 자산·화면 합성·Core 검사 결과는 이 문서 PR에서 주장하지 않는다. 설계 PR은 main 기반 문서만이며 병합 승인 대기다. 구현 PR은 #485 재개·수정·검증과 사용자 지시 후 별도로 만들고, 설계 예산은 실행 증거로 판정한다.
