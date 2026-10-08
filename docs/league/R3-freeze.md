관문: 통과 — 튜닝 3개·프로필 4개는 승인된 기본 ID 치환을 제외하면 역사 R2 데이터와 의미상 동일하다.
변경: 기본 1/1을 새싹 변경·개척 기사로 바꾸고 선택적 잔재 고리만 추가한다.
결정: R2 관문(c) 실패를 맞추기 위한 수치 재보정은 하지 않는다.
한계: 이 문서는 수치 동결 검증이며 R3 실행·밸런스 결과는 별도 보고한다.

# R3 변경 범위와 재실험 동결

기준은 R2 측정 소스 `bcaa00f763ec6950707635569befb558e8de1731`의 `data/`다. 정확한 243파일·376,167바이트를 `core/tests/SowSiege.Tests/Fixtures/phase0-r2/data`에 보존하고 인접 매니페스트로 연결한다.

활성 ID 치환은 `core:founder` → `core:frontier_knight`, `core:meadow` → `core:sprout_march`다. 이를 역치환한 뒤 `tuning.json`, `tuning-s4b-01.json`, `tuning-s4b-02.json` 및 네 프로필 JSON을 과거 입력과 재귀 비교했다. **숫자·이동·mixed 순서·정책 가중·XP·비옥도·성장 보너스·선택 콘텐츠의 추가 변경은 0**이다. 외부 증거 `technical/bs-r3-freeze-audit.mjs`와 `frozen-numerics-diff.csv`가 각 파일의 전후 바이트 해시와 의미 비교를 보존한다. 활성 ID가 바뀌므로 카탈로그·프로필·tuning의 바이트 해시는 R2와 같다고 주장하지 않는다.

기본 영지의 새 `remainsLoop`는 capacity1000/lifetimeTicks900/absorptionRadius600이다. 더미 `test:moor`는2/60/130이다. 기존 비옥도 수율을 재사용하고 추가 XP·피해·점수를 지급하지 않는다. 데이터 필드와 공간·시간 비용의 이유는 [ADR0015](../adr/0015-local-remains-loop.md), 원래 선택은 [#32](https://github.com/hyunlord/bs-mobile/issues/32), 구현은 [#62](https://github.com/hyunlord/bs-mobile/issues/62)에 있다. 영웅의 운반 능력은 미구현 후보로 남는다.

## 실행 계약

사전 선언된 [S4b 프로토콜](S4b-protocol.md)을 그대로 쓴다. A576=6정책×사람3×seed42–73, B288=random×사람3×이동3×동일seed32, 각3반복이다. 독립 seed블록32개의 대응 설계이며 R2/R3 또는 반복을 독립 표본으로 늘리지 않는다. A는 모든 정책 동일 circuit, B는 참고용이며 관문에 섞지 않는다.

프로필은 활성 `s4b-02`다. 최대21600틱/30Hz/720초, XP30/15/5와 모든 위협 수치를 유지한다. R2 원자료 경로·Release를 덮어쓰지 않고 새 출력에 실행한다. 측정 전 소스 커밋을 고정하고 실행 중 tracked 파일을 바꾸지 않는다. 실제 측정 SHA와 활성 바이트 해시는 각 사례 metadata와 최종 R3 보고서에 기록한다.

[이전 S4b 동결](S4b-freeze.md)의 해시는 **R2 측정 시점** 값이다. 현재 활성 데이터 경로가 같은 이름이어도 새 ID로 달라졌으므로 역사 fixture 또는 해당 커밋에서 재현해야 한다. R2 FAIL과 원본 S4 FAIL은 그대로 보존한다.
