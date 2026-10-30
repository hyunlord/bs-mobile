# 영지 UI #544 실제 화면 검증

날짜: 2026-10-30. 관문: **부분** — 두 프로필 검사 실패 0, 청록 0. 승인 그림과 실제 화면 비교를 제출하며 사용자 시각 판정은 대기한다. PR은 초안이고 병합하지 않는다.

## 출처와 범위

- 실행 소스: `269a2b58e103b1db37b0de01b1ad5072622ba263`.
- 승인 그림: [estate-ui-507-20261029](https://github.com/hyunlord/bs-mobile/releases/tag/estate-ui-507-20261029), 상단 r1 / 건물 3상태 r2 / 하단 r3.
- 비교: [estate-ui-game-544-review-20261030](https://github.com/hyunlord/bs-mobile/releases/tag/estate-ui-game-544-review-20261030). 각 PNG는 왼쪽 승인안, 오른쪽 실제 390×844 Editor PlayMode 화면이다. 건물 상태 비교의 왼쪽은 r3 합본에서 해당 390×844 칸만 추출하여 승인된 r3 하단까지 함께 비교한다.
- Core·data·저장 형식 변경 없음. 실제 기기, 네이티브 빌드, 성능 측정 또는 사용자 승인으로 표현하지 않는다.

## 실행

공유 잠금 아래 13:17~13:30 KST에 실행했다. 마지막 first-playable export 복구와 tracked diff 0 확인 후 자기 잠금을 해제했다.

```sh
UNITY_TESTS_OUTPUT="$PWD/artifacts/estate-ui-515/unity-269a2b58" bash tools/unity-tests.sh
```

| 프로필 | 검사 | 통과 | 실패 | skip |
|---|---|---:|---:|---:|
| first-playable | EditMode | 910 | 0 | 25 |
| first-playable | PlayMode | 55 | 0 | 52 |
| wave-1a | EditMode | 920 | 0 | 15 |
| wave-1a | PlayMode | 94 | 0 | 13 |

전체 스크립트의 최초 종료 코드 1은 두 입력 의존 검사만 양쪽 프로필에서 skip된 결과다. 예외 목록은 바꾸지 않고 아래 추가 입력을 실제 생성하여 동일 소스에서 각각 실행했다.

| 추가 입력 검사 | 입력·프로필 | 결과 |
|---|---|---|
| `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes` | C05 파생 data의 `showcase-fixtures`, wave-1a | 1 통과 / 0 실패 / 0 skip |
| `Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures` | `melee-aim-fixture data … m1-showcase-review`, opt-in export | 1 통과 / 0 실패 / 0 skip |

6개 XML을 `tools/unity-skipped-everywhere.mjs`로 합쳐 **미설명 skip 0, 기존 명시 skip 1**을 확인했다. 남은 명시 skip은 `Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`의 선택적 영상 출력이다. 원본 4개 XML과 최초 실패 기록은 로컬에 그대로 보존한다. 추가 결과는 `artifacts/day-20261030/supplemental/{combat,melee}.xml`이다.

## 실제 캡처와 UI 계약

캡처 폴더는 `artifacts/portrait-234/editor-meta`, 파일 접미사는 모두 `-390x844.png`다. 실제 게임 UI를 렌더한 Editor 표본이며 승인 그림 배경은 오프라인 합성 표본이므로 건설 단계 지면의 픽셀 동일성은 요구하지 않는다.

| 파일 이름 앞부분 | 프레임 | 청록 픽셀 |
|---|---:|---:|
| estate-approved-r3-overview-formal | 636 | 0 |
| estate-approved-r3-selection-formal | 640 | 0 |
| estate-approved-r3-construction-formal | 644 | 0 |
| estate-approved-r3-complete-formal | 648 | 0 |
| estate-development-chrome-off | 1909 | 0 |
| estate-development-chrome-on | 1913 | 0 |

- 내비게이션/출정 높이 48px, 닫기 48×48px, 선택 창 위 줌 버튼 비겹침 검사가 통과했다.
- 가속은 세 상태 모두 비활성이다. 콜백 직접 호출에도 저장 바이트가 바뀌지 않는다.
- 공사 중 이동 비활성, 완공 후 이동 활성, 개발 표시 on/off 보존을 검사했다.
- 새 영지 곡창 Lv0→공사→Lv1을 실제 명령으로 전환했다. 완공 표본에는 테스트의 명시적 `SkipTime(600)`을 사용했다. 정상 시간 대기나 BM 가속 기능으로 주장하지 않는다.
- 정식 화면 4장 수동 이미지 확인에서 표시 문구 잘림이나 청록은 발견하지 않았다. 모든 언어·긴 재고값·기기 비율을 포괄하는 증거는 아니다.

## 승인 그림과 남은 차이

현재 기존 게임 자산을 재사용하므로 승인안보다 종이 패널의 장식 테두리가 두껍고, 선택 창 글꼴과 닫기 버튼 모양이 다르다. 줌 버튼도 승인안의 파란 원 대신 기존 테두리형이다. 배치·3상태·비활성 계약 통과를 그림 일치나 사용자 최종 승인으로 확대하지 않는다. 이 차이는 비교판에 그대로 드러내고 사용자가 판정한다.

첫 overview 기록의 컴파일 대기 진입 값은 true였지만 대기 뒤 캡처 후 값은 false, 청록은 0이었다. 동기 컴파일 설정과 기존 감시 도우미를 유지했다.
