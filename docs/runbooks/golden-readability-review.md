# 황금 1분: 정상 실행 움직임과 전투 판독

추적: [#204](https://github.com/hyunlord/bs-mobile/issues/204). 기준판은 `533a12d`이며 C05 비용 측정, Fold7 #98, 수치·경제 #126, 1b는 계속 보류한다. 적 분리는 [ADR0049](../adr/0049-deterministic-wave-enemy-separation.md)의 설계 변경이다.

## 정상 실행 좌표 기록

Mac Release에 다음 인자를 전달한다. `open -a`로 전경에 활성화한다. 출력과 저장 경로는 비어 있어야 하며 기존 사용자 저장 경로를 재사용하지 않는다.

```sh
open -a '/path/Sow and Siege.app' --args \
  --smoothness-trace --smoothness-scripted-input \
  --smoothness-trace-output /path/new-trace \
  --smoothness-profile /path/new-profile --smoothness-trace-quit
```

합성 입력은 가상 Mouse → InputSystem → EventSystem → 실제 버튼/조이스틱 경로를 사용한다. 후속 scripted trace는 복제한 UI action asset을 합성 Mouse에만 연결하고 끝나면 원래 action/device 상태를 복원한다. 일반 실행에는 이 격리를 켜지 않는다. 이 구현의 존재만으로 실제 입력 크기·정속 구간이 검증되었다고 하지 않는다. 실제 손 조작이나 물리 입력-광자 지연의 증거가 아니다. 캡처 모드, Core 예열, 프레임 상한 변경, 상태·자원 주입을 사용하지 않는다. 합성 입력 인자를 빼면 사용자가 직접 조작한 동일 좌표를 기록한다.

실제 저속 입력을 관측한 뒤 저속·정지·고속·정지 각4초를 시작하고 총18초를 기록한다. 기본 방향은 오른쪽이며 `--smoothness-oblique`는 같은 크기의 대각선 입력(저속0.2/0.15, 고속0.8/0.6)을 사용한다. `--smoothness-left`는 선택한 방향을 반대로 하며 오른쪽 기본값에서는 왼쪽으로 이동한다(사선과 함께 쓰면 반대 사선). 지도 안쪽을 향하는 방향으로 경계 클램프와 보행 변동을 구별하는 진단에 쓸 수 있다. 경계 클램프를 제거하거나 경계에 닿은 원자료를 삭제하지 않는다. 서로 다른 방향의 실행을 개선율로 직접 비교하지 않는다. 실제 일정 입력 구간 전체와 시작 과도 구간을 보존하고, 앞선0.5초 이상이 있는 마지막3초 이상을 별도 정상 구간 후보로 분석한다. 피격 화면 흔들림을 빼서 통과시키지 않는다.

후속 계측의 `engineFrameTimeSeconds`는 Unity unscaled frame 시각, 기존 `wallSeconds`는 BeginFrame 실시간 시각(세션 상대), `callbackEndWallSeconds`는 EndFrame 실시간 시각(세션 상대)이다. 서로 다른 callback 위치의 시각을 비교해 관측 지연을 진단하되 분석기의 실제 dt 관문 시계를 조용히 교체하지 않는다. 어느 열도 디스플레이 present 시각이나 물리 입력 지연이 아니다.

`normal-frames.csv`는 실제 프레임 시각·입력·틱·누산 잔여·권위/예측 위치·카메라 위치/배율을, `normal-entities.csv`는 RenderedEntitySample의 영주·고정된 첫 적 5 ID·투사체/궤도 파편 좌표를 보존한다. 죽은 적을 다른 ID로 바꾸지 않으며 실제로 관측되지 않은 개체는 검증했다고 하지 않는다. `normal-trace.txt`에 commit/sourceHash와 합성 입력·드롭 수를 남긴다.

```sh
node tools/analyze-motion-trace.mjs /path/new-trace /path/analysis
node tools/plot-motion-comparison.mjs /path/pairs.json /path/comparison.svg
```

`pairs.json`은 `[{"label":"normal cardinal","before":"/path/before-analysis","after":"/path/after-analysis"}]` 형식이다. Node 표준 라이브러리만 사용하며 영상 디코딩에는 이미 설치된 ffmpeg/ffprobe를 사용한다. 저장소 의존성을 추가하지 않는다. `analyze-motion-video.mjs`는 영상·시작초·길이초·ROI의 x/y/폭/높이·출력폴더를 순서대로 받는다. 위상 상관의 ROI·PTS·신뢰도를 함께 보존한다. 입력·장면·ROI가 다른 영상의 수치를 원인 개선율로 해석하지 않는다. 영상 추정과 네이티브 좌표는 같은 그래프 패널에 섞지 않는다.

저속/고속 일정 입력 각각 3초 이상에서 이동량/실제 프레임 시간 CV ≤0.10, lag 2–12 자기상관 ≤0.3, 영주 화면 위치의 15프레임 이동평균 대비 RMS ≤0.5px(1080p 환산)를 확인한다. 정지·카메라 수렴 구간은 평균 속도 0의 CV를 억지로 계산하지 않고 별도 원자료와 RMS를 남긴다. 자동 분석은 사용자 직접 플레이 판정을 대체하지 않는다.

## 투사체가 실제 나온 정상 실행 표본

기본 18초 실행에서 투사체가 없으면 기록 코드의 존재만으로 투사체 관문을 통과 처리하지 않는다. 별도 빈 저장·출력 경로에서 다음 옵션을 사용한다.

```sh
open -a '/path/Sow and Siege.app' --args \
  --smoothness-trace --smoothness-scripted-input --smoothness-await-projectile \
  --smoothness-seed 1078312934 \
  --smoothness-trace-output /path/new-projectile-trace \
  --smoothness-profile /path/new-projectile-profile --smoothness-trace-quit
```

`--smoothness-seed 1078312934`는 재현용 선택 사항이며 새 세션의 초기 seed만 지정한다. 생략하면 일반 기본값을 유지한다. 실행 중 상태·카드 제시·성장을 주입하지 않으며 `normal-trace.txt`의 `requestedSeed`를 보고한다. 서로 다른 seed를 같은 조건의 개선율로 비교하지 않는다.

이 옵션은 실제 출정 준비 UI에서 곡물/씨앗 자루를 고르고, 실제 제시된 레벨업 카드 선택과 이동으로 비적대 투사체가 생기기를 기다린 뒤 같은 18초 입력 순서를 시작한다. 준비 제한은 단조 증가하는 Stopwatch 기준120초이며 player callback에서 확인한다(앱 자체가 멈춘 시간에 강제 종료를 보장하는 외부 watchdog은 아니다). 상태·장비·경험치를 직접 주입하거나 빠르게 감지 않는다. 준비 실패·카드 대기·포커스 상실을 보존한다. 카드 선택으로 잠시 멈춘 구간을 이어 붙여 정속 3초를 만들지 않는다.

`normal-trace.txt`의 `projectilePreparation`, `projectileObserved`, `projectileCardClicks`, `projectileReadyWallSeconds`와 **실제로 저장한** `recordedProjectileSamples`·`recordedFriendlyProjectileSamples`를 확인한다. `normal-entities.csv`의 `friendlyProjectile`로 적대/비적대 표본을 구별하며, 비적대 저장 행이 0이면 실패다. 사용자의3초 조건은 일정 입력의 저속·고속 보행 구간에 적용하며 개별 투사체 수명에 적용하지 않는다. 실제 투사체의 생존 길이·ID별 연속성·입력/카드 정지를 보고한다. 짧은 수명은 ACF 통계의 신뢰도 한계로 명시하고 서로 다른 ID나 분절 구간을 이어 붙이지 않는다. 기록0행은 미관측으로 남기며 기록 기능의 존재를 실제 관측 증거로 대신하지 않는다. 새로운 수명·표본수 관문을 추가하지 않는다.

## 캡처 파이프라인 진단

후속 네이티브 캡처의 `capture-pipeline.csv`는 프레임별 `readback_ms`, `ready_wait_ms`, `flip_ms`, `jpeg_ms`, `file_write_ms`, `screenshot_ms`, `writer_total_ms`를 남긴다. 읽기 완료·대기·행 뒤집기·JPEG 인코딩·파일 쓰기·스크린샷·writer 비용을 구분하는 진단이며 링 크기나 실패 기준을 완화한 수정이 아니다. 이를 게임 프레임 전체 비용이나 물리 입력 지연으로 해석하지 않는다. ring overflow와 누락 슬롯은 manifest 실패로 보존하며, 진단 열이 생겼다는 이유만으로 실패 원인이 확인되었다거나 다음 실행이 통과했다고 하지 않는다. 계측 실행 동안 다른 게임 앱·벤치마크·무거운 분석을 병행하지 않는다.

## 지연된 FrameTiming 원자료

`normal-frame-timings.csv`는 FrameTimingManager가 반환한 CPU/GPU 비용과 raw timestamp를 기존 좌표 CSV와 **별도** 저장한다. `normal-frame-timing-boundary.txt`의 feature 활성 여부, CPU timer frequency, 반환/빈 poll·중복/오래된 값·zero-start/zero-present·저장 초과 수를 함께 확인한다. 0은 사용할 수 없는 값이며 비용0으로 해석하지 않는다.

`observedUnityFrame`은 값을 꺼내 온 프레임이고 `sourceUnityFrame`은 unknown이다. 일정 지연을 가정하거나4프레임을 빼서 좌표 CSV와 연결하지 않는다. raw CPU ticks는 frequency가0이 아닐 때만 초로 바꾸며 retrieval 시각과 공통 epoch라고 가정하지 않는다. `cpuTimePresentCalled`는 Present 호출 시각이지 실제 디스플레이 표시 시각이 아니다. 마지막 지연 tail과 반환되지 않은 source frame은 미확인으로 남기고 기존 모션 관문의 dt를 교체하지 않는다.

## 마스크와 경험치

`normal-fallow-xp.csv`는 장면 활성 조건·셰이더 지원·마스크 바인딩·전체 지도 CPU 마스크 점유와 성장 단계, 처치 경험치·성장 보상 생성/흡수를 기록한다. 처치 경험치는 즉시 지급되며 성장 경험치만 보상 흡수를 거친다. 누락 틱·사건·행 수를 확인한다. CPU 마스크 점유율은 실제 화면의 녹색 비율이나 GPU 출력 증명이 아니므로 같은 실행의 화면과 대조한다.

## 판독 증거

돌진 예고는 화면 안의 영주 대상 위협을 충돌 시각 순으로 합쳐 최대 세 경로만 보여야 한다. 실제 판정 영역을 덮는 얇은 경로·방향 화살표·예고 채움과 첫 두 안내를 확인한다. 같은 입력으로 새 분리 계약을 실행한 결과는 역사 해시 검증과 구별한다. 밀집 재생에서 비보스 중심 거리 < 두 몸 너비 평균 ×0.6인 상태가 1초 넘은 쌍은 0이어야 한다.

사용자 22:19 화면의 원본·해설은 로컬 증거로 보존한다. 앱 경로 또는 해당 실행의 기록 없이 화면만으로 실행 commit을 확정하지 않는다. 공개 증거에는 게임 부분만 사용한다. 보고 첫 이미지는 유사한 돌진·밀집 상황의 전후, 다음은 움직임 그래프이며 그래프에 서로 다른 입력의 비교 한계를 표시한다.

## 실제 후반 구간 녹화

후반 판독을 재생 화면으로 대체하지 않으려면 새 출력 경로에서 다음 옵션을 사용한다. 값은 `--flag value`처럼 공백으로 분리한다.

```sh
open -a '/path/Sow and Siege.app' --args \
  --autoplay-capture --golden-minute \
  --capture-start-seconds 240 --capture-duration-seconds 30 \
  --capture-landscape --capture-output '/path/new-late-capture'
```

시뮬레이션은0초부터 정상 속도·실제 UI 입력으로 진행하며240초부터30초만 캡처한다. 시간을 건너뛰거나 Core 상태·적 수·무적·자원을 주입하지 않는다. 인자를 생략하면 기존0초 시작/60초 캡처 기본값을 유지한다. 초 단위는 게임 진행 시간이며 카드 정지·실제 PTS·최종 manifest를 함께 확인한다. 가로 화면 옵션은 별도 화면 구성 선택이며 기존 세로 표본과 구별한다.

`6aa5731`의 집중 Capture 검사40/40·경계 검사와 독립 버퍼 검토는 구현 검증이다. 실제 후반 녹화는 별도 실행 증거가 필요하며 빌드·테스트 통과만으로 적 밀도·판독·영상 누락 관문을 통과 처리하지 않는다. 시작 전 사망·미도달·버퍼/슬롯 실패는 보존하고 정상 완료 표본으로 바꾸어 부르지 않는다.

후반 표본은 실제 개체 수의 정의(적만인지 다른 개체 포함인지), 카드 정지 프레임·실제 영상 길이를 명시한다.240–270초 표본이 적은 밀도라면 더 혼잡한 장면의 대체 증거로 부르지 않는다. 조이스틱 그림의 화면 잘림·장비창 가림도 실제 프레임에서 확인하며 입력 동작과 그림 크기 문제를 구별한다.360–390초 후속을 실행할 때는 위 시작 값만360으로 바꾸고0초부터 다시 정상 진행한다.
