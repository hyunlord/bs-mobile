# 새싹 문장 2·3차 제작 결과 (#405)

관문: 제작·내부 검사 통과 — 13종. 최종 그림 승인은 사용자 대기.

## 사용자 결정

1차 levy_banner / fx.ward_orbit.shield 2종은 사용자 승인이다. 이전 보고의 깃대/테두리 차이 판정은 당시 기록으로 보존한다. 이후 외형 기준은 bbox 각 변 차이 8px 이내 및 바닥/기준점 유지이며, 허용 범위 안에서 더 맞추기 위한 재생성은 하지 않는다.

씨앗 왕관은 왕관 형태를 유지하고 왕가 금색 장식·보석을 제거한 곡식 고리 중앙에 새싹 하나를 둔다.

## 전달

- [2차 5종 Release](https://github.com/hyunlord/bs-mobile/releases/tag/sprout-emblem-batch2-405-20261026): ruin_keystone, rally_boundary, heirloom_service, rally_kitchen, person.vassal.
- [3차 8종 Release](https://github.com/hyunlord/bs-mobile/releases/tag/sprout-emblem-batch3-405-20261026): carpenter_hammer 완성/손상/폐허, harvest-flag, meal_oath, crop_guard_signet, wind_seed_flag, seed_crown.

각각 원본 crop와 같은 크기의 투명 PNG 및 1배/3배 전후 비교판을 제공한다. ASSETS.md는 크기·기준점·원본 atlas 좌표·pivot·SHA256, PROMPTS.md는 편집 지시 명세를 기록한다. 2차 완료를 먼저 보고한 후 차단 없이 3차를 진행했다.

built-in image_gen으로 원본 crop와 기준 새싹을 참조하여 편집했다. Swift로 알파 보존 축소/정렬 및 비교판을 만들었다. 씨앗 1개·휜 줄기·높이가 다른 잎 2장의 문장을 사용한다. 돌/반지는 같은 재질의 부조, 깃발/천/방패는 크림색 또는 원래 잉크색을 사용한다.

## 검사와 한계

| 항목 | 2차 | 3차 |
|---|---:|---:|
| 원본 crop 크기 및 투명도 | 5/5 | 8/8 |
| bbox 각 변 최대 차이 | 3px | 5px |
| 기준점 래스터 반올림 최대 오차 | 0.5px | 0.5px |
| 전후 비교판 시각 검사 | 5/5 | 8/8 |
| 사용자 최종 그림 승인 | 대기 | 대기 |

아이콘은 불투명 bbox 중심, 건물/수확깃발은 bbox 바닥 중심, 가신은 오른쪽 부츠 최하단 접점으로 정렬했다. 원본 manifest pivot을 메타데이터로 보존했다. 이는 crop 기반 오프라인 검사이며 Unity 실행 증거가 아니다. 생성 편집이므로 문장 외 픽셀 동일을 보장하지 않는다.

원본 crop 출처 commit: `e08854568b4bfb060a9dc3dbc3190e0cbfb308aa`. Release 대상 commit: `2ce799a81800714972dfc3c2f68ba57fdb49cc9a`. 원본 atlas, 게임 코드·데이터 및 기존 Release 자산은 덮어쓰지 않았다. 변경은 이 제작 결과 문서뿐이다. 로컬 원본/생성 원본/검사는 `output/sprout-replacement-batches/`에 보존했다.

## measured_volley

3차 Release의 `measured-volley-original-1x4x8x.png`로 원판을 확대 확인했다. 눈금과 중심축이 있는 시계/계기판 형태로 판단했고, 백합·사자 문장이 확인되지 않아 원본을 유지했다. 시각 해석이며 원작자의 의도를 단정하지 않는다.
