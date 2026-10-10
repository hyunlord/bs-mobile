# #169 고밀도 비용 최적화 독립 코드 검토

관문: 통과 — 검토한 코드에서 병합을 막는 결함을 발견하지 않았다. 이는 네이티브 성능 관문 통과 판정이 아니다.

검토일: 2026-10-10. 검토자: 독립 Codex `wave_query_audit`. 최종 소스 기준은 `1d674a3`이며, Core 변경 `0ae41d9818c54ec81505c0029fd30b0130340e21`, 하네스 `0e54032`와 수정 `7367389`·`47c1a7a`, 역사 재생 검사 `e15d7d6`, 빌드 복원 `8549fd8`을 읽었다. 작성자와 분리된 검토이며, 검토자는 제품 코드를 변경하지 않았다.

## 검토 파일과 확인 내용

| 파일 | 확인한 계약 |
| --- | --- |
| `core/src/SowSiege.Core/SpatialHash.cs`, `WaveEnemyQueries.cs` | 기존 격자는 후보 수집에만 사용한다. 거리→Id, Id 단독, 원래 적 목록 순서를 별도로 복구한다. Id 조회는 사전이며 죽은 대상은 제외한다. |
| `core/src/SowSiege.Core/WaveRuntimeSystem.cs` | 적 이동 뒤 재구축, 넉백 즉시 재색인, 중첩 검색 버퍼 분리, 연쇄의 젖은 대상 우선 및 넉백 후 위치, 궤도 목록 순서, 투사체 제거 순서가 보존된다. |
| `core/src/SowSiege.Core/WaveEnemySystem.cs` | 적·스폰 Id 순서, 전역 최근접 작물의 거리→Id, 연결 경로의 기존 목록 동률 순서가 유지된다. 투사체 차단은 넓은 후보 범위 뒤 기존 정확 판정을 적용한다. |
| `core/src/SowSiege.Core/WaveWorkSystem.cs` | 건물 공격의 Id 순서, 아군 검색 반경의 중심과 거리 순위 기준점의 차이, 물 상태 적용 순서, 보상 수집 순서가 유지된다. 독립 생성 검사 경로는 자체 인덱스를 갱신한다. |
| `core/src/SowSiege.Core/WaveItemSubscriptions.cs` | 기존 `State.Items`는 ordinal `SortedSet`이다. 새 구독 목록 정렬과 소유 검사도 같은 순서를 유지한다. 첫 일치 구독과 자원 이동 계약을 바꾸지 않는다. |
| `core/src/SowSiege.Core/WavePrimitiveProgram.cs` | 파생 연산 캐시는 비공개 모듈에 둔다. 공개 Params·세계·카탈로그 해시 입력은 바꾸지 않는다. legacy 정의에 새 Id가 추가되어 재컴파일되면 캐시도 다시 만든다. |
| `core/tests/SowSiege.Tests/WaveEnemyQueriesTests.cs`, `WaveRuntimeTests.cs` | 원래 목록과 Id 순서의 차이, 거리 순위 기준점, 즉시 이동·사망·삭제, 젖은 대상 우선, 셀을 넘는 넉백, legacy 추가 정의 및 레벨 행 교체 회귀를 확인했다. |

최종 추가된 레벨 캐시는 `(Id, levelIndex)`만 믿지 않고 원본 gear와 선택한 레벨 행의 참조를 함께 확인한다. 따라서 테스트가 gear 또는 `Levels[index]`를 새 불변 레코드로 바꾸면 다시 계산한다. 이 캐시도 시스템 내부에만 존재한다. 수리 대상의 정렬 제거는 같은 거리→Id 최소 선택으로 대체되었다. 이 최종 변경을 초기 검토 이후 다시 읽었다.

## 하네스·자료·빌드 검토와 해결된 지적

- `RunCoordinator.WaveBenchmark.cs`, `RunCoordinator.cs`: 명시적 옵션에서만 원본 입력을 적용하며 frozen dataHash와 재생 SHA를 검사한다. 명령 전체와 원본 종결 tick·종결 종류·해시를 검증한다. 비측정 준비와 렌더 준비 구간을 실제 성능 표본과 구별한다.
- 초기 지적: 포커스/일시정지 전환이 샘플 사이 또는 측정 구간 사이에 발생하면 모두 `focused=1`인 CSV로 남을 수 있었다. `7367389`는 준비 종료 뒤 콜백에서 영구 `interrupted` 표시를 남기고 분석기가 이를 거부하도록 수정했다. 실패 행을 삭제하지 않는다. 코드상 지적은 해결되었다.
- 초기 시간 범위 지적: 이전 행 기록과 지연 타이밍 조회가 프레임 시계 밖에 있었다. `7367389`는 Update 진입에서 시각·할당·GC 카운터를 읽어 연속 프레임 간격을 기록한다.
- `summarize-wave-benchmark.mjs`: 전후 각각 3회, 같은 소스·기기·설정, 실제 Release, 원본 종결 해시를 검사한다. 밀도별 각 반복의 p95를 판정하며 느린 프레임·명령 없는 프레임을 제외하지 않는다. 지연된 렌더 타이밍을 현재 밀도의 GPU 시간으로 잘못 붙이지 않는다.
- `prepare-wave-benchmark.mjs`, `UnityExportCli.cs`, `unity-export.mjs`, `unity-build-identity.mjs`, `FoundationBuild.cs`: 역사 자료를 지정 커밋에서 재생성하고 별도 카탈로그로 내보낸다. 일반 실행 정본을 덮어쓰지 않는다. 새 출력 경로 및 소스 무결성 검사를 유지한다. `47c1a7a`는 프레임 타이밍 지원 설정을 소스에 고정한다.
- `verify-wave-history-parity.mjs`와 `benchmarks/wave-history/contract.json`: 원본 입력 SHA와 역사 종결 해시를 고정하며 실제로 다른 framework·MVID·SHA·물리 위치의 두 Core DLL을 검사한다. 실행 후 입력과 DLL 변경도 거부한다. CI 품질 작업은 전체 Git 역사를 가져온다.
- `build-mac.sh`, `restore-urp-authoring.mjs`: 빌드 전 원본 바이트를 보존하고 인식된 URP runtime-list 부분만 달라진 경우에만 복원한다. 다른 변경·알 수 없는 형식은 실패하며 두 버전을 보존한다. EXIT 복원은 원래 실패를 성공으로 바꾸지 않는다. 최종 strict preparation 검사는 그대로다.

## 전달받은 검증 결과와 검토 경계

다음은 부모 실행 에이전트가 보고한 결과이며, 이 검토자가 테스트를 재실행한 결과가 아니다. 네이티브 측정과 경쟁하는 테스트·빌드·대량 검색을 실행하지 않았다.

| 부모가 보고한 검증 | 결과 | 이 문서의 주장 범위 |
| --- | --- | --- |
| 집중 Wave 회귀 검사 | 121 통과 | Core 동작·순서·캐시 회귀 |
| 원본 역사 재생 | 6 통과 | 27000틱 두 표본 및 C05 14400틱 × 두 실제 Core 대상 |
| 분석기·URP 복원 도구 검사 | 22 통과 | 도구 입력·실패·복원 계약 |
| Unity 준비 검사 | 20 통과 | 준비·소스/출력 계약 |

최종 CI 링크와 실제 실행 원자료는 PR 및 #169 보고에 연결해야 한다. 이 문서는 CI 성공, 네이티브 무중단 3회 비교, p95 ≤16.7ms 또는 사용자 재미 승인을 대신하지 않는다. 특히 포커스/일시정지의 실제 네이티브 경계 검증과 최종 전후 비교는 별도 증거로 판단한다. 렌더 GPU 시간이 지원되지 않으면 미측정으로 남기며 화면 구성 CPU와 혼동하지 않는다. #126·Fold7 #98은 보류이고, 1b는 성능 결과와 별개로 사용자 직접 플레이 소감 이후 시작한다.

## 할당 카운터 해석 후속 검토

`tools/summarize-wave-benchmark.mjs`와 `tools/test-wave-benchmark.mjs`의 후속 diff를 독립적으로 읽었다. 전부 0인 할당 카운터는 무할당 증거로 해석하지 않고 `unavailable/all-zero-counter`와 `null`로 보고한다. 초기 후속 diff에서 한 반복의 미지원 0을 다른 반복의 양수와 합쳐 유효한 통계로 표시하는 문제가 있었으나, `summarizePooled`가 기여한 반복·밀도 묶음별 가용성을 확인하여 `unavailable/mixed-counter-support`와 `null`로 처리하도록 수정되어 해결되었다. 지원이 확인된 반복 안의 실제 0 표본은 제거하지 않으며 GC 수집 횟수·프레임 시간·p95 관문은 변경하지 않는다. 전체 0 및 혼합 가용성 회귀 검사 코드를 확인했고 추가 차단 지적은 없다. 검토자는 네이티브 측정 잠금 동안 해당 테스트를 실행하지 않았다. 위 22개 도구 검사 보고에 이 후속 검사의 실행 결과를 소급 포함하지 않는다.
