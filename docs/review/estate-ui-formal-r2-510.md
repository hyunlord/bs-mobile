# 정식 영지 UI r2 상태 시안

관문: 내부 검토 통과 — 승인된 r1 상단·건물 시트 스타일을 유지한 기본 화면과 세 건물 상태를 실제 390×844로 검토했다. 최종 그림 승인은 사용자에게 있으며 게임 코드·데이터는 변경하지 않았다.

[아트 이슈 #510](https://github.com/hyunlord/bs-mobile/issues/510) · [개발 UI 숨김 구현 이슈 #509](https://github.com/hyunlord/bs-mobile/issues/509) · [동일 Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-ui-507-20261029)

## 그림과 상태

- [기본 화면 r1/r2 비교](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-overview-r1-before-r2-after-780x844.png): 개발 문구·장식 토글·시간 건너뛰기·보기 chip을 제거하고, 하단을 어둡게 줄여 선택된 영지만 파랑으로 표시한다. 줌 ±와 생산 재고 목표는 유지한다.
- [공사 중 r1/r2 비교](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-building-r1-before-r2-after-780x844.png): 파란 닫기, 비활성 이동·가속 자리와 공통 우상단 닫기를 표시한다.
- [세 상태 나란히](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/estate-ui-three-states-r2-1170x892.png): 지을 수 있음의 파란 짓기, 공사 중의 파란 닫기, 완공의 파란 올리기. 각 화면390×844를 축소 없이 놓고 설명은 바깥48px 머리말에 분리했다.

비교판 왼쪽은 이전 r1 오프라인 검토판, 오른쪽은 r2다. 세 건물 상태는 같은 역사적 공사 배경에서 UI 상태만 비교한 합성 표본이며 건물 모습까지 일치하는 새 게임 캡처가 아니다.

현재 r8 source `630ef28598a9ef887a448b6c42e06cbd29f12513`에서 곡물창고 최대3레벨, 짓기 목재30·1:00, Lv1 이후 올리기 곡물30·목재50·철10·5:00을 확인했다. 병영 지원/다른 지출·보상 없는 표본이며 지갑은 초기120/180/60/0, 착공·완공120/150/60/0이다. 남은0:40은 역사 캡처 값을 재사용했다. 가속은 전 상태 비활성 자리만 있고 가격·효과·경제 정책을 설계하지 않았다.

## 검증과 한계

[감사표](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/AUDIT-r2.md) · [계약 JSON](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/UI-CONTRACT-r2.json) · [독립 QA](https://github.com/hyunlord/bs-mobile/releases/download/estate-ui-507-20261029/QA-r2.md)

- 내부 시각 QA91, 네 화면의 실제 복원 mask와 새 UI 밖 straight RGBA 변경0. 원본 관련 source6파일 SHA도 보존했다.
- 제안 hit는48×48논리px 이상이며 활성 조작 영역 중첩0. 선택상태 줌의 새 위치는 기존 숨김 동작과 다른 사용자 제안이다. 물리 dp/pt나 실제 클릭 통과를 뜻하지 않는다.
- 전체 rectangle union proxy는 기본23.36%→19.05%, 선택43.99%→42.46%. 고정 탭 표본 휘도는 r1 하단 검토판145.55→r2 47.25로 감소했다. 예약 strip/alpha/복원면적/지각적 가림은 별도이며 서로 대체하지 않는다.
- UI 아래 땅은 imagegen 추정 복원본을 제거 core와 주변12px의 알려진 땅에만 합성했다. 전체 생성 화면으로 원본 세계를 대체하지 않았고 마스크 밖 땅은 재생성·색조 변경하지 않았다. 내부r1 이음새 실패는 로컬에 보존했다.
- 기존 Release r1 11파일을 보존하고 r2 11파일(7PNG·1JSON·3MD)을 새 이름으로 추가했다. 원격 이름·크기·SHA와 기존 자산 보존을 독립 검사한다.

개발 빌드에서만 장식·시간 건너뛰기·보기/저장 상태를 작은 접이식 패널로 모으는 분리안은 감사 문서의 명세이며 미구현이다. 게임 적용·Unity 실행·실기 터치·기기 safe area·성능은 NOT_RUN이다. 승인된 22건물과 전투 UI 원본은 변경하지 않았다.
