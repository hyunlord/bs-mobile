# #266 무기 손맛 수정: 검토 기록

상태: **부분, 사용자 영상 승인 전**. 기준 브랜치 `feat/258-frame-hero-impact`(초안 PR #261) 위의 View 전용 수정이다. Core 판정·피해·스폰·사건·재생 해시는 바꾸지 않았다. 영웅은 기존 컷아웃 기준선(`a-knight-live`)으로 촬영했다.

## 원인

| 문제 | 원인 | 수정 |
|---|---|---|
| 처치 깜빡임 | Core는 처치 틱에 적을 목록에서 지운다(`WaveRuntimeSystem.cs` 153·160행). 보스 표시 시계는 그 틱의 사건을 표시 시계가 틱에 닿을 때 전달한다(`BossPresentationTimeline.cs` 153행). 30Hz/60fps에서 2표시프레임 동안 적·효과가 모두 없다. 8종 모두 같은 경로 | 현재 스냅샷에 처치 사건이 있는 적의 마지막 렌더 자세를 유지하고 번쩍임→밀림→쓰러짐 재생 |
| 거대 검 | 반지름=Core 사거리(진화 6000)라 화면 밖, 0.18초 동안 계속 흐려짐 | 화면 안 기본 반지름, 화면 안 처치 위치를 모두 감싸는 최소 반지름까지 확장, 0.15초 유지 |
| 영웅 피격 | 0.2초 전신 붉은 tint가 연속 피격으로 재시작 | 흰 번쩍임 2프레임(재발동 0.25초)+그림만 0.25 몸 너비 밀림 |
| 뿔나팔 | 무리당 1~2명, 원래 크기 | 런타임 장비/정본 최대 레벨 배율로 인원·크기(상한 7명·1.5배), 진화 연출은 런타임 진화만 |

세부 수치는 [피드백 GDD #266 절](../design/gdd/06-feedback.md#266-무기-손맛-수정-구현값-사용자-영상-승인-전)에 있다.

## 실제 실행 증거

캡처 앱: `f093839` ARM64 Mono Release, `UNITY_WAVE_BENCHMARK=1 UNITY_CONTENT_PROFILE=wave-1a`. 입력은 M1-4와 같은 24개 사례 `m1-258-combat-candidate1/net8`(replay·sidecar SHA는 각 `production-plan.json`에 기록)과 같은 오프라인 60fps·실제 소리 경로다. `tools/m1-production-review.mjs validate`가 24개 모두 통과했다.

| 장면 | 화면 안 처치 / 궤적 밖 | 부채꼴 전체가 화면 안인 타격 | 동시 처치 연출 최대 / 초과 |
|---|---|---|---|
| 철날 검 Lv1 | 18 / 0 | 11/11 | 3 / 0 |
| 철날 검 최대 | 104 / 0 | 7/13 | 25 / 0 |
| 철날 검 진화 | 148 / 0 | 3/11 | 33 / 0 |

처치가 화면 가장자리까지 퍼진 타격은 포함 관계를 우선하므로 부채꼴 일부가 화면을 넘는다. 휘두르는 검은 항상 화면 안에서 끝난다.

뿔나팔 검토 로그(`m1-combat-review.json` `musterPresentation`): Lv1 `GearRatio` r=1.88·3명·1.09배, 최대 r=8·5명·1.3배, 진화 r=32·7명·1.5배. 검토 진화 단계는 런타임 진화가 아니므로 진화 전용 연출은 켜지지 않는다.

정상 플레이 경로: PlayMode `NormalPlayRallyKitchenEvolutionIsTheOnlyPathToTheEvolutionLook`(first-playable, seed 30002, 무적·레벨 부여 없음)에서 6630틱 무렵 `core:rally_kitchen` 활성 시에만 `RuntimeEvolution`·7명이 되고 기록은 1회다. 같은 seed의 Mac 자동 플레이(`d95c282`)도 7802·8324틱에 같은 경로를 기록했다. 실시간 앱 캡처는 후반 first-playable 장면에서 60Hz를 유지하지 못해(24.8fps, 누락 1,428슬롯) **실패 표본**이며 60fps 영상으로 쓰지 않는다.

## 8종 결합 장면 비용 (캡처 끔)

`m1-256-combined-v3` 결합 재생(SHA `8ae45858…`), 창 900–1500, 전경 프레임만, 같은 기계·같은 날 각 2회.

| 앱 | p95 (ms) | 최대 (ms) | >25ms | 프레임 |
|---|---:|---:|---:|---:|
| 수정 전 `1db05f0` 1회 | 33.3632 | 50.2901 | 38.308% | 851 |
| 수정 전 `1db05f0` 2회 | 33.0361 | 50.8632 | 12.147% | 1,062 |
| 수정 후 `f093839` 1회 | 32.8205 | 50.7577 | 6.893% | 1,117 |
| 수정 후 `f093839` 2회 | 32.5009 | 50.8390 | 7.175% | 1,115 |

수정 후 프레임 비용 증가는 측정되지 않았다. >25ms 비율은 같은 앱에서도 크게 흔들려 개선으로 해석하지 않는다. 결합 성능 관문(p95 33ms 실패)은 이번 범위가 아니며 여전히 실패다. 결합 장면 동시 처치 연출 최대 26개(상한 64)·초과 0.

## 재생성

```sh
UNITY_WAVE_BENCHMARK=1 UNITY_CONTENT_PROFILE=wave-1a UNITY_MAC_OUTPUT=<새 폴더> bash tools/build-mac.sh
node tools/m1-production-review.mjs combat-plans <새 폴더> <24개 case 폴더>
node tools/m1-production-review.mjs run <plan> "<앱>"
node tools/m1-production-review.mjs weapons <plan> <새 출력>
node tools/m1-knight-measure.mjs run <결합 plan> "<앱>"   # 계획 형식은 m1-256 retry2와 같음
```

영웅 피격 비교는 같은 `benchmarks/m1-254/dense-approach-candidate2.ssreplay`를 `--knight-review --camera-scale 1.4 --camera-scale-window damage --knight-damage-replay --offline-replay-capture --offline-replay-audio`로 두 앱에서 촬영했다. 두 실행의 실제 영주 피해 기록 160건은 같다.
