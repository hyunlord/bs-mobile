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

## 사용자 승인 재개: 도달 가능한 단계 검사 (2026-10-27)

앞의 두 프로필 실패 기록은 보존한다. 사용자가 검사 계약 수정을 명시적으로 승인했다. 각 opt-in 설정 r2~r7의 건물 MaxLevel까지 Core 레벨을 열거하고 View의 Stage 매핑으로 필요한 그림을 검사한다. 기존 10종은 별도 회귀 검사에서 1~3단계 Body/Shadow를 계속 요구한다. 제철소는 승인된 stage1만 존재해야 하며 stage2·3 복제를 금지한다. Core 규칙·설정·그림은 이 재개에서 변경하지 않았다.

최신 main을 merge로 반영하며 GDD의 영지 공사 계약과 새싹 문장 계약, #415 최종 검증 기록을 모두 보존했다. Unity 두 프로필·native 1회 재시도와 화면 확인은 이 수정 커밋에서 다시 해야 한다. 이번 수정의 정적 검사를 실제 런타임 통과로 확대하지 않는다.

후속 주민 작업 루프는 **#431 병합 뒤** 별도 작업으로만 진행한다. 이 PR에는 구현하지 않는다.
