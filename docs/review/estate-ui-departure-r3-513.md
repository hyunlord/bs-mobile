# 영지 UI 출정 강조 복원 r3

승인된 r2에서 출정 버튼만 원래 파랑·금색 테두리로 복원한 오프라인 그림 시안이다. 사용자 r2 전체 승인은 유지하며 게임 코드·데이터를 바꾸지 않았다.

- [전체 화면 390×844](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-overview-r3-390x844.png)
- [세 상태 1170×892](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-three-states-r3-1170x892.png)
- [기존 r1/r2와 같은 Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-ui-507-20261029)

출정의 위치·hit는 `[290,788,92,48]` 그대로다. 승인 `ui-btn-blue.png`의 양쪽 52px cap을 표시 18px로 수평 분할해 금색 테두리와 모서리를 유지했다. 출정 글자는 흰색 16px bold이며, 선택 영지의 68.5×48·14px와 원본 픽셀은 그대로 보존했다. 새 그림 생성이나 기능 변경은 없다.

로컬 네 상태(기본/지을 수 있음/공사 중/완공)의 출정 rect 밖 straight RGBA 변경은 모두 0이다. 공개 상태판의 기존 헤더 및 세 상태의 나머지 픽셀도 동일하다. 공개 PNG는 위 2개뿐이며 로컬 진단·중간판을 추가 게시하지 않는다. 기존 Release 22개는 덮어쓰지 않는다.

세 상태는 같은 역사 공사 배경 위의 UI 상태 비교 시안이며 실제 게임 실행·상태 전환 증거가 아니다. 비활성 가속 자리와 공사 중 이동 상태 등은 승인 r2 그대로다. 48px는 390 논리 좌표 기준이며 실기 터치, Unity 및 게임 적용은 NOT_RUN이다. r3 최종 그림 승인은 사용자 담당이다.

검증 원자료는 로컬 `output/estate-ui-r3-20261029/`의 `export/VERIFICATION-r3.json`, 독립 QA 및 exact publication manifest에 보존한다. 문서 변경은 `node tools/check-docs.mjs`와 필수 PR CI로 검증한다.
