# 사람 전투 기여 진단 실행

계약은 [ADR0019](../adr/0019-people-ablation-diagnostics.md), 구현은 #72, 전체 결과는 #68, 보정안 결정은 #61에서 추적한다. 코드·사전 선언 CI가 통과해 병합된 깨끗한 커밋에서 실행한다. Unity는 실행하지 않는다.

## 검사와 작은 실행

```sh
export PATH="$HOME/.dotnet:$PATH"
dotnet build --configuration Release
node --test tools/test-diagnostic-*.mjs
node tools/diagnostic-runner.mjs smoke artifacts/diagnostic-smoke 4
```

출력 디렉터리는 새 경로여야 한다. smoke는 seed9000·900틱의 27조건×3반복이며 밸런스 또는 역사 전체 길이 재현 결과가 아니다. 실패 시 `.incomplete-*` 경로를 유지하고 완성 디렉터리로 승격하지 않는다.

## 전체 실행

R2 `phase0-r2-s4b-20261008`, R3 `phase0-r3-sprout-march-20261008`의 기존 원자료에서 각 evaluation-A의 runs.csv와 determinism.csv가 있는 디렉터리를 준비한다. 새 패키지는 만들지 않는다. 경로는 환경변수로 지정하며 저장소에 사용자 경로를 고정하지 않는다.

```sh
export BS_DIAGNOSTIC_R2=/path/to/r2/evaluation-A
export BS_DIAGNOSTIC_R3=/path/to/r3/evaluation-A
node tools/diagnostic-runner.mjs full artifacts/diagnostic-full 4
```

원본 네 CSV의 SHA-256과 3반복·288 control 기준을 검증한 뒤 864조건×3반복을 실행한다. 실행 중에는 같은 checkout의 소스·입력·Release DLL·커밋을 변경하거나 재빌드하지 않는다. 종료 시 frozen provenance를 다시 검사한다. 최대4 프로세스·각600초 제한이며 오류나 누락을 성공으로 보고하지 않는다.

## 분석과 선택 원자료

```sh
node tools/diagnostic-report.mjs artifacts/diagnostic-full artifacts/diagnostic-replay full
node tools/diagnostic-select.mjs artifacts/diagnostic-full artifacts/diagnostic-selected
```

첫 명령은 로컬 compact packet에서 CSV를 재생성한다. 두 번째는 사전 기준의 최대16사례만 같은 동결 소스에서 다시 실행해 gameplay/diagnostic hash와 provenance 일치를 확인하고 상세 원자료를 만든다. 새 실험의 독립 표본으로 세지 않는다. 최초 실패 자료를 재실행 성공으로 덮지 않는다.

- runs: 사례 결과·해시·RNG 및 표본 계측 분기.
- paired/interactions/paired-summary/policy-effect-differences: 같은 seed 총효과와 2×2 상호작용. 반복을 표본 수로 합산하지 않는다.
- snapshots: 정규300틱 격자에서 생존자 표본 합계와 실제 분모. 사망 후 값을 채우지 않는다.
- terminal-counters: 격자 밖 사망을 포함한 모든 실제 마지막 계측과 종료 틱.
- attack-sources/population/equipment/card-choices: 실제 피해 출처·인구·장비·카드 chronology. 억제량은 실제 피해 합계에 넣지 않는다.
- provenance/public-selection: 실행 출처와 사전에 선언한 선택 기준.

정본 CSV·재현 코드는 저장소 예산 안에서 남긴다. 전체 성공 packet을 업로드하지 않으며 Release에는 선택한 실패·표본 원자료만 넣는다. CSV에서 재생성 가능한 보고·그래프·집계는 업로드하지 않는다. 보고는 커밋·PR·CI 및 필요한 Release 태그만 사용한다. 결과 해석은 보정 제안이며 자동 수치 적용을 허용하지 않는다.
