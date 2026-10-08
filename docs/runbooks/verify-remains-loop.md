관문: 통과 — .NET 138/138·콘텐츠 123/123·잔재 보고 5/5, 기본·더미 각 21,600틱×3회 검증. 밸런스는 별도 R3 보고에서 FAIL(c)로 보존한다.
변경: 선택 A안의 잔재 생성부터 실제 작물 성장·수확까지 검증하는 절차를 고정한다.
결정: 기본 영웅·영지는 각 1개, 초기 설정은 1,000개·900틱·600 world-unit이며 R2 관문에 맞춰 재보정하지 않는다.
한계: 영웅 운반·고유 능력·Unity·실기·재미 검증은 포함하지 않는다.

# 잔재 고리 검증 런북

[ADR 0015](../adr/0015-local-remains-loop.md)와 [이슈 #62](https://github.com/hyunlord/bs-mobile/issues/62)를 먼저 읽는다. 저장소 루트 `~/bs-mobile`에서 실행한다. .NET 8·Node/npm과 기존 잠금 의존성을 사용하며 새 패키지를 설치하지 않는다. 아래 명령이 끝났다는 사실만으로 통과를 선언하지 않고 종료 코드·실제 테스트 이름/수·결과 파일·소스 SHA를 함께 기록한다.

## 1. 구조와 집중 테스트

```sh
npm run validate
dotnet test core/tests/SowSiege.Tests -c Release --filter FullyQualifiedName~RemainsTests
```

집중 테스트는 다음을 실제 상태로 확인해야 한다. 누락한 항목은 미검증으로 남기고 원장 숫자만으로 대신하지 않는다.

- 농지 없는 처치가 잔재로 남고, 나중에 반경 안에 심은 농지가 한 번만 흡수한다. 같은 초기 상태의 비활성 대조보다 실제 성장 단계가 앞서고 마지막에는 식량·수확 XP가 발생한다.
- 반경 경계 포함·범위 밖·최근접/동거리 선택·만료 시각 일치에서 만료 우선·상한의 최신 잔재 거부를 확인한다.
- 잔재 독립 순번이 `World.NextId`나 RNG draw 수를 바꾸지 않는다. 생성 틱의 즉시 흡수는 없다.
- 성숙 농지에 전달한 비옥도가 수확 후 다음 주기에 소비된다. 성장 정지 중 소비 0, 실제 추가 성장량 일치, 파괴한 작물 주기의 수확 표식 제거를 확인한다.
- 같은 seed 세 번 해시, 결과 조회의 비변경성, 0틱·표본 틱·실제 종료 표본을 확인한다.

## 2. 기본 선택으로 실제 결과 생성

```sh
mkdir -p artifacts/r3-remains
dotnet run --project core/src/SowSiege.Sim -c Release -- --data data --profile s4-stage-one --seed 42 --policy mixed --people-rule C --iterations 3 --output artifacts/r3-remains/results.json --metrics artifacts/r3-remains/metrics.json
```

기본 선택은 `core:frontier_knight`·`core:sprout_march`여야 하며 Core에서 그 ID로 분기하지 않아야 한다. 결과의 선택적 `remains` 원장과 metrics의 실제 설정 provenance를 읽는다. 도구 직접 피해와 성장 산출·잔재 수·식량을 서로 더해 피해 점수로 만들지 않는다.

필수 보존식은 `Created = Absorbed + Expired + Active`다. 생성 대상 사망은 `Created + Dropped`로 별도 대조한다. 실제 농지 증가량과 `FertilityTransferred`, 실제 소비와 `FertilityConsumed`, 실제 증가한 성장량과 `GrowthBonusApplied`를 집중 fixture에서 대조한다. 농지에 남거나 파괴로 소실된 비옥도를 무시해 전달량=소비량이라고 요구하지 않는다. `FertilizedHarvests`는 비옥도를 사용한 작물 주기의 수확 수이지 전체 수확량의 인과 귀속이 아니다.

## 3. 더미 데이터와 역사 보존

데이터 담당자가 확정한 더미는 `test:scout`·`test:moor`이며, 영지 설정은 보관 2개·수명 60틱·반경 130이다. 기본과 다른 설정으로 같은 공통 고리를 검증한다. 아래 실행은 측정 소스 `d9653353f0c17fcea756910d9b1db28d400680b6`에서 완료했다.

```sh
dotnet run --project core/src/SowSiege.Sim -c Release -- --data data --profile s2-baseline --include-test --hero test:scout --estate test:moor --seed 42 --policy mixed --people-rule C --iterations 3 --output artifacts/r3-remains/dummy-results.json --metrics artifacts/r3-remains/dummy-metrics.json
```

요구 증거는 **다른 테스트 ID/설정으로 같은 공통 잔재 고리 실행**, Core 수정 없음, 기본 정본 수량 1/1 유지다.

역사 데이터 루트는 `core/tests/SowSiege.Tests/Fixtures/phase0-r2/data`, 테스트 출력 복사본은 `Fixtures/phase0-r2/data`다. 원본 `bcaa00f763ec6950707635569befb558e8de1731`의 데이터 243파일·376,167bytes와 인접 `source-manifest.csv`를 대조한다. 활성 데이터로 과거 테스트를 돌린 뒤 golden 값을 새로 받는 방식은 허용하지 않는다.

```sh
dotnet test core/tests/SowSiege.Tests -c Release
./tools/check.sh
```

전체 테스트에서 기존 S2 golden 여섯 조건과 기존 S4 golden이 그대로인지 확인한다. 선택적 설정·상태·결과가 없는 역사 직렬화에는 새 null 필드가 나타나면 안 된다. 검증 담당자는 fixture 해시 비교와 golden 결과를 실제 실행 로그로 연결한다.

## 4. R2 조건 재실행과 보관

[S4b 사전 선언](../league/S4b-protocol.md)·[동결](../league/S4b-freeze.md)의 동일한 A/B 프로토콜·seed·위협·XP 수치를 재사용한다. R3용 결과 디렉터리와 원자료 식별자를 별도로 쓰고 R2 파일을 덮어쓰지 않는다. 실제 실행은 `node tools/s4b-league.mjs A --profile s4b-02 --output <빈 외부 A 경로> --workers 4` 및 같은 명령의 `B` 변형이다. A 576·B 288사례를 각각 세 번 실행했다. 결과가 나쁘다는 이유로 관문·정책·전역 수치를 바꾸지 않는다.

동일한 반복 실행을 독립 표본으로 세지 않는다. 잔재 보존식·집중 인과 증거·전체 리그 밸런스 판정을 분리해 보고한다. 원자료는 외부 증거 보관 정책을 따르고, 저장소에는 요약·매니페스트·재현 안내만 남긴다. ZIP/Release 다운로드·깨끗한 해제·전체 해시 검증 전에는 전달 무결성 통과라고 쓰지 않는다.

## 완료된 실행 증거

[R3 보고](../review/R3-report.md)와 [증거 안내](../evidence/R3/README.md)에 소스·결과·보관 경로를 연결했다. 외부 `technical/bs-r3-fullcheck.log`의 전체 검사와 `technical/full-default-dummy-verification.json`의 수동 CLI 결과를 함께 보존한다. 기본은 Created 2,292 = 흡수 698 + 만료 1,447 + 활성 147, 비옥도 소비/성장 605, 비옥도 사용 주기 수확 185다. 더미는 586 = 27 + 557 + 2, 상한 폐기 1,369, 소비/성장 17, 비옥도 사용 주기 수확 8이다. 각 세 반복 해시가 일치한다. 동일 초기 상태의 비활성 대조에서는 성장량 1, 활성에서는 3이며 실제 단계·첫 수확 시각도 비교했다.

이 수동 실행은 이전 기술 프로필을 쓴다. `s4b-02`의 비선형 XP·위협 설정을 사용한 전체 밸런스 판정과 혼합하지 않는다. 전체 수확량을 비옥도의 추가 인과 효과로 귀속하지 않는다.
