# SLG 기능 화면용 자산 r1 · #581

상태: 사용자 그림 승인 대기. 승인된 [#563 화면 흐름](https://github.com/hyunlord/bs-mobile/pull/563)의 양피지·어두운 바·파란 주 버튼 체계를 유지한다. 콘텐츠 ID는 [#573 초안](https://github.com/hyunlord/bs-mobile/pull/573)의 병종 3종·연구 10종을 따른다. 콘텐츠 수치·능력은 [가설]이며 그림 제작으로 확정하지 않는다. 게임 코드·데이터·SDK·원본 atlas는 변경하지 않는다.

## 계약과 범위

#563은 병영의 병종 텍스트 탭 2개와 연구 텍스트 노드 9개를 보여 준 시안이다. 초상·아이콘 크기, 세 번째 병종과 열 번째 노드의 그림은 이번 신규 자산 계약이다. 승인된 시안에 이미 있던 규격이라고 소급하지 않는다. 병종 3은 기동 창병이며 원거리·기병을 추가하지 않는다.

| 병종 ID | 이름 | 그림에서 유지할 구분 |
|---|---|---|
| `slg:levy` | 징집 창병 | 기본 창·작은 새싹 방패 |
| `slg:guard` | 방패 수비병 | 큰 새싹 방패·두꺼운 방호구 |
| `slg:skirmish_spear` | 척후 창병 | 가벼운 장비·긴 창 |

| 연구 ID (`slg:` 접두사) | 이름 | 분야·연결 |
|---|---|---|
| `research-field-yield` | 밭 순환 | 경제 시작 |
| `research-wood-yield` | 목재 선별 | 밭 순환 다음 |
| `research-iron-yield` | 광석 분류 | 목재 선별 다음 |
| `research-training` | 훈련 교범 | 군사 시작 |
| `research-formation` | 전열 협동 | 훈련 교범 다음 |
| `research-cover` | 엄호 교범 | 전열 협동 다음 |
| `research-hero-drill` | 영주 교련 | 훈련 교범에서 분기 |
| `research-building` | 공사 도면 | 영지 시작 |
| `research-storage` | 보관 기록 | 공사 도면 다음 |
| `research-road-survey` | 행군 측량 | 보관 기록 다음 |

초상은 승인 인물 그림을 따르고, 작은 아이콘은 판독성을 위해 광택 화풍을 유지한다. 아군 문장은 씨앗·휜 줄기·높이 다른 두 잎의 새싹만 쓴다. 백합·사자 왕가 문장을 추가하지 않는다.

진행 막대는 바탕과 채움, 연구 상태는 아이콘과 테두리, 보고서는 카드 틀과 문자를 분리한다. 시간·비용·확률·서버 결과를 PNG에 굽지 않는다. 얇은 연결선·행군 선 자체는 버튼이 아니며 상호작용하는 노드·타일에는 최소 48×48 터치 영역 계약을 둔다. PNG만으로 실제 터치가 구현됐다고 주장하지 않는다.

## 검증과 적용 경계

390×844 조립판은 실제 자산의 표시 크기와 판독을 확인하는 오프라인 표본이다. 예시 상태는 서버 결과가 아니다. 최종 사용자 승인과 Unity 연결·atlas·필터링·실제 터치·휴대폰 성능은 별도 관문이다.

## 내보내기 규격

| 묶음 | 신규 출력 계약 | 레이어·확장 계약 |
|---|---|---|
| 병종 초상 3종 | 324×383 / 648×766 RGBA | 중앙 기준점. 화면 표본은 비율을 유지해 축소 |
| 병종 아이콘 3종·연구 아이콘 10종 | 48×48 / 96×96 RGBA | 중앙 기준점. 아이콘 내부에 상태 문구를 넣지 않음 |
| 훈련 막대·부상 표시 | 1·2배, manifest에 개별 크기 | 바탕·채움·부상 바탕·부상 아이콘 분리. 채움은 가로 클립 |
| 연구 상태·연결선 | 잠김·진행·완료 3종씩 | 색 외에 잠금·시계·체크로 구분. 64px 원본 프레임을 80px로 9-slice하면 48px 아이콘을 수용 |
| 월드맵 표시·경로 | 타일 선택/차단, 아군·연맹·적 선과 화살표 | 논리 타일 선택의 시각 표현. 그림이 경로·통행을 정하지 않음 |
| 정찰·전투 보고서 | 양피지 바탕과 제목 띠, 각각 1·2배 | 바탕은 의도적으로 같은 양피지. 제목 띠 색과 런타임 제목으로 구분 |

합계 **논리 자산 38종 / PNG 76장**: 병종 6종, 연구 10종, 공통 부품 22종. 부품은 최종 화면과 분리하여 교체·상태 전환·가변 길이 표시가 가능하다. 병영 훈련 화면·연구 3분야·월드 선택/행군·보고서 2종, 총 9개 실제 크기 표본을 제공한다.

부상 표식은 새 붕대·팔걸이 그림이며 전투용 회복 물품이나 판매 상품을 추가하지 않는다. 체크가 들어 있던 목재 선별 아이콘은 진행 상태와 혼동되어 체크를 제거했다. 실패 후보는 로컬 제작 이력에 보존하고 최종 출력만 전달한다.

## 전달 링크

- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/slg-screen-assets-581-20261030)
- [자산 목록·계약](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/ASSET-LIST-r1.md), [통합 manifest](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/MANIFEST-r1.json)
- [화면 조립판](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/slg-assets-screen-board-r1.png)
- [병종 초상·아이콘](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/troops-portraits-icons-native-r1.png), [연구 10종](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/research-icons-native-r1.png)
- [공통 부품 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/primitives-native-r1-390x844.png), [늘이기 검사](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/primitives-stretch-proof-r1.png)

PNG·manifest는 같은 디렉터리에 둔다. 큰 생성 원화·중간 후보·프롬프트는 제작 경로에 보존한다. Release는 게임용 PNG와 검토 표본·계약을 전달하며 ZIP은 만들지 않는다. 이 PR의 변경은 기록 문서 하나뿐이다.

## 최종 오프라인 검사

| 검사 | 결과 |
|---|---|
| 38종·76 PNG 크기·RGBA·해시·최소 터치 계약 | 오류 0 |
| 연구 ID 10개·병종 ID 3개 | #573 초안과 일치 |
| 9화면 글자·터치 검사 | 텍스트 223개·터치 84개, 오류 0 |
| 화면 참조 소스 해시 | 41개 일치 |
| 독립 시각 검토 | 최종 93/PASS, 사용자 승인과 구분 |
| 수정 이력 | 목재 완료 체크 제거, 행군 화살표 26.6도 경로 정렬; 이전 후보 보존 |
| Unity·휴대폰·서버 동작 | NOT_RUN |

[정적 검사](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/ROOT-STATIC-QA-r1.json), [화면 검사](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/PROOF-QA-r1.json), [독립 시각 검토](https://github.com/hyunlord/bs-mobile/releases/download/slg-screen-assets-581-20261030/INDEPENDENT-REVIEW-r1.json). 48px 그림이 48pt 실제 기기 터치로 연결되는지는 Unity 적용 시 별도 확인한다.
