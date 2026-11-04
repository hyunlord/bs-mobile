# ADR 작성

1. 연결 이슈에 구조 결정이 필요한 이유·경계·수용 기준을 쓴다. 0001~0005는 사용자 확정 사항이며 임의로 번복하지 않는다.
   - **번호 정하기:** main과 **열린 PR 전체**에서 이미 쓰인 ADR 번호를 확인한 뒤, 그중 가장 큰 번호 +1을 쓴다. 여러 세션이 동시에 ADR을 쓰면 main만 보고는 충돌한다([#676](https://github.com/hyunlord/bs-mobile/issues/676)).
   - 확인 명령: `git ls-tree --name-only origin/main docs/adr/`. 그리고 `gh pr list --state open --limit 200 --json number,files --jq '.[] | .number as $n | .files[].path | select(startswith("docs/adr/00")) | "\($n) \(.)"'`.
   - 병합 직전에 같은 확인을 한 번 더 한다. 충돌하면 번호를 바꾸고, ADR 본문에 바꾼 이유를 한 줄 남긴다.
2. `docs/adr/NNNN-short-topic.md`에 제목, 상태(제안/승인/대체됨), 날짜, 연결 이슈, 맥락, 결정, 결과·비용, 거절한 대안, 검증을 쓴다.
3. 출처를 확인한 사실과 제안을 구별한다. 채택되지 않은 SDK·플랫폼 지원·관리자 설정을 작동한다고 쓰지 않는다. 기존 결정을 바꾸면 새 ADR에서 이전 번호를 명시하고 승인 필요 항목은 needs-decision 이슈로 남긴다.
4. 구조 변경 PR에 ADR을 같이 넣고 PR 설명에 경계·대안·마이그레이션을 쓴다. 자동 검사가 파일 유무를 확인해도 결정의 타당성은 리뷰 대상이다.
5. `./tools/check.sh` 및 해당 동작 검증 후 필수 CI를 통과해 병합한다. ADR만으로 런타임 관문을 통과했다고 보고하지 않는다.
