# 영지 지도 A/B 비교·강변 생활 요소 r2

관문: 내부 시각 QA 통과 — A·강변92, B91. 사용자 그림 승인 대기입니다.

[이슈 #471](https://github.com/hyunlord/bs-mobile/issues/471) · [같은 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-expansion-concept-468-20261028)

## 승인 범위와 이번 비교

기존 마을·풍차·강변3단계는 사용자 승인입니다. 강변 개발 후는 어살·건조대·작은 물레방아로 생활감을 보강하는 r2 수정 대상이며, 기존 전체 지도 r1은 승인되지 않았습니다. 이번 A/B 지도는 결정용 제안으로, B를 채택하거나 게임 데이터에 반영하지 않았습니다.

|비교|A 현행 r7 배치|B 재배치 제안 g10|
|---|---|---|
|총면적|1,664셀|1,664셀|
|구역별 면적|현행13구역 유지|13구역 면적 재분배|
|절반쯤 회복|888셀,53.365%|989셀,59.435%|
|도식 셀 표시|64×32|80×40: A보다 선형25% 큼|
|제공 화면|390×844 및1560×3376 확대|390×844 및1560×3376 확대|

두 안의 절반쯤 회복은 start·주변4구역·village·windmill·fields·fallow의9개 의미 구역입니다. 정확히50% 또는 서로 같은 비율이라는 뜻이 아닙니다. 같은 출력 해상도이지만 같은 줌은 아니며, 동일 면적은 논리적 셀 수를 뜻합니다. 그림상 픽셀 넓이를 직접 면적 차이로 읽으면 안 됩니다.

B 구역별 셀 수: start62, north89, east134, south103, west82, village149, wood114, hill134, windmill135, fields120, fallow115, ruins256, riverside171. 최종 후보 기하에서 각 구역 연결성과 구멍 없음, 합계1,664셀을 확인했습니다. 독립 QA도 정확한 합계·연결성을 재계산했습니다.

## 표현·정밀도 한계

- A는 현행 구역을 유지하고 유기적인 숲·안개 경계로 표현합니다. B는 중앙 시작 구역과 조밀한 둥근 합집합을 위한 재배치 제안입니다.
- 전체 회복 지도에서 구역 밖 밝은 자연 지형은 배경이며 추가 개방 가능 면적이 아닙니다.
- 셀 집합·면적·기하 보조판은 수치 계약이고, 생성 원화의 물·숲·안개·붓 경계는 근사 표현입니다. spritesheet, 정확한 atlas crop·앵커·충돌·게임2×2바닥 네이티브 크기 증거가 아닙니다.
- 확대판은 생성 원화 리샘플입니다. 새로 그린 고해상도 세부나 실제 게임 캡처를 뜻하지 않습니다.
- 동일 카메라·B 개별 원래 면적·동일888셀 회복은 제작자가 추가했던 불필요한 제약으로 해제했습니다. B 면적 재분배와 배율 변경을 명시하며 이전 가이드를 조용히 맞춘 것으로 표현하지 않습니다.
- 초기 원화·B 기하v1/v2·72×36 후보의 구역 의미 불일치 등 실패 기록은 보존합니다. 후속 통과가 이전 실패나 사용자 미승인을 소급 변경하지 않습니다.
- 게임 코드·데이터·기존 atlas는 수정하지 않습니다. 기존 Release22파일을 보존하고 고유 r2 파일만 추가합니다.

## 파일 및 검증

최종 그림17PNG, AB-GEOMETRY-r2.json, README-r2.md, QA-r2.md를 기존 Release에 추가합니다. 기존22파일은 보존합니다. 게시 후 SHA-256과 기존 파일ID·digest를 읽어 확인합니다.

A/B 각각 full·half의390×844,1560×3376 확대판, grid-overlay를 제공합니다. 강변은 개발 후768판·3단계 비교·개발 전후 native/2x판, 별도로 A/B4지도390크기 비교판을 제공합니다. 정확한 파일 목록은 아래 표에 최종 export 파일명으로 기재합니다.

QA-r2.md는 원화 판독성·전체/절반 상태 정합과 기하 재계산의 범위를 구별합니다. 격자판의 옅은 셀선은 수치 격자를 보조할 뿐 그림 붓 경계와의 픽셀 정합을 뜻하지 않습니다.


|파일|링크|
|---|---|
|A-full-r2-390x844.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-full-r2-390x844.png)|
|A-full-r2-1560x3376.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-full-r2-1560x3376.png)|
|A-full-grid-overlay-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-full-grid-overlay-r2.png)|
|A-half-r2-390x844.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-half-r2-390x844.png)|
|A-half-r2-1560x3376.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-half-r2-1560x3376.png)|
|A-half-grid-overlay-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-half-grid-overlay-r2.png)|
|B-full-r2-390x844.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-full-r2-390x844.png)|
|B-full-r2-1560x3376.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-full-r2-1560x3376.png)|
|B-full-grid-overlay-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-full-grid-overlay-r2.png)|
|B-half-r2-390x844.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-half-r2-390x844.png)|
|B-half-r2-1560x3376.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-half-r2-1560x3376.png)|
|B-half-grid-overlay-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/B-half-grid-overlay-r2.png)|
|riverside-developed-r2-768.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-developed-r2-768.png)|
|riverside-three-stages-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-three-stages-r2.png)|
|riverside-developed-before-after-r2-native.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-developed-before-after-r2-native.png)|
|riverside-developed-before-after-r2-2x.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-developed-before-after-r2-2x.png)|
|A-B-four-panel-native390-r2.png|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/A-B-four-panel-native390-r2.png)|
|AB-GEOMETRY-r2.json|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/AB-GEOMETRY-r2.json)|
|README-r2.md|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/README-r2.md)|
|QA-r2.md|[보기](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/QA-r2.md)|

## 공개 검증

같은 Release에 허용 목록20파일을 덮어쓰기 없이 추가했다. 원래22파일의ID·이름·크기·digest 보존, 새20파일SHA-256 일치, 추가 본문 일치를 GitHub 읽기로 확인했다. 총42파일이다. 셀 격자 최종 서식은 대표 B전체판을 직접 확인해 판독 가능 판정했다. 사용자 그림 승인·B안 채택·게임 적용은 별도다.
