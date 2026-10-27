# M3a r7 빛 경계 후속

- 날짜: 2026-10-26. 상태: 수치·건설 검증 완료, 사용자 화면 판정 대기. #390 초안 유지.
- 사용자 재개 승인: 합성 ROI를 밝은 땅 기준으로 사용한다. 앞 보고의 순수 잔디 R131.78은 실패가 아니다. 합성 ROI146.63/121.28/81.12는 통과이며, 순수 잔디 하한 적용은 검토 기준 오류였음을 정정한다. 이전 측정 원자료는 보존한다.
- 경계 알파 차이 `<0.08` 검사와 r7 폐쇄 지면 .90을 유지한다. 완전 밝음 시작점(d=-.5)과 완전 어둠 도달점(d=2.5)을 고정하고, edgeAlpha 지점을 d=0에서+.5로 옮겨 첫 feather를 .5→1셀로 넓힌다. 역사 r3~r6 분기는 변경하지 않는다.
- 신규 Python 측정 도구는 저장소에서 제외하고 소유 worktree의 `artifacts/m3a-r7-resume-20261026/measure-estate-layers.py`에 보존한다. 기존 실패 보고의 재현 명령은 당시 상태 기록이며 현재 저장소 명령이 아니다. 정본 측정 CSV는 유지한다. 새 의존성 없음.
- 승인된 #386은 최신 main merge와 정책 검증 후 squash 병합한다. #390은 merge로 동기화하며 강제 push하지 않는다.

## 검증

- 첫 수정 f5e8767: 단독 경계 검사 .06274511 통과였지만 전체 first-playable EditMode 854/1/25에서 기초 (24,24) alpha .09019608(기대0)이 실패했다. 안쪽 feather 확대가 기초까지 어둡게 만든 원인이다. 순차 하네스를 중단해 두 번째 프로필의 동일 실패를 예방했고, 이미 시작한 first-playable PlayMode는 50/0/45로 정상 종료했다. 원자료 `artifacts/unity-tests-f5e8767-day` 보존.
- 두 번째 수정은 바깥쪽 구간만 넓힌다. RGBA8·320×320·3회 blur 검산에서 기초0, 경계 .074509804, 깊은 어둠 .898039216, 내부0. 이는 계산 검산이며 실제 Unity 결과는 아래에 추가한다.

## 최종 로컬 검증

### Unity tests
commit: 6f589cdf6642a201c626af78af4f5b06a4882b10
first-playable EditMode: passed 857, failed 0, skipped 25
first-playable PlayMode: passed 50, failed 0, skipped 45
wave-1a EditMode: passed 867, failed 0, skipped 15
wave-1a PlayMode: passed 82, failed 0, skipped 13
skipped in every profile: 2 unexplained, 1 listed

- 전체 하네스의 마지막 wave PlayMode가 17:26경 SIGTERM(15)으로 종료되어 XML을 남기지 못했다. 종료 출처 미확인. 자기 잠금 PID11603은 유지됐으며, 해당 단계만 같은 커밋에서 한 번 재시도해 82/0/13으로 완료했다. 첫 중단의 로그·불완전 기록은 `artifacts/unity-tests-6f589cd-day`, 재시도는 `artifacts/unity-tests-6f589cd-wave-resume`에 각각 보존한다. 첫 세 결과와 마지막 재시도 결과를 합친 기록이며 한 번의 무중단 실행으로 표현하지 않는다.
- 공통 skip2건은 기존 M1 fixture sidecar와 opt-in melee catalog 부재이며 예외를 추가하지 않았다. assertion 실패0과 전체 하네스 종료0을 혼동하지 않는다.
- #386은 quality/secrets 통과 후 main `041e1e4`로 squash됐다. #390은 main을 정상 merge한 `8fcbcd7`과 검사 커밋의 전체 파일 바이트가 동일하다. 강제 push 없음.
- 경계 최대 알파 차이 **0.0745098144 < 0.08**, 가장자리 기초 alpha0. 최초 시도에서 발생한 기초 침범을 해결했다. 역사 r3~r6 저장 검사·리플레이 parity 통과.
- crowd411 원본 입력 SHA `4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c`를 현재 Core로 재검증: 종료11530틱, `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619` 동일. 기본 데이터 해시 `6AC1ACFC4B2E80DD510EE0A41D0039BC05932A5FEF1776CC1B2A4BF2E928C36D` 동일. ADR0058 역사 헤더 해시를 유지하며 원본 재생을 쓰지 않았다. 이는 성능 측정이 아니다.

### 실제 화면 수치

390×844 Editor, 재시도 PlayMode UTC08:28:23.856~24.856 캡처. 10장 모두 commit6f589cd/sourceDirtyFalse/청록0/컴파일 전후false. 원자료·재생성 명령은 `artifacts/m3a-r7-resume-20261026/evidence-prep-6f589cd.md`와 `evidence-6f589cd`, `metrics-6f589cd`에 있다.

| 항목 | 결과 | 판정 범위 |
|---|---:|---|
| 어두운 상단 ROI RGB | 61.98 / 67.15 / 76.58 | ≤64/70/78 |
| 어두운 하단 ROI RGB | 63.90 / 68.64 / 77.21 | ≤64/70/78 |
| 밝은 합성 ROI RGB | 147.12 / 121.63 / 81.15 | 사용자 정정 기준 통과; 순수 잔디 R131.78은 실패 아님 |
| 큰길 독립 단면 id4 | 18px (FWHM13.5px) | 보이는 폭≥14px |
| 가지길 독립 단면 id14/20/23 | 11 / 10.5 / 11.5px | 보이는 폭≥9px |

길은 stage2−stage1 RGB 최대차≥12의 중심 연결 구간으로 측정한다. 24개 단면 중 20개는 교차로 근접 표본으로 표시하고 독립 폭의 근거에서 제외한다. 수치로 시각 승인을 대신하지 않는다.

| 설정 | on base+landmark=total | off base+landmark=total | Lv0 추가 on/off |
|---|---|---|---|
| r3 | 12+2=14 | 10+2=12 | 2 / 1 |
| r4 | 12+2=14 | 10+2=12 | 2 / 1 |
| r5 | 12+2=14 | 10+2=12 | 2 / 1 |
| r6 | 12+2=14 | 9+2=11 | 2 / 1 |
| r7 | 10+2=12 | 8+2=10 | 0 / 0 |

실제 Graphic의 material/texture 연속 구간 구조 세기다. GPU 성능 측정으로 표현하지 않는다. r6 예산과 r7 landmark≤4 조건은 바꾸지 않았다.

### Native 건설 1회

- macOS ARM64 Mono Development, source8fcbcd7, wave-1a. 요청 창390×844와 실제 ScreenCapture834×1808은 다르므로 실제 파일 크기를 그대로 표시한다.
- 실행1회, 일반 UI·실제 시간: 새 영지 곡물120/목재180 → 경작지 `parent-required`, 버튼 비활성·사유 표시 → 곡창 목재30 소비·60초 → 경작지 곡물10/목재10 소비·30초 → 둘 다 Lv1. 자원 지급·시간 건너뛰기 없음.
- 0/1/30 진입, 거부 사유, 곡창 공사/완공, 경작지 공사/완공 **8장 청록0**. 진입0은 제목 동작 이후 첫 완료된 렌더이며 앱 프레임304다. 모든 캡처 focused=True.
- 원자료: `artifacts/m3a-r7-native-flow-pnCNVD9V`의 build result, frames.csv, construction.csv, result.txt. 새 계정 격리 경로 사용.

### 화면 판정 대기와 알려진 한계

두 독립 읽기 전용 검토에서 첫 접속의 회색 반복 삼각형/띠 제거, 흙길·둥근 빛·기초 밝기·건설 시트 가림 해소를 확인했다. 다만 **전체 보기 좌하단(약 x0–195,y674–780)의 큰 지면 외곽 사선**은 남아 있다. 강둑 반복, 넓은 길 합류부도 보존된 한계다. 키아트와 픽셀 일치 또는 사용자 시각 통과를 선언하지 않으며 초안을 유지한다. 최종 CI 결과는 PR 본문에 링크로 기록한다.
