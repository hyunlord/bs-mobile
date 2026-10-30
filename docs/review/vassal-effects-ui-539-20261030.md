# 가신 효과 UI #539 검증 기록

관문: 부분 — 두 프로필 테스트 실패 0, 보충 실행 후 미설명 skip 0. 실제 출정 확인 화면의 배경 비침은 시각 수정이 필요하다.

- 검증 소스: `192bfaedfde8ba609bc1f8ff96356e4f23223c75`, `feat/539-vassal-effects-ui`.
- [Core PR #528](https://github.com/hyunlord/bs-mobile/pull/528)의 `feat/527-vassal-run-v1` 위에 쌓는 초안이다. 부모 PR과 이 UI는 사용자 병합 승인 전이다.
- 승인 기준: [가신 탭·출정 r1 / HUD r2](https://github.com/hyunlord/bs-mobile/releases/tag/vassal-effects-ui-531-20261029). 승인 시안과 실제 계정의 값·편성 수는 다르다.
- 변경 범위는 View·UI·Unity 테스트 12파일이다. 이 UI 커밋은 Core 규칙·저장·콘텐츠 데이터를 수정하지 않는다.
- CI 최종 결과와 공개 화면 링크는 아직 미확정이다. 아래 로컬 캡처를 공개 전달한 것으로 표현하지 않는다.
- 시각 검토 뒤 담당자가 패널 입력 차단기의 `transition=None`·raycast 유지와 회귀 assertion을 추가했다. **그 후 소스의 Unity 검증은 아직 하지 않았다.** 아래 기록은 수정 전 `192bfaed`의 역사 기록이며 수정 후 최종 관문으로 재사용하지 않는다.

## 실행 관문

| 프로필 | EditMode 통과 / 실패 / skip | PlayMode 통과 / 실패 / skip |
|---|---:|---:|
| first-playable | 919 / 0 / 25 | 55 / 0 / 52 |
| wave-1a | 929 / 0 / 15 | 94 / 0 / 13 |

`tools/unity-tests.sh`의 원래 종료값은 **1**이다. 모든 테스트는 실패 0이지만, 생성 입력이 없는 두 검사가 네 실행에서 모두 skip되어 기록 도구가 거부했다. 원본 `artifacts/day-20261030/vassal-unity/record.md`를 보존한다.

| 보충 실행 | 입력 준비 | 결과 |
|---|---|---|
| `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes` | 기존 `showcase-fixtures` 도구로 frozen combat sidecar 생성 | 1 / 1 통과, 실패·skip 0 |
| `Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures` | 기존 `melee-aim-fixture`와 opt-in `m1-showcase-review` catalog export | 1 / 1 통과, 실패·skip 0 |

기본 4개 XML과 보충 2개 XML을 기존 `tools/unity-skipped-everywhere.mjs`에 함께 넣은 결과는 `skipped in every profile: 0 unexplained, 1 listed`다. 남은 listed는 `Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`의 선택적 recorded-input export다. 새 예외 등록이나 검사 약화는 없다. 보충 뒤 first-playable generated content를 복원했으며 추적 파일 diff는 0이다.

실행 정본은 로컬 `artifacts/day-20261030/vassal-unity/`와 `artifacts/day-20261030/supplemental/{combat.xml,melee.xml,combined-skips.txt}`다. `VassalEffectsUiTests`는 OFF 안내 보존, ON 빈 편성의 적용값 0, 실제 Core cap 값, 변경 이후에도 고정되는 출정 snapshot, 소수 permille 표시를 검사한다. 이 단위 검사들은 해당 모든 상태의 실제 화면 판독성을 대신하지 않는다.

## 실제 화면 대조

실제 Unity Editor PlayMode, 390×844 및 1080×2520 화면 비율 모사다. 물리 기기·사용자 승인·정상 완주 증거가 아니다. 세 화면군의 상단/하단 12개 청록 JSON은 모두 `cyanPixels=0`, 컴파일 전/후 `false`이며 2026-10-30 11:24 KST에 기록됐다.

| 표본 | 실제 확인 | 남은 문제 |
|---|---|---|
| 가신 목록 | 390×844에서 6행, 보유/적용/상한, 잠금과 편성 표시, 하단 탭이 읽힌다 | 현재 계정은 1명·Lv1이다. 최대 편성의 상한 도달 문구는 EditMode로 확인했으나 이번 실제 캡처에서 확인하지 못했다 |
| 출정 확인 | 고정 능력 1명, 미편성 0 값과 출정/편성 변경 버튼이 표시된다 | **승인 시안과 달리 패널 뒤의 챕터·가신·장비 글자가 비쳐 전경 글자와 겹친다. 시각 불합격** |
| 전투 시작 HUD | 상단 HP·시간·`가신 1 · 켜짐`·일시정지가 보인다 | 실제 캡처는 시작 무기 1개 상태다. 승인 HUD r2의 후기 14개와 `+N` 실제 화면 대조는 미확인 |

로컬 표본 경로:

- `artifacts/portrait-234/editor-meta/vassals-approved-list-390x844.png`
- `artifacts/portrait-234/editor-meta/vassal-departure-approved-390x844.png`
- `artifacts/portrait-234/editor-meta/vassal-hud-start-390x844.png`
- 승인 비교 원본: `artifacts/vassal-527/ui-approved-readonly/`의 목록·출정 PNG 및 `vassal-hud-late14-r2-390x844.png`.

출정 패널은 `PortraitMetaPanels.VassalEffects.cs`의 `VassalDepartureConfirmation`에서 `ui.panel` 위에 비활성 `Button`을 추가한다. 담당자는 비활성 색 전환을 막는 수정과 회귀 assertion을 추가했다. 이 문서는 수정 전 실패 화면을 보존하며 수정 후 실제 캡처를 다시 확인하기 전 전체 시각 관문 통과를 선언하지 않는다.

## 한계와 후속

- 필수 CI 최종 결과 미확정. 초안 유지, 자동 병합 없음.
- OFF/빈 편성/상한 도달/후기 HUD의 실제 시각 비교는 아직 모두 갖춰지지 않았다.
- 이 커밋의 C05/FP01 성능 측정은 NOT_RUN이다. UI 테스트를 성능 관문으로 대체하지 않는다.
- 역사 해시의 Core 증거는 부모 #528 기록에 있으며, 이번 문서가 새 전체 해시 실행을 했다는 뜻이 아니다.
- 설계 문서 영향: 승인 가신 UI 계약을 구현하는 범위이며 새 Core 정책은 없다. GDD 동반 갱신 여부는 PR 작성자가 최종 정리한다.
