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

## 재개 실행 결과: Editor 통과, Native 반복 실패 중단 (2026-10-27 저녁)

검증 source: `3184465b517b26d6803c0600ff43ec4a2503c04b`. 21:45:52~21:58:42 KST 두 프로필과 보충 실행을 완료했다.

| Suite | passed | failed | skipped |
|---|---:|---:|---:|
| first-playable EditMode |885|0|25|
| first-playable PlayMode |55|0|45|
| wave-1a EditMode |895|0|15|
| wave-1a PlayMode |87|0|13|

원본 runner exit1은 공통 fixture skip2건 때문이다. 입력 생성 후 combat/melee 보충 검사 각1/1 통과, 합산 skip은 unexplained0/listed1이다. 원본 기록을 수정하지 않았다. first-playable·wave-1a replay parity와 메타5seed 검사가 통과했다. 실제 PlayMode 캡처268건 청록0; guard 자체검사4건은 분리했다. 실행 후 추적 파일 변경0. 과거 ironworks stage2 실패는 재발하지 않았다.

macOS ARM64 Mono Development 빌드 성공(574930217bytes). 허용된 Native 재시도1회는 종료코드2로 실패했다. `Title.Enter` matches0, screen=Title, frame269, uiActive=True. 실제 버튼은 `시작|변경 지도|장원|가신|도전|설정`이며 모두 active/enabled이다. 하네스가 기대한 버튼과 실제 UI가 다르다는 관측이며, 어떤 초기화 분기가 원인인지는 아직 확정하지 않았다. 같은 Title.Enter 실패가 반복되어 추가 수정·실행하지 않는다. Native 화면0장, 건설 흐름 미도달. 이번 실행 로그: `artifacts/day-20261027/reachable-level-followup/estate-native-20261027-215849-43982/`.

그림 표본은 실제 Editor 공사 흐름의 곡창·경작지 각1→2→3→완공, 첫 진입/키아트C 비교다. [Release](https://github.com/hyunlord/bs-mobile/releases/tag/estate-approved-art-431-r3-20261027). 공사 그림 painted-height 상한은 검사로 확인했지만, 불투명한 공사중 표지가 경작지1~3단계 대부분을 가려 화면상의 연속성/단계 가독성은 부분이다. 완공 기초의 밝은 각진 영역도 남아 있다. 제철소 완공 캡처는 기존 하네스에 없어서 미확인이다. 정적 등록 검사를 실제 화면 증거로 대체하지 않는다.

CI 최종 실행 링크·결과는 PR 코멘트에 기록한다. #431 초안 유지. 추가 Unity/빌드 없이 잠금을 해제했으며, 제철소 캡처와 Native 재개는 반복 실패 보고 뒤 다음 승인 범위에서 진행한다. 작업 루프444는 #431 병합 전 구현하지 않는다.

## 2026-10-28 승인 재개: 공사 이름표와 native 프로필 원인

사용자가 공사 이름표 가림 수정 및 native 원인 수정 후 1회 재시도를 승인했다. 이전 두 실패는 보존한다.

- 공사 시트가 열려 있으면 지도 위 중복 이름표를 숨긴다. 시트를 닫으면 이름표 상단을 부지 앞쪽 기초 모서리보다 아래에 둔다. 공사 그림·완성 배율0.75·Core는 바꾸지 않는다. PlayMode 정상 곡창→경작지 흐름에 시트 열림/닫힘의 이름표 위치 검사를 추가했다.
- 실패 로그의 실제 버튼은 `시작`이었지만, 이를 `Title.Enter`의 동의어로 클릭하면 안 된다. `RunCoordinator.cs:190`은 `Ui.Initialize(font, IsWave)`로 화면 경로를 고르고, `:240`의 first-playable `시작`은 전투/소개로 간다. `RunCoordinator.Portrait.cs`의 `ShowPortraitTitle`만 소개를 본 계정의 정상 클릭을 영지로 연결한다. 기존 launcher가 first-playable로 빌드한 것이 실제 버튼 목록과 일치한다.
- 따라서 제품 UI를 수정하지 않고 native 실행 설정을 **wave-1a**로 교정한다. probe는 클릭 전에 profile/isWave/portrait를 기록하고 잘못된 프로필이면 명확히 실패한다. 잘못된 `시작` 대체 클릭·자원 지급·튜토리얼/건설 규칙 우회는 하지 않는다.
- 새 native 실행은 아직 하지 않았다. 정상 곡창→경작지 하네스 1회와 두 프로필 결과·화면 비교는 부모 세션 담당이다. 제철소 완공은 별도 명시적 Editor fixture로 검증하며 native에서 강제 완성한 것처럼 보고하지 않는다.

정적 확인: Unity boundaries·diff whitespace·native launcher bash 문법 통과. 실제 실행 통과는 별도다. 주민 작업 루프 #444는 여전히 #431 병합 뒤에만 진행한다.
