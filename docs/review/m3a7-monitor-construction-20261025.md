# M3a-7 감시·건설 UI와 시작 배치 진단 — 2026-10-25

상태: 청록 원인 미확정. 배치는 진단만 수행했으며 변경하지 않았다. PR #386은 초안을 유지한다. Core·저장 형식 변경은 범위 밖이다.

## 과거 청록 실패의 실행 순서

보존 로그 기준, 743d40c의 두 청록 실패는 20:40:54 셰이더·규칙 수정 직후 첫 PlayMode 프로세스(20:41:24–28 KST)의 첫 두 테스트에서 발생했다. 프로세스 초기 어셈블리 재로드는 확인되지만 PlayMode 진입의 Domain Reload는 꺼져 있었으므로, 시간적 일치만 확인되며 원인은 미확정이다. 두 실패가 각각 별도의 수정 직후 실행이었다는 뜻은 아니다.

| 근거 | 보존 위치 |
|---|---|
| 같은 실행에서 6개 중 첫 두 테스트 실패, UTC 11:41:24–28 | `artifacts/m3a7-targeted/PlayMode.xml:2,35,52` |
| 첫 실패 청록 68,526px, 두 번째 116,525px | 같은 XML `:45,62` |
| 뒤이어 첫 5프레임 청록 검사 통과 | 같은 XML `:69` |
| 스크립트 컴파일 0.601914초 → assembly reload → 성공 | `artifacts/m3a7-targeted/PlayMode.log:245,246,256` |
| domain reload 1회, PlayMode 진입은 Reload Domain disabled | 같은 로그 `:288,409` |

기존 파일 시간 기록: `EstateTint.shader`·`EstateDecorRules.json` 수정 시각 20:40:54, configure/EditMode/PlayMode 로그 수정 시각 20:41:14 / 20:41:20 / 20:41:29. 파일 시각과 실행 순서는 인과를 증명하지 않으며, 해당 실패 캡처 순간의 `anythingCompiling` 값은 소급 확인할 수 없다. 이번 확인을 위해 새 Unity 실행을 하지 않았다. 이후 0/68 및 cold 실행 미재현 기록은 [셰이더 가설 보고](m3a7-shader-hypothesis-20261025.md)에 보존한다.

## r6 시작 배치

정본: `data/opt-in/account-estate/m3a-prototype-r6.json`. 모든 부지는 2×2이고, 집만 L1로 시작한다. L0 건물도 부지를 예약하므로 빈칸으로 세지 않는다. instance ID는 설정 순서+1이다(`AccountEstate.cs:99–103`). 좌표는 설정의 X/Y 시작 칸이다.

| ID | 건물 kind | X,Y | 부지 | 시작 단계 | 설정 근거 줄 |
|---:|---|---|---|---|---:|
| 1 | `m3a:home` | 28,28 | 2×2 | L1 | 258 |
| 2 | `meta:granary` | 26,26 | 2×2 | L0 | 267 |
| 3 | `meta:forge` | 32,26 | 2×2 | L0 | 302 |
| 4 | `meta:scriptorium` | 28,32 | 2×2 | L0 | 338 |
| 5 | `meta:barracks` | 30,32 | 2×2 | L0 | 374 |
| 6 | `m3a:watchtower` | 32,32 | 2×2 | L0 | 411 |
| 7 | `m3a:well_shrine` | 30,28 | 2×2 | L0 | 443 |
| 8 | `m3a:field` | 26,30 | 2×2 | L0 | 475 |
| 9 | `m3a:lumber_camp` | 26,32 | 2×2 | L0 | 496 |
| 10 | `m3a:ironworks` | 32,28 | 2×2 | L0 | 517 |

| 집계 | 칸 수 |
|---|---:|
| 정의된 13개 구역 합집합 | 1,664 |
| 시작 열린 영역(26,26), 8×8 | 64 |
| 건물 점유(10×4), 논리 중복 0 | 40 |
| 열린 빈칸 | 24 |
| 닫힌 구역 | 1,600 |

열린 땅 점유율은 62.5%다. 전체 구역을 감싸는 사각형은 68×60=4,080칸이지만, 그중 2,416칸은 구역에 속하지 않으므로 건설 가능한 영지 칸으로 세지 않았다.

```text
       x26..33 (# 점유, . 빈칸)
y26    ##....##
y27    ##....##
y28    ..######
y29    ..######
y30    ##......
y31    ##......
y32    ########
y33    ########
```

맨 아래 두 행은 4채가 빈칸 없이 이어진다. 논리 부지 충돌은 없지만 이 배치가 화면의 건물 기초·마당 사이 여유를 보장하지는 않는다.

## 배치 선택지 영향 범위 — 구현하지 않음

| 항목 | 격자 유지: 시작 배치 벌리기 + 빈칸 자동 길·마당 | 실제 건물 부지를 3×3으로 변경 |
|---|---|---|
| 열린 8×8 수용 | 40점유+24빈칸. 선택적 간격 조절 가능하나 모든 건물에 넓은 마당은 어려움 | 10×9=90 >64. 열린 영역 확대 또는 시작 건물 수 변경 필요 |
| Core | 기존 위치·충돌 알고리즘 재사용 | 폭·높이가 매개변수라 알고리즘 재사용 가능하나 규칙 데이터 계약 변경 |
| 저장 바이트 | 장식만 변경하면 불변. 시작 X/Y를 옮기면 새 저장 바이트 변경 | configVersion·좌표/열린 구역 변경으로 새 저장 바이트 변경 |
| 역사 저장 | r3~r6 설정 동결, 새 시제품 버전으로 분리하는 방안 권고 | 동일. 기존 r6 좌표에 크기만 3×3 적용하면 겹침 발생 |
| 저장 형식 | 버전6 유지 가능 | 크기는 저장하지 않으므로 형식 변경은 필수 아님. 설정 계약 새 버전 필요 |
| 해시 | View 장식은 규칙 해시 불변. 저장 좌표 변경은 저장 checksum에 영향 | 저장 checksum·배치 검증 결과 변경. 역사 전투 재생은 별도 회귀 확인 |
| 화면 | 기존 자동 길·마당 생성 활용 | 현재 폭+높이 기반 표시 크기도 1.5배 증가하므로 부지와 표시 크기 분리 검토 필요 |
| 테스트 | 새 설정 배치·빈칸·자동 길·기초 겹침 화면·역사 저장 회귀 | 왼쪽 항목 및 3×3 이동/충돌/경계·선택·공사 미리보기·길 생성 |
| 설계 문서 | 시작 배치와 자동 장식 계약 갱신 | 격자·부지·시작 땅 규모·아트 앵커 계약 갱신 |

근거:

- r6의 건물·경제값은 r5와 동일해야 한다: `core/tests/SowSiege.Tests/AccountEstateR6MapTests.cs:31–38`.
- 초기 부지는 열린 구역 내부여야 하고 중복할 수 없다: `core/src/SowSiege.Core/AccountEstate.cs:138–154`.
- 이동 충돌은 실제 Width/Height를 쓴다: 같은 파일 `:373–388`.
- 저장은 ConfigVersion과 건물 X/Y를 포함하지만 Width/Height는 쓰지 않는다: `core/src/SowSiege.Core/MetaSaveCodec.cs:237–245`.
- 자동 길은 이미 집 문에서 각 건물 문으로 이어지며 점유 칸을 제외한다: `unity/Assets/Game/App/EstateDecor.cs:159–172`.
- 기초와 건물 표시 크기는 부지 폭+높이에 연결되어 있다: 같은 파일 `:265,281`.

3×3을 View 장식 예약 영역으로만 취급하면 Core의 건물 이동은 여전히 2×2이므로, 실제 3×3 부지와 동등하지 않다. 어느 안이든 사용자 결정 전에는 배치·열린 영역·설정 버전을 바꾸지 않는다. 현재 브랜치에는 PR #360의 `docs/design/m3-account-estate.md`가 없으므로, 결정 후 그 설계 문서의 반영 상태도 확인해야 한다.

## 이번 변경·검증 결과

- 상시 캡처 감시 구현 완료. 정상/청록 음성 대조 2/2 통과. 비동기·동기·RT readback 모두 감시 연결.
- 건설 UI: 선택 기초 중심 정렬, 이름은 시트에만, 빈 고랑·공사 표지, 시트 중 확대/장식 버튼 숨김. Editor 흐름 확인 완료. 최종 공사 표지 layer 조정은 빌드 컴파일만 확인했고 화면은 미검증.
- 집중 EditMode 33/33, PlayMode 8/8. 전체 두 프로필은 **미실행**: 아래 같은 native 건설 오류 2회로 중단. 최종 표지 layer 변경 후 PlayMode 재검증도 남음.
- macOS ARM64 Mono Development 빌드 성공. 첫 진입 0/1/30프레임은 두 실행 모두 청록 0픽셀. 건설 흐름은 같은 오류 2회로 **실패·중단**.
- `git diff -- core data schema` 빈 결과. Core·설정·저장 형식·r6 좌표 변경 없음. 이번에 전체 리플레이 검증을 새로 실행했다고 주장하지 않음.

이 표의 대기는 통과를 뜻하지 않는다. PR #386은 초안을 유지한다.

### 감시 계약

모든 PlayMode screenshot/ReadPixels 경로는 `PlayModeCaptureGuard`로 기록한다. RGB의 `r<16 && g>230 && b>230`을 청록으로 세며 1픽셀 이상이면 실패 PNG·JSONL을 남기고 assertion 실패한다. `anythingCompilingBefore/After`, 캡처 프레임, 도메인 로드 시각, 도메인 내 캡처 순번, 직전 리로드 관찰 여부를 함께 남긴다. `precedingDomainReloadObserved=false`는 리로드가 없었다는 증명이 아니라 관찰하지 못했다는 뜻이다. 비동기 PNG는 `request-to-file-check` 구간으로 명시하며 GPU 캡처 순간의 상태라고 해석하지 않는다. 의도적 청록 1픽셀 음성 대조는 `guard-self-test-negative-control-*`로 분리한다.

### 검증 이력

- 첫 집중 실행: EditMode 33/33, PlayMode 5/6. 숨긴 확대 버튼을 기존 이동 테스트가 클릭해 `Sequence contains no matching element`로 실패했다. 청록 실패가 아니다. 지도 확대 경로로 바꿔 같은 이동·목적지 가시성 검사를 유지했다.
- 재실행: EditMode 33/33, PlayMode 8/8(영지 6 + 감시 자체검사 2). 이후 공사 표지를 이웃 건물보다 나중에 그리는 View 순서만 수정했으며 최종 전체 검사는 별도 기록한다.
- 첫 Standalone 빌드: 개발 빌드가 요구하는 `artifacts/phase1b/replays/30000.ssreplay`가 없어 중단됐다. wave-1a 정본으로 .NET fixture를 생성한 뒤 재시도하며, 실패 로그는 `artifacts/m3a7-native-monitor-missing-fixture`에 보존했다.
- 두 번째 Standalone 빌드는 하네스의 Editor 전용 `SetPortraitMobileSimulation` 호출로 CS1061이 발생했다. 호출을 제거하고 실제 Mac의 기존 세로 viewport와 390×844 창을 사용한다. Editor App DLL 기반 사전 컴파일은 Player의 조건부 API 제거를 검증하지 못했다. 해당 실패는 `artifacts/m3a7-native-monitor-editor-api`에 보존한다. 첫 누락 fixture와는 다른 원인이며, 같은 원인 재실패 시 중단한다.
- 첫 실행된 Standalone에서는 실제 첫 진입 0/1/30프레임 청록 0, 포커스 유지였다(834×1808 Retina render). 건설 UI가 명령을 다음 Update에 처리하기 전에 하네스가 즉시 상태를 검사해 실패했다. 기존 PlayMode 흐름과 같게 클릭 뒤 1프레임 대기한 뒤 재검사한다. 실패 기록은 `artifacts/m3a7-native-monitor-before-action-update`에 보존했다.


### 최종 중단 판정

22:15 KST 이전에 Standalone 재실행에서도 `Normal build button did not start field construction`이 반복됐다. 한 Update 대기가 원인이라는 가설은 확인되지 않았다. 같은 실패 두 번 규칙에 따라 추가 수정·Unity 실행·전체 두 프로필 실행을 중단했다. PR은 초안이며 병합하지 않는다. 게임 규칙을 바꾸거나 자원을 지급해 건설 캡처를 통과시키지 않았다.

| 실제 Player 캡처 | 첫 실행 cyan | 재실행 cyan | 포커스 | 표면 |
|---|---:|---:|---|---|
| 첫 진입 0 | 0 | 0 | 유지 | 834×1808 Retina |
| 첫 진입 1 | 0 | 0 | 유지 | 동일 |
| 첫 진입 30 | 0 | 0 | 유지 | 동일 |
| 건설 | 미생성 | 미생성 | 오류 후 종료 | 미검증 |

Player에는 ShaderUtil이 없으므로 컴파일 상태는 `NA-Standalone`이다. 첫 진입 0은 타이틀 진입 동작 뒤 첫 완료 렌더다. 개발 하네스는 별도 저장/환경설정 접두사를 사용하며 일반 Release에는 컴파일되지 않는다. Native 증거는 4224f7f 기반 미커밋 소스 빌드이며 sourceHash `D3D8D5D10D8BBD3328CE2E2919980B4BEB75B9DC0A4FCB7C205712CB7E3F62D5`, sourceDirty=true다. 빌드가 생성한 URP runtime-settings 직렬화만 원래 바이트로 복구했다.

상시 감시 도입 전 0/68 미재현과 이번 native 첫 진입 0/6은 별도 표본이다. 과거 실패를 해소했다는 증거가 아니며 셰이더 원인 미확정은 유지한다. 화면의 조밀한 배치는 사용자 결정 전 그대로 유지한다.
