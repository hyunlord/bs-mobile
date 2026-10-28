# 영지 신규 건물 6종 그림 세트

관문: 부분 — 신규 24 PNG의 크기·기준점·밝음/어둠 짝과 기존 10종 비교판의 독립 기술 검사를 통과했다. 최종 그림 승인은 사용자 대기이며 게임 적용은 하지 않았다.

## 범위와 납품

풍차, 물레방앗간, 벌목 오두막, 채석장, 복원 예배당, 휴경지 헛간을 승인된 기존 10종과 같은 등각 방향으로 만들었다. 각 건물은 밝음/어둠 × 1·2배의 투명 PNG 4장이다. 예배당의 초기 문장 시안은 실패 기록으로 보존하고 문 위를 장식 없는 돌로 고친 원본을 사용했다.

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-landmarks-503-20261029)에 24 sprite와 비교판 4장, 조각 목록·출처·검토 요약을 선별하여 추가한다. 기존 atlas나 과거 Release 자산은 덮어쓰지 않는다.

- [기존 10종 + 신규 6종, 이름 없는 동일 축척 한 줄판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks-503-20261029/landmarks-all16-nameless-atlas1x-r1.png)
- [집 본체 폭 75px 기준 한 줄판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks-503-20261029/landmarks-all16-nameless-house75-r1.png)
- [신규 6종 밝음/어둠 비교](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks-503-20261029/landmarks-six-bright-dark-r1.png)
- [같은 기준점 겹침판](https://github.com/hyunlord/bs-mobile/releases/download/estate-landmarks-503-20261029/landmarks-six-anchor-overlay-r1.png)

## 크기와 출처

1배는 256×320, 2배는 512×640이다. 위쪽 원점 기준점은 (128,280)/(256,560), 아래쪽 원점 pivot은 (0.5,0.125)이다. 논리 부지는 2×2, 바닥 투영은 128×64이다. 생성 원본의 앞쪽 접지점을 육안 추정하여 등록했으며 바닥 중앙을 실측했다는 뜻은 아니다.

현재 기준 소스는 `6a5f70b8c7fd796647816f48698457b11f4fbf90`이다. 승인 atlas SHA-256은 `258b242a1ffc5dbe51bfd28bf96929e8ad45b35aa96db90945b807e3e1e199f0`이다. 현재 r8의 `UsesPlazaRendering` 배율은 1.5이므로 atlas→world 배율은 0.75이다. 과거 작업의 0.7을 그대로 적용하지 않았다. 집 75px 비교판은 모든 그림에 같은 `75/141` 배율을 적용했으며 다른 건물도 폭 75px이라는 뜻은 아니다.

한 줄판은 4096×320이며 256×320 셀 16개를 사용한다. 왼쪽부터 집, 곡창, 대장간, 기록소, 병영, 망루, 우물사당, 경작지, 벌목장, 제철소, 풍차, 물레방앗간, 벌목 오두막, 채석장, 복원 예배당, 휴경지 헛간이다. 기존 10종은 현재 atlas crop을 그대로 사용하고 건물마다 높이나 폭을 따로 정규화하지 않았다.

## 검증과 한계

- 24 PNG의 1·2배 캔버스·기준점·해시 검사 통과. 모든 캔버스 바깥 테두리 alpha는 0이다.
- 밝음/어둠 bbox의 각 변 차이는 2배에서 최대 3px로 8px 기준 이내이다. 생성된 짝은 색만 바뀐 동일 픽셀 배열은 아니다.
- 독립 직선 RGBA 측정의 어둠 평균 luma는 약 37.32–41.62로 기존 어둠 땅 표본 66.28보다 낮다. 이는 표본 비교이며 모든 미래 배경에 대한 대비 보장은 아니다.
- 기존 10종이 포함된 한 줄판의 불투명 원본 픽셀 비교는 차이 0이다. 투명 경계는 검토용 배경과 합성했다.
- 풍차 어둠 생성 원본은 확대되어 등비 등록으로 맞췄다. 다른 건물도 크롭·등비 축척·투명 캔버스 배치만 했고 기계적 재도색은 하지 않았다.
- 분리된 미세 alpha 잡음은 경계가 정해진 문맥 크롭으로 제외했다. 원본·제외 수치·변환은 보존하며 건물 부분을 다시 그리거나 비등비 변형하지 않았다.
- 실제 Unity 실행, 게임 적용, 기기 성능은 `NOT_RUN`이다. 현재 완성 건물의 별도 어둠 role이 연결되었다는 주장도 하지 않는다.

로컬 증거는 `output/estate-r8-landmarks-20261029/`의 `art/raw`, `export`, `preview`, `qa`에 보존한다. 공개 납품 목록과 각 파일 SHA-256은 Release의 `LANDMARKS-r1.json`을 따른다. 이 변경은 검토 문서만 추가하며 게임 코드·데이터·원본 atlas는 변경하지 않는다.
