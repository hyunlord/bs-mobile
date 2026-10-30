# 영웅 모집 화면 자산 r1

상태: 사용자 그림 승인 대기. 추적 [#592](https://github.com/hyunlord/bs-mobile/issues/592). [GDD16](../design/gdd/16-slg-heroes-proposal.md), 승인 초상 #567, 모집 연출 #570, SLG UI #563을 따른다. 게임 코드·데이터·서버·SDK는 변경하지 않는다.

## 자산 계약

| 묶음 | 계약 |
|---|---|
| 모집 | 파란 1회 모집 버튼, 비활성 상태, 파란 보석, 천장 막대, 확률 공개 버튼 |
| 확률 공개 | 양피지 패널, 표 제목·행, 등급별 및 영웅별 확률, 천장 설명 영역 |
| 결과 | 일반·희귀·서사 테두리, 새 영웅·중복 조각 상태. 초상과 등급은 확정 결과 공개 때 함께 표시 |
| 초상 | 승인된 농로 길잡이·강가 수호자·폐허 탐구자 원본 324×383을 보존하며 96×114 표시본 추가 |
| 영웅 목록 | 초상·등급·조각 수를 담는 카드와 별도 진행 막대 |

텍스트·보유량·비용·천장·확률·조각 수는 PNG에 고정하지 않는 런타임 레이어다. 표의 행 수와 문구 길이에 맞춰 패널을 늘이고, 값을 잘라 표시하지 않는다. 각 PNG의 크기·중앙 기준점·9-slice·채움 클립·문자 영역은 manifest에 기록한다. 48px 터치 영역은 구현 계약이며 정적 그림이 실제 기기 터치를 증명하지 않는다.

초상은 승인 원화를 그대로 재사용한다. 새로운 화풍 변경이나 인물 재설계는 하지 않았으며, 원본 복사본은 해시로 대조한다. 화면용은 비율을 유지하여 축소한다. 부품은 1·2배로 제공한다.

## 확률과 숫자 예시

모든 수치는 **[가설]·예시**다. 실제 잔액·지급량·카운터는 서버의 확정 응답으로 표시한다.

| 상태 | 일반 / 농로 길잡이 | 희귀 / 강가 수호자 | 서사 / 폐허 탐구자 |
|---|---|---|---|
| 보통 모집, 연속 서사 미획득 c=0~18 | 70% | 25% | 5% |
| 천장 다음 1회, c=19 | 0% | 0% | 100% |
| 중복 조각 예시 | 5 | 10 | 20 |

현재는 등급마다 영웅 1명이라 개별 확률과 등급 확률이 같다. 두 표를 구분하여 표시하며, 후보가 늘면 새 풀의 개별 확률을 받아야 한다. 최대 20회 내 서사 보장은 미보유 영웅 보장이 아니다. 중복 서사도 c=0으로 초기화한다.

모집 예시는 보유 보석 300, 비용 100, 모집 후 200, c=12와 최대 8회다. 일반·희귀 결과는 c=13과 최대 7회, 서사 결과는 c=0과 최대 20회다. UI 예시를 출시 가격이나 서버 구현 검증으로 해석하지 않는다.

## 연출과 구현 경계

공개 전 초상·등급 노출이나 일반 결과의 가짜 서사 고조를 추가하지 않는다. 이번 결과 카드는 확정 응답 후 공개 단계와 결과 단계에 사용하는 정적 자산이다. 승인된 [모집 연출 후속 메모](slg-hero-recruitment-r2-569.md)의 희귀·서사 잔광, 서사 공개 1회 흔들림·효과음, 초상 안착은 Unity 구현 때 적용한다.

사용자 그림 승인, 실제 Unity 연결·atlas·필터링·터치, 서버 RNG·차감·중복 요청 처리는 별도 관문이다. 이번 작업의 해당 실행 검증은 **NOT_RUN**이다. #589는 별도 승인 전 미병합 상태를 유지한다.

## 전달과 검증

- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/hero-recruit-assets-592-20261030)
- [통합 manifest](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/MANIFEST-r1.json), [자산 목록](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/ASSET-LIST-r1.md)
- [실제 크기 화면 비교판](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/hero-recruit-screen-board-r1.png)
- [승인 초상 원본·96×114 비교](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/hero-portraits-native-comparison-r1.png)

게임용 PNG는 **50장**: 초상 6장과 재사용 UI 부품 22종의 1·2배 44장이다. 390×844 화면 11장은 모집·확인·보통/확정 확률·3등급 신규/중복 결과·영웅 목록을 보여 준다. ZIP으로 다시 포장하지 않는다.

| 오프라인 검사 | 결과 |
|---|---|
| 초상 원본 보존 | 승인 원본 3/3 해시 동일 |
| UI 부품 | 22종·44 PNG 크기·RGBA·해시 오류 0 |
| 화면 글자·터치 | 11화면·266문구·83터치 영역 오류 0, 최소 48×48 |
| 수정 이력 | 첫 후보의 확률 제목·상태 배지 대비 부족을 수정. 후보1/2를 보존 |
| Unity·휴대폰·서버 | NOT_RUN |

정적 화면의 내부 통과는 사용자 아트 승인 및 실제 게임 적용 승인을 대신하지 않는다.

최종 독립 시각 검토는 **94/PASS**다. 화면 참조 소스 34개 해시도 일치한다. [독립 판정](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/INDEPENDENT-REVIEW-r1.json)과 [범위 검사](https://github.com/hyunlord/bs-mobile/releases/download/hero-recruit-assets-592-20261030/INDEPENDENT-SCOPE-AUDIT-r1.json)에 실제 검사와 미실행 경계를 기록했다.
