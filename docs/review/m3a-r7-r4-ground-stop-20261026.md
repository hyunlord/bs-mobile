# M3a-7 r4 지면·곡선 길 검증과 중단

- 날짜: 2026-10-26. 상태: **실패, 초안 유지**.
- 구현 `86e6ba5`, 검사 보정 및 최종 Unity source `cbf8d00d46da647eb07d9dab6dc8eca2321988cf`.
- #386 병합 승인은 확인되지 않아 병합하지 않았고 #390의 base도 유지했다.
- 화면: [선정 표본 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3a-r7-r4-ground-stop-20261026).

## 바뀐 내용과 원인 구분

승인된 `ground-grass-r4` 단일 그림을 동일 UV·색의 8×8셀 타일로 연속 배치했다. 이전 r7의 상태별 restoration/sage/fallow 큰 패치 중첩은 명암 얼룩의 원인 후보였으나, 이번 변경 뒤에도 회색 대각선이 남았으므로 이것만을 전체 원인으로 확정하지 않는다. 흙6종은 결정적으로 배치하며 전체 그림 범위가 부지·길과 겹치지 않게 검사한다. 실제 배치 수는 01=1, 02=5, 03=1, 04=2, 05=2, 06=3이다.

도로는 44개 논리 칸·43간선의 나무 구조를 유지하고 9개 곡선 리본으로 그린다. 명목 폭은 첫 화면 큰길18px·가지길11px이며, 한 칸 통로의 수직 폭14.53px 때문에 기초 앞에서 좁아진다. 이 **메시 폭이 화면에서 읽히는 흙길 폭을 증명하지 않는다**. 논리 끝점은 다리(35,23.8)와 일치하지만 화면에서는 다리까지 이어지는 길이 보이지 않는다.

r7 어둠 마스크만 해상도8 texel/cell 및 3회 분리형 블러를 적용했다. 기존 r3~r6 방식·설정은 유지했다. 건설 가능 여부는 기존 Core 명령을 사본에 실행해 거부 사유를 얻고 원상태는 버린다. 새 영지 경작지 선택 시 `곡물창고 Lv1 필요`와 비활성 짓기 버튼이 실제 Editor 캡처에 보인다. 규칙·자원·저장 형식을 바꾸지 않았다.

## 실제 화면 판정: 실패

11:42 새 캡처를 두 검토자가 독립적으로 확인했다. 이전 10:40 화면은 이번 증거로 사용하지 않았다.

| 항목 | 관측 |
|---|---|
| 길 | 여전히 가는 황금 선·직사각 윤곽이 먼저 읽힘. 18/11px 흙길의 위계 미충족 |
| 지면 | 밝은 마을 우하단으로 회색 대각선이 파고듦. 첫 화면 강둑과 전체 보기 지도 끝에 큰 각진 경계 노출 |
| 다리 | 논리 연결 검사와 달리 실제 화면에서 큰길→동쪽 다리 연결이 끊겨 보임 |
| 빛 | 흰 테두리·칸 계단이 보이지 않고 둥글게 번짐 |
| 거부 설명 | 경작지의 `곡물창고 Lv1 필요`와 비활성 버튼 확인 |
| 기존 한계 | 선택/완공 밭 크림색 받침, 공사 표지 글씨 뭉침, 반복 강굽이 보존 |

앞선 낮 검토에서 실패한 가는 길·회색 지면이 다시 나타나 **같은 실패 두 번 중단 규칙을 적용**했다. 추가 수정·Unity 실행은 하지 않았다. native 개발 빌드는 이미 ConfigureMac 단계에 들어갔으므로 자기 build/Unity 프로세스만 종료했다. 앱 빌드 완료·native 재실행은 **미실행**이며 성공으로 기록하지 않는다. 곡창→경작지 하네스 코드는 준비됐으나 재개 승인 뒤 실제 실행이 필요하다.

## Unity 결과

최종 source `cbf8d00`, 11:36~11:43 KST. 자기 잠금 11:23:21 획득, 11:45:15 해제 확인. 다른 작업의 프로세스·출력·야간 대기열은 변경하지 않았다.

| 프로필 | EditMode 통과 / 실패 / skip | PlayMode 통과 / 실패 / skip |
|---|---:|---:|
| first-playable | 852 / 0 / 25 | 41 / 0 / 44 |
| wave-1a | 862 / 0 / 15 | 72 / 0 / 13 |

이번 두 PlayMode 시간구간의 일반 캡처248건 청록0. 의도적인 negative-control4건은 제외했다. 기존 공통 미목록 skip2로 `tools/unity-tests.sh`는 종료1이다. 예외를 추가하지 않았다.

- `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`: 실제 fixture sidecar 부재.
- `Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`: opt-in melee aim catalog 부재.
- 목록에 있는 `RecordedWaveRangeExportsActualTickFrames`는 선택적 입력 시퀀스 내보내기이다.

첫 시도 `86e6ba5`에서는 기존 검사가 모든 구역 안에 지면 패치 **중심**을 요구해 실패했다. 큰 타일은 여러 구역을 덮을 수 있으므로 r7만 모든 구역의 **각 셀이 실제 타일에 덮임**을 검사하도록 바꿨다. 역사 프로필 검사는 유지했고 최종 실행에서 통과했다. 최초 실패 원자료는 로컬 `artifacts/unity-tests-r4-ground`에 보존했다.

## 묶음 수: 구조 계수

11:42:04~08의 새 Graphic trace CSV 기준이다. GPU 시간·C05 성능 수치가 아니다.

| 설정 | on 기본+지형지물=합계 | off 기본+지형지물=합계 | Lv0 제외 시 차이 on/off |
|---|---:|---:|---:|
| r3~r5 | 12+2=14 | 10+2=12 | 2 / 1 |
| r6 | 12+2=14 | 9+2=11 | 2 / 1 |
| r7 | 10+2=12 | 8+2=10 | 0 / 0 |

r7은 앞 실행과 동일12/10. 승인 부지와 새 지면/길은 같은 terrain atlas를 쓰며 Lv0 추가0이다. 과거 off 기본10의 +2 원인 전체는 당시 trace 부재로 여전히 미확정이다. 이번 실패를 예산 변경으로 덮지 않았다.

## 증거 재현

- `UNITY_TESTS_OUTPUT="$PWD/artifacts/unity-tests-r4-ground-final" ./tools/unity-tests.sh`
- 선택 화면: `artifacts/portrait-234/editor-meta/estate-r7-*-390x844.png` (11:42 생성분).
- 비교: `artifacts/m3a-r7-r4-ground-review/first-entry-C-r4-actual.png`, `first-entry-3x.png`, `field-parent-required.png` 및 전체/건설/완공 비교.
- 화면 비교는 실제 Editor 캡처이며 게임 빌드·사람 플레이 증거가 아니다. 이미지 차이 비율은 서로 다른 배치의 참고값일 뿐 통과 문턱이 아니다.
- 이번 변경은 Core 소스·설정 JSON·저장 codec을 건드리지 않았다. 이전 crowd411 해시 일치 증거는 앞 낮 기록에 보존하며 이번에 새로 재생했다고 주장하지 않는다.

PR #390은 초안 유지, 사용자 재개 판단 전 추가 Unity/native/화면 수정 없음. CI의 최종 상태는 PR 본문에 링크와 함께 기록한다.
