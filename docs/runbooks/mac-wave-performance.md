# wave-1a Mac 혼잡 성능 (#169)

목표는 후반 적400–800+ 장면의 프레임 p95≤16.7ms다. 적 수·스폰·밸런스는 #126 보류이고 Fold7 #98에는 연결하지 않는다. Core 틱 시간, Editor Recorder 시간, Mac 실제 프레임 시간은 별도 증거다.

## 진단 추적

Development Mac 또는 Editor에만 `--diagnostic-trace-tick 20250`을 전달한다. 처음으로 해당 틱 이상이며 기록 중·실행 중·1배속·비일시정지인 프레임에서 한 번만 기존10초 프로파일러 추적을 시도한다. 다른 프로파일러가 점유하면 거부하고 반복 시작하지 않는다. 포커스 상실·중단·종료는 기존 정리 경로를 따른다. 음수·빈 값·중복·정수 범위 밖 값은 거부한다. 옵션 없는 실행은 자동 추적하지 않으며 Release 빌드에는 추적 클래스와 호출이 컴파일되지 않는다. 추적 중 스톨 때문에 실제10초를 넘길 수 있다.

실행 소스·dataHash·seed·입력·해상도·Mac 모델·OS·Mono/Metal·targetFPS/vSync와 진단 틱 범위를 먼저 기록한다. Unity·빌드·Recorder는 한 소유자가 직렬 실행한다. Deep Profiling을 켜지 않는다. 실제 기록의 `diagnostic-*.raw`와 메타데이터를 기존 [추적 분석](phase1b-playtest.md#진단-raw의-editor-분석)으로 읽는다. 중첩 inclusive 합계를 더하거나 GC 정보 누락을0으로 취급하지 않는다.

진단용 앱은 Release 전용 `tools/build-mac.sh` 대신 `FoundationBuild.MacDiagnostic`으로 만든다. 아래는 저장소 루트에서, 다른 Unity가 종료된 뒤 실행하며 출력 경로는 새 디렉터리를 쓴다. 이 Development 앱의 프레임 시간은 최종 Release 성능 승인에 사용하지 않는다.

```sh
set -e
export PATH="$HOME/.dotnet:$PATH"
export UNITY_CONTENT_PROFILE=wave-1a
export UNITY_MAC_PATH="$PWD/artifacts/polish/mac-diagnostic-01/Sow and Siege.app"
test ! -e "$(dirname "$UNITY_MAC_PATH")"
mkdir -p "$(dirname "$UNITY_MAC_PATH")"
bash tools/prepare-unity.sh
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll \
  interactive-fixtures data artifacts/phase1b/replays wave-1a
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD/unity" -buildTarget OSXUniversal \
  -executeMethod Game.Editor.FoundationBuild.ConfigureMac \
  -logFile "$(dirname "$UNITY_MAC_PATH")/configure.log"
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD/unity" -buildTarget OSXUniversal \
  -executeMethod Game.Editor.FoundationBuild.MacDiagnostic \
  -logFile "$(dirname "$UNITY_MAC_PATH")/build.log"
bash tools/prepare-unity.sh --verify
```

앱 실행 인자에 `--diagnostic-trace-tick 20250`을 추가하고 기존 정상 입력 자동 실행 옵션은 그대로 유지한다. `mac-build-result.json`의 `development:true`와 해당 실행의 추적 메타데이터를 확인한다. 최종 성능 표본은 별도로 `UNITY_CONTENT_PROFILE=wave-1a tools/build-mac.sh`의 Release를 사용한다.

## 프레임 분석

```sh
node tools/mac-wave-frame-window.mjs <실제-run-디렉터리> --commit <40자리-앱-소스-SHA>
node --test tools/test-mac-wave-frame-window.mjs
```

입력은 `recording.json`, `device.json`, `frame-summary.json`, `frames.csv`이다. Mac Mono·소스·build·dataHash·세션 일치와 CSV 전체 행 수·프레임 순서·숫자를 확인한다. 기존 Fold7 분석기는 그대로 보존한다.

출력은 전체와 후반20250–27000틱 통계, 후반400–599/600–799/800+ 각각의 표본·p95·최대·연속 구간 및400+ 전체를 분리한다. 적은 구간이 섞인 전체 후반 수치만으로 혼잡 통과를 주장하지 않는다. `speed=1`, `paused=suspended=partial=0`만 통계에 포함하고, 제외 행과 밀도 이탈은 연속 구간을 끊는다. 느린 유효 프레임은 모두 보존한다. p95는 nearest-rank다.

`denseLateTarget`은 전체 후반 끝까지 유효한 기록이 있고400+ 연속60초 이상일 때만 `within-target`/`over-target`, 아니면 `incomplete`다. 이는 분석용 사전 기준이며 새로운 밸런스 판정이 아니다. 도달하지 않은 밀도 묶음은 표본0·p95=null이고 통과하지 않는다. CSV는 프로파일 선택·프로파일러 꺼짐·녹화 여부를 독립 증명하지 않으므로 `within-target`도 그 자체로 출시 관문 승인은 아니다. 깨끗한 Release·일반1배속·프로파일러/Recorder 없음·정상 입력·시계 진단 출처를 같이 검토한다. 화면 판독은 별도의 혼잡 일반 녹화로 검증하며 재생 영상만으로 통과하지 않는다.

개선 후보는 실제 profiler의 CoreApply/Capture*/WorldPresent/Hud/Telemetry 비용과 GC/렌더 대기를 보고 고른다. Core 변경은 기존 재생의 순서·동률 처리·이벤트·종료 해시와 두 실제 .NET 타깃 DLL을 보존한다. 무기·도구 데이터나 출현 수를 줄여 성능을 맞추지 않는다. 성공 전체 원자료나 이 출력 JSON을 Release에 중복 업로드하지 않고, 사전 선정 표본·실패만 남긴다.

## 첫 추적과 조회 비용 수정

변경 전 Development 진단(게임 규칙03c5f22, 진단 빌드93a3f1d, seed30001, 9000–9239틱)은 약180개 적의385프레임이다. 경고0개였으며400+ 관문 표본이 아니다. 주 스레드의 `WaitForTargetFPS`는 프레임당 같은 이름의 중첩 표본 최댓값으로 평균21.548ms였다. inclusive 표본을 합산하지 않는다. `WorldPresent` 호출 평균0.722ms/p950.818ms, `CoreApply` 호출 평균1.203ms/p951.687ms였고 렌더 대기를 GPU 연산 비용으로 해석하지 않는다.

이 추적에서 확인한 렌더 구간의 반복 작업 조회를 줄인다. 불변 스냅샷마다 작업ID와 미완성 건물 위치의 첫 일치를 한 번 인덱싱하고, 수리 앵커 제거 목록을 재사용한다. 새 스냅샷·카탈로그·null에서 캐시를 갱신하며 중복ID의 첫 일치 순서와 실제 제출된 지붕 그림을 보존한다. 대상 수·스폰·피격 규칙은 바꾸지 않는다. 이는 비용 제거의 구현 기록이며, 혼잡한 Release p95 개선량은 별도 측정 전까지 미확인이다.
