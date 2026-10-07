관문 결과: S4 밸런스 FAIL — 무기형이 96/96 조건에서 엄격한 레벨 우위. 실행·결정론·CSV 재현 검증 PASS: 576사례 × 3회, 25/25 효과 관측.

# S4 실행 증거와 인계

[이슈 #38](https://github.com/hyunlord/bs-mobile/issues/38) → [PR #39](https://github.com/hyunlord/bs-mobile/pull/39). 측정 소스는 깨끗한 `e4eb9d960e3f81522fd91c7a0ed5205694e3b9f8`, [해당 원격 CI](https://github.com/hyunlord/bs-mobile/actions/runs/37695563484)의 quality·secrets 통과. 이후 증거·보고서 정리는 실행한 Core·데이터를 바꾸지 않는다.

## 결과와 재현

[CSV에서 생성한 전체 보고서](../league/S4-report.md)에 생존, 도달 시간, 레벨, XP 출처, 무기·도구·아군 피해, 교차, 영지 체류, 빌드·물품·도구 성장·효과 분포와 실패 가설 세 개를 보존했다. 무기형을 포함한 모든 사례가 전체 21,600틱을 생존했다. 따라서 생존·시간은 순위 변별력이 없으며, 무기형은 모든 조건에서 나머지 정책보다 레벨이 높다. 음식·자재·체류를 순위 점수로 바꾸지 않았다.

- 입력: 여섯 정책 × 사람 규칙 A/B/C × seed 42–73 = 576사례. 사례별 세 번은 결정론 반복이며 독립 표본 수를 늘리지 않는다.
- [원본 리그](../evidence/S4/league/): 일곱 CSV, 실행·결정론 JSON 1,152개, 메타데이터와 측정 지표.
- [원자료 무결성](../evidence/S4/raw-integrity.json): JSON SHA와 CSV 연결 1,152개, 결정론 반복 1,728개 및 원본 설계 문서 세 개 확인.
- [CSV 전용 재생성](../evidence/S4/csv-reproduction.json): 복사한 CSV에서 보고서·SVG·PNG를 포함한 24개 파일 바이트 동일.
- [독립 순위 재계산](../evidence/S4/independent-ranking.json): 96조건의 무기형 엄격한 레벨 우위 확인.
- [플랫폼 비교](../evidence/S4/cross-platform.json): macOS Arm64와 Linux X64의 같은 54개 연기 시험 상태 해시 일치. 원시 CSV는 인접 `cross-platform/`에 있다. 시간 측정값과 바이너리 해시까지 같다는 주장은 아니다.

실행 명령과 CSV 재생성 명령은 [리그 런북](../runbooks/run-league.md)에 있다. 전체 리그를 다시 돌리지 않고 CSV로 분석을 재현할 수 있다. [ADR 0011](../adr/0011-executable-prototypes-and-auditable-league.md)은 실행 프로필·효과·측정 계약을 설명한다.

## 검사와 실제 사용

[최종 로컬 검사](../evidence/S4/final-check.log): 콘텐츠 검사 68개, PR 정책 6개, CSV·리그·보고서 검사 57개, .NET 검사 95개, Guard 자체 검사 24개. Release 빌드·형식·정적 정책 검사를 포함한다. S2 전체 길이 golden 여섯 개를 유지하고, S2/S4 연기 시험은 각각 18/54조건에서 세 번의 해시가 일치한다.

[독립 Core 검토](../evidence/S4/review/core-review.md), [보고서 변조 검토](../evidence/S4/review/report-audit.md), [후속 검토](../evidence/S4/review/followup/)에서 잘못된 자료와 효과 경계 조건을 검사했다. 데이터만 바꾼 [더미 영주·영지](../evidence/S4/dummy/results.json)는 전체 21,600틱을 세 번 생존하고 동일한 상태 해시 `3B3688BF31CF5C4BD3E09417A9FA135368B67A37AB171E2B1C7492DB3C9566A9`를 냈다. Core에 구체 영주·영지 ID를 추가하지 않았다.

[지표 화면](../metrics/index.html)은 누적 20개 기록을 런타임·설정별 시리즈로 구분한다. [브라우저 증거](../evidence/S4/metrics/browser.json)는 세 화면 폭의 실제 DOM/SVG·원본 JSON 링크·수평 넘침·오류를 확인한다. S4의 대표 사례 tick p95는 해당 런타임·입력의 측정이며 모바일 성능이나 전체 사례 p95를 대신하지 않는다.

## 실패 원자료와 수정 이력

첫 실행 소스 `e0b5b9b`에서 시간 효과 연장량 집계와 요구 태그 조건 결함이 발견됐다. 완료된 174사례를 포함한 첫 실행 전체를 중단하고 [별도 보관](../evidence/S4/rejected/README.md)했다. 최종 576사례에 섞지 않았다. 시간 효과의 만료·갱신·연장 계산과 모든 요구 태그의 AND 조건을 회귀 검사로 고친 뒤, 깨끗한 수정 커밋에서 전체 리그를 처음부터 실행했다.

## 해석과 다음 결정

무기형과 다른 정책은 카드 선택뿐 아니라 이동도 다르므로 카드 가중치만의 인과 효과를 측정한 실험이 아니다. mixed와 random은 현재 정규화된 선택 분포가 같고, 96쌍의 선택·시간 원장도 일치한다. 여섯 라벨을 서로 다른 여섯 전략의 증거로 해석하지 않는다. 모든 효과의 관측은 효과별 재미·밸런스·실전 유효성 승인이 아니다. 초기 교차를 지속적인 후반 교차의 증거로 바꾸지 않는다.

후속 통제 실험 순서는 [needs-decision #40](https://github.com/hyunlord/bs-mobile/issues/40)에 남겼다. 실패 결과를 보존하고 S5 시장·BM 조사로 진행한다. 인간 플레이·모바일 화면·터치 조작·운영 SDK·과금·리텐션은 이번 헤드리스 리그에서 검증하지 않았다. 기본 조합 등 기존 결정은 [#32](https://github.com/hyunlord/bs-mobile/issues/32), Projects 권한 제한은 [#2](https://github.com/hyunlord/bs-mobile/issues/2)에 남아 있다.
