# M3 구역 지형·자동 장식 계약 초안

- 버전: v0.2 / 2026-10-28
- 상태: **DESIGN READY / IMPLEMENTATION WAITING FOR #485 MERGE**. 설계 준비는 런타임 완료나 그림 승인을 뜻하지 않는다.
- 추적: [#489](https://github.com/hyunlord/bs-mobile/issues/489), 선행 [#485](https://github.com/hyunlord/bs-mobile/pull/485).
- 변경 요약: v0.1의 13구역 역할 계약에 기본 동네 확대·드래그·화면 밀도 가설과 새 승인 지형 Release를 연결했다. 영지 면적과 Core 계약은 그대로다.
- 의존 상태: #485의 legacy 흐름 높이 실패 후 fixture null 모순으로 중단한 기록은 보존한다. 최신 사용자 지시는 r8 수정 1회 재개를 허용했으며, 이 문서는 그 작업의 결과를 선취하지 않는다. **이 설계 구현은 #485 병합 후에만 시작한다.** 그림·설계 정리는 병행 가능하다.

## 1. 범위와 정본

[승인 r8 배치 설계](m3a-r8-layout-proposal.md)의 13개 사각형과 [기하 계약](m3-estate-r8/GEOMETRY.md)을 사용한다. 구역 개방 시 테마가 드러나되, 새 생산시설·자원·인구·상호작용은 생기지 않는다. 저장 형식, Core 규칙, 리플레이·골든 해시, 기본 데이터 트리는 불변이다. r3~r7 설정과 그림 배치는 보존한다. r8의 마을 생산시설 조건 2, 나머지 안쪽 7구역 조건 3은 선행 설계/구현 소유이며 여기서 바꾸지 않는다.

검토 기준 B는 [m3-estate-expansion-concept-468-20261028 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-expansion-concept-468-20261028)의 `B-half-r2-390x844.png`와 `B-full-r2-390x844.png`다. 실제 Release 첨부 이름을 확인했다. 7/13 개방은 half, 13/13 개방은 full과 같은 390×844 크기로 비교하고, 같은 확대와 전체 보기를 함께 낸다. 이전 m3-estate-keyarts-r2-20261024 동네 B는 이번 반개방/전체 개방 판정 기준이 아니다. 현재 열 채의 건물만 있는 사실을 숨기거나 B의 장기 건물 수·주민 수를 구현했다고 표현하지 않는다. [구역 아트 검토](../review/estate-zone-art-482.md)의 새 콘셉트는 사용자 승인 PENDING이다. 내부 QA PASS를 런타임 자산 사용 승인으로 간주하지 않는다.

## 1A. 기본 동네 화면과 드래그 [사용자 방향 + 수치 가설]

- r8 면적은 13구역·1,664셀 그대로다. 밀도를 높이기 위해 지도·부지를 줄이지 않는다.
- 기본 화면은 킹샷처럼 끌어 둘러보는 동네다. 390×844에서 완공 건물의 불투명 몸체 높이 약 **45–55px**를 목표로 한다. 원판 패딩·그림자·이름표는 측정에서 뺀다. 각 건물 측정값과 중앙값을 함께 보고하고, 작은 우물 등 본래 비례 차이는 동일 배율로 보존한다. 건물 표시 0.75 자체를 임의 변경하지 않고 카메라 확대에서 맞춘다.
- 열린 구역이 늘어도 기본 화면이 자동으로 전체 영지를 맞추며 작아지지 않는다. 전체 지도는 사용자가 축소/전체 보기를 고를 때만 표시한다. 반개방/전체 비교에는 전체 보기 외에 **동일 기본 확대** 화면을 반드시 낸다.
- 기본 드래그 범위는 열린 구역의 외곽 +1–2셀 여유 [가설: 1.5셀]다. 닫힌 지역을 열거나 선택 가능하게 만들지 않는다. 비볼록 열린 영역을 단순 사각형으로 둘러싸며 생긴 닫힌 큰 빈 공간으로 카메라가 멀리 빠지지 않도록 경계 사례를 검사한다. 기본 화면의 허용 중심 집합은 열린 사각형들의 **합집합을 셀 좌표에서 여유만큼 확장한 영역**이다. 그 전체 AABB로 대체하지 않는다. 화면의 UI 제외 중심을 셀로 역투영해 집합 밖이면 가장 가까운 허용 점으로 clamp하고, 동률은 zone ID ordinal 순서로 정한다. 이는 View 카메라 중심 제한이며 화면 모서리에는 경계 어둠이 보일 수 있다. 전체 보기만 전체 13구역을 맞춘다. UI 상·하단, 열린 시트의 가림 영역을 제외해 중심과 한계를 계산한다.
- 기존 관성·경계 반동 구현이 있으면 재사용한다. **현재 조사한 main 3d7ddcf1에는 해당 동작이 없다:** [EstateMapModel.Pan/Clamp](../../unity/Assets/Game/App/EstateMapModel.cs)는 즉시 clamp하고, MapMin/MapExtent는 생성 당시 전체 구역의 투영 AABB다. SetFocus는 맞춤 배율용이며 드래그 한계가 아니다. [OnDrag/OnEndDrag](../../unity/Assets/Game/App/PortraitMetaPanels.EstateMap.cs)는 직접 이동 후 포인터를 제거하며 속도 적분·반동 상태가 없다. 따라서 열린 구역 한계 변경은 View 구현 항목이고, 관성/반동은 구현 시작 시 최신 main을 재확인해 재사용 가능 여부를 보고한다. 없으면 기존 기능이라고 주장하거나 이번 자산 적용에 몰래 추가하지 않는다. 이번 범위에서는 현재의 즉시 clamp 입력감을 유지하고 관성·반동을 신설하지 않는다.

### 기본 화면 밀도 측정 [가설]

390×844에서 UI를 뺀 지도 viewport, 기본 확대 고정, 7/13·13/13의 마을/숲/밭/폐허/강변 표본으로 측정한다. 밀도 목표는 연속된 복원 생활 구역을 중심으로 잡은 표본에 적용한다. 의도적으로 비워 둔 회색 휴경·어둠 경계는 별도 면적/개수로 보고하며 같은 목표로 채우지 않는다. 화면 안 alpha 영역이 50% 이상 보이는 군집/소품만 개체로 세며, 군집 그림 안 나무 여러 그루를 중복 계수하지 않는다.

| 항목 | 시제품 목표 [가설] | 판정 방법 |
|---|---:|---|
| 중형 테마 군집 | 화면당 4–8개 | 나무·밭·폐허·돌 군집; 건물 제외 |
| 작은 생활·길가 소품 | 화면당 10–18개 | 기존 마당 포함, 같은 물체 중복 제외 |
| 전체 빈 땅 비율 | 복원 생활 건조 땅 ROI의 40% 이하 | 길·광장·물·건물 예약 제외 후 실제 소품/군집 alpha>8 투영 점유 밖 전체 면적 / ROI 면적 |
| 최대 연속 빈 땅 | 같은 ROI의 20% 이하 | 실제 alpha>8 점유 밖 최대 연결 성분 면적 / ROI 면적 |
| 여백 보정 참고값 | 통과 기준 아님 | 점유 마스크를 8px 확장한 뒤 전체/최대 연결 성분을 별도로 기록; 실제 빈 땅 비율로 부르지 않음 |
| 필수 가독성 | 문·접근길 가림 0 | 기본 확대에서 실제 알파 범위로 확인 |

밀도 수치로 가독성을 희생하지 않는다. 같은 소품을 격자로 복제해 수를 채우지 않고 인접 동일 변형 반복을 검사한다. 제외 영역 때문에 목표를 못 채우면 실제값과 원인을 남기며 강제 배치하지 않는다. 전체·최대 연결 빈 땅 비율의 분모와 제외 면적, 실제 alpha 점유율을 함께 보고한다. 8px 확장 참고 마스크로 빈 땅을 줄여 통과 처리하지 않는다. 단순 숫자 충족은 B의 생활감이나 사용자 시각 승인을 대신하지 않는다.

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

정본: [estate-art.json](../../unity/Assets/Resources/EstateArt/estate-art.json). 각 entry의 source.release/file/sha256, rect, pivot, displayWidth, layer를 따른다. 아래 표는 기존 구현에서 재사용 가능한 역할이다. 새 승인 자산의 우선 매핑은 3A절을 따르며 이 문서 PR에서는 원판·atlas를 변경하지 않는다. `01/02`, `1..3` 표기는 명시된 범위의 실제 ID 집합이다.

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

## 3A. 새 사용자 승인 지형 세트 연결

[estate-r8-terrain-488-20261028](https://github.com/hyunlord/bs-mobile/releases/tag/estate-r8-terrain-488-20261028)를 우선 자산 출처로 사용한다. 2026-10-28 실제 첨부를 조회했다. **ASSETS.md는 없고 PIECES.md가 목록**이며, 아래 JSON들이 파일·SHA·canvas·기준점 정본이다. 문서 안 과거 APPROVAL_PENDING 표기는 제작 당시 기록으로 보존하되 이번 사용 승인은 최신 사용자 결정에서 온다. JSON 4종의 파일 참조 128개가 모두 Release 첨부에 존재함을 확인했다(픽셀/전체 원본 다운로드 검증은 이번 문서 작업 범위 아님).

| 테마/기존 계약 | 새 역할 후보와 실제 파일 규칙 | 정본 |
|---|---|---|
| wood / 기존 나무 군집 | forest-cluster-01..04-{lit,dark}-r1-1x.png | CLUSTERS-r1.json |
| fields / garden+soil | field-cluster-01..04-{lit,dark}-r1-1x.png | CLUSTERS-r1.json |
| ruins/hill/fallow / 기존 frontier·rocks·군집 | terrain-r8-{ruins,hill,fallow}-01..04-{bright,dark}-r2-1x.png | CLUSTERS-r1.json |
| 테마 바닥 | terrain-{forestfloor,fallowweeds,ruinsgravel,hillrockground}-{bright,dark}-r3b-512.png; fieldsoil 밝음 r4·어둠 r3b | PATCHES-final-r4.json |
| riverside / 기존 강변 소품 | terrain-r8-shore-accent-01..04-bright-r2-1x.png 및 dark-r3-1x.png | ACCENTS-final-r2.json |
| 기존 다리 3곳 | terrain-r8-bridge-{north-wood,east-fallow,south-riverside}-bright-r1-1x.png; dark는 east만 r2, 나머지 r1 | BRIDGES-final-r2.json |
| 고정 강 | river-r8-r5 chunk/context | river-r8-r5-placement.json, river-r8-r5-bank-attachments.json |

위 이름은 **Release 파일**이며 아직 EstateArt 역할 ID로 등록됐다는 뜻이 아니다. 구현 시 의미 key를 유지하고 승인 파일/상태/크기/SHA를 역할에 매핑한다. 1x와 2x는 같은 그림의 대안이므로 둘 다 런타임에 넣지 않는다. 지도 면적·강 논리 좌표·다리 예약은 r8 그대로 유지한다.

숲 1x canvas136×176/바닥128×64/pivotTopLeft(68,140), 작은 밭136×96/(68,60), 큰 밭200×112/바닥192×96/(100,56), ruins/hill/fallow160×136/바닥128×64/(80,96), shore96×72/바닥96×48/(48,44)를 사용한다. Unity pivot은 (x/width, 1-y/height)다. 캔버스 여백은 땅 면적이 아니다. 밝음/어둠은 별도 그림이므로 알파 실루엣 바이트 동일을 요구하지 않는다.

바닥은 512×512 **flat UV material**이며 이미 등각 투영된 마름모가 아니다. 전체 지면 UV로 투영하고 부지 스프라이트처럼 늘여 붙이지 않는다. 강 r5는 승인된 r8 전역 등록 chunk 12개 또는 대응 context module을 사용하며 두 방식을 같은 위치에 겹치지 않는다. 범용 반복 타일로 취급하지 않는다. 13구역 조립 PLACEMENT-r1.json은 그림 검토 배치일 뿐, 건물 예약·길 제외 검사를 통과한 게임 설정이라고 가져오지 않는다.

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
| 신규 런타임 텍스처 | 기존 atlas 재패킹 우선, 증가량 기록 | 승인 새 세트는 아직 미패킹; 0개 증가를 기정사실로 주장하지 않음 |
| 새 테마 동적 애니메이션 | 0 | 이번은 정지 환경 조각 |
| 화면 묶음 증가 | 같은 상태 기존 대비 20% 이내 목표 | Y 정렬 훼손/임계치 상향 대신 원인 보고 |

위 값은 성능 측정값·#126 경제 확정값이 아니다. 7/13·13/13 상태별 객체 수, 텍스처 전환, 실제 묶음, 계획 생성 시간과 할당량을 기록한다. 전체 지도를 매 프레임 재계획하지 않는다. 영지 화면 수치를 C05/FP01 전투 성능으로 대신하지 않는다.

## 6. 후속 Astra 역할 수요와 승인 세트 대응

| 역할 | 요청 단위/크기 가이드 | 기존 대체 | 승인 전 |
|---|---|---|---|
| 띠밭 (3A field-cluster 우선) | 4×2셀 바닥, 단계별 씨앗/싹/익음; 경계 alpha | 작은 garden+soil | 새 생산 기능 없이 장식만 |
| 언덕 암반 (3A hill 우선) | 3×3셀 군집 2종, 바닥 기준점 | rocks+관목 | 지형 높이·충돌 변경 없음 |
| 폐허 돌 군집 (3A ruins 우선) | 2×2셀 2~3종, 낮은 실루엣 | frontier+rocks | 큰 벽 중복 밀도 제한 |
| 숲 공터 지면 (3A forestfloor 우선) | 4×4셀 지면 2종, 수목 분리 | ground-grass+soil | 종이색 받침 금지 |
| 강변 생활 키트 (3A shore 우선) | 2×2셀 범위 소품 묶음 2종 | bucket/cart/grass | 강·다리 형태 변경 없음 |

이 표는 v0.1의 역할 수요 기록이다. 새 승인 세트로 충족되는 항목은 새 그림을 요청하지 않고 3A의 실제 크기·기준점을 우선한다. 미충족 변형만 별도 제안/승인한다. 64×32 셀 기준 크기는 접지 footprint이며 수직 캔버스 크기가 아니다. 납품 시 실제 canvas/alpha 경계/pivot/displayWidth/기초 footprint와 lit/dark 대응을 명시한다. 새로운 그림의 Release·SHA·사용자 승인을 기록한 후 교체한다. 콘셉트 비교판을 통째로 잘라 runtime 자산으로 간주하지 않는다.

## 7. 구현 후 검증 계약 (현재 NOT RUN)

1. 13 zone ID 정확히 한 번, role 존재, 양수 크기/수량, dark/lit 대응, r8 전용 routing을 검사한다.
2. 동일 상태 반복·저장 재로드·입력 배열 역순·카메라/줌 변경에서 배치 목록 동일. 인접 구역 추가 개방 시 기존 구역 위치 불변. 두 개방 순서가 같은 최종 배치를 만드는지 검사한다.
3. 건물·Lv0·공사·문·길 띠·강·다리 제외 영역과 예약 footprint 교차 0. 큰 조각의 투영 영역도 별도 검사한다.
4. 닫힌 곳 lit 장식 0, 개방 후 테마 등장, 기존 frontier 중복 0. 이동·완공으로 장식이 상호작용을 막지 않는지 확인한다.
5. r3~r7 계획 결과 및 역사 저장 바이트 유지. Core/data diff 0, 기존 리플레이·골든 해시 불변. View JSON이 save에 들어가지 않는지 검사한다.
6. 정상 명령으로 얻은 7/13·13/13 상태의 first-playable/wave-1a EditMode·PlayMode 기록. fixture의 자원 주입을 정상 진행으로 표현하지 않는다. 선행 #485의 실패를 이 PR 통과로 덮지 않는다.
7. 승인 B와 같은 확대 1배·3배 및 전체 보기. 구역 이름/열린 목록/시드/소스 SHA를 기록하고 숲·밭·폐허·언덕·강변의 판독성, 경계 이음새, 반복, 길 가림을 확인한다. 건물은 기존 열 채임을 표시한다.
8. 기본 확대 건물 45–55px 목표, 개방에 따른 자동 축소 없음, opened+1–2셀 드래그 한계, 전체 보기 복귀, 시트/회전/경계 드래그를 검사한다. 관성·반동은 신설하지 않고 기존 즉시 clamp 입력감을 보존한다. 비볼록 열린 합집합의 닫힌 홈과 전역 AABB 차이를 검사한다. 기본 화면의 군집/소품 수와 최대 빈 땅 면적을 표로 낸다.
9. 상태별 객체/묶음/메모리·계획 비용 표. 낮 공유 Mac 잠금과 중단 규칙을 지킨다. 같은 실패 두 번이면 중단한다.

## 8. 현재 증거와 인계

현재 증거는 main 자산 manifest 및 r8 구현 작업트리의 읽기 전용 조사다. Graft file API는 EstateDecor 정의 미색인이므로 실제 소스를 확인했다. 게임 실행·새 자산·화면 합성·Core 검사 결과는 이 문서 PR에서 주장하지 않는다. 설계 PR은 main 기반 문서만이며 병합 승인 대기다. 구현 PR은 #485 병합 후에만 별도로 만들고, 설계 예산은 실행 증거로 판정한다.
