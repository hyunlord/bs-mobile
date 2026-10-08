# weapon-growth-79 정본

관문: 통과 — 구현 PR #80의 동결 후보를 새32seed에서 평가했다. 요약과 지표는 [검토 보고서](../../review/weapon-holdout-79.md), 사전 규칙은 [런북](../../runbooks/run-weapon-holdout.md), 구현 계약은 [ADR0022](../../adr/0022-weapon-level-tables-and-facing-sectors.md)를 따른다.

- 실행 소스: `65fd21ac97f23840ab081a86d78f121d0af57693`, clean HEAD. 정확한 소스·DLL·콘텐츠·성장표 해시와 런타임은 `provenance.csv`.
- seed20000–20031 × 정책6 × 사람규칙ABC =576조건, 실제3반복은 검증용이며 독립 표본으로 세지 않는다.
- 정본: `runs.csv`, `weapon-sources.csv`, `terminal.csv`, `weapon-equipment.csv`, `provenance.csv`.
- 파생 검토 표 `outcomes.csv`, `ratios.csv`, `condition-ranks.csv`, `gates.csv`, `report.md`는 아래 명령으로 로컬에 생성하며 Git·Release에 보관하지 않는다. 최종 판단·한계는 별도 검토 보고서에만 남긴다.
- 전체 compact packet은 로컬 `artifacts/weapon-holdout-full-20261008/packets`에만 보존했다. 새 ZIP·CRC·영수증·중복 Release 자산은 만들지 않았다. 필요시 공개 표본은 사전 선언된 최대4사례만 사용한다.

## 재생성

저장소 루트에서 새 출력 경로를 지정한다. 이 명령은 시뮬레이션이나 새 조건을 실행하지 않는다.

```sh
node tools/weapon-holdout-report.mjs docs/league/weapon-holdout-79 artifacts/weapon-holdout-replay full
```

`outcomes.csv`, `ratios.csv`, `condition-ranks.csv`, `gates.csv`, `report.md` 다섯 파일은 정본 실행 결과와 바이트 일치했다. 반복 증거·inputHash·diagnosticIdentity·sourceIdentity를 출처에 연결해 검사한 뒤 관문을 계산한다. 이전 R2/R3와 진단 CSV는 수정하지 않았다.

무기 지표 표는 `weapon-sources.csv`의 `caseId`가 `weapon__`로 시작하는 행만 골라 `sourceId`별 합계로 계산한다. 사례 수는 행 수, 과잉은 `sum(requestedDamage)-sum(appliedHpDamage)`, 허공은 `sum(emptyActivations)/sum(attackAttempts)`다. 각 행의 비율을 단순 평균하지 않는다. 무기별 획득·생존 노출 시간은 같지 않다.

## 중단 및 환경 복구

최초 세션 중단 디렉터리에는 완료 packet이0개였다. 다음 시도는334조건 검증 후 시스템 Git이 Xcode 라이선스 오류를 반환했다. commit=`unavailable`을 출처 검사가 거부해 완결 결과로 게시하지 않았다. 불완전 디렉터리·실패 목록·로그는 로컬에 보존하고 최종 행렬에 합치지 않았다. 라이선스 동의나 시스템 설정 변경 없이, 기존 Command Line Tools를 프로세스에만 지정해 복구했다.

```sh
DEVELOPER_DIR=/Library/Developer/CommandLineTools PATH="$HOME/.dotnet:$PATH" \
  node tools/weapon-holdout.mjs full artifacts/weapon-holdout-full-20261008 4
```

이후 동일 커밋·후보·seed를 처음부터 실행하여576조건 전부를 완료했다. 후보 재조정이나 추가 조건은 없으며, 완결1,728실행 외 중단 시도는 독립 표본에 더하지 않는다. [#81 실행 기록](https://github.com/hyunlord/bs-mobile/issues/81#issuecomment-6057036149)에 복구 경위를 남겼다.
