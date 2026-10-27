# 괭이질 작업 루프 r2 #447

[작업 이슈](https://github.com/hyunlord/bs-mobile/issues/447) · [같은 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-work-loops-444-20261027)

## 사용자 판정과 변경 범위

망치질·포대 내려놓기 r1은 사용자 승인이다. 괭이질 r1은 게임 크기에서 동작이 작다는 사용자 불합격 판정을 보존한다. 이번 r2는 괭이질만 수정하며 게임 코드·데이터·기존 atlas·r1 파일은 변경하지 않는다. 게임 적용과 최종 그림 승인은 별도다.

승인 주민의 밀짚모자·파란 상의·갈색 바지와 부츠를 기준으로 관절용 부품을 다시 그렸다. 원화 픽셀을 그대로 유지한 것은 아니며, 괭이 날은 이전보다 가늘다. 상체 힌지, 고정 길이 양팔 IK, 양손과 자루의 강체 연결, 고정 발, 별도 흙 튐 레이어를 사용한다. 별도 제작 프로젝트의 Unity6000.6.4f1 / 2DAnimation16.0.1에서 실제 SpriteSkin 변형을 렌더하며 게임 실행 검증으로 확대하지 않는다.

## 보존한 실패

첫 내부 캡처는 큰 괭이 행정과 고정 발 수치를 만족했으나, 들어 올릴 때 먼 팔이 몸 뒤에 가려져 손이 떠 보이고 상체 숙임이 약해 불합격 처리했다. 수치 충족을 전신 이음새 시각 검사 대신 쓰지 않는다. 해당 캡처와 코드 스냅샷은 로컬 제작 기록으로 보존한다.

## 검증 및 전달

공개 r2는 내부5차 캡처다. 중간2·3차의 어깨 돌출과 팔꿈치 외곽 실패도 보존했고,4차는 부품별 분리 렌더로 원인을 확인했다. 최종8프레임 전체 접사와 실제1배 MP4에서 디코딩한8프레임을 작성자·독립 검토자가 확인했다. 연속 재생을 직접 시청한 것으로 표현하지 않는다.

- 셀96×96, atlas768×96, 기준점(30,83). 사람 높이51.873px, 각200ms/주기1.6초.
- 날 끝 최대 이동64.523px, 세로 이동63.681px, 머리 위21.345px. 접지 후 몸 쪽 당기기6.213px.
- 상체 머리 표본 수평 이동14.094px, 양발 표본 이동0, 양손 간격11.99999~12px, 메시 뒤집힘0.
- 합성/주민/흙 튐/고해상도 각각 시작·종료 픽셀 차이0. 별도 흙 튐 atlas 제공.
- 영상은1배128×144와3배320×336, sprite를 각각96×96/288×288로 고정하여 축소하지 않음. 각16초/10회 반복.

[실제1배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-r2-loop-1x.mp4) · [3배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-r2-loop-3x.mp4) · [전8프레임 접사](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-r2-all8-fullbody-seams.png) · [자산 목록](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-r2-assets.md) · [프레임별 관절 검사표](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-r2-joint-review.md).

들어올림 구간의 어깨와 회복 구간의 팔꿈치에 약간 각진 외곽은 남아 있다. 독립 검수에서는 떠 있거나 끊긴 조각으로 보이지 않아 내부 통과했으며, 완벽한 해부학적 표현이나 사용자 승인을 주장하지 않는다. 기존 원본6개 해시를 대조해 보존을 확인했다. 이전 #435 전체 검사 실패는 이번 문서/아트 검증으로 해결했다고 주장하지 않는다.

## 제작 출처

로컬 제작 루트: `output/estate-work-loops-20261027/hoe-r2/`. 기존 승인 주민 참조와 imagegen 부품/흙 튐 원본을 보존하고, 분절 조각을 실제 SpriteSkin으로 렌더했다. 최종 캡처 `capture-r5`, 기계적 조립 `review-r5`.

|파일|SHA-256|
|---|---|
|originals/peasant-articulated-parts.png|97a2aa3f97fa5ccf88a27ba2cab2c131260238af93e2cd668c7140a852acec1c|
|dust-original.png|7d9ee709961458697a5d8f3a7246dd8ace4ae4fe28e76020af6354759cca7e5d|
|unity-trial/Assets/Editor/ArticulatedHoeCapture.cs|f6883aac47b6113e26f18b5c4a5abc76e3de231236025215431deefaa4859f8f|
|unity-trial/Assets/Editor/R8SpriteSkin.cs|f8989327f4beaacdcf90f6e8a1dd02b6b96b197a648e410867b5fbeef8a707d9|
