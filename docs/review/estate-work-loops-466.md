# 승인 영지 작업 루프 통합 — #466

[작업 이슈](https://github.com/hyunlord/bs-mobile/issues/466) · 소스 `313a803e3b63c25b7f9cef704fc04c96703df377` · 2026-10-28.

완성 밭의 hoe r2(8×200ms), 공사 가장자리의 hammer r1(8×150ms), 곡물창고 도착 때 한 번 재생하고 걷기로 복귀하는 sack r1(8×300ms)을 View에 연결했다. 건물 ID 순서를 고정하고 정적 주민까지 합쳐 최대 6명을 배치한다. 기존 운반자 한 명의 포즈만 바꾸며 sack 인물을 추가하지 않는다. 전역 unscaled View 시간을 사용해 화면 재구성으로 도착 동작이 재시작하지 않는다. Core·data·저장 형식·골든 해시 파일 변경은 없다.

승인 24프레임은 기존 atlas에 추가했다. 2048×2328 → 2048×2428, RGBA32 산술 증가 819,200 bytes(0.78125 MiB), 실제 GPU 메모리 측정은 아니다. 기존 152개 sprite의 메타데이터와 crop 픽셀 변화는 0이다. 기존 사각 atlas 영역 전체는 동일하지 않다. 투명했던 마지막 선반에 새 프레임이 들어가 5,661픽셀이 달라졌다. 원본과 이전 실패 아트는 보존한다.

## 실행 증거

- Unity 6000.6.4f1, macOS Editor. `execution/final-20261028-114958/profiles`에서 first-playable EditMode 898 / PlayMode 55, wave-1a EditMode 908 / PlayMode 89 통과, 실패 0. 실제 입력 combat·melee 보충 실행 각각 1/1 통과.
- 기본 runner는 보충 입력이 없는 두 검사를 건너뛰어 종료 코드 1을 냈다. 이 기록을 보존했다. 같은 소스에서 실제 입력 보충 XML까지 포함한 `unity-skipped-everywhere.mjs` 재검사는 unexplained 0 / listed 1이다. 유일한 listed는 선택적 recorded-input sequence export다.
- 원자료 루트: `/Users/rexxa/work/bs-mobile-estate-loops-466/artifacts/work-loops-466`. `execution/run-20261028-113508`은 초기 소스의 기록이며 최종 소스 증거로 대체 사용하지 않는다.
- 비교 전은 main `2074f574`에 검토 fixture만 추가한 `304a38d1`, 별도 소유 worktree에서 실행했다. 비교 후는 `313a803e`. 완성 밭·곡물창고와 긴 대장간 공사를 같은 저장 상태로 구성하고 표시 시각 0,5,…,30초를 고정했다. 변경 전 fixture 검사 1/1 통과.

## 그리기 비교의 한계

[고정 시각 비교 CSV](estate-work-loops-466-batches.csv)의 모든 7개 표본에서 materialRuns는 전후 17, SetPass는 전후 14였다. materialRuns는 구조적 재질 전환 수이며 실제 draw 수가 아니다. 동일 Canvas를 임시 Screen Space Camera로 렌더해도 ProfilerRecorder의 batches/drawCalls는 양쪽 모두 0을 반환했다. 따라서 두 칸은 미확인(빈 값)으로 기록하며 실제 draw 증가 0 또는 성능 통과로 해석하지 않는다. 원본 0 값은 각 worktree의 `artifacts/work-loops-466/active-fixture-batches.csv`에 보존한다.

## 화면과 검토 범위

첫 진입, 밭 공사, 밭 완성 화면은 최종 두 프로필 실행 중 실제 Editor UI에서 캡처했다. 작업 세 동작을 함께 보여주는 별도 장면은 저장 상태 fixture이며 자연 성장으로 세 동작에 도달했다는 증거가 아니다. 30초 영상 역시 View 시각을 명시적으로 진행하는 1,800프레임·60fps 오프라인 검토용이다. 정상 속도 런타임 성능·실기·사람 수용 판정을 대신하지 않는다.

[선정 화면과 영상 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-work-loops-466-20261028). 선정 기준은 첫 화면·공사·완성 상태 및 세 작업 동작과 운반 복귀를 확인할 최소 표본이다. 전체 성공 테스트 원자료나 재생성 가능한 이미지 묶음은 업로드하지 않는다. PR은 draft로 유지하고 사용자 결정 전 병합하지 않는다.

영상 실행 `execution/video-20261028-130252`의 fixture 검사 1/1 통과. 1,800개 PNG와 각 capture guard를 대조해 cyan 0, 캡처 전후 셰이더 컴파일 0을 확인했다. 명시적 View 시작 시각은 44.2895977313702초이며 첫 창고 도착 8초 전이다. frame 480–600에서 sack, frame 624/660에서 걷기 복귀를 확인했다. 표본 프레임 검토를 연속 동작의 사람 승인으로 확대하지 않는다. 자산 집중 검사 `node --test tools/test-estate-{art,approved-art,approved-sites,work-loops}.mjs`는 14/14 통과했다.
