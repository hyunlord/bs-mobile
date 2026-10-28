# 승인 전투 효과 그림 적용 (#475)

버전: v1 · 날짜: 2026-10-28 · 상태: 구현 초안, 런타임 검증 대기.

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
따라서 **actor crop과 기존 공병 그림은 바꾸지 않았다.** 새 몸체 적용 후 그 몸체의 승인 기준점으로 연결해야 한다.

실패 보조 그림은 작성 worktree `artifacts/combat-fx-475/fuse-mask-preview.png`에 보존했다.
이는 게임 적용본이 아니다. 추가 그림 생성·몸체 수정·#432 코드 병합은 하지 않는다.

## 검증 기록

| 검사 | 결과 |
|---|---|
| 정확 crop 교체·바깥 RGBA 해시·유효 알파 | Node 5/5 통과 |
| first-playable/wave 아트 계약 테스트 | Node 21/21 통과 |
| manifest/registry 검증 | 두 프로필 통과 |
| Unity EditMode/PlayMode 두 프로필 | NOT_RUN, 공유 Mac 잠금 슬롯 대기 |
| 같은 입력 30초 60fps 전후 렌더·그리기 수 | NOT_RUN |
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
