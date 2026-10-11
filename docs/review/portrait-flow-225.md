관문: 부분 — 세로 흐름·입력 검사는 통과, 새 HUD의 네이티브 끊김 관문과 전체13화면은 후속 단계다.
전투 하단 막대를 없애고76pt 상단 HUD로 줄였다. 타이틀 첫 탭은 첫 판으로, 이후에는 영지로 이어진다.
카드 선택300ms 보호, 일시정지·설정·포기 확인·백그라운드 복귀를 실제 화면으로 검증했다.
Core·data·schema 변경은 없다. #126·#98·1b와 #223 성능 실패는 유지한다.

# #225 세로 흐름·판 화면

| 검증 | 실제 결과 | 한계 |
| --- | --- | --- |
| Unity6000.6.4f1 EditMode | 뷰포트·카드 동작5/5 통과 | 기기 실행 아님 |
| Unity6000.6.4f1 PlayMode | 흐름4·오디오/진동 경계7, 총11/11 통과 | Android/iOS 진동·시스템 Back 실기 미확인 |
| 화면 캡처 | 390×844·1080×2520·1920×1080에서 타이틀/HUD/카드/일시정지/설정/포기 확인 | 1080×2520은 Fold7 비율 시뮬레이션 |
| 실제 UI 입력 | raycast 클릭,300ms 차단, 같은 제안 복귀, 새 판 입력 보호, lifecycle 일시정지 | OS 권한 사용 없음 |
| 구조 | Unity 경계 검사9개 어셈블리 통과, Core/data/schema diff 없음 | 성능 통과를 뜻하지 않음 |
| 코드 검토 | 독립 읽기 전용 검토, 차단 결함 없음 | 화면·네이티브 관문 별도 |

## 재현

wave-1a 프로필로 `UNITY_CONTENT_PROFILE=wave-1a bash tools/prepare-unity.sh`를 실행한다. Unity Test Runner의 EditMode에서 `PortraitViewportTests`, `PortraitCardActionsTests`, PlayMode에서 `PortraitFlowTests`, `FirstPlayableAudioTests`를 실행한다. 캡처는 `artifacts/portrait-flow-225/editor/`, 마지막 검사 결과는 `artifacts/portrait-225/FinalFlow.xml`이다. 초기 실패 캡처·검사 파일은 로컬에 보존하며 마지막 성공과 혼동하지 않는다.

390×844에서는 HUD76pt가 높이9.0%를 차지한다. Mac 가로 창은390:844 전장을 중앙에 두고 바깥 여백을 입력에서 제외한다. 카드 전체 본문·진화 조건은 공동 스크롤로 읽으며 하단 동작은 고정한다. 도움말은 보유 장비 옆 빈 공간에만 놓고 공간이 부족하면 가린다. 긴 세로 화면에서도 일시정지 스크롤과 고정 버튼 영역이 겹치지 않게 한다.

## 범위

`RunCoordinator.Portrait`가 화면 복귀·일시정지·입력 보호를 맡고, `PortraitRunPanels`가 판 안 패널을 만든다. `PortraitViewport`, `PortraitHud`, `PortraitWidgets`, `PortraitTokens`는 기존 프로필과 분리된 표시 경로다. 기존 아트를 재사용하며 새 패키지는 없다. 진동은 플랫폼 기본 API를 쓰고 설정에서 끌 수 있다.

영지와 결산은 현재 연결용 화면이며 #226의 실제 자원·장원·가신·도전·보상 UI로 대체한다. 상점·방치·부활·패스는 #227 범위다. 연결 불가 canvas 대신 [서면 화면 정본](../design/screen-flow-v0.md)을 사용하며 canvas와 직접 비교했다고 주장하지 않는다. 전체13화면 비교 시트와 새 HUD의 네이티브 성능 검사는 세 단계 통합 뒤에 수행한다. [이전 끊김 실패](stutter-part1-219.md)는 그대로 남긴다.
