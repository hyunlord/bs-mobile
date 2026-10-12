![같은 입력의 구운 모션과 실시간 뼈대](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/baked-vs-live-tick11200.png)

관문: 부분 — 모션 두 방식의 네이티브 재생·비용 비교와 C05 800+ 표본 검증 완료. 영웅·모션 방식 선택은 사용자에게 남아 있고, 타격감 실시간 영상은 60Hz 누락 실패 표본이다.

[나란히 영상](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/baked-vs-live-offline-60fps.mp4)은 **오프라인 렌더·60fps·무음**이다. [영웅 5종 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/hero-five-native-390pt.png)는 기존 A/B와 새 후보 3안을 비교한다. 어떤 후보나 제작 방식도 대신 선택하지 않았다.

## 출처와 판정 경계

도전의 실제 청구 가능한 `받기`는 금빛 강조로 구별한다. `8df1cec` Editor PlayMode에서 청구·재시작·중복 지급 방지 검사 1/1을 다시 통과했고, [390pt 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/challenge-claim-editor-8df1cec.png)을 남겼다. 계정 시험 상태의 실제 Editor UI이며 정상 플레이로 달성한 기록이나 실기 증거가 아니다.

- Mac 앱·최종 모션 영상·비용: 깨끗한 `8097c5c23ab84715908a7f10cc1d8aebd13d253e`, sourceHash `55050E9A6380538377F2260B86E3C50DEAC6309064B01486C5C15061DB135824`, Unity 6000.6.4f1, ARM64 Mono Release.
- [PR248](https://github.com/hyunlord/bs-mobile/pull/248) 및 [CI38209242180](https://github.com/hyunlord/bs-mobile/actions/runs/38209242180)의 소스는 `8df1cec1f673fb6fb25d52e77bf562f8b6c15959`다. 8097 이후 차이는 Editor 전용 카메라 테스트 접근 경로 수정이며 새 Player를 만든 것이 아니다. CI 커밋을 앱 커밋으로 표시하지 않는다.
- (b)/(c)는 현재 B의 동일 부위·검·동작 키를 사용한다. (c)는 영웅 1명에 무료 Unity 2D Animation의 실제 SpriteSkin을 사용하고 적은 구운 방식이다. 새 영웅 외형 선택은 별도다.
- 초반 각 1,800장, 진화 각 1,460장, 총 네 캡처가 예상 프레임 수와 일치했다. 네 실행 모두 원본 나머지 입력까지 검증한 최종 해시는 `59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619`다.
- 역사 411마리 재생은 구형 지도·약한 빌드 입력이다. 비교 전용 카메라는 지도 끝에서 영웅이 잘리는 문제를 보정한다. 현재 끝없는 들판의 일반 플레이·자연스러움·실기 성능을 이 표본으로 대신하지 않는다. 14장 육안 표본에서는 영웅 잘림·분홍 셰이더·팔다리 누락이 없었다.

## 캡처 없는 모션 ABBA

같은 입력 10,800–11,530틱, 각 약 24.33초. 1920×1080 창 가운데 세로 전장, 120Hz 화면의 vSyncCount=2/targetFrameRate=-1, Metal, 비교 계측 ON·캡처 OFF. 전경 유지·경계가 아닌 프레임만 집계했다. 수치는 [정본 요약 CSV](../metrics/m1-246/motion-abba.csv)에 있다.

| 순서 | 장 p95(ms) | >25ms | 화면 구성 p95(ms) | GPU p95(ms) | Unity 할당 중앙값(MiB) | Gen0/초 |
|---|---:|---:|---:|---:|---:|---:|
| A1 구움 | 16.786 | 0.000% | 2.180 | 0.474 | 171.75 | 0.206 |
| B1 실시간 | 17.586 | 0.554% | 2.142 | 0.480 | 211.25 | 0.205 |
| B2 실시간 | 17.006 | 0.206% | 2.142 | 0.480 | 210.81 | 0.205 |
| A2 구움 | 16.979 | 0.069% | 2.173 | 0.447 | 172.25 | 0.206 |

실시간 관절 적용 p95는 0.0079/0.0078ms지만 SpriteSkin LateUpdate 마커는 수집 불가다. 이 수치를 스키닝 전체 비용으로 쓰지 않는다. 구운 방식의 전용 포즈 타이머도 없으며 0ms가 아니다. 메인 스레드 시간은 대기를 포함하고 GPU/렌더 스레드는 비동기 최신 관측이다. 메모리는 각 24개 표본의 Unity 할당량이며 RSS가 아니다. 실시간 방식에서 긴 프레임 비율이 높았지만 이 짧은 두 쌍만으로 원인이나 최종 방식을 확정하지 않는다.

제작량·빌드 크기는 [07 정본](../design/gdd/07-art-character.md)에 있다. 같은 앱에 두 방식이 모두 들어 있어 설치 크기는 같고, 방식별 전용 빌드는 미측정이다. 358,978,067바이트의 전체 BuildReport는 d12 대비 29,835,496바이트 증가했지만 다른 M1 변경도 포함한다.

## 타격감 C05 회귀

캡처·진단·모션 계측 없이 Release, 고정 C05 입력, 1600×900, targetFrameRate=60/vSync=0. 밀도는 실제 enemies 열로 분류했다. d12 역사판 1회와 같은 8097 앱의 효과 OFF/ON 각 1회이며 반복 ABBA가 아니다. [정본 CSV](../metrics/m1-246/impact-c05.csv)는 네 밀도 묶음을 모두 보존한다.

| 실행 | 구간 | 장 p95(ms) | p99(ms) | 최대(ms) | >25ms |
|---|---|---:|---:|---:|---:|
| d12 역사판 | 세 구간 합계 | 17.152 | 17.586 | 49.003 | 0.03046% |
| 8097 효과 OFF | 세 구간 합계 | 17.229 | 17.594 | 33.741 | 0.01524% |
| 8097 효과 ON | 세 구간 합계 | 17.208 | 17.588 | 49.522 | 0.03048% |
| d12 역사판 | 800+ | 17.128 | 17.588 | 23.945 | 0% |
| 8097 효과 OFF | 800+ | 17.166 | 17.589 | 24.049 | 0% |
| 8097 효과 ON | 800+ | 17.141 | 17.577 | 23.629 | 0% |

800+는 세 실행 모두 >25ms 0건으로 이번 표본의 비악화 조건을 만족한다. 합계는 선택한 세 측정 구간 약 220초이며 15분 판 전체가 아니다. 합계 ON은 OFF보다 긴 프레임이 2→4건이며 개선이라고 부르지 않는다. 이번 최대가 50ms 미만이지만 #223의 반복 관문 완료·원인 해결을 뜻하지 않는다. 역사판과 최종판은 dataHash·입력·상태 해시·그래픽 설정을 맞췄다. 전경 관측에는 시작 전 Chrome도 포함되며 집계는 앱 내부 전경 유지 구간만 썼다.

타격감 비교 실시간 영상은 `6e393a9`의 실제 앱 내부 녹화다. 두 `capture-result.json` 모두 `status=failed`, 원인은 60Hz 슬롯 누락이다. 원본 타임스탬프를 보존한 VFR 실패 표본이며 정상 60fps 관문이나 8097 최종판 영상으로 제시하지 않는다. [OFF 실패 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/impact-baseline-capture-failed-60hz-sample-vfr.mp4)·[ON 실패 표본](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/impact-applied-capture-failed-60hz-sample-vfr.mp4). 사용자 질문은 그대로다: **소리를 끄고 봐도 맞았다는 게 느껴지나?**

## 재생성

아래 명령은 저장소 루트에서 실행한다. `APP`, `CROWD`, `OUT`은 실제 앱·원본 입력·새 출력 디렉터리로 지정한다. 모든 실행을 직렬로 하고 화면 녹화·손쉬운 사용 권한은 필요 없다. 모션 입력 SHA256은 `4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c`, C05는 `f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5`다.

```sh
APP='/absolute/Sow and Siege.app'
CROWD='/absolute/rules-off-six-minute-failure-8c94ce2.ssreplay'
OUT='/absolute/new-review-output'
mkdir -p "$OUT"
# 같은 명령을 baked, live, live, baked 순으로 새 출력 이름에 실행한다.
"$APP/Contents/MacOS/Sow and Siege" \
  --wave-benchmark "$CROWD" --wave-benchmark-output "$OUT/a1-baked" \
  --crowd-replay-benchmark --hero-motion baked --hero-motion-window evolved \
  --hero-motion-metrics "$OUT/a1-baked-metrics" \
  -screen-width 1920 -screen-height 1080 -logFile "$OUT/a1-baked.player.log"
# 영상은 별도 실행에 --offline-replay-capture를 붙인다.
# 초반 영상은 --hero-motion-window early, 진화 영상은 evolved다.

"$APP/Contents/MacOS/Sow and Siege" \
  --wave-benchmark "$PWD/benchmarks/wave-c05/run.ssreplay" \
  --wave-benchmark-output "$OUT/c05-on" --wave-benchmark-mode capped \
  -screen-width 1600 -screen-height 900 -logFile "$OUT/c05-on.player.log"
# 같은 8097 앱의 OFF 비교에만 --impact-feedback-baseline을 붙인다.
# d12 역사판은 앱 경로만 바꾸며 해당 플래그를 붙이지 않는다.
```

실행 중 게임을 전경에 두고 전경을 가져간 앱 이름을 추가 권한 없는 NSWorkspace 관측기로 기록한다. 이번 관측기는 `.omo/evidence/observe-foreground`였으며 로컬 보조 도구일 뿐 앱 실행의 필수 의존성이 아니다. 재측정에서 외부 관측을 하지 못하면 그 한계를 기록한다. 앱 `benchmark.json`의 verified/performanceEligible 및 Release 여부를 확인하고, `frames.csv`에서 focused=1·partialBoundary=0·해당 tick 범위만 사용한다. C05 범위는 7200–9900/10200–12900/13200–14400, 모션은 10800–11530이다. p95/p99는 정렬 후 ceil(p×개수)번째 값, >25ms 비율은 초과 프레임/유효 프레임이다. 밀도는 tick 구간 이름 대신 enemies로 분류한다. 비동기 GPU 관측을 같은 프레임의 원인으로 연결하지 않는다.

로컬 원자료: `artifacts/m1-246/native-8097c5c/`, `motion-metrics-8097c5c/`, `impact-performance-8097c5c/` 및 `.omo/evidence/m1-historical-final.json`. 성공 실행 전체 원자료를 Release에 중복 업로드하지 않는다. Release에는 선택용 네이티브 영상·영웅 표본·명시된 실패 표본과 앱만 둔다.

판정은 부분이다. 영웅 1안과 (b)/(c)는 사용자 선택 전까지 미정이며 #126·#98·1b·#224 보류, 광고·결제·분석 SDK 미도입을 유지한다.

[Mac 앱](https://github.com/hyunlord/bs-mobile/releases/download/m1-1-8097c5c/Sow-and-Siege-M1-8097c5c-macOS-arm64.zip) · [Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-1-8097c5c) · [앱 커밋](https://github.com/hyunlord/bs-mobile/commit/8097c5c23ab84715908a7f10cc1d8aebd13d253e) · [PR248](https://github.com/hyunlord/bs-mobile/pull/248) · [CI](https://github.com/hyunlord/bs-mobile/actions/runs/38209242180)
