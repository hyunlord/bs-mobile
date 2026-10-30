# 영웅 모집 등장 콘티 r2

상태: **r2 흐름 사용자 승인 — 2026-10-30**. 추적 [#569](https://github.com/hyunlord/bs-mobile/issues/569). 초상화 3명은 사용자 승인된 r1 그대로 사용하며, r1 콘티는 결과 선공개·등급 차등 부족으로 재작업한다. r1 파일은 역사 자료로 보존한다. 게임 코드·데이터·SDK를 변경하지 않는다.

## 공개 순서와 등급별 차이

서버가 확정한 실제 등급에만 대응하는 연출을 재생한다. 봉인 단계에는 초상화·인물 윤곽·이름·등급 띠·결과 천장값이 보이지 않는다. 희귀/서사는 해당 등급의 빛 색만 살짝 비치고 일반은 빛이 없다. 짧은 고조 뒤 초상화와 등급을 동시에 공개하고, 결과 화면의 확인으로 마친다. 일반인데 서사처럼 고조하는 연출, 가짜 승급·아슬아슬한 실패는 없다.

| 등급 | 봉인 | 고조 지속 | 공개 | 결과 | 차이 |
|---|---|---|---|---|---|
| 일반 | 0~0.10초 | 0.40초 | 0.50초 | 0.90초 | 빛·입자 없는 단순 개봉, 전체1초 이내 |
| 희귀 | 0~0.20초 | 0.60초 | 0.80초 | 1.50초 | 파란 빛과 작은 파티클 |
| 서사 | 0~0.25초 | 0.75초 | 1.00초 | 2.50초 | 보라·금 빛 기둥, 화면 흔들림1회, 등장 대사1줄, 전체3초 이내 |

시간·진폭·입자 수는 **[가설]**이며 실제 Unity 체감 측정값이 아니다. 결과 홀드를 포함하는 영상 길이와 타임라인은 전달물에 명시한다.

## 결과 보존·중복·접근성

건너뛰기는 최초 프레임부터 가능하며 같은 서버 확정 결과의 ④로 바로 간다. 모션 줄이기에서는 고조·흔들림 없이 같은 정적 결과를 표시한다. 응답이 불명확하면 결과를 새로 정하지 않고 기존 요청의 확정 여부를 조회한다. 연출 길이·재생 종료·확인·건너뛰기는 비용·확률·지급량·천장을 바꾸지 않는다.

중복이면 ③ 공개에서부터 해당 영웅과 `조각 +N`을 함께 표시하고 ④에도 유지한다. [GDD16 §4](../design/gdd/16-slg-heroes-proposal.md)의 일반5·희귀10·서사20조각 **[가설]**을 시안의 **[예시]**로 사용한다. 실제 클라이언트는 서버가 확정한 지급량을 표시하며 UI에서 수량을 산출하지 않는다. 서사 중복도 천장 카운터를 초기화한다는 기존 GDD 규칙은 유지한다.

## 검증 범위

정적 콘티와 오프라인 합성 영상이며 게임 구현이 아니다. 실제 서버·RNG·Unity 재생·터치/건너뛰기 동작·모션 줄이기 동작·모바일 실기·복구 검증은 **NOT_RUN**이다. 봉인→고조→공개→결과의 흐름은 승인되었으며, 아래 구현 메모의 연출 강도·체감은 Unity 구현 후 확인한다.

## 전달물

기존 [Release](https://github.com/hyunlord/bs-mobile/releases/tag/slg-hero-recruitment-564-20261030)에 r2 파일만 추가한다. 초상화 r1과 기존 콘티 r1은 덮어쓰지 않는다. 영상은 소리 없는 **390×844·30fps 오프라인 합성 시안**이다.

| 등급 | 4컷 콘티 | 1배 영상 | 결과 홀드 포함 길이 |
|---|---|---|---|
| 일반 | [PNG](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-field-guide-storyboard-r2.png) | [MP4](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-field-guide-r2-390x844.mp4) | 1초 |
| 희귀 | [PNG](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-river-guardian-storyboard-r2.png) | [MP4](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-river-guardian-r2-390x844.mp4) | 2초 |
| 서사 | [PNG](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-ruin-explorer-storyboard-r2.png) | [MP4](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-ruin-explorer-r2-390x844.mp4) | 3초 |

콘티의 상·하단 등급/시점/단계 설명은 검토용 주석이며 실제390×844 화면·영상 안에는 포함하지 않는다. 12키프레임과 중복 공개3장도 각390×844로 별도 제공한다. 서사 대사는 공개 후1.00~2.50초에만 보이며 결과 화면에서는 천장정보로 돌아간다.

## 검증 결과

- 15개 실제크기 화면(12키프레임+중복3), 텍스트207개 잘림/누락0, 터치15개 최소48×48 확인.
- 독립 검토 2건: 초기 희귀 광원 판독 부족과 서사 방사형 광선 문제를 발견해 보강했다. 실패 후보는 로컬 보존하고 최종 수직빛기둥·문구 대비·한글 판독을 다시 확인했다.
- 최종 MP4를 디코딩해 일반14→15프레임, 희귀23→24, 서사29→30의 공개 경계를 직접 확인했다. 공개 직전 인물/이름/등급 노출 없이 동시에 공개된다.
- ffprobe: 390×844·30fps, 각각30/60/90프레임·1/2/3초. 타임라인195표본의 공개 가드 위반0. 서사 흔들림은1.00~1.16초 한 주기의 좌우3px [가설]이며 다른 등급에는 없다.
- 버튼 표기·영역만 확인했다. 실제 건너뛰기 클릭이나 서버 거래의 동작 통과로 해석하지 않는다.
- 승인 초상화3장 및 r1 콘티3장의 SHA-256을 작업 전후 비교해 동일함을 확인했다. 신규 체크섬 전달물이나 ZIP은 만들지 않았다.

[검증 기록](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/verification-r2.json), [설계](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/RECRUIT-DESIGN-r2.md), [재생성 소스](https://github.com/hyunlord/bs-mobile/releases/download/slg-hero-recruitment-564-20261030/recruit-renderer-r2.swift). 시안 파일과 검증은 아래 Unity 구현 메모의 반영·체감 검증을 대신하지 않는다.


## 사용자 승인 범위와 Unity 구현 메모

사용자는 r2의 봉인→고조→동시 공개→결과 **흐름**을 승인했다. 다만 현재 영상은 공개 순간의 강조가 약하고 서사 흔들림이 눈에 보이지 않는다고 평가했다. 위의 좌우3px·1회 수치 검증은 이 판독성 문제를 해결했다는 증거가 아니다. 기존 r2 이미지·영상은 그대로 보존한다.

Unity 구현에서 다음을 반영한다. 이 메모는 현재 오프라인 영상에 이미 반영된 동작을 설명하지 않는다.

- 희귀·서사: 공개 뒤 0.5초 동안 등급색 폭발광과 배경 잔광을 유지한다.
- 서사: 공개 프레임에 맞춰 흔들림을 한 번 주고 효과음을 강조한다.
- 초상화: 약간 확대되었다가 안정된 크기로 정착하게 한다.

흔들림의 세기·곡선, 잔광 감쇠, 초상화 확대량·정착 시간과 효과음 세부는 Unity 구현 단계에서 판독성과 동작 줄이기 규칙을 지키며 조정한다. 게임 코드·데이터·음원·기존 아트는 이번 문서 변경에 포함하지 않으며 실제 Unity 재생·효과음·모바일 체감 검증은 **NOT_RUN**이다.
