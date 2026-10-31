# 영지 UI 감사와 실제 크기 시안

관문: 내부 검토 통과 — 390×844 시안 3종과 긴 정보 표본을 확인했다. 사용자 그림 승인은 대기 중이며 게임 코드·데이터·승인된 22건물은 변경하지 않았다.

[이슈 #507](https://github.com/hyunlord/bs-mobile/issues/507) · [Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-ui-507-20261029)

## 세 가지 비교

각 비교판은 왼쪽 역사적 Editor 원본, 오른쪽 오프라인 시안이다. 각 화면을 390×844 그대로 배치한 780×844 이미지다.

- [상단 자원·목표](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-top-comparison-r1-780x844.png): 숫자와 목표 14px, 원래 생산 재고 조건·목재 20 보상 보존. 곡식·목재 PNG 원본과 기존 철/특허장 도형 규칙을 재사용한다.
- [곡물창고 공사 시트](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-sheet-comparison-r1-780x844.png): 제목 18px, 상태·비용 14px. Lv 0/3, 남은 0:40, 다음 Lv 1·목재 30·1:00 및 공사 시작 문구를 유지하며 불가 행동과 닫기를 구분한다.
- [하단·줌·장식·공사 상태](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-bottom-comparison-r1-780x844.png): 승인 크림·파랑 패널, 활성 영지 강조, 공사 14px. 4탭+출정과 +1분/+10분을 보존한다.
- [긴 정보 실제 1배 표본](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-long-content-r1-390x120.png): 999,999는 값 폭 48px, 긴 공사 상태와 재고 받기는 같은 44px 높이에서 두 행으로 검증했다. 숫자 축약이나 경제 규칙 변경은 없다.

## 감사와 검증

기준 소스는 `a844ef14ee11d85d389dd4db402d0cde5f0ba0c7`이다. 역사 r8 소스와 관련 UI 파일 6개는 byte-identical이지만 캡처별 실제 빌드 pin은 미확인이므로 현재 게임을 실행한 화면이라고 주장하지 않는다.

[감사표](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/AUDIT-r1.md)는 자원·목표·받기·설정·장식·줌·건물 시트·이동·전문화·공사·하단·공사표지를 포함한다. [계약 JSON](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/UI-CONTRACT-r1.json)과 [독립 QA](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/QA-r1.md)에 원본 출처, 글자·그림·hit 범위와 한계를 기록했다.

- 내부 시각 QA 91/100. 세 화면 모두 선언한 편집 영역 밖 straight RGBA 변경 0이며, 검토된 UI 내부 픽셀도 보존했다.
- 수정 대상 hit는 48×48논리px 이상이고 공통 첫 진입/선택/목표 완료 설계에서 중첩 0이다. 개별 그림의 수정하지 않은 44px 버튼은 그대로 남는다. 48px는 설계 proxy이며 실기 dp/pt나 실제 터치 통과가 아니다.
- 같은 상태 전체 rectangle union proxy는 기본 23.36%, 건물 선택 43.99%로 증가하지 않았다. 카메라 예약 strip 25.83%/51.18%와 다른 지표다. 크림 불투명 판은 기존 반투명 판보다 무거워 보일 수 있으며 역사 원본의 분리 alpha 가림은 UNKNOWN이다.
- 공개 11파일은 7PNG·1JSON·3MD다. 내부 r1 아이콘 칩/목표 축약 실패와 r2 raw RGB roundtrip 실패는 로컬에 보존하고 공개 r1에는 최종 r3 byte 보존본을 사용한다.

게임 적용·Unity 실행·동적 긴 문자열·기기 safe area·물리 터치·성능은 NOT_RUN이다. 긴 정보 통과는 별도 synthetic 표본 범위이며 모든 상태를 증명하지 않는다. 사용자가 승인한 전투 HUD r2·카드 r2·일시정지 r1은 그대로 유지한다.
