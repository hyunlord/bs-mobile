# 단일 무기 후보 holdout 사전 선언

관문: 미검증 — 이 문서는 실행 전 규칙이다. 후보가 실패하면 결과를 보존하고 다음 후보 한 개만 제안한다.

## 입력과 동결

- [ADR0022](../adr/0022-weapon-level-tables-and-facing-sectors.md)의12레벨 단일 후보. 기본 R3 프로필의 무기 계약만 확장하며 사람 규칙·위협·XP·정책·이동 설정은 바꾸지 않는다.
- seed **20000–20031**. 기록된238개 seed 포함 CSV와 기존 실행 범위를 확인했으며 이 범위의 사용 기록은 없었다. 외부의 미기록 실행까지 증명하지는 않는다.
- `weapon, land, building, people, mixed, random` × `A,B,C` ×32seed = **576조건**. arm은control만, 정상21,600틱/자연사망, 기존 circuit 이동. 실제3반복=1,728실행이며 표본 수를 늘리지 않는다.
- 개발 smoke는9100, 짧은900틱이며 holdout을 미리 실행하지 않는다. smoke 결과를 전체 생존 관문으로 쓰지 않는다.
- 구현/사전선언 PR의 CI·병합 뒤 clean source commit, 실제 Core/Sim DLL, 데이터·프로필·입력 해시를 고정한다. 측정 중 source/HEAD/build를 바꾸지 않는다. 측정 후 수치 재조정이나 새 후보 재실행은 없다.

## 관문

유효한 정확한 행렬·3반복 결정론·동일 출처가 먼저다. 누락, 중복, 출처 불일치, 불완전 실행은 유효한 밸런스 판정이 아니다.

| 관문 | 고정 판정 |
|---|---|
| 무기 하한 | A/B/C 각각 생존 수W와 같은 묶음 random R에 대해 `R>0 && 5W>=3R` |
| 기존(b) | 96개 규칙×seed 각각 `[생존,종료tick,레벨,실제총전투피해]` 내림차순 공동1위를 구하고 모든 조건의 공동1위 정책 교집합이 비어야 통과 |
| c′ | 무기·땅·건물·사람 × A/B/C의12셀 모두 `R>0 && 5P>=3R && 2P<=3R`, 즉0.6~1.5 inclusive |

실제총전투피해는 weaponDamage+toolActivationDamage+toolGrowthDamage+allyDamage이고 식량·자재는 점수에 넣지 않는다. random생존0이면 그 비율은 판정불가이며 통과로 취급하지 않는다. 기존(b)의 실패를 평균 순위나 묶음 최다 생존으로 대체하지 않는다. 무기 하한을 통과해도 c′나(b)가 실패하면 전체 관문은 실패다.

## 실행과 재생성

명령과 정본 CSV 목록은 구현과 함께 아래에 고정한다. 전체 compact packet은 무시된 로컬 `artifacts/`에만 두며 중복된 대형 성공 결과를 Git/Release에 넣지 않는다. 정본에는 사례별 결과·3반복 증거·실제 무기 발동/피해·성장·출처와 관문을 다시 계산할 최소 데이터를 보존한다. 파일 생성은 예산 검증 뒤 원자적으로 끝낸다.

실행 후 같은 입력으로 CSV/표를 재생성하여 바이트 일치를 검사한다. 원자료 공개가 필요하면 고정 표본 `random/C/20000`, `weapon/C/20000`와 weapon사망 중 caseId 사전순 앞2개만 선정한다(중복 제거, 최대4사례). 전체 실패 목록은 작은 CSV에서 식별할 수 있게 하며 표본을 전체 실패로 부르지 않는다. 원자료를 공개할 경우 Release 태그만 사용하고 CSV에서 재생성 가능한 문서·그래프·집계는 자산으로 올리지 않는다. 새 ZIP·CRC·전달 영수증은 없다.

## 보고와 실패 처리

첫10줄 이내에 관문,576조건/반복, 무기하한·(b)·c′ 결과, 후보 변경, commit/PR/CI를 요약한다. 표는 정책별 생존, 같은 묶음 random비율, 무기별 요청/실제피해·과잉낭비·허공발동 정도로 제한한다. 누적 비율에는 분모와 노출 시간 차이를 명시한다. 역사 진단과 같은 seed의 통제 비교가 아니므로 개선의 인과효과를 주장하지 않는다.

실패면 코드·표·seed·출처를 보존한다. 관측된 병목에 맞춘 **다음 후보 하나**를 needs-decision 이슈로 제안하고 적용하지 않는다. 사람형 추가 분해와 Unity1단계는 시작하지 않는다.

## 고정 명령과 정본

```sh
export PATH="$HOME/.dotnet:$PATH"
node --test tools/test-weapon-holdout.mjs
node tools/weapon-holdout.mjs smoke artifacts/weapon-holdout-smoke 4
# 구현 PR 병합과 source 동결 이후에만:
node tools/weapon-holdout.mjs full artifacts/weapon-holdout-full 4
node tools/weapon-holdout-report.mjs artifacts/weapon-holdout-full artifacts/weapon-holdout-replay full
```

출력은 새 경로만 받는다. 최대4프로세스·각600초, 실패는 `.incomplete-*`와 누락 목록에 보존한다. 결과가 유효하고 밸런스 관문만 실패하면 프로세스는 정상 종료하면서 FAIL 표를 남긴다. 유효성 오류는 비정상 종료한다.

정본은 `runs.csv`, `weapon-sources.csv`, `terminal.csv`, `weapon-equipment.csv`, `provenance.csv`다. `outcomes.csv`, `ratios.csv`, `condition-ranks.csv`, `gates.csv`, `report.md`는 그 결과를 검토하기 위한 작은 표다. 전체 packet은 로컬 분석용이다. 새 무기 계측의 `emptyActivations`는 후보가 있지만 부채꼴 밖인 발동까지 포함하고 `eligibleBeforeCap`은 타격 상한 전 적 수다. 역사 `noTargetAttempts`와 `shapeEligibleCount`의 뜻을 바꾸지 않는다.

`eligibleBeforeCap`의 광선 값은 이미 선택된 발사 수만큼의 광선 통로에 들어온 고유 적 합집합에서 관통 상한을 적용하기 전 개수다. 발사 수 선택 이전의 모든 가능한 적 수를 뜻하지 않는다. 부채꼴·원판에서는 해당 기하 영역의 타격 수 상한 전 적 수다.

자동 생성 `report.md`는 CSV 관문 재생성용 하위 표다. 전달용 최종 검토 보고서는 별도 `docs/review/` 문서이며, 그 첫10줄에 commit/PR/CI를 연결하고 무기별 지표 표를 포함한다. 자동 표의 생성을 최종 보고 완료로 취급하지 않는다.
