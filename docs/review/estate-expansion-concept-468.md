# 영지 확장 구역 콘셉트 r1

관문: 내부 시각 QA 통과 — 마을92·풍차91·강변91·전체 지도93. 사용자 최종 승인은 대기 중입니다. 게임 코드·데이터·기존 atlas는 수정하지 않았습니다.

[이슈 #468](https://github.com/hyunlord/bs-mobile/issues/468) · [Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-expansion-concept-468-20261028)

## 무엇을 비교하나요

현행 r7의13구역1,664셀을 감사하고 마을 터·풍차 터·강변의 어둠 / 되찾은 직후 / 개발 후를 비교합니다. 되찾은 직후는 같은 폐터의 색 복구이며 개발 후 건물은 별도 시간의 공사 제안입니다. #360의10구역 초안 및 D8 수호자 자격 제안은 현재 r7 매핑·구현 규칙과 구별합니다. 상세 근거는 [AUDIT.md](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/AUDIT.md)에 있습니다.

마을과 강변은8×12, 풍차는12×8로 각96셀입니다. 기준 셀64×32·건물2×2바닥128×64를 비교 격자에 표시했습니다. 이는 의미적 구역 형상과 규모를 읽는 콘셉트이며 픽셀 단위 atlas 정합·앵커·충돌·실제 게임 배치를 검증한 것은 아닙니다.

## 그림 목록

|구역|3단계 비교|축척 보조판|개별 단계|
|---|---|---|---|
|마을 터|[2304×768](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/village-three-stages-r1.png)|[격자·2×2 참고](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/village-scale-overlay-r1.png)|[어둠](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/village-dark-r1-768.png) · [직후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/village-just-reclaimed-r1-768.png) · [개발 후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/village-developed-later-r1-768.png), 각768×768|
|풍차 터|[2304×768](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/windmill-three-stages-r1.png)|[격자·2×2 참고](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/windmill-scale-overlay-r1.png)|[어둠](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/windmill-dark-r1-768.png) · [직후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/windmill-just-reclaimed-r1-768.png) · [개발 후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/windmill-developed-later-r1-768.png), 각768×768|
|강변|[2304×768](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-three-stages-r1.png)|[격자·2×2 참고](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-scale-overlay-r1.png)|[어둠](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-dark-r1-768.png) · [직후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-just-reclaimed-r1-768.png) · [개발 후](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/riverside-developed-later-r1-768.png), 각768×768|

전체 지도: [390×844](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/full-estate-goal-r1-390x844.png) · [1560×3376 확대](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/full-estate-goal-r1-1560x3376.png) · [13구역 보조판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/full-estate-goal-r1-zone-overlay.png).

## 크기·정밀도·검증 한계

- 전체 지도 생성 원화는853×1844입니다. 1560×3376판은 확대 리샘플이며 새로 그린 고해상도 세부가 아닙니다. 390 overview의2×2부지 참고32×16과 확대판128×64를 구별합니다.
- 붓 가장자리·돌·잔해·마당은 근사 경계와 돌출이 있습니다. 강변3단계는 동일한 균일 배율0.8602와 평행이동을 적용했습니다. 추정 지면 범위 약635×332는 가이드640×320와 다르며 모서리 잔차 약6–40px가 남습니다. 정확한 타일 맞춤으로 표현하지 않습니다.
- 어둠 속 나무가 땅보다 어둡다는 판정은 시각 검토입니다. 영역 평균 RGB 실측을 주장하지 않습니다.
- 전체 지도는 목표 풍경 조립 콘셉트입니다. 현행 게임 캡처·해금 순서·수호자 조우·런타임 성능 증거가 아닙니다.
- 실패 원화와 판정은 작업 증거로 보존했습니다. 내부 QA 통과는 사용자 승인과 다릅니다.

[QA.md](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/QA.md)는 독립 시각 검토, [CONCEPT-GEOMETRY.json](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/CONCEPT-GEOMETRY.json)은 생성 원화 출처·크기·투영·등록 오차·18PNG의 SHA-256을 기록합니다. 납품은18PNG와 이 README·AUDIT·QA·기하 정보의 총22파일이며 원본을 덮어쓰지 않습니다.

## 직전 FX 사용자 결정과 공개 상태

후속 전투 FX3종과 불씨 공병 도화선6프레임은 사용자 승인이고 추가 FX 다듬기는 종료했다. [기존 FX Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-combat-fx-audit-456-20261028)에 결정을 덧붙인 뒤 기존 본문·자산57개 보존을 확인했다. 실제 게임 적용은 별도다.

신규 prerelease의22파일을 게시하고 GitHub에서 읽어 확인했다. 모든 파일의 SHA-256과 본문이 준비본과 일치하며 target은4e4dbb63c7f826e7c1db8ae469c1c9598c3bc805이다. 사용자 그림 승인은 대기 상태다.
