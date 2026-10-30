# 신규 플레이어 첫 접속 흐름 시안 r1

관문: 부분 — 설계·현재 코드의 첫 접속 순서를 정적으로 확인하고 승인 UI 기반 390×844 시안10단계를 구성했다. 실제 신규 플레이10분·기기 조작·게임 적용은 NOT_RUN이며 사용자 검토 대기다.

이슈: https://github.com/hyunlord/bs-mobile/issues/542

감사 기준: `origin/main`의 `21ff25304a47c6f2e649c9a7fd3e581a0e695e5a`.

## 순서와 구현 경계

현재 portrait 경로는 신규 타이틀→첫 Run↔Cards→Summary→Estate→Departure→Run이다. 수확 안내는 익은 밭이 생기는 실제 사건에 따른다. 첫 종료가10분 안에 온다는 보장이나 강제 타이머는 없다. 귀환·건설·두 번째 출정은 첫 판이 끝난 뒤 이어지는 조건부 후속 장면이다.

정식 빌드는 자동 장원으로 귀환한다. 직접 터를 눌러 건설하는 지도는 현재 Editor/Development와 opt-in 경로이며, 영지가 없는 계정에는 저장하지 않는 시제품 사본을 사용한다. 화면07–09는 승인된 영지 목표 UI를 쓴 시안이며 현재 정식 신규 계정의 직접 건설 구현을 뜻하지 않는다.

| 화면 | 표시·행동 |
| --- | --- |
| 01 타이틀 | 제목과 시작 안내, 화면 탭 |
| 02 첫 출정 | 끌어서 이동, 공격은 자동 |
| 03 첫 수확 | 익은 밭에 접근, 실제 사건에서만 안내 |
| 04 첫 카드 | 실제 콘텐츠 선택 예시, 고정 첫 제시 아님 |
| 05 위험 | 체력 바와 회피 안내, 강제 피격 없음 |
| 06 귀환 | 첫 판 종료 후 영지로, 미관측 보상은 `—` |
| 07 터 선택 | opt-in 지도 기반 목표 UI |
| 08 첫 건설 선택 | 곡창 표본 비용 확인·짓기, 종류 강제 없음 |
| 09 공사 | 공사 시작·닫기, 무료 가속/완료 강제 없음 |
| 10 두 번째 출정 | 초기 가신1명과 효과 미적용 상태, 준비되면 출정 |

수치와 상태를 발명하지 않는다. 신규 가신은 고랑 방패수 Lv1/R0 출전1칸이며 현재 가신 전투 효과는 미적용이다. 시작 장비는 철날 검·씨앗 자루다. 시제품 곡창 표본은 목재30·60초이며 목재180→150을 표시하지만 이 지갑을 첫 전투 보상이나 출시 확정값으로 해석하지 않는다.

## 설계 필요

정식 신규 계정의 영지 지도·저장 활성화, 첫 건물 추천 여부, 안내 완료/건너뛰기/재표시 저장, 첫 귀환 트리거 및 실제10분 발생률, 승인 가신 UI와 실제 스냅샷 연결 상태는 별도 설계·구현·관측이 필요하다. 이번 시안은 새 보상·기능·선형 잠금을 추가하지 않는다.

## 검증 경계

승인된 전투 HUD r2·카드 r2·영지 UI r3·가신 효과 UI의 양피지·어두운 바·광택 아이콘·파란 주 버튼을 재사용한다. 모든 단계는390×844, 안내는 짧은 말풍선과 비상호작용 포인터, 터치는48×48 이상, 전투 HUD 예약84px(9.95%)다. 실제 실행과 정적 시안 검증을 구분한다.

게임 코드·데이터·기존 원본은 변경하지 않는다. 야간 Unity·빌드·실기·측정 앱 실행 없음. 최종 사용자 승인 전 문서 PR 병합·자동 병합은 하지 않는다.

## 전달

최종 c3 root 시각 검토92/PASS. c1 실패와 기존 후보를 보존했다. 글자 오류0,48px 미만 hit영역0, hit겹침0. HUD 예약9.95%와 별도로 일시 안내/포인터의 보수적 사각형을 합친 전투 가림은15.91–17.79%다(alpha 픽셀 또는 기기 측정 아님). 기존 승인 bitmap 부품을 AppKit/CoreText로 렌더했으며 이번 작업에 새 imagegen 그림 생성은 없다.

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/onboarding-first10-542-20261030) · [단계01–05](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/onboarding-flow-page-1-r1-1950x920.png) · [단계06–10](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/onboarding-flow-page-2-r1-1950x920.png)

[감사표·출처·설계 필요](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/AUDIT.md) · [화면 계약](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/DESIGN.md) · [README](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/README.md)

화면PNG10개와 흐름판2개,문서3개,정적QA JSON1개로 전달한다.

## r2 사용자 검토 반영

r1 그림·감사 원본과 당시 구현 경계는 보존한다. r2는04의 종류별 색(특허장 보라·도구 초록)을 복원하고06의 빈 결산을 시안용 **[예시] 생존06:42 · Lv8 · 수확12 · 곡물24 · 목재18 · 철재3 · 인장0 · 새 해금 없음**으로 채운다. 예시 수치를 정상 플레이 실측, 실제 지급 약속 또는 경제 확정값으로 해석하지 않는다.

**07–09는 정식 r8 영지 지도 흐름 기준으로 사용자 결정이 완료되었다.** 위 r1의 “정식 신규 계정 지도 진입/저장 활성화는 설계 필요”는 방향 미정 상태를 뜻하는 항목에서 제외한다. 자동 장원에서 정식 r8 지도로의 신규/기존 계정 및 저장 이관 상세는 별도 문서 PR에서 진행한다. 이 결정은 게임 구현 완료나 첫 계정 저장 검증을 뜻하지 않는다.

r2 root 시각 검토94/PASS. 04·06의 글자 오류0,48px미만hit0,흐름판의 다른8화면은 r1 대비 RGBA byte 차이0이다. 구체적인 결산 숫자는 사용자가 지정한 값이 아니라 이번 시안에서 작성한 예시다. 06예시 자재와07시제품 지갑은 연속 저장 시뮬레이션이 아니다.

[04·06 전후 비교](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/onboarding-revisions-r1-r2-1560x900.png) · [흐름01–05 r2](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/onboarding-flow-page-1-r2-1950x920.png) · [흐름06–10 r2](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/onboarding-flow-page-2-r2-1950x920.png) · [감사 r2](https://github.com/hyunlord/bs-mobile/releases/download/onboarding-first10-542-20261030/AUDIT-r2.md). 게임 코드·데이터·기존 Release 파일은 변경하지 않으며, 사용자 최종 그림 승인 전 병합하지 않는다.
