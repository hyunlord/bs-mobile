# Character Forge F1/F2 게임 걷기 검토

2026-10-15 사용자 승인 범위: F0 v5 몸 걷기·상체 스키닝·어깨 부착을 유지하고, 의상과 한 방향 걷기만 실제 게임 화면에서 검토한다. 시각 판정은 사용자 영상 승인 전까지 **부분**이다.

## 실행 계약

`--forge-walk-review`는 외부 Character Forge의 한 방향(E) 12~16장 걷기 아틀라스를 검토하는 명시적 옵션이다. 일반 실행에는 켜지지 않는다. 다른 모션·전투·성능 검토 옵션과 섞을 수 없다. 기존 C05 고정 해시 계약은 유지한다.

현재 `wave-1a` 카탈로그, seed 30001, 900틱(30초)의 일정 동쪽 입력 `(1000,0)`을 쓴다. 빠른 걸음은 현재 기본값대로 켜고, 실제 획득한 카드만 첫 번째 제안으로 선택한다. 무적·적/자원 부여·규칙 변경은 없다. 데이터·재생·최종 상태 해시를 확인한다. Core와 실행 데이터는 변경하지 않는다.

카메라 1.4배, 실제 잔디 지형과 적, 고정 60fps 앱 내부 오프라인 렌더와 게임 소리를 기록한다. 화면 합성이 아니다. 확대본은 같은 프레임의 영주 위치를 따라 자른다. 캡처 비용을 성능 증거로 사용하지 않는다. 걷기만 저작했으므로 공격·피격·쓰러짐·다른 방향 자세는 미제작이며 이를 승인된 동작으로 표현하지 않는다.

## 재생성

```sh
bash tools/forge-walk-fixture.sh "$PWD/artifacts/forge-walk/fixture"
UNITY_CONTENT_PROFILE=wave-1a UNITY_WAVE_BENCHMARK=1 \
  UNITY_MAC_OUTPUT="$PWD/artifacts/forge-walk/mac" bash tools/build-mac.sh
"$PWD/artifacts/forge-walk/mac/Sow and Siege.app/Contents/MacOS/Sow and Siege" \
  --wave-benchmark "$PWD/artifacts/forge-walk/fixture/forge-walk.ssreplay" \
  --wave-benchmark-output "$PWD/artifacts/forge-walk/capture" \
  --forge-walk-review \
  --hero-production /Users/rexxa/character-forge/outputs/f2_bake/manifest.json \
  --hero-production-direction E --offline-replay-capture --offline-replay-audio \
  -screen-width 1920 -screen-height 1080
```

모든 출력 디렉터리는 새 경로를 사용한다. 외부 Forge 원본 BLEND·FBX·BVH와 비공개 소스는 업로드하지 않는다. 검토 영상·구운 그림과 작은 provenance 기록만 Release에 남긴다. Forge 폴더는 별도 로컬 작업이며 이 PR은 재사용 검토 경로만 소유한다.

## 의상 출처와 보존

MPFB 공식 CC0 의상: `toigo_wool_pants`, `culturalibre_hero_boots_1`, `toigo_gloves_medium`, `cortu_short_messy_hair`. 코드 GPL과 결과물/선택 자산 CC0를 구분한다. 원문과 21개 파일 SHA는 외부 Forge의 `docs/licenses/f1-assets.md`, `evidence/f1-assets/manifest.json`에 기록한다. GarmentCode 상체·앞뒤 자락과 MPFB 몸, 원본 UAL 걷기를 재사용한다. 앞뒤 자락 3뼈, 양어깨 망토 4뼈로 변형하고 원본 전체 80프레임 주기에서 16장을 굽는다.

F0 실패 기록과 원본은 그대로 보존한다. 이번 옷차림/걷기 사용자 승인 전 베기·피격·8방향 제작을 시작하지 않는다. #126·#98·1b·#224 보류와 SDK 미도입을 유지한다.
