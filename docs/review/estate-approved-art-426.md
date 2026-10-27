# #426 승인 영지 그림 적용

버전: v0.1 · 2026-10-27 · 상태: 구현 브랜치, Unity/사용자 화면 검증 대기.

## 범위와 출처

공사 407 일반1·2/일반3r2/밭1–3, 소품4·일꾼2, 강둑416 밝음r1/어둠r2, 나무416 밝음r1/어둠r2, 운반423 SE/SW16칸. [정확한 출처/앵커](../licenses/m3-estate-art.md), `tools/estate-art.json`에 파일별 지정. 새 의존성·Core·프로필·저장 변경 없음. 모든 수치 조정은 View 시제품이다.

- 공사: 진행률 33/66%에서만 정적 그림 계획을 새로 만든다. 완료 시 기존 완공 그림 사용. stage1/2/3의 바닥 기준점은 동일 (68,124) /136×160. 4종 소품 중 ID로 고른 두 조각을 부지 바깥 앞 가장자리에 놓는다.
- 운반: 완성된 곡창과 공사 부지의 기존 길 그래프를 따라 왕복 한 명. 둘이 같거나 경로가 없으면 정지 운반 자세. 별도 컴포넌트가 캐시한 32스프라이트를 50ms마다 바꾸고 위치만 보간한다. 전체 계획/페이지를 프레임마다 재생성하지 않는다. 발 깊이 순서가 바뀔 때만 hierarchy를 조정한다.
- 강둑: 기존 둥근 돌이 `riverbank-patch`/`river-bend-patch`에 구워져 있었으므로 View shader의 물색 선택으로 물만 남기고 승인 강둑 조각을 얹는다. 원판 PNG는 변경하지 않는다. 밝음/어둠은 열린 상태로 선택, 어둠 r2는 별도 위층에 원본 밝기로 그려 중복 어둠 색조를 피한다.
- 나무: 복원 땅의 군집 후보와 닫힌 숲 후보를 승인된 침엽/활엽/덤불 2변형씩으로 바꾼다. 결정적 배치 seed/일반 소품 규칙은 유지한다.

## 정적 검증

| 항목 | 결과 |
|---|---|
| Node source/anchor/revision 계약 | 2/2 통과 |
| atlas 정합 검사 | 152 sprites / 30 building stages 통과 |
| Unity 경계 검사 | assemblies=9 통과 |
| git diff --check | 통과 |
| 기존 건물 body/shadow·terrain PNG | SHA-256 동일 |
| decor atlas | 동일 1장, 2048×2328 (기존 사용높이738 → 2328), importer 최대4096 이내 |
| EditMode 신규 | 진행률 경계7사례, 셀/앵커, 공사/경로/소품, 왕복, 캐시 단계 전환, 물/강둑 계약 작성. 실행 대기 |
| PlayMode | 기존 공사 화면 기대 그림을 승인 field-stage1로 갱신. 실행 대기 |

`node --test tools/test-estate-approved-art.mjs`, `node tools/estate-art.mjs --check`, `node tools/verify-unity-boundaries.mjs`, `git diff --check`.

## 실제 확인이 필요한 것

- 두 프로필 EditMode/PlayMode와 리플레이 해시. 정적 변경 경계는 실제 해시 결과를 대신하지 않는다.
- 첫 접속/공사1·2·3/완공 vs 키아트 C, 3배 확대. field/일반을 각각 확인.
- 강둑의 물색 마스크가 희미한 물결을 지우거나 구운 돌 잔재를 남기는지, 독립 강둑이 물과 접하는지. 아직 화면 통과 아님.
- 어둠 강둑/땅 RGB·Y′ ROI, 실제 묶음 전후. atlas 수 불변은 묶음 수 불변 증거가 아니다.
- 운반 주민의 길 접점/건물 가림/애니메이션. 승인된 SE/SW만 있어 위로 걷는 구간에서도 아래쪽 자세가 남는다. 새 방향을 생성하거나 기존 시트를 변형하지 않는다.

공유 Mac의 Unity 실행·잠금은 부모 세션에서 직렬로 수행한다. 이 구현 작업은 Unity/Standalone/빌드/야간 측정을 실행하지 않았다. PR은 초안, 사용자 승인 전 병합 없음.

## 실행 진입점 (부모 세션 전달)

- PlayMode `EstateMapFlowTests.FreshR7EstateBuildsItsGranaryBeforeTheFieldAndCompletesWithoutAccountWrites`: 첫 진입, 전체, 선행조건, 공사1/완공. 테스트 안의 기존 `Skip60`는 Editor 개발 시계 증거이며 native 정상 시간 증거가 아니다.
- 개발 macOS `--estate-entry-probe --estate-entry-output <비어 있는 자기 소유 절대 경로>`: 첫 진입0/1/30, 경작지 거부 사유, 정상 곡창60초→경작지30초. 이번 변경은 진행률을 관측만 하여 stage2/3에서 추가 캡처한다. 곡창은 시작 약20/40초, 경작지는 약10/20초에 도달한다. 실제 시점은 `construction.csv` elapsedSeconds에 남긴다. 기존180초 deadline 유지.
- stage 관측은 게임 규칙이나 저장을 바꾸지 않는다. 새로운 자원 지급·공사 시간 수정·native 가속 없음.

## 2026-10-27 두 프로필 실행: 반복 실패로 중단

소스 `d6928f7d3890fa40dc3c29bdc33ba27fb2dd628b`, Mac 소유 잠금 아래 11:26:49–11:38:06 실행. 첫 configure의 `EstateCarryView` 네임스페이스 누락은 한 번 수정했으며 최초 실패 로그를 보존했다.

| 프로필 | EditMode 통과/실패/skip | PlayMode 통과/실패/skip |
|---|---:|---:|
| first-playable | 867 / 3 / 25 | 53 / 0 / 45 |
| wave-1a | 877 / 3 / 15 | 85 / 0 / 13 |

같은 세 실패가 두 프로필에서 반복되어 사용자 중단 규칙을 적용한다. 추가 수정·Unity 재실행·native 건설 시도는 하지 않았다. 공통 입력 의존 skip 두 건은 앞 PR에서 실제 실행됐지만 이 실패 실행에서는 후속 입력 생성 단계로 넘어가지 않았다. 이 PR의 실행 결과를 다른 PR의 성공 기록으로 대체하지 않는다.

| 실패 테스트 | 확인한 원인 | 미적용 수정안 |
|---|---|---|
| LargeLandmarksHaveFortyPercentSilhouettesButSmallPropsStayUnderTheNight | 모든 위층 그림을 기존 landmark 실루엣으로 가정, 새 승인 river-bank 불인정 | 기존 landmark 밝기 검사 유지 + 승인 dark bank 역할·층·원본색 검사 |
| RestorationColoursFollowTendedLand | 이미 어두운 tree 원화에도 기존 closed tint 요구 | frontier 검사 유지 + dark tree 역할·Veiled층·white 검사로 이중 어둠 방지 |
| ConstructionQueuesAndCompletesWithRealTimeAndTheSkip | 60초 공사의 30초 시점에도 redraw=false 요구, 새 stage2 전환과 충돌 | 같은 단계 false / 단계 경계 true 및 자원·Lv0·예약 유지 검사 |

소유 worktree의 `artifacts/day-20261027/pr426-retry-tests`에 XML/로그, `estate-test-contracts.patch`에 미적용 테스트 수정안, `repeated-failure-stop.md`에 중단 기록을 보존한다. 초기 실패는 `pr426-tests`에 있다. 화면·묶음·강둑 밝기·native는 미완료이며 시각 통과로 보고하지 않는다. 잠금은 11:38:06 해제했다.

## 2026-10-27 사용자 승인 후 테스트 계약 재개 (#431)

위 반복 실패와 당시 미적용안은 역사 기록으로 유지한다. 사용자가 재개를 승인하여 보존한 `estate-test-contracts.patch`를 적용했다. 게임 코드·Core·저장·자원·테스트 예외·묶음 예산은 변경하지 않았다.

- 기존 `landmark`의 40% 실루엣 및 닫힌 구역 검사, `frontier`의 closed tint 검사는 유지한다. 승인 강둑에는 dark role 존재·Landmark 층·원본색을, 승인 나무에는 dark role 존재·Veiled 층·원본색을 추가 검사한다. 원화가 이미 어두운 항목에 중복 tint를 요구하지 않는다.
- 같은 공사 단계의 Advance는 false/계획 동일 객체, 33% 경계를 넘으면 true/stage2 그림으로 검사한다. 30초 잔여·Lv0·활성 건물·예약 두 칸은 유지되고, 기존 자원 차감·완공·대기열 검사도 보존한다.
- 이 재개 커밋의 검증은 patch 적용 가능성, diff 공백, Unity 경계 정적 검사뿐이다. 두 프로필의 새 실행·native 공사·화면/강둑 밝기 검증은 부모 세션 담당이며 아직 통과로 기록하지 않는다. 새 Unity 기록 전 push하지 않는다.

## #431 재개 실행 결과 — 2026-10-27 13:16:57–13:27:11 KST

소스 `bdbc578d60c51aaea42bf1460509f28660a99f58`, 부모 세션 소유 잠금 아래 batchmode 두 프로필과 기존 입력 의존 테스트 두 개를 실행했다. wrapper 최종 exit 0, 13:27:11 잠금 해제. 앞선 실패 기록은 그대로 보존한다.

## Unity tests
commit: bdbc578d60c51aaea42bf1460509f28660a99f58
first-playable EditMode: passed 870, failed 0, skipped 25
first-playable PlayMode: passed 53, failed 0, skipped 45
wave-1a EditMode: passed 880, failed 0, skipped 15
wave-1a PlayMode: passed 85, failed 0, skipped 13
skipped in every profile: 0 unexplained, 1 listed


기본 runner는 미제공 입력 때문에 전체 skip 두 건을 보고 exit 1이었다. 이어 기존 도구로 fixture를 생성해 `M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes`와 `MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures`를 각각 1 passed / 0 failed / 0 skipped로 실제 실행했다. 여섯 XML을 합친 결과는 unexplained 0 / listed 1이며 예외 추가 없음. listed 항목은 수동 내보내기 `WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`다. 기본 기록을 덮지 않고 `record-combined.md`에 합친 기록을 남겼다.

청록 감시 UTC [2026-10-27T04:16:57Z, 2026-10-27T04:27:12Z)에서 실제 캡처 관측 262건, 청록 양성 0건 / 0픽셀. 정확한 `guard-self-test-negative-control-(True|False)` 4건만 따로 제외했다. 원자료는 `artifacts/playmode-capture-monitor/events.jsonl`, 집계는 `pr431-authorized-tests/actual-cyan-summary.json`이다. 관측 수는 고유 PNG 수와 다르다.

실행 XML·fixture·로그는 `artifacts/day-20261027/pr431-authorized-tests`. native 공사·키아트 비교·강둑 ROI·묶음 차이와 사용자 화면 승인은 아직 미완료다. CI 최종 결과는 이 기록 push 뒤 PR에서 확인한다. Unity 통과를 native 또는 시각 통과로 확대하지 않는다.

## Dark river-bank display follow-up (2026-10-27; runtime verification pending)

The fresh Editor whole-map capture at source `bdbc578` shows three manually selected bank/adjacent-ground samples with Y′ 32.46/66.44, 28.47/67.33, and 28.47/66.78 (encoded RGB weighted by .2126/.7152/.0722). The banks are only 42–49% as bright and read as near-black outlines. Evidence: `artifacts/day-20261027/editor-visual-review-431/measurements.json`, `whole-bank-ground-rois-3x.png`; these are localized samples, not a new global acceptance threshold.

The closed-bank path uses approved `bank-*-dark` art, white restoration parameters, and an above-night layer. It does **not** use the older 40% landmark tint, so applying a second restoration tint is not the remedy. A display-only candidate adds 0.04 linear RGB through the spare UI vertex UV component exclusively for those closed-bank roles. This preserves authored texture bytes, alpha, old landmark/water shader branches, the single shared material, layer order and planned draw items. Lit banks, trees, water and old landmarks are unchanged. The lift is a calibration candidate derived from the measured dark-ground gap, not a passed visual result.

Focused checks cover real-plan role selection, neutral restoration parameters, exclusions and UV-channel isolation. Fresh first-playable/wave-1a EditMode/PlayMode results and the same bank/ground ROI captures are required after this View/shader change; the earlier successful Unity record is not current evidence for it. No new native attempt is authorized: the one native run failed at `EstateEntryProbe`'s `Title.Enter` lookup before its first capture and remains stopped.

### Real-time Editor stage capture coverage (pending execution)

The existing fresh-r7 flow now keeps its parent-required/resource/account-save assertions and replaces only its two development `Skip60` clicks with normal elapsed-time waits. It observes the real UI's granary and field stages 1/2/3, verifies each displayed sprite against its approved role before guarded capture, rejects skipped/crossed stages, and waits for normal completion. Each building has a bounded total-duration+15-second deadline within the existing 180-second test timeout (normal construction totals approximately 90 seconds). `estate-r7-construction-observations.csv` records source SHA, Editor development-copy surface, work identity, total/remaining seconds and observed role. This adds actual stage2/3 screenshots without another native attempt; no new execution result is claimed here.

## #431 bank and natural-clock review — 2026-10-27 14:34:24–14:51:31 KST

관문: 부분 — 두 프로필·보충검사 통과, 실제 Editor 6단계 관측. Native Title.Enter 실패 중단과 사용자 화면 승인은 남음. 실행 소스108236b35590dc231b97280a26667ddbe6a719e7, END/OWN_LOCK_RELEASED14:51:31, tracked clean.

## Unity tests
commit: 108236b35590dc231b97280a26667ddbe6a719e7
first-playable EditMode: passed 878, failed 0, skipped 25
first-playable PlayMode: passed 53, failed 0, skipped 45
wave-1a EditMode: passed 888, failed 0, skipped 15
wave-1a PlayMode: passed 85, failed 0, skipped 13
skipped in every profile: 0 unexplained, 1 listed

기본 runner exit1의 미제공 fixture 2건은 그대로 보존하고, 기존 도구로 생성한 combat/melee를 각1/0/0 실제 실행했다. 여섯 XML 합산 unexplained0/listed1(선택적 RecordedWaveRangeExportsActualTickFrames). 예외 추가 없음. ReplayParityTests 30000–30004 및 MetaReplayParityTests 5건 실제 통과, wave 프로필은 WaveReplayParityTests 검증, 보충 melee도 통과. 별도 신규 CLI 해시나 native 해시를 실행한 것으로 주장하지 않는다.

청록 감시 UTC05:34:24–05:51:32: 272관측 중 정확한 자체검사4건(의도적 양성2) 제외, 실제268건 전부 cyan0. 파일: pr431-bank-brightness-tests-r2/actual-cyan-summary.json. 최초 d0c6b9 시도의 UiShell 미해결 CS0103와 wrapper XML없음 ENOENT는 pr431-bank-brightness-tests에 보존; 수정108236b 재실행만 위 성공 기록이다.

### 실제 자연시계 공사 관측

[6행 정본 CSV](estate-approved-art-426-stages.csv): 곡창 remaining60/40/20 of60초, 밭30/20/10 of30초에서 stage1/2/3 및 실제 sprite role을 검사하고 캡처했다. 정상 UI Build 후 elapsed-time 대기, Skip60·자원주입·수동 작업상태 변경 없음. Editor 계정 사본 증거이며 native/실기 증거가 아니다.

[화면 Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-approved-art-431-r2-20261027): 첫진입/전체/곡창3단계/곡창완공/밭3단계/밭완공 각각 KeyArtC 나란히, 첫진입3배 및 강둑ROI3배. 직접10화면을 검토했다. 곡창 골조 단계는 구분되지만, 현재 공사 중 표지가 밭 그림 상당부분을 가려 단계별 차이를 읽기 어렵다. 이 시각 한계는 통과로 바꾸지 않으며 이번 기록 작업에서 UI를 고치지 않았다. 이동 주민은 승인 SE/SW 외 uphill 모습 부재가 유지된다.

### 같은 강둑 ROI 전후

| ROI | 이전 bank Y′ | 새 bank Y′ | 인접 ground Y′ | 이전/새 ground대비 |
|---|---:|---:|---:|---:|
| 1 | 32.46 | 66.33 | 66.44 | 48.8% / 99.8% |
| 2 | 28.47 | 64.52 | 67.33 | 42.3% / 95.8% |
| 3 | 28.47 | 64.57 | 66.78 | 42.6% / 96.7% |

[같은5×3px 위치 정본 CSV](estate-approved-art-426-bank-rois.csv). sRGB code값 가중Y′이지 광도계 측정 아님. 이전42–49% near-black 우려는 역사로 보존. 새96–100%는 표시 밝기 후보 결과, 사용자 승인 대체 아님. ed9c854→108236b PNG diff없음: 승인 원본/atlas 그림 byte불변, shader 표시만 조절.

묶음: 최신 estate-decor-batches.json 구조 materialBreaks on12/off10. ProfilerRecorder batches/drawCalls는 둘다0, SetPass13/12; 0을 유효 그리기 비용으로 해석하지 않는다. 동일조건 이전 runtime레코드를 보존해 비교할 자료가 없어 전후 실측delta 미확인. 동일재질·텍스처 수 유지 구조검사는 실제delta를 대신하지 않는다.

Native는 앞서 ed9c854 빌드 한 번의 Title.Enter 조회 실패로 첫 캡처 전 중단, 이번에 재시도하지 않았다. 원인 미확정·native0/1/30/건설 검증 미완료. 최종CI는 PR 체크로 별도 확인, 초안 유지.
