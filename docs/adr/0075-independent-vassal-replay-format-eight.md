# ADR 0075: 가신 전투 snapshot을 replay format8 독립 블록으로 기록

- 상태: 승인 — 2026-10-29 사용자 r2 설계 결정
- 날짜: 2026-10-29
- 연결: [#523](https://github.com/hyunlord/bs-mobile/issues/523), [PR #525](https://github.com/hyunlord/bs-mobile/pull/525)
- 상세 계약: [가신 전투 설계 r2](../design/gdd/13-vassal-combat-proposal.md)

## 맥락

현재 replay format7의 estate-effects schema1/2는 기존 영지 효과와 역사 재생을 기록한다. 승인된 가신 전투 연결은 출전 roster와 해석된 능력값을 전투 시작 시 고정하고, 저장·복원·재생에서 같은 값과 정수 나머지를 유지해야 한다. 현재 reader와 runtime에 가신 snapshot 지원이 구현됐다는 뜻은 아니다.

## 결정

새 replay **format8**과 독립 **`vassal-run-v1`** 블록을 사용한다. format7 schema1/2의 의미·바이트·golden은 변경하지 않는다. 가신 블록이 없는 기존 재생은 과거 형식과 실행 경로를 유지한다. 명시적 enabled 상태의 빈 roster는 absent와 구분한다.

블록은 시작 시 확정한 roster·규칙/콘텐츠 근거·해석된 능력값을 보존한다. 복원·재생 중 현재 계정이나 live catalog를 다시 읽어 값을 바꾸지 않는다. format8을 모르는 reader는 unknown-version 오류로 실패하며, 가신 효과를 조용히 버려 format7로 재생하지 않는다. 중복 ID, 범위 밖 값, 알 수 없는 블록 버전·flag와 hash 불일치는 거부한다. 상세 필드·검증 규칙은 연결된 설계의 §3을 따른다.

유료 자원·시간 단축을 통한 간접 성장은 승인됐으므로 earned-only 원장이나 유료 자격 provenance/hash/flag를 도입하지 않는다. roster 검증 근거는 현행 슬롯·레벨상한 및 콘텐츠 정합성을 위한 것이다. 직접 가신·조각·판 안 능력 판매 금지와 능력별 cap은 상세 설계대로 유지한다.

## 거절한 대안

- format7에 schema3 추가: 기존 estate schema와 독립 가신 snapshot의 호환 경계를 섞으므로 채택하지 않는다.
- 재생 시 현재 계정/catalog 재조회: 시작 이후 계정 변화로 결과가 달라지므로 채택하지 않는다.
- 유료·무료 자격별 snapshot 분기: 승인된 간접 성장 정책에 불필요하며 도입하지 않는다.

## 결과·비용

후속 Codex 구현은 format8 codec과 reader 오류 처리, snapshot/runtime hash·복원, 조합 검사를 추가해야 한다. 구체 wire 순서와 함수 연결은 후속 기술 문서 및 구현 검사로 구체화한다. 이 ADR은 이미 승인된 형식 선택을 기록하며 새 승인 게이트를 추가하지 않는다. 게임 코드·데이터·SDK는 이번 PR에서 변경하지 않는다.

## 검증

문서 정합성 검토와 `node tools/check-docs.mjs`, `git diff --check`를 수행한다. 후속 구현에서 format1–7 및 format7 schema1/2의 기존 bytes/golden 보존, format8 estate/가신 옵션 조합, unknown-version 거부, snapshot 복원 후 동일 해시를 검사한다. 이 ADR의 추가만으로 runtime·codec·밸런스 관문을 통과한 것으로 보고하지 않는다. 가신 효과는 wave 전용이다. 효과 없음/최대 비교는 wave 정상 입력과 C05에서 수행하고 C05 스트레스·Quit 결과를 정상 생존율로 일반화하지 않는다. FP01에는 가신 효과를 적용하지 않으며, 가신 블록 absent의 기존 해시·성능 회귀만 확인한다. 해당 실행 검증은 **NOT_RUN**이다.
