관문: 부분 — 288사례·3반복, 이동 비교 참고 결과, A 관문 미평가.
변경: 사전 선언된 이동·위협·경험치 실험의 관측 결과.
사용자 결정 필요: 결과 실패를 보존하며 후속 수치 변경은 별도 실험으로 진행한다.

# S4b 결과

실험 구조 검증 통과. 

- 서로 다른 사례 288, 대응 seed 블록 32, 결정론 반복 3. 사례·반복을 독립 표본으로 합산하지 않았다.
- 프로필 core:s4b_02, source bcaa00f763ec6950707635569befb558e8de1731, tuning SHA-256 39D577F80FCF77F03891FDAFC9387F0CBE90C7A2F80A7D5AA454C828CE5F700D.
- 원자료 CSV 해시는 source-manifest.csv에 있다. 통계는 열 개 CSV로 계산한다. raw/가 있으면 원본 JSON 해시를 추가 검증하고 CSV-only 재현은 기록된 원본 매니페스트를 신뢰한다.
- XP 계수: {"base":30,"linear":15,"quadratic":5}. world unit: world-unit.

|관문|적용|결과|
|---|---|---|
|validity|예|PASS|
|a|아니오|NOT_EVALUATED|
|b|아니오|NOT_EVALUATED|
|c|아니오|NOT_EVALUATED|
|d|아니오|NOT_EVALUATED|
|XP|아니오|NOT_EVALUATED|
|mixed-ledger|아니오|NOT_EVALUATED|

기준 random/C: 18/32, Wilson 95% [0.3932559113685198, 0.7183466888495963]. A의 우세 정책 관문은 이 모드에서 평가하지 않는다.

## 해석과 재현

거리 통계는 실제 표본의 sqrt(distanceSquared), 중앙값은 가운데 두 값 평균, p95는 nearest-rank다. 표본수·분포를 함께 기록하고 사망 후 좌표를 보간하지 않는다. 여러 시점은 독립 사람/seed 표본이 아니다. 익은 밭 대기에서 harvest만 완료 대기이고 destroyed는 경쟁 사건, death/duration은 우측 검열 하한이다. 검열 하한의 평균을 수확 완료 평균에 섞지 않는다.

rank는 생존→도달틱→레벨→weaponDamage+toolActivationDamage+toolGrowthDamage+allyDamage다. 그룹 순위는 같은 seed수의 정수 합을 비교하므로 부동소수 허용오차로 공동1위를 늘리지 않는다. 식량·자재 stockpile은 점수가 아니다. B는 random 카드 정책의 이동 비교이며 A 관문과 합산하지 않는다.

모바일 조작·재미·첫 도구 이해도·메타 진행은 이 수치 실험으로 검증하지 않았다. 원본 S4 FAIL을 덮어쓰지 않는다.
