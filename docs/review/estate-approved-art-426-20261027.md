## 승인 그림 반영 후 반복 실패로 중단

source `1cc8dde5859d675369fcae74f20db4457b90c4d8`:
- 기록소·병영 s1~s3 새싹 r3: 승인된 여섯 crop 내부814픽셀 변경, crop 밖0·알파 변경0. 기존30셀 좌표 유지. Node crop/manifest 검사10/10 통과.
- 제철소 r2는 별도31번째 셀로 추가. 완성 배율0.75 유지, 공사 높이는 해당 완성 그림 높이 이하로 제한.

두 프로필에서 같은 `EstateDecorTests.EveryRoleAndBuildingStageHasPackedArt` 실패가 반복돼 사용자 중단 규칙을 적용합니다.

| Suite | passed | failed | skipped |
|---|---:|---:|---:|
| first-playable EditMode |878|1|25|
| first-playable PlayMode |53|0|45|
| wave-1a EditMode |888|1|15|
| wave-1a PlayMode |85|0|13|

원인: `EstateArt.Building`은 kind+stage를 정확히 조회하고, `EstateDecorTests.cs:43`은 모든 kind의1~3단계를 요구합니다. 새 ironworks는 승인된 완성 그림1개를 stage1로 등록했으므로 **Missing building art ironworks stage 2**가 양쪽에서 발생했습니다. 기록소·병영 crop 문제와 별개입니다. 검사 기대값 변경·임의 그림 복제·Core 변경은 하지 않았습니다.

보충 fixture 실행과 native 재시도는 진행하지 않았습니다. 실제 화면을 통과로 선언하지 않습니다. 잠금은18:06:25 KST 해제했고 다른 PR의 독립 작업만 이어갑니다. 기존 native Title.Enter 실패도 보존합니다.

CI: 이 실패 커밋의 최종 통과 없음. 새 main과 문서 충돌도 남아 있어 GitHub PR CI가 시작되지 않는 상태이며, 이전 커밋 CI를 재사용하지 않습니다. #431 초안 유지, 병합 없음.
