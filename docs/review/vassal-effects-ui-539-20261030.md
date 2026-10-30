# 가신 효과 UI #539 검증 기록 초안

관문: 부분 — 수정본 두 프로필 실패 0, 보충 검사 후 미설명 skip 0, 실제 표본 8장 청록 0. `02fbf8ad`의 CI quality·secrets 통과. 출정 패널 비침은 해소됐으며 사용자 화면 판정은 별도다.

- 최신 소스: `42f4043df6d4c92881d7894957654dcd9a70d731` (`feat/539-vassal-effects-ui`).
- 부모: [Core #528](https://github.com/hyunlord/bs-mobile/pull/528), base `feat/527-vassal-run-v1`. 부모와 UI 모두 사용자 병합 승인 전 초안이다.
- 승인 기준: [가신 UI r1·HUD r2](https://github.com/hyunlord/bs-mobile/releases/tag/vassal-effects-ui-531-20261029).
- Core 규칙·콘텐츠 수·편성/장비 한계를 바꾸지 않았다. UI와 검증 입력만 수정했다.
- [초안 PR #568](https://github.com/hyunlord/bs-mobile/pull/568)의 `02fbf8ad190251bcbceaa240ba2f9ea356ea914c`에서 CI가 통과했다. 아래 결과는 실제 XML·JSON·화면·CI에서 확인한 값이다. 이 기록을 추가한 후속 문서 커밋의 CI는 별도로 확인하며, 이전 통과를 새 head의 결과로 표현하지 않는다.

## 최종 실행

| 검증 | 결과 |
|---|---|
| first-playable EditMode / PlayMode | 919 / 0 / 25, 55 / 0 / 53 (통과/실패/skip) |
| wave-1a EditMode / PlayMode | 929 / 0 / 15, 95 / 0 / 13 |
| 생성 입력 보충 검사 / 공통 skip | combat 1/1·melee 1/1 통과, 각각 실패·skip0. 6XML 합산0unexplained/1listed |
| 종료 후 추적 파일 diff | `git status --short` 출력0, HEAD `42f4043d` |
| CI quality / secrets | [40996261531](https://github.com/hyunlord/bs-mobile/actions/runs/40996261531) 둘 다 PASS, head `02fbf8ad` |

## 보존한 실패와 수정

| 소스 | 실행 결과 | 판단·조치 |
|---|---|---|
| `192bfaed` | FP Edit 919/0/25, Play 55/0/52; wave Edit 929/0/15, Play 94/0/13 (통과/실패/skip) | 원래 runner exit1: 공통 입력 누락 skip2. 기존 도구로 sidecar·melee catalog를 생성해 해당 검사 각1/1 통과. 6 XML 합산 미설명0/listed1. 이 별도 보충은 원래 exit1을 지우지 않는다 |
| `192bfaed` 실제 화면 | 출정 확인 뒤의 챕터·장비 글자가 비쳐 전경과 겹침 | 시각 실패. 비활성 패널 Button의 색 전환을 끄고 raycast 차단은 유지, 회귀 assertion 추가 |
| `dad3e2c9` | FP Edit 919/0/25, Play 55/0/53; wave Edit 929/0/15, Play 94/1/13 | 실패1은 `VassalEffectsCaptureTests.RenderOffEmptyCappedDepartureAndEquipmentStressFixtures`: `Expected: 7 / But was: 5`, 126행. wave catalog의 서로 다른 무기 수를7로 가정한 새 표본 검사 오류. 게임 실패로 확대하지 않으며 실행 자체의 FAIL은 보존 |
| `42f4043d` | 위 최종 네 suite 실패0. 원래 runner exit1은 공통 입력 누락 skip2. 최신 소스 보충2개 통과와 합산으로 해소 | 실제 catalog 전체의 독립 표본과 14entry 표시 스트레스 표본을 분리. 후자는 숨긴 항목만 반복해 `+N`을 검사하고 실제 loadout으로 주장하지 않음. 게임 콘텐츠·장비 상한 증가 없음 |

첫 실행의 공통 skip2는 `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`, `Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`다. listed1은 선택적 recorded-input sequence export다. 새 예외를 추가하지 않는다. `dad3e2c9`의 first-playable skip1 증가는 wave 전용 화면 fixture가 해당 프로필에서 의도적으로 건너뛰기 때문이다.

## 실제 렌더 확인과 증거 범위

최종 `42f4043d` 표본은 Unity Editor PlayMode에서 화면을 실제로 그린 것이지만 `testFixture=true`, `normalPlayEvidence=false`, `physicalDevice=false`, `userAcceptance=false`다. 계정 진행·성능·실기·사용자 승인 증거로 대체하지 않는다. r2에서 확인했던 패널·목록과 함께 r3의 OFF 하단·실제 catalog·late14를 확인했다.

| 표본 | 확인 | 범위 제한 |
|---|---|---|
| `fixture-vassal-departure-capped-390x844.png` | 3명·고정 값과 미편성0이 읽히며 승인 시안처럼 패널 뒤 글자가 비치지 않음 | 목록 위에 직접 올린 capped snapshot fixture, 정상 계정 흐름 캡처 아님 |
| `fixture-vassals-capped-390x844.png` | 6행, 3명 편성, `상한 도달 — 더 강화해도 전투 효과 없음` 표시 확인 | 최대 상태를 구성한 테스트 입력 |
| `fixture-vassals-enabled-empty-390x844.png` | 보유 능력은 있지만 `편성0/3`, 여섯 적용값0 표시 | 빈 편성 fixture |
| `fixture-vassals-off(-bottom)-390x844.png` | 하단의 `이번 출정에서는 가신 능력 보너스가 적용되지 않습니다.` 안내가 읽힘 | 기존 OFF 상세 화면을 보존한 fixture |
| `fixture-vassal-hud-early4-390x844.png` | 2무기·2도구, `가신3 · 켜짐`, 일시정지 표시 확인 | **회색 바탕의 render-only UI fixture**. 실제 전투 세계 위 후기 HUD 증거 아님. HP1800은 주입 frame의 값으로 실제 최대 가신 HP 검증에 사용하지 않음 |
| 실제 catalog / late14 | 실제 catalog 무기5·도구4는 `+3`/`+2`, late14는 `+5`/`+5`로 접힘. 2+2 아이콘과 수치 읽힘 | 두 이미지 모두 회색 render-only. late14는 반복 숨김 entry가 있는 표시 스트레스이며 합법적인 실제14장비 편성 아님 |

최종 r3 fixture8장의 청록 기록은 모두0, 컴파일 전/후 false다(2026-10-30 12:07 KST). 각 PNG의 `.txt`가 `42f4043d`, `sourceDirty=False`를 기록한다. 로컬 위치는 `artifacts/vassal-539/fixture-captures/`이며 각 PNG의 `.txt`에 소스 commit과 fixture 성격이 있다. 원래 실제 계정 흐름의 목록·출정·시작 HUD는 `artifacts/portrait-234/editor-meta/`이며 새 렌더 fixture와 혼동하지 않는다.

최종 전체 기록은 `artifacts/day-20261030/vassal-unity-r3/record.md`, 보충은 `supplemental-r3/{combat.xml,melee.xml,combined-skips.txt}`다. 보충은12:08KST 완료했다. 원래 전체 runner의 skip 집계 exit1은 보존하고 보충 성공으로 덮어쓰지 않는다.

## 공개와 다음 관문

- [검토 Release](https://github.com/hyunlord/bs-mobile/releases/tag/vassal-effects-ui-game-539-20261030)에 선정 실패·승인 비교용 표본 7장을 공개했다. Release target과 Unity 검증 소스는 모두 `42f4043d`다. 전체 성공 로그를 중복 업로드하지 않았다.
- Unity 검증과 `02fbf8ad`의 CI는 완료했고 사용자 승인 전 초안 유지. 후속 문서 커밋은 CI를 다시 확인한다. 그림의 최종 사용자 승인은 별도다.
- C05/FP01 성능은 이 UI 검증에서 NOT_RUN. 역사 Core 해시는 부모 #528 증거이며 이번 문서가 새 전체 해시 실행을 뜻하지 않는다.
- 설계 문서 영향은 기존 승인 UI 구현 상태 갱신이며 새 Core 정책은 없다. GDD 동반 변경 여부는 담당자가 최종 확인한다.
