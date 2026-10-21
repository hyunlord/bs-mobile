Closes #

## Gate result
PASS / FAIL / PARTIAL, with measured values and evidence paths.

설계 문서 영향: 없음 / 있음(문서 이름)
없음이면 이유를 적고, 있음이면 같은 PR에서 해당 GDD를 갱신한다.

## Verification
Commands, actual results, and known gaps. Include ADR for structural changes.

## Unity tests
unity/를 바꾸는 PR은 필수. 맥에서 ./tools/unity-tests.sh를 실행하고 출력한 기록(commit과 두 프로필 EditMode·PlayMode의 passed/failed/skipped)을 이 절에 그대로 붙인다. 없거나 실패가 있으면 CI가 막는다(docs/runbooks/unity-tests.md). unity/를 바꾸지 않으면 "해당 없음"으로 둔다.

## Screens
Screenshot or N/A (headless/data/docs); attach raw CLI evidence instead.
