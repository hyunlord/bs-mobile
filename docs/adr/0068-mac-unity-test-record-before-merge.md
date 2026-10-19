# ADR0068: unity/를 바꾸는 PR은 맥 Unity 전체 실행 기록이 있어야 병합한다

- 상태: 승인 — #309, 사용자 결정(2026-10-18)
- 날짜: 2026-10-18
- 연결: [#309](https://github.com/hyunlord/bs-mobile/issues/309), [Unity 테스트 기록](../runbooks/unity-tests.md)
- 번호: main 최대 0067, 열린 #266 스택이 0065·0066을 쓰므로 0068.

## 맥락

CI에는 Unity 실행기가 없다. Unity EditMode·PlayMode는 맥에서 `tools/check-unity.sh`나 수동 실행으로만 돌았고, PR마다 실행이 강제되지 않았다. 그 결과 2026-10-17 main과 무기 스택에서 실패가 쌓였다. 원인은 프로필 가드 누락, 낡은 기대값, 수동 fixture 준비 의존이었다(#294 분류 댓글, #298·#303·#304·#305). 저장소가 공개라 맥 셀프호스트 러너는 붙이지 않는다. 외부 PR 코드가 개인 맥에서 실행될 수 있기 때문이다(사용자 결정).

## 결정

- `unity/` 아래 파일을 바꾸는 PR은 병합 전에 맥에서 `tools/unity-tests.sh`를 실행한다. 스크립트는 두 콘텐츠 프로필의 EditMode·PlayMode 전체를 돌리고, 출력한 `## Unity tests` 기록(커밋, 스위트마다 passed/failed/skipped)을 PR 본문에 붙인다.
- CI `quality`의 `tools/pr-policy.mjs`가 `tools/unity-test-record.mjs`로 기록을 확인한다. 다음 경우 실패한다: 기록 없음, 네 스위트 중 누락, failed 0 아님, passed 0, 기록 커밋이 저장소나 head 역사에 없음, 기록 커밋 뒤 `unity/` 변경. `unity/` 밖의 이후 커밋은 기록을 무효로 만들지 않는다.
- PR 본문 수정으로 기록을 붙일 수 있도록 `pull_request`에 `edited` 유형을 더한다. 필수 검사(`quality`)가 그대로 병합을 막는다.

## 결과와 비용

- 병합 전에 맥에서 두 프로필 전체를 한 번 실행해야 한다. main을 가지에 병합해 `unity/`가 바뀌면 다시 실행해야 한다.
- 기록은 자기 신고다. CI는 숫자의 진위를 검증하지 못하고 형식·커밋 관계만 본다. 위조 방지가 아니라 실행 누락 방지 장치다. 결과 XML은 로컬 `artifacts/unity-tests/`에 남는다.
- `edited`로 본문 수정마다 `quality`가 다시 돈다.

## 거절한 대안

- 맥 셀프호스트 러너: 공개 저장소에서 외부 코드 실행 위험(사용자 결정으로 제외).
- GitHub 호스트 Linux 러너 + Unity 라이선스(GameCI): 라이선스 비밀 관리와 Mac 에디터와의 차이. 지금 범위 밖이다.
- 결과 XML을 커밋: 저장소 예산과 생성물 규칙(ADR0057)에 어긋난다.
- runbook 문구만 두기: 이번에 쌓인 실패가 바로 그 방식에서 나왔다.

## 검증

`node --test tools/test-unity-test-record.mjs` 통과(기록 없음·실패·누락·이후 unity/ 변경·없는 커밋은 거부, 이후 비unity 커밋은 허용). `tools/check.sh`. 이 PR은 `unity/`를 바꾸지 않으므로 기록이 필요 없다. 필수 CI.
