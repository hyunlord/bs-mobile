# wave-1a 동작 확인

`wave-1a`는 D3 v1.1에서 승인된 첫 수직 슬라이스다. 기존 first-playable 실행과 저장은 별도로 유지한다. 이 문서는 실행 순서이며 관문 통과 보고가 아니다.

## 빌드와 입력

```sh
UNITY_CONTENT_PROFILE=wave-1a UNITY_MAC_OUTPUT="$PWD/artifacts/wave1a/mac-new" bash tools/build-mac.sh
```

빌드 출력은 새 빈 경로를 사용한다. 실행판에서 WASD 또는 드래그로 이동하고 실제로 제시된 성장 카드를 선택한다. 출정 전 목표 재료에 따라 관련 도구가 첫 카드 묶음에 보장된다. 현재 슬라이스는 곡물·목재·특허 재료 목표를 제공한다. 철 관련 도구는 후속 웨이브이므로 이번 선택에 넣지 않는다. 장원 경제와 가신 성장의 기존 메타 처리를 이 프로필에 자동 적용하지 않는다.

자동 입력은 앱에 `--autoplay-capture --capture-output <새 경로>`를 전달한다. `--capture-seed <정수>`와 `--capture-priority <쉼표로 구분한 ID>`는 정상 카드 제안 안에서 선택과 이동만 바꾼다. 무적·경험치 지급을 하지 않는다. 사람의 클릭 확인은 사용자가 맡으며 화면 기록·손쉬운 사용 권한을 요구하지 않는다.

## 실제로 달라져야 하는 장면

| 장면 | 관찰할 동작 | 증거로 세지 않는 것 |
|---|---|---|
| 무기 다섯 형태 | 전방 검, 움직이는 수호 파편, 젖은 적을 잇는 번개, 이동 유도탄, 익은 밭의 낫 파편 | 이름·아이콘만 다른 원형 피해 |
| 씨앗 자루 | 파종·성숙 후 접촉 수확과 XP 회수 | 성숙과 동시에 자동 XP 지급 |
| 빗물 바가지 | 유한 물을 실제 마른 밭에 공급해 성장 재개·XP | 물을 쓰지 않거나 이미 젖은 밭의 반복 보상 |
| 목수 망치 | 반경 안 이동 중 건설·유한 목재 출고·XP | 가만히 서 있어야만 진행하거나 무한 출고 |
| 뿔나팔 | 한 무리의 임무 참여·귀환·XP와 훈련 후 교대 | 사람 수만큼 엔티티 생성·피해량만 증가 |
| 진화 세 가지 | 파종 검무의 공격 궤적, 호위 성가퀴의 수리 거점, 지붕 아래 파종의 결합 작업 | 별도 이펙트만 추가하고 기존 공격 중복 유지 |
| 적과 보스 | 서로 다른 여섯 적의 행동과 고정된 예고, 물길·돌진·회복 국면 | Core 판정과 무관한 경고 그림 |

한 장비 조합으로 모든 형태를 보여 주었다고 주장하지 않는다. 영상 표본마다 seed·선택 우선순위·실제 사건 시각을 남긴다. 가속 구성요소 검사는 정상 속도 플레이와 구분한다. 사망도 실제 결산 결과로 보존하며 생존율 관문으로 바꾸지 않는다.

## 검증과 보류

CI는 설계 참조·스키마·동작 회귀와 동일 입력의 두 .NET 대상 결정론을 검사한다. 에디터·Mac 실행판의 화면과 소리는 별도 확인한다. 진행 중 공격은 허공 발동 분모에 넣지 않고 완료된 발동의 성공/실패와 따로 집계한다. 과잉 타격 낭비는 실제 요청 피해와 적용 피해의 차이다.

[#126](https://github.com/hyunlord/bs-mobile/issues/126)의 수치·밸런스·경제와 [#98](https://github.com/hyunlord/bs-mobile/issues/98) Fold7은 보류다. 봇은 결정론·무충돌 확인만 하며 우세 정책·생존율을 판정하지 않는다. 보고는 커밋·PR·CI, 원자료는 필요한 실패·표본의 Release만 남긴다. CSV로 재생성 가능한 자료와 별도 증빙 ZIP·CRC·전달 영수증은 만들지 않는다.

## 선별 영상 실행

Unity Recorder는 Mac 실행판 영상과 구분한 에디터 증거다. 다음 명령은 정상 입력 한 판을 녹화하며 이전 출력 경로를 재사용하지 않는다. 실제로 제시된 카드만 선택하므로 우선순위는 취득 보장이 아니다. 진화에는 장비 쌍 외에 실제 처치·수리·수확 조건이 필요하다.

```sh
P1_CAPTURE_DIR="$PWD/artifacts/wave1a/movie-a-new" \
P1_CAPTURE_EXIT=1 P1_CAPTURE_SEED=30000 P1_CAPTURE_TARGET=meta:timber \
P1_CAPTURE_PRIORITY=core:warded_masonry,core:sowing_sworddance,core:carpenter_hammer,core:ward_orbit,core:seed_bag,core:storm_fork,core:rain_ladle,core:iron_blade \
'/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity' \
-projectPath "$PWD/unity" -buildTarget OSXUniversal \
-executeMethod Game.P1Capture.P1Capture.Start \
-logFile "$PWD/artifacts/wave1a/movie-a-editor.log"
```

표본 A는 검무·수리 진화, B는 뿔나팔·잔불 지팡이·낫, C는 지붕 진화를 관찰 대상으로 사전 선정한다. 영상의 실제 카드 선택과 사건 원장에서 관측 여부를 확인하고, 보이지 않은 장면은 미관측으로 남긴다. 전체 영상에서 필요한 연속 구간만 고를 수 있으나 속도 변경·중간 프레임 생략으로 정상 플레이를 꾸미지 않는다. 보스는 기존 일정인 14분에 출현한다. 촬영을 위해 일정·체력·피해를 바꾸지 않는다.

Mac 정상 속도 증거는 Recorder와 동시에 실행하지 않고 앱을 전면에 둔 새 실행에서 수집한다. `--autoplay-capture`의 `clock-diagnostic`은 Stopwatch·Unity realtime·unscaled 델타·프레임·카드 대기를 함께 기록한다. `timeScale=1`만으로 정상 속도를 판정하지 않는다. 실제 경과 시간에서 카드 대기를 뺀 값과 `tick/30`을 대조하고 큰 델타 도약이 없는지 확인한다. 동시 실행의 시간 불일치 표본은 실패 자료이며, 뒤에 정상 속도로 돌아와도 같은 판을 정상 증거로 재분류하지 않는다.

후반 판독성 검사는 초반 화면과 따로 수행한다. XP 표식이 영웅·건물보다 먼저 읽히지 않는지, 말벌 무리가 지도 전체를 가리지 않는지 확인한다. 보스는 피격 중에도 실제 예고 자세를 유지하며 물길 후 회복과 돌진 후 멈춤을 구분한다. 위험 구역은 Core의 선분·반경과 끝 원 영역을 표시한다. 진화·수확·귀환 사건이 기록됐다는 사실만으로 화면 판독을 통과시키지 않는다.

실제 실패 입력의 최적화 전후 재생은 [PR #157](https://github.com/hyunlord/bs-mobile/pull/157)에 남겼다. 두 .NET 대상의 상태 해시 보존은 확인했지만 그 실행 시간은 동시 Recorder 부하가 있는 진단값이다. 이를 Mac FPS나 실기 성능 통과로 사용하지 않는다.

## 실제 입력의 연속 GPU 재생

혼잡한 일반 영상에서 짧은 동작이 가려지면 PlayMode 검사 `Game.Tests.WaveWorldTests.RecordedWaveRangeExportsActualTickFrames`로 기존 `.ssreplay`의 연속 틱을 그릴 수 있다. `WAVE_QA_REPLAY`, `WAVE_QA_START_TICK`, `WAVE_QA_END_TICK`, 새 빈 `WAVE_QA_SEQUENCE_OUTPUT`, 예상 실행 소스 커밋 `WAVE_QA_RENDERER_COMMIT`을 지정한다. 먼저 깨끗한 실행 소스에서 Unity 준비 검사를 통과시키고, 생성된 BuildIdentity와 현재 소스 diff가 일치하는지 별도로 확인한다. 생성 메타데이터만으로 현재 소스가 같다고 판단하지 않는다.

최대1800개 실제 틱을 같은 입력 순서로 렌더링하며, 구간 이후도 같은 세션에서 끝까지 진행해 원본 종료 틱·명령 수·종류·해시를 검증한다. 원본에 checkpoint가 없으면 종료 해시 검증만 있다고 명시한다. 디버그 지급·생성 명령과 조립한 상태를 실제 플레이 증거로 사용하지 않는다.

출력은720×1560 월드 전용 PNG다. 틱당 한 프레임을30fps로 인코딩하며 HUD·소리·카드 대기 시간은 포함하지 않는다. 이 표본은 실제 입력의 형태·변화 관찰용이며 정상 타이밍·성능 증거가 아니다. 선택 영상만 Release에 올리고 원시 프레임·재생성 가능한 표는 로컬에 둔다. 정상 영상에서 불명확했던 판정은 재생 영상의 제한된 통과와 함께 보존한다.
