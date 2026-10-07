관문 결과: PR #39 시한 집계·태그 조건 후속 독립 검토 PASS — 21/21 관측 검사 통과.

2026-10-08. 범위를 RuntimeSystem의 만료/미래 시한 집계와 RequiredTags 자격으로 한정했다. 저장소 파일 수정 없음. 구현 담당자의 테스트와 별도로 `/tmp/bs-s4-followup/Program.cs` 콘솔 fixture를 작성하고 실제 Core/Sim 프로젝트를 참조해 실행했다.

명령: `/Users/rexxa/.dotnet/dotnet run --project /tmp/bs-s4-followup/Audit.csproj`

수정 전 동일 fixture는 11개 실패(종료1)였다. 수정 후 종료0, `Failures=0`. 전후 출력은 같은 디렉터리의 `before.txt` / `after.txt`에 보관했다.

## tick 10000, duration 100 경계

각각 worker-buff, rally-returners, pause-neighbor-growth, extend-duty, guard-return에 기존 시한 0·9900·10050·10500을 넣었다. 총20개 사례에서 실제 최종 시한, AppliedTotal, ActivationCount를 모두 예상값과 비교했다.

| 효과 유형 | 기존 시한 | 새 시한 | 실제 추가 활성 tick / AppliedTotal |
|---|---:|---:|---:|
| 갱신형(worker/rally/pause) | 0 또는 9900 | 10100 | 100 |
| 갱신형 | 10050 | 10100 | 50 |
| 갱신형 | 10500 | 10500 | 0 (ActivationCount도0) |
| 연장형(duty/guard) | 0 또는 9900 | 10100 | 100 |
| 연장형 | 10050 | 10150 | 100 |
| 연장형 | 10500 | 10600 | 100 |

`RuntimeSystem.cs:161,168,214`는 현재 tick과 기존 시한 중 큰 값을 집계 기준으로 삼는다. 지나간 10000 tick을 새 효과로 세지 않는다. `:200`과 `:206`의 단일 대상 연장은 max(now, old)에 기간을 더해 이미 남아 있는 기간을 줄이지 않는다. 코드의 넓은 정수 계산과 상한 처리도 읽어 확인했지만 이 fixture를 정수 최대치까지의 검증으로 확대해 주장하지 않는다.

## 실제 물품의 복수 태그

실제 S4 데이터의 scaffold_step(필요 태그 building + people)을 실제 chest channel의 월드 loot로 배치했다. 건물 도구 carpenter_hammer만 소유하면 획득0, 사람 도구 muster_horn까지 소유한 뒤에는 획득1이었다. 별개 장비가 각각 선행 태그를 제공해도 모든 선행 조건을 만족하면 된다. `RuntimeSystem.cs:71`의 All은 생성과 pickup 양쪽의 공통 ItemEligible에 사용된다.

이 검토는 중단된 전체 리그 결과의 유효성을 복구하지 않는다. 수정 전 원시 결과를 새 결과와 혼합하지 말고, 통합 담당자가 최신 코드로 전체 행렬을 재실행한 증거를 따로 제시해야 한다. 전체 테스트·CI·리그 보고서 관문은 본21개 표적 검사와 별도다.
