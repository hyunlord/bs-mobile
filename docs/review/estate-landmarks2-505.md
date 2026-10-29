# 추가 영지 랜드마크 6종

관문: 부분 — 승인된 기존 16종을 보존한 신규 6종의 그림을 제공한다. 사용자 최종 그림 승인과 게임 적용은 별도이며 게임 코드·데이터·atlas는 변경하지 않는다.

## 납품 범위

경계 초소, 이정표와 길가 성소 한 묶음, 시장 가판대, 여관, 탈곡장, 나루터 창고의 밝음/어둠 × 1·2배 투명 PNG 24장을 추가한다. 초소의 녹색 사각 지붕과 목책, 작은 성소·표지판 묶음, 줄무늬 천막, 자주색 2층 여관, 원형 탈곡장, 둥근 청록 지붕과 하역대가 서로 구분된다. 기존 벌목장·벌목 오두막·헛간 그림을 덮어쓰지 않는다.

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-landmarks2-505-20261029)

- [이름 없는 22종 원본 크기 한 줄판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks2-505-20261029/landmarks2-all22-nameless-atlas1x-r1.png)
- [집 본체 폭 75px 공통배율 한 줄판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks2-505-20261029/landmarks2-all22-nameless-house75-r1.png)
- [신규 6종 밝음/어둠](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks2-505-20261029/landmarks2-six-bright-dark-r1.png)
- [기준점 겹침판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks2-505-20261029/landmarks2-six-anchor-overlay-r1.png)

## 크기·기준점·보존

1배256×320/위쪽 원점 기준점(128,280), 2배512×640/(256,560), 아래쪽 원점 pivot(0.5,0.125)이다. 논리2×2 부지의 바닥 투영은128×64이다. 생성 원본의 앞쪽 접지를 육안 추정하여 등록하며 바닥 중앙이나 충돌 영역의 실측을 주장하지 않는다.

기준 소스는 `8a3ae94bed907ed95495f7ffdf97e2426d1bb07e`이다. 현재 r8 atlas→world 배율은0.75이다. 22종 원본 크기판은5632×320이고 각256×320셀을 같은 기준점에 배치한다. 기존16종은 승인된 원본 PNG의 SHA를 고정해 재사용하며 비교판에서 개별 크기를 정규화하지 않는다. 집75px판은 전체판에75/141배율을 적용한다. 실제 camera배율은75/(141×0.75)이며 모든 건물의 폭이75px이라는 뜻은 아니다.

한 줄판의 왼쪽 순서는 집, 곡창, 대장간, 기록소, 병영, 망루, 우물사당, 경작지, 벌목장, 제철소, 풍차, 물레방앗간, 벌목 오두막, 채석장, 복원 예배당, 휴경지 헛간, 경계 초소, 이정표·길가 성소, 시장 가판대, 여관, 탈곡장, 나루터 창고이다. 이름은 별도 키에서 제공하며 그림판에는 쓰지 않았다.

## 검증의 범위

원본·변환·제외 alpha 수치와 파일별 크기·기준점·SHA는 Release의 `LANDMARKS2-r1.json`, 조각 목록은 `PIECES-r1.md`, 독립 검토는 `QA-r1.md`를 따른다. 밝음/어둠은 별도 생성 원본으로 크롭·등비축척·캔버스 배치만 수행하며 기계적 재도색이나 비등비 변형은 하지 않는다. 생성된 짝은 픽셀 단위로 같은 형상이란 뜻이 아니다.

로컬 원본과 상세 증거는 `output/estate-r8-landmarks2-20261029/`에 보존한다. 공개 Release에는 선별된 납품과 요약만 포함한다. 실제 Unity 배치, 충돌, 하천 연결, draw sorting, 기기 성능은 `NOT_RUN`이며 사용자 최종 그림 승인은 `PENDING`이다.

밝음/어둠 bbox 각 변 차이는 2배에서 최대3px로8px 기준 이내이며 캔버스 바깥 alpha는0이다. 기존16종 한 줄판의 불투명 원본 픽셀 차이는0이다. 생성원본 기반 어둠 평균 밝기는 기존 어둠땅 표본보다 낮으며 실제 미래 배경의 대비를 보장한다는 뜻은 아니다.
