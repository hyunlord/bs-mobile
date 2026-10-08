# 이슈 처리

1. `gh repo view --json nameWithOwner`가 `hyunlord/bs-mobile`인지 확인한다. 다른 저장소면 멈추고 작업 경로를 고친다.
2. 중복 이슈를 확인하고 문제·수용 관문·범위 밖·검증 방법을 기록한 이슈를 만든다. `type/feature|bug|balance|content|tech-debt|research`, `area/core|data|unity|ui|art|meta|bm|infra`, `phase/0`~`phase/5` 중 해당 라벨과 로드맵 마일스톤을 붙인다. 결정 요청에는 `needs-decision`도 붙이고 근거·선택지·권고·가역적 임시 가정을 적는다. 답을 기다리지 않고 독립 작업을 계속한다.
3. GitHub Projects 보드에 이슈를 넣고 진행 상태로 옮긴다. 보드 권한이 없으면 API 오류와 필요한 권한을 needs-decision 이슈에 남긴다. 로컬 목록만 만들고 보드가 존재한다고 보고하지 않는다.
4. 최신 main에서 `git switch -c <type>/<issue>-<topic>`로 짧은 가지를 만든다. 기존 변경을 보존한다. 병행 에이전트에는 파일 소유권을 정한다.
5. 요구사항을 만족하는 최소 변경과 필요한 검증을 실행한다. 구조 변경이면 ADR을 포함한다. `TODO #123:`처럼 실제 이슈 참조 없는 TODO를 남기지 않는다.
6. `./tools/check.sh`를 실행하고 결과를 읽는다. 실패를 숨기는 변경·검사 삭제·타입 억제는 금지다. 단계 증거에는 실제 command·SHA·환경·원자료 경로를 남긴다.
7. Conventional Commit + 필요한 Lore trailers로 커밋하고 가지를 푸시한다. PR에 `Closes #번호`와 정확한 제목 `## Gate result`, `## Verification`, `## Screens`를 넣고 각각 관문 결과, 검증 명령과 실제 결과, 화면 또는 헤드리스라 해당 없음의 이유를 쓴다. PR 템플릿의 제목을 번역하거나 생략하지 않는다.
8. `gh pr checks <번호> --watch`로 필수 CI를 확인하고 `gh pr merge <번호> --auto --squash --delete-branch`로 자동 병합을 예약한다. 실제 병합을 확인하고 main으로 복귀해 갱신한다. 정책/권한 차단은 needs-decision으로 남기며 우회 푸시하지 않는다.
9. 의도적인 음성 대조 PR은 병합하지 않는다. 의도한 CI가 실패했음을 확인한 뒤 증거 링크를 남기고 닫고 가지를 삭제한다. 한 PR에 한 위반을 넣어 실패 원인을 구분한다.
10. 단계 보고 첫 줄에 관문 결과를 쓰고 커밋 해시·PR·CI 링크를 남긴다. 신규 ZIP·CRC 검사·전달 영수증은 만들지 않는다. 검증하지 않은 관리자 설정·보드·CI 동작을 완료라고 쓰지 않는다.
