# 연맹 화면 자산 r1

상태: 사용자 그림 승인 대기. 추적 [#596](https://github.com/hyunlord/bs-mobile/issues/596). [연맹 GDD19](../design/gdd/19-slg-alliance-proposal.md), 승인된 SLG 화면 #563과 양피지·어두운 바·파란 주 버튼 체계를 따른다. 게임 코드·데이터·서버·SDK 변경 없음.

## 화면과 부품

| 대상 | 전달 계약 |
|---|---|
| 목록·가입 | 연맹 문장·이름·ID·소개·인원·가입 방식, 공개 가입/가입 신청과 대기 상태 |
| 정보 | 이름·인원·내 역할·공지, 영향력 표시 자리 |
| 도움 | 요청/도움 주기 버튼과 상태, 작업명·남은 시간·횟수·확정 단축 표시 |
| 채팅 | 일반 텍스트 영역·입력·전송 상태. 사진·아바타·첨부·외부 채팅 SDK는 포함하지 않음 |
| 문장 | 새싹과 구분되는 8가지 문양. 바탕·문양·틀을 나누어 색 조합 가능 |

문자·인원·시간·횟수는 런타임 레이어다. PNG에 숫자를 굽지 않으며 최종 값은 서버 응답을 표시한다. 색 조합은 연맹 외형만 바꾸고 도움량·권한·전투 효과를 주지 않는다. 각 부품의 크기·기준점·9-slice·문자/입력 영역은 manifest에 기록한다.

## 설계 근거와 미정 항목

모든 수치는 **[가설]·예시**다. 정원 20명, 도움 작업당 최대 5회·제공자당 1회라는 GDD19 계약을 사용한다. 작업 최초 지속시간 200초, 남은 시간 120초, 누적 도움 8초, 받은 도움 2/5회에서 4초를 제공하면 남은 시간 116초·누적 12초·3/5회가 된다. 이는 작업 완료가 아니라 도움 확정이다.

도움은 활성 건설·훈련·연구·치료에만 적용하며, 작업 시작 시 연맹 소속과 현재 소속을 검사한다. 완료/취소/대기 작업, 미가입 때 시작한 작업에는 도움을 요청하지 않는다. 다른 회원에게 줄 도움을 자기 작업 도움 요청과 구분한다. 가입 신청만으로 소속이나 자리를 확정하지 않는다.

**영향력은 현행 GDD에 정의가 없다.** 요청된 정보 영역은 제공하되 값은 `설계 필요`로 표시한다. 합산 공식·순위·보상·전투력을 만들지 않으며, 실제 제품 노출 여부와 의미는 별도 설계 결정이 필요하다.

채팅은 일반 텍스트 200자와 5초 간격의 예시 계약을 따른다. 전송 중·실패·확정 상태는 구별하며 실제 서버 전송은 구현하지 않는다. 이번 자산이 신고·차단·연맹 권한의 서버 구현을 대신하지 않는다.

## 검증과 전달

게임용 PNG는 52장이다. UI 부품 18종의 1·2배 36장과 문장 8종의 48/96px 16장으로 구성한다. 실제 크기 화면은 목록·가입 대기·정보·도움 요청 전/대기·채팅·문장 조합 7장이다. 사용자 승인, Unity 연결·필터링·실제 터치, 서버 동시성·채팅·도움 정산은 별도 관문이며 이번 실행 검증은 NOT_RUN이다.


## 문장과 색 조합

돌다리·모루·등불·나침반·매듭·세 봉우리·성문·물결의 8종이다. 새싹·백합·왕가 사자·왕관을 사용하지 않는다. 투명한 중립 문양의 음영은 유지하고 RGB에 선택 색을 곱한다. 알파는 그대로 두며 바탕과 틀은 별도 레이어다. 청색/크림·벽돌색/금색·숲색/크림을 실제 48/96px로 비교했다. 벽돌색/금색은 다른 조합보다 대비가 낮으며, 실제 Unity 색 공간과 표시 크기에서 다시 검증해야 한다. 색 선택은 자산 조합 예시이고 새 저장 기능·비용·권한을 확정하지 않는다.

## 전달 링크

- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/alliance-ui-assets-596-20261030)
- [통합 manifest](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/MANIFEST-r1.json), [자산 목록](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/ASSET-LIST-r1.md)
- [390×844 화면 비교판](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/alliance-ui-screen-board-r1.png)
- [문장 8종·3색 실제 크기 비교](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/alliance-emblems-three-palettes-native-r1.png)

문구 174개·선언된 터치 영역 62곳은 잘림·48px 크기 검사 오류 0이다. 입력 PNG 33개 해시가 일치한다. 첫 후보의 같은 연맹 문장 배경색 불일치를 수정하고 실패 판정은 보존했다. 이 PR은 기록 문서 하나만 변경하며, 원본 그림과 게임 코드는 보존한다.

최종 독립 시각 검토는 **94/PASS**다. [판정](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/INDEPENDENT-REVIEW-r1.json)과 [범위 검사](https://github.com/hyunlord/bs-mobile/releases/download/alliance-ui-assets-596-20261030/INDEPENDENT-SCOPE-AUDIT-r1.json)는 오프라인 자산 검증과 실제 게임 미검증을 구분한다.
