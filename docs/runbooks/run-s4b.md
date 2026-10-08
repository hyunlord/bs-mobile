# S4b 보정·평가 실행

사전 선언은 [S4b-protocol](../league/S4b-protocol.md), 설계 결정은 [ADR0014](../adr/0014-controlled-league-experiment.md)다. 기존 S4 명령·원자료·FAIL은 유지한다. 아래 명령은 저장소 루트에서 .NET 8과 Node로 실행한다.

## 구현 확인과 측정 구분

`./tools/check.sh`는 기존 S2/S4 회귀와 S4b 짧은 smoke를 검사한다. smoke는 900틱이므로 720초 생존·레벨 관문의 증거가 아니다. 정식 실행 전에 모든 소스·프로필·후보 JSON을 커밋하여 dirty source가 아니어야 한다.

```sh
export PATH="$HOME/.dotnet:$PATH"
dotnet build --configuration Release
node tools/s4b-league.mjs smoke-A --profile s4b-01 --output artifacts/league-s4b-smoke-A --workers 2
node tools/s4b-report.mjs artifacts/league-s4b-smoke-A artifacts/report-s4b-smoke-A
node tools/s4b-league.mjs smoke-B --profile s4b-01 --output artifacts/league-s4b-smoke-B --workers 2
node tools/s4b-report.mjs artifacts/league-s4b-smoke-B artifacts/report-s4b-smoke-B
```

## 보정과 동결

후보마다 전체 위협·XP 수치 묶음을 새 `data/tuning-s4b-NN.json`과 연결 프로필로 보관한다. 최대 6개이며 이전 후보를 덮어쓰지 않는다. 변경 이유와 모든 후보 결과는 `docs/league/tuning-log.md`에 남긴다. 보정 seed1000–1031만 사용하며 평가 A/B 결과를 먼저 보지 않는다.

```sh
node tools/s4b-league.mjs calibration --profile s4b-01 --output artifacts/s4b-calibration-01 --workers 4
node tools/s4b-report.mjs artifacts/s4b-calibration-01 artifacts/s4b-calibration-01-report
```

생존율 30–70%와 720초 생존자 최종 레벨 중앙값 25–45를 함께 충족한 첫 후보를 고정한다. 후보 6까지 충족하지 못하면 최종 후보와 실패를 보존한다. 평가 전에 고정 프로필·tuning SHA-256·소스 SHA·중단 이유를 기록한다.

## 평가 A와 참고 B

아래 PROFILE은 동결 기록의 실제 프로필명으로 바꾼다. A는 576개 서로 다른 입력 조합, B는 288개다. 두 실험은 같은 32 seed를 공유하므로 정책·사람·이동 간 결과는 대응 비교다. 각 3반복은 결정론 검사이며 독립 표본 수를 늘리지 않는다.

```sh
node tools/s4b-league.mjs A --profile PROFILE --output artifacts/s4b-A --workers 4
node tools/s4b-report.mjs artifacts/s4b-A artifacts/s4b-A-report
node tools/s4b-league.mjs B --profile PROFILE --output artifacts/s4b-B --workers 4
node tools/s4b-report.mjs artifacts/s4b-B artifacts/s4b-B-report
```

실험 유효성, a–d, XP, mixed/random 원장 구별을 각각 확인한다. 밸런스 FAIL을 CI 결함이나 데이터 손상과 혼동하지 않는다. 유효성 실패는 원인을 고친 뒤 새 실행 경로로 다시 측정하며 실패 원자료도 보존한다. 원자료를 삭제한 별도 CSV 복사본에서도 보고서를 재생성하고 모든 보고 산출물 해시를 대조한다.

## 보관과 R3

대량 JSON·사례별 CSV·trace는 git에 넣지 않는다. 원자료, 보고서, 실행 로그, 고정 입력을 Release 자산으로 압축하고 자산·파일별 SHA-256/바이트 매니페스트를 만든다. 새 다운로드를 깨끗하게 추출하여 정확한 파일 집합과 전수 해시를 검사한다. 저장소에는 요약 CSV·보고서·작은 매니페스트·재현 링크를 남긴다.

R3은 동일한 동결 수치·프로토콜·seed로 A/B를 다시 실행하되 출력·Release 이름을 분리한다. 새로운 기본 영지 고리와 ID를 제외한 재보정은 하지 않는다. R2 결과를 덮어쓰지 않는다.
