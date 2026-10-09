# Phase1B 첫 판·설정·오디오 QA

이 문서는 실행 절차이며 관문 통과 보고가 아니다. First Playable은 `first-playable` 프로필·30Hz·27000틱(15분)을 사용한다. 아래 M3 Editor 검증과 M4 폴드7 실측을 별도로 기록한다. 기본 기록·원자료 경로는 [기기 기록 런북](device-play.md), 오디오 출처·청취 절차는 [오디오 원자료](../licenses/assets.md)를 따른다. 구 Phase1A 런북의21600틱·`phase1a/replays` 경로를 이번 판에 적용하지 않는다.

실기 관문 이후 사용자에게 전달할 목록은 [첫 플레이 가능판에서 해 볼 것](first-playable-player-checklist.md)에 있다. 아래 개발 QA 절차를 사용자의 첫 판 안내로 대신 전달하지 않는다.

## 첫 판과 재시작

1. 새 QA 환경에서 타이틀 **시작 → 개척 시작**으로 진입한다. 첫 안내를 닫기 전에는 판이 시작되지 않아야 한다. 기존 사용자의 설정을 지우지 않는다. 자동 테스트는 `RunPreferenceScope`로 해당 테스트 설정만 임시 보관·복원한다.
2. 빈 화면을 끌어 이동한다. 공격은 자동이다. **이동 / 카드 / 익은 밭 수확 / 징집** 힌트가 각 상황에서 짧게 표시되고 조작·선택을 가리지 않는지 확인한다. 실제 화면에 표시된 힌트만 저장되며, 재시작 후 본 힌트가 반복되지 않아야 한다.
3. 레벨업에서 무기·도구·특허장 카드의 아이콘, 이름, 한 줄 효과, 현재→다음 변화, 희귀도와 진화 조건을 확인한다. 고정·리롤·금지 후 선택 대상과 표시가 일치하는지 본다.
4. HUD의 체력·경험치·계절·남은 시간·장비 아이콘을 읽고 익은 밭과 백성의 작업/징집 상태를 구별한다. 세로·정사각 화면 모두 safe area, 스크롤 끝의 버튼, 텍스트 잘림을 확인한다.
5. 종료 후 결산에서 생존·보스 결과, 레벨, 처치·수확, 경험치 출처 비율, 무기/도구 피해와 최종 빌드를 확인한다. **다시 하기**가 새 판을 시작하고 지난 이벤트·힌트·입력을 남기지 않는지 확인한다. 정상 종료와 사망→재시작은 별도 사례로 남긴다.

## 설정과 복귀

| 조작 | 확인할 결과 |
|---|---|
| 타이틀·판·카드·결산에서 설정 열기/돌아가기 | 원래 화면으로 복귀. 카드 선택을 기다리는 동안 설정이 카드 화면으로 덮이지 않고, 제시 카드도 바뀌지 않음 |
| 배경음·효과음 슬라이더 각각0/중간/최대 | 각 음량만 바뀜. 설정·카드에서는 음악이 계속되고 레벨업 소리가 잘리지 않음 |
| 진동 끄기 | 피격·보스 상황에서 진동 없음. 켜짐 확인은 실제 기기로 수행 |
| 화면 흔들림·피해 숫자 끄기/켜기 | 실제 전투 표시가 즉시 반영됨. 게임 규칙이나 피해량은 바뀌지 않음 |
| 이동 방향 ↔ 가까운 적 자동 조준 | 판 안 변경은 Core 명령으로 기록되며, 설정 복귀·앱 재실행에도 선택 유지 |
| 백그라운드/포커스 상실 후 복귀 | 게임 입력과 소리가 일시정지·복원되고 복귀 직후 누적 입력으로 순간 이동하지 않음 |

설정은 `sowsiege.fp.v1.*` PlayerPrefs 키에 저장한다. 기본 배경음0.55, 효과음0.8, 진동/흔들림/피해 숫자 켜짐, 이동 방향 조준이다. 앱을 다시 띄워 변경값과 첫 안내·본 힌트 상태가 유지되는지 확인한다.

## 숨겨진 개발 도구

Editor 또는 Development Build에서만 **F12**로 개발 패널을 여닫는다. 일반 화면에는 개발 패널 실행 버튼이 없다. 폴드7의 M3 검증용 Development APK에서 `adb shell input keyevent 142`로 실제 열림/닫힘을 확인했다. 앱 시작·씬 로드가 끝난 뒤 사용한다. Release에서는 개발 코드·fixture가 제외되어야 한다.

패널의 배속·레벨 지급·무적·생성 배수는 재현/부하 진단 전용이다. 열린 패널은 게임을 멈춘다. 이 기능을 사용한 판은 정상 플레이 관문에 포함하지 않는다. **기기 재생5개 검증**도 게임을 멈추고 정확성 fixture를 실행하며, 실제 플레이3판을 대신하지 않는다.

## 자동 검증과 소리의 증거 범위

- `tools/check-unity.sh`는 현재 FP bridge, `artifacts/phase1b/replays`의30000–30004 seed, Editor EditMode/PlayMode와 Android 빌드를 준비·검사한다. 실행 담당자가 Unity 프로세스를 단독 소유하고 로그/XML·정확한 커밋을 보관한다.
- `FirstRunFlowTests`는 실제 UI 경로와 저장/복귀를 다루지만 레벨 지급 등 테스트 조작을 포함한다. `UiCaptureTests`·`FirstPlayableWorldCaptureTests`의 Editor 캡처는 합성 상태·무적/가속 부하를 포함하는 화면 검사이며 정상15분 실기 증거가 아니다.
- `FirstPlayableAudioTests`는 합성 이벤트로 실제 AudioSource 라우팅·중복 방지·설정을 검사한다. `FirstPlayableAudioOutputTests`는 미리 준비한1024샘플 master 이력 창에서7cue·mute·믹스 peak와 음악2주기 반복을 관찰한다. DSP 불능이나 양성 대조 무음은 실패이지 통과/skip 사유가 아니다.
- master RMS/peak 표본은 연속 녹음·실제 스피커 청취·음악의 미적 평가가 아니다. 실제 출력으로 타격/처치/수확/레벨/진화/피격/보스7개를 듣고, 음악2주기 경계·전투 폭주 중 중요 신호·최대 음량·mute를 별도로 확인한다.

## M4 대기 관문과 원자료

M3 Editor 검사로 다음 항목을 통과 처리하지 않는다.

- 폴드7에서1배속·무적/레벨 지급/생성 조작 없이 서로 다른 정상15분 판3회. 무기/도구/혼합 빌드, 두 조준 방식, 별도 사망·재시작을 실제 입력으로 실행하고 자동 조작 여부를 밝힌다.
- 사전 선언한 연속60초·1배속 구간의 모든 측정 프레임에서 살아 있는 적500마리 이상, 프레임 p95≤16.7ms. 최고 개체 수만500이거나 낮은 개체 수/끊김 표본을 제외한 결과는 인정하지 않는다.50ms 초과 프레임·열 상태·원본 CSV를 보존한다.
- 정확히 같은 FP replay bytes의 .NET/실제 Unity Mono/Android IL2CPP5seed 해시 대조와 실제 플레이 기록의 CLI 재생. 별도 .NET 호스트에서 netstandard DLL을 실행한 결과는 실제 Unity/Android 증거가 아니다.
- 실제 초반·중반·후반·보스·결산 캡처와 각각30초 이상 `adb shell screenrecord` 영상2개. 물리 접기/펼치기 #83은 사용자 조작 대기로 유지하며 화면 비율 검사로 대체 통과하지 않는다.

기기 원자료는 익명 sessionId별 `Application.persistentDataPath/runs/`에 보존한다. FP 기록 재검증은 `dotnet run --project core/src/SowSiege.Sim -- interactive-replay data <run.ssreplay> first-playable`을 사용한다. 기존 `device-metrics.mjs`의 final-quarter 지표는 연속60초500+ 관문과 별개다. 공개 원자료는 실패 사례·사전 선정 표본과 요청 영상/캡처만 Release 태그에 올리고, CSV에서 재생성 가능한 요약·HTML·ZIP·CRC·전달 영수증을 추가하지 않는다. 보고는 관문 결과 첫 줄과 커밋·PR·CI 링크로 남긴다.

## M4 현재 보류 범위

사용자 요청으로 **연결된 폴드7을 사용하는 모든 작업을 보류한다**. 설치·입력·진단 수집·실기 성능·정상3판·기기 parity·화면 녹화·최종 Release APK 실기 검사는 재개 지시 전까지 실행하지 않는다. 아래 명령은 이후 재현 절차이며 새 실행/통과 주장으로 읽지 않는다. 기존 실기 관측은 보존하되 미완료 관문을 대신하지 않는다. 비기기 소스 검사·Editor 분석·APK 생성은 별도로 진행할 수 있다.

## 진단 raw의 Editor 분석

Development 메뉴의 **진단 추적10초 시작**은 메뉴를 닫고 별도 프로파일러 수집을 시작한다. 수동 중지·시간 한도·포커스 상실·중단·종료로 닫힌 `diagnostic-*.raw`와 인접한 `.raw.json`을 함께 보존한다. 진단 출현×10은 기존 기록 가능한 Core 명령이며 정상 판과 분리한다. 실제 수집 시간은 스톨로10초를 넘을 수 있다.

이미 확보한 raw를 분석할 때 실행 중인 Editor/프로파일러를 먼저 종료하고, **존재하지 않는 새 출력 디렉터리**를 지정한다. 저장소 루트에서:

```sh
UNITY_PROFILER_TRACE="$PWD/artifacts/phase1b/m4-diagnostic/trace-01/diagnostic-<id>.raw" \
UNITY_PROFILER_EXPORT="$PWD/artifacts/phase1b/m4-diagnostic/export-02" \
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD/unity" \
  -executeMethod Game.Editor.ProfilerTraceExport.Run -logFile /tmp/p1b-m4-export.log
```

일반 Editor에서는 **Sow and Siege → Export diagnostic profiler trace**도 사용한다. `frames.csv`·`samples.csv`·`export.json`·원본 `trace-provenance.json`이 생성된다. 모든 캡처 스레드의 엔진/대기/렌더 샘플을 보존하며 이름이 없는 샘플은 빈 이름과 `unnamedSamples`로 남긴다. 중첩 inclusive 시간을 합산하거나 누락된 GC 메타데이터를0바이트로 읽지 않는다. 이 출력의 profiler frame 인덱스는 실기 텔레메트리 행 ID가 아니다. 원본 SHA·출처 검증과 필수 마커 관측은 분석 가능성 검사이며 병목 원인·성능 관문 통과를 자동 증명하지 않는다.

## 지속500 관문의 엄격한 분석 명령

`tools/first-playable-frame-window.mjs`는 실제 `recording.json`, `device.json`, `frame-summary.json`, `frames.csv`와 정확한 사전등록 선언 바이트를 요구한다. 선언 스키마는 추가 필드도 거부한다:

- 최상위: `schemaVersion:1`, `identity`, `profile:"first-playable"`, `mode:"stress"|"normal"`, `startTick`, `posture`, 비어 있지 않은 `setup`, `capture`, `preregistrationReference`.
- `identity`: `sessionId`, `seed`, `build`, `dataHash`, `sourceHash`, `sourceDirty`, `model`, `os`, `unityVersion`, `backend`. 실제 기록과 같아야 하며 기기는 `SM-F966N`, backend는 Android IL2CPP여야 한다.
- `capture`: boolean `screenRecording`, `replayRecording:true`, `profilerRecording:false`, `deepProfiling:false`.

측정 구간이 시작되기 전에 실제 session 식별자로 선언을 확정하고 외부 시각 기록에 파일 SHA256과 참조를 남긴다. 선언된 시작 틱보다 앞선 원자료도 보존한다. 분석 시 SHA를 다시 계산해 사전등록을 대신하지 않는다. 사전등록한64자리 값을 아래에 넣는다:

```sh
node tools/first-playable-frame-window.mjs \
  artifacts/device-runs/<sessionId> artifacts/phase1b/m4-declaration.json \
  --declaration-sha256 <externally-preregistered-64-hex-sha256> \
  --output artifacts/phase1b/m4-window-result.json
```

출력 파일은 새 경로여야 한다. 선언 시작 틱에 도달한 첫 행부터 누적60000ms를 넘기는 행 전체까지 검사하며 저개체 수·정지·중단·부분 구간·가속·느린 행을 제외하지 않는다. 실패도 결과로 저장하고 종료 코드1을 반환한다. 선언 자체의 시각/프로파일러 꺼짐/녹화 여부는 외부 증거와 대조한다. 분석기는 무치트 정상 판이나 리플레이 정확성을 인증하지 않는다.

## 최종 Release와 개발 fixture parity의 분리

최종 사용자 APK는 bridge 준비·전체 검사 후 별도 출력 폴더에 Development 옵션 없이 생성한다:

```sh
UNITY_APK_PATH="$PWD/artifacts/phase1b/release/sow-siege.apk" \
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD/unity" -buildTarget Android \
  -executeMethod Game.Editor.FoundationBuild.AndroidRelease -logFile /tmp/p1b-m4-release-build.log
```

같은 폴더의 `build-result.json`에서 `development:false`·IL2CPP·ARM64와 실제 APK 크기를 확인한다. Release에는 개발 메뉴/진단 캡처와30000–30004 fixture가 제외되지만 실제 판 기록은 유지된다. 빌드 성공은 설치·조작·실기 성능을 증명하지 않는다.

`tools/check-unity.sh` 및 `FoundationBuild.Android`는 별도 **Development** 검증 경로다. 정확한 `artifacts/phase1b/replays/30000–30004.ssreplay` 바이트를 개발 빌드에만 패키징하여 Editor Mono와 개발 메뉴 **기기 재생5개 검증** 결과의 입력 SHA·27000틱·상태 해시를 비교한다. 원자료는 `Application.persistentDataPath/parity/device-parity.json`이다. Development parity를 최종 Release APK 실행 검사나 정상3판으로 대체하지 않는다. 현재 사용자 보류에 따라 기기 parity와 최종 APK 실기 확인도 실행하지 않는다.
