# First-playable FP01 검증 기록 (#443)

2026-10-27. 상태: 코드·Editor 관문 통과, Native 성능 기준선 미측정(사용자 보류).

## 범위와 provenance

- 제품 기준 main: `a44bf7ae6a5811de057c8f12410ce4a5d8fb0d3f`.
- 검사 소스: `5f48da5f34bf418615c93abbe52dc911e615d7f4` (main + FP01 benchmark 전용 패치).
- Core 규칙, 기본 data, 기존 골든, C05 계약·리플레이는 변경하지 않았다. 과거 main 자체가 FP01을 지원했다는 의미가 아니다.
- 사용자가 3회 기준선은 뒤로 미루고 코드 PR부터 진행하도록 지시했다. Release 빌드·Native FP01 실행·3회 성능 측정은 하지 않았다. #432/#439의 성능 재판정은 아직 불가능하다.

## 실제 Editor 실행

공유 Mac 잠금 아래 부모 세션이 18:32:52~18:43:26 KST에 실행했다. 아래는 XML 4개 및 supplemental XML 2개를 읽어 확인한 기록이다. 기록 이후 문서만 추가하므로 Unity 소스는 검사 커밋 그대로다.

## Unity tests
commit: 5f48da5f34bf418615c93abbe52dc911e615d7f4
first-playable EditMode: passed 865, failed 0, skipped 25
first-playable PlayMode: passed 55, failed 0, skipped 45
wave-1a EditMode: passed 875, failed 0, skipped 15
wave-1a PlayMode: passed 87, failed 0, skipped 13
skipped in every profile: 0 unexplained, 1 listed

원자료 위치: `artifacts/first-playable-fp01/verification/two-profiles/`. 원래 `record.md`는 공통 미실행 2건 때문에 실패(exit 1)였으며 그대로 보존한다. 아래 두 입력을 기존 도구로 생성한 뒤 각 테스트를 실제 실행했다. 예외 추가나 원래 suite 숫자 변경은 하지 않았다.

| 보충 실행 | 통과 | 실패/skip | 입력 |
|---|---:|---|---|
| M1CombatReviewTests.ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes | 1 | 0/0 | showcase-fixtures 생성 sidecar |
| Tests.EditMode.MeleeAimReplayParityTests.MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures | 1 | 0/0 | melee-aim-fixture + opt-in 카탈로그 export |

6개 XML 전체를 기존 `tools/unity-skipped-everywhere.mjs`로 합산한 결과는 미설명 0 / 기존 목록 1이다. 남은 목록은 `Tests.PlayMode.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`이며 사유는 optional recorded-input sequence export다. `combined-skip-record.txt`, `combined-skips.txt`, 원래 실패 console.log를 함께 보존한다. 완료 뒤 추적 파일 변경 없음과 자기 잠금 해제를 확인했다.

## 가벼운 검증과 검토

- `node --test tools/test-first-playable-benchmark.mjs tools/test-wave-benchmark.mjs tools/test-wave-benchmark-v2.mjs tools/test-wave-regression.mjs`: 77/77, 실패 0, skip 0.
- `node tools/verify-unity-boundaries.mjs`: PASS, assemblies=9.
- `git diff --check`: 통과.
- C05 3개 정본 파일 바이트/SHA 보존 검사 통과.
- FP01 요약은 19891~20790 전체 900틱과 연속 프레임을 요구한다. 시작 누락·중간 틱 누락·끝 절단·3행 희소 자료를 거부한다. 합성 타이밍 테스트는 성능 증거가 아니다.
- 부모 세션이 전달한 별도 읽기 검토: 추가 차단 지적 없음. 작성자의 lifecycle/prefix/warmup/tail 재검토와 구분하며, Editor 테스트나 정적 검토로 Native 실행을 대신하지 않는다.

## 남은 관문

Native 프로필 선택·실제 900틱 계측·GPU timing·전경/외부 부하 유효성은 미확인이다. 향후 승인된 시점에 main + 같은 하네스 패치로 Release를 빌드하고 유효 3회 기준선을 기록한다. 준비된 artifact runner는 실행하지 않는다. 성능 수치·개선·퇴행 판정은 현재 없음. CI 최종 결과는 PR 게시 후 별도로 기록한다.
