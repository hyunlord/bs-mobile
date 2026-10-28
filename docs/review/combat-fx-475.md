# 승인 전투 효과 그림 적용 (#475)

버전: v2 · 날짜: 2026-10-28 · 상태: 초안, 동일 시각 검증 실패 두 번으로 중단.

## 범위와 보존

사용자가 승인한 Release
[`m3-combat-fx-audit-456-20261028`](https://github.com/hyunlord/bs-mobile/releases/tag/m3-combat-fx-audit-456-20261028)의
포자 r4·방어 구체 r4·불씨 r4·철검/수확 낫/운하 활 r1을 기존 `fx.png` crop에 교체한다.
Core·콘텐츠·입력·리플레이·기존 역할의 셀/피벗/크기/재생 시간은 변경하지 않는다.

- `tools/art-authoring/combat-fx/installed.json`: 각 승인 PNG SHA-256, 기존 crop, atlas 전후 SHA-256.
- 변경 59,853픽셀, 승인 crop 6개와 역사 보존 복사 칸 4개 바깥 변경 0. atlas 해상도·텍스처 수 불변.
- 같은 crop을 공유하던 비승인 역할 47개/프로필(피격 피드백·도구·진화 등)은 원판 4개를 같은 atlas의 빈칸에 복사하고 참조만 바꿨다. 두 프로필의 비승인 fx 역할 전체에 대해 RGBA·셀 크기·피벗·world size·재생값 계약 해시가 동일하다.
- 포자 combined PNG에는 내부 0.10·테두리 0.34의 유효 알파가 이미 포함된다. 이 field role만 View 불투명도 1로 그린다. 다른 field·nova·projectile 알파는 유지한다.
- 철검/낫은 first-playable의 transient area(최대 0.26, 0.18초)이며 wave 전용 arc를 교체하지 않는다.
- wave-1a의 불씨 투사체는 공용 `attack.core.ember_wand.projectile`을 읽으므로 C05 대상이다. 측정은 Claude Code 야간 대기열에 요청한다.

## 도화선: 보류, 완료 아님

승인 도화선은 256×256 칸의 새 공병 16칸 걷기에 맞춰 제작됐다. 현재 main의 공병은
`actors.png`의 167×196 단일 칸이다. [#432](https://github.com/hyunlord/bs-mobile/pull/432)는 미병합이다.
새 6칸 도화선은 9~11×20~24px의 좁은 불꽃이지만 역사 칸은 더 넓은 정적 불꽃을 포함한다.
그대로 겹치면 두 실루엣이 남는다. 기존 불꽃만 마스킹하는 시험도 어깨 쪽 수평 절단·잘린 심지·떠 있는 불꽃으로 실패했다.
따라서 **actor crop과 기존 공병 그림은 바꾸지 않았다.** 새 몸체 적용 후 그 몸체의 승인 기준점으로 연결해야 한다. 미완료 항목은 [#481](https://github.com/hyunlord/bs-mobile/issues/481)에서 별도로 추적한다.

실패 보조 그림은 작성 worktree `artifacts/combat-fx-475/fuse-mask-preview.png`에 보존했다.
이는 게임 적용본이 아니다. 추가 그림 생성·몸체 수정·#432 코드 병합은 하지 않는다.

## 검증 기록

| 검사 | 결과 |
|---|---|
| 정확 crop 교체·바깥 RGBA 해시·유효 알파 | Node 8/8 통과 |
| first-playable/wave 아트 계약 테스트 | Node 21/21 통과 |
| manifest/registry 검증 | 두 프로필 통과 |
| Unity EditMode/PlayMode 두 프로필 | b288ddd: FP 899/55, wave 909/89 통과·실패0. 공통 미설명 skip2로 runner exit1. 후속 fixture 소스 2f97c7c의 전체 기록은 NOT_RUN |
| 같은 입력 30초 60fps 전후 렌더·그리기 수 | NOT_RUN, 시각 fixture 반복 실패 후 중단 |
| 리플레이 종료 해시 | NOT_RUN (Core/리플레이 소스 diff 0은 실행 증거가 아님) |
| C05 | Claude Code 야간 대기열 요청 예정 |

최초 crop 검사 시 Git LFS 포인터를 PNG로 읽어 실패했다. 역사 commit의 전체 PNG를 재다운로드하는 대신
기존 로컬 원판에서 승인 crop을 제외한 RGBA 해시를 기록해 재현 검사하도록 수정했고 통과했다.

## 재생성

```sh
node tools/art-authoring/combat-fx/install.mjs RELEASE_DIRECTORY BASELINE_DIRECTORY
node --test tools/art-authoring/combat-fx/install.test.mjs tools/art-authoring/sprout-emblems/replace.test.mjs
node tools/first-playable-art.mjs
node tools/wave1a-art.mjs
```

그림 승인과 이 정적 검사는 런타임 시인성·모바일 성능 통과가 아니다. 사용자 승인 전 병합하지 않는다.

## 2026-10-28 실제 실행과 중단

- main #473(0f9096a)을 정상 merge한 b288ddd에서 표준 네 suite는 assertion 실패0이었다.
- 기본 실행의 두 미설명 skip은 M1 frozen combat sidecar 부재와 opt-in melee catalog 부재다. 기존 보완 실행 절차를 찾았으나, 아래 시각 실패 때문에 아직 실행하지 않았다. 예외를 추가하지 않았다.
- FP01 원본 입력에는 철검·불씨·방어 고리가 있으나 낫·운하 활·포자가 없다. 따라서 6종 전체를 실제로 렌더하기 위한 View-only snapshot fixture를 2f97c7c에 추가했다. 이것은 Core 공격·충돌이나 정상 플레이 증거가 아니다.
- 첫 fixture는 6종×120칸을 실제 WorldRenderer/셰이더로 출력했고 테스트는 1/0/0이었다. 하지만 육안상 건설 발판 그리드가 장면을 채우고 효과·적이 판독되지 않아 **실패**로 판정했다. 픽셀 차이 검사 통과는 적절한 검토 화면을 증명하지 않았다.
- 프로필 복원 후 Configure 누락을 의심해, first-playable Configure 후 한 번 재시도했다. 두 번째 역시 1/0/0이지만 포자 대표 PNG가 첫 실행과 바이트 동일했고 같은 시각 실패였다. 따라서 **registry 준비 가설은 입증되지 않았다.**
- 원본 세션의 Buildings/Farms/People을 유지한 fixture가 건설 그리드를 남겨 효과를 가렸을 가능성이 있다. 이는 정적 추정이며, 원인 확정 또는 수정 완료로 보고하지 않는다.
- 같은 실패 두 번 규칙으로 추가 Unity·캡처·수정 재시도를 중단하고 소유 잠금을 해제했다. 게임 적용 승인·성능 통과·6종 시인성 통과를 주장하지 않는다.

### 보존 경로

자기 worktree의 다음 경로만 사용했다. 외부 작업 폴더·프로세스·측정 출력은 수정하지 않았다.

- `artifacts/combat-fx-475/unity-tests-b288ddd`: 원본 네 suite XML·로그·runner exit1 기록.
- `artifacts/combat-fx-475/fixture-after`, `fixture-after-logs`: 첫 시각 실패, 720칸·coverage.csv·XML·Editor.log.
- `artifacts/combat-fx-475/fixture-after-configured`, `fixture-after-configured-logs`: Configure 후 두 번째 시각 실패, 같은 출력과 Configure 로그.
- `unity/Assets/Game/Tests/PlayMode/CombatFxCaptureTests.cs`: 실패를 통과로 잡은 fixture 소스 보존. 재개 시 가시성·장면 유효성 검사 개선 필요.

두 포자 PNG는 각각 477,734바이트이며 byte-identical이다. 해시 불변 검사는 fixture의 미변경 authoritative session만 증명한다. 요구된 실제 전체 리플레이 종료 해시는 아직 실행하지 않았다.
