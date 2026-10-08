관문: 통과 — 576조건·3반복, b=true, weapon=true, c′=true.
CSV 데이터 하위보고서다. 커밋·PR·CI 종합 판정은 별도 검토 보고서를 따른다.
seed 20000–20031 (32대응블록); 반복은 표본수에 포함하지 않는다.
(b)는 각 사람규칙×seed의 [생존,틱,레벨,실제전투피해] 공동1위 교집합이 비어야 한다.
무기 하한은 같은 규칙 random생존의0.6배, c′는 전문4정책×ABC의0.6–1.5배다.
random생존0은 NOT_EVALUABLE이며 통과하지 않는다.
전투피해는 무기+도구활성+도구성장+아군; 식량·재료는 제외한다.
overkill은 실제무기의 requestedDamage-appliedHpDamage 진단값이며 억제피해를 더하지 않는다.
noRadiusTargetRate는 반경후보0, trueEmptyActivationRate는 형태판정까지 명중0; 분모는 실제attackAttempts다.
실패 결과는 보존하며 후속 제안은 별도 승인 문서의 단일 제안만 따른다.

|정책|사람|생존/사례|
|---|---|---|
|weapon|A|16/32|
|land|A|25/32|
|building|A|21/32|
|people|A|24/32|
|mixed|A|16/32|
|random|A|19/32|
|weapon|B|19/32|
|land|B|24/32|
|building|B|26/32|
|people|B|23/32|
|mixed|B|17/32|
|random|B|23/32|
|weapon|C|15/32|
|land|C|21/32|
|building|C|23/32|
|people|C|23/32|
|mixed|C|19/32|
|random|C|24/32|

|정책|사람|random대비|c′|
|---|---|---|---|
|weapon|A|0.8421052631578947|PASS|
|land|A|1.3157894736842106|PASS|
|building|A|1.105263157894737|PASS|
|people|A|1.263157894736842|PASS|
|weapon|B|0.8260869565217391|PASS|
|land|B|1.0434782608695652|PASS|
|building|B|1.1304347826086956|PASS|
|people|B|1|PASS|
|weapon|C|0.625|PASS|
|land|C|0.875|PASS|
|building|C|0.9583333333333334|PASS|
|people|C|0.9583333333333334|PASS|
