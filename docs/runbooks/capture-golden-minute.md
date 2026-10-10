# 휴경의 왕국: 실제 Mac 첫 1분 캡처

관련 이슈: #197, #200. 사용자가 기준 그림과 실제 화면을 비교해 시각 관문을 판단한다.
자동 정상 입력의 네이티브 실행 증거이며 사람의 조작·완주 증거가 아니다.

## 실행

`wave-1a` Mac Release 실행 파일에 다음 인자를 전달한다. 출력은 새 폴더여야 한다.

```sh
'/path/Sow & Siege.app/Contents/MacOS/Sow & Siege' \
  --autoplay-capture --golden-minute \
  --capture-output '/path/new-golden-capture' --capture-seed 30000
```

`--golden-minute` 단독으로 자동화를 켜지 않는다. 타이틀은 실제 9:16 창의 PNG이며
전투 영상은 실제 네이티브 렌더 프레임이다. OS 화면 기록·손쉬운 사용 권한이 필요 없다.
타이틀→설정→출정 UI 버튼·가상 스틱을 사용하고 카드 선택의 2초 정지도 영상에 남긴다.
규칙·자원·적·카드를 주입하지 않는다. 52~60게임초에는 실제 살아있는 완성 건물 중심의
오른쪽 위로 정상 이동한다. 건물이 없으면 기존 이동, evasive 선택 시 위험 회피를 유지한다.
일반 실행·기존 autoplay에는 이 마지막 8초 동작을 적용하지 않는다.

게임은 1배속이며 캡처 중 `Time.captureFramerate`를 사용하거나 tick에 맞춰 재렌더하지 않는다.
60게임초 전에 죽으면 실패를 보존한다. 카드 정지를 포함한 실제 영상은 60초보다 길 수 있다.

## 캡처 경로와 실패 조건

시작 시 12개 슬롯의 RenderTexture·지속 NativeArray·관리 픽셀 배열을 준비한다.
900×1600 RGBA32에서는 세 사본을 합쳐 약 207MB이며 캡처 도구의 메모리다.
실제 EndOfFrame에서 화면을 RT에 복사하고 AsyncGPUReadback으로 읽는다. 주 스레드는
동기 ReadPixels, Texture2D 생성, JPEG/PNG 인코딩, 프레임별 파일 쓰기를 하지 않는다.
60Hz에서는 프레임마다 읽고, 더 빠른 화면에서는 누적 60Hz 마감 시각으로 고유 프레임을 선택한다.
GPU 슬롯은 읽기가 완료될 때까지, 관리 버퍼는 writer가 끝날 때까지 재사용하지 않는다.
GPU·writer를 비동기로 비운 후 정상 종료한다. 강제 앱 종료에서만 제한된 정리를 기다린다.

이미지 worker는 thread-safe EncodeArrayToJPG로 인코딩하고 실제 시간 간격의 ffconcat을 만든다.
JPEG 결과 배열 등 worker 할당은 존재하며 `imageWriterAllocatedBytes`로 별도 기록한다.
worker의 할당도 전역 GC에 영향을 줄 수 있으므로 Update/draw 할당 0과 프로세스 전체 할당 0을
혼동하지 않는다. 이 캡처 결과만으로 게임 성능 관문을 통과했다고 주장하지 않는다.

오디오는 소스 없는 전용 Listener의 실제 혼합 출력을 사전 할당 PCM 링에 복사한다.
오디오 callback에는 파일 쓰기나 새 버퍼 할당이 없다. PCM writer가 WAV를 만들고,
종료 때 원 Listener를 복원한다. 영상 첫 프레임 DSP와 첫 PCM DSP의 차이가 오디오 오프셋이다.

링 포화, GPU 오류, 오디오 블록 유실, 전부 0인 PCM, 제출/기록 프레임 불일치,
실제 누적 벽시계 시간 대비 부족한 60Hz 프레임 수는 실패로 남긴다. 판정은 첫 프레임부터의 경과 시간에 단 한 번 2ms 시계 흔들림 여유를 빼고 가장 가까운 60Hz 슬롯 수로 반올림한 뒤, 실제 기록 간격 수와 비교한다. 따라서 경계의 최대 여유는 합계 약10.333ms(반 슬롯8.333ms+2ms)이며 프레임마다 새로 주지 않는다. 누적 부족 수의 최댓값을 보존하므로 뒤의 빠른 프레임이 앞의 누락을 지우지 않는다. 지속50fps·41.7fps를 작은 개별 간격이라는 이유로 통과시키지 않는다. 프레임을 복제하거나 CFR로 바꿔
60fps를 증명하지 않는다. 카드 화면도 실제 화면이므로 그대로 기록한다.

## 결과와 확인

- `01-native-title.png`: 실제 타이틀.
- `05-native-golden-battle.png`: 55게임초 이후 선정한 실제 전투 프레임.
- `frames.ffconcat`, `frames/*.jpg`, `frames.csv`: 실제 벽시계·DSP·렌더 프레임·게임 틱.
- `audio.wav`: 실제 혼합 PCM.
- `golden-minute.json`: provenance, measuredFps, frameP95Ms/frameMaxMs, missed60HzSlots,
  gpuErrors/ringOverflows, 제출·기록 수, main 제출/callback/worker 할당과 audioStartOffsetSeconds.
- 기존 `capture-result.json`, `capture-ledger.jsonl`, 격리 프로필 입력 기록.

`golden-minute.json`의 `failure`와 실제 프레임 간격을 먼저 확인한다. 요청 fps가 60이라는
필드나 결과 영상의 명목 프레임률만으로 60fps라고 보고하지 않는다.

```sh
cd '/path/new-golden-capture'
# OFFSET에는 golden-minute.json의 audioStartOffsetSeconds 실제 값을 쓴다.
ffmpeg -f concat -safe 0 -i frames.ffconcat \
  -itsoffset OFFSET -i audio.wav \
  -map 0:v:0 -map 1:a:0 -c:v libx264 -preset medium -crf 18 \
  -pix_fmt yuv420p -fps_mode vfr -c:a aac -b:a 192k golden-minute.mp4
```

영상·PNG를 직접 열어 색·상하 방향·HUD·이동·성장·수확·카드·오디오 동기를 확인한다.
기준과 같은 비율로 나란히 비교하되 기준 그림을 실제 화면에 합성하지 않는다. Release에는
영상·선정 비교 캡처만 올리고 재생성 가능한 프레임 전체나 증거 ZIP을 올리지 않는다.

## API 근거

- [Unity 6000.6 CaptureScreenshotIntoRenderTexture](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotIntoRenderTexture.html)
- [Unity 6000.6 RequestIntoNativeArray](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Rendering.AsyncGPUReadback.RequestIntoNativeArray.html)
- [Unity 6000.6 EncodeArrayToJPG: thread safe](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/ImageConversion.EncodeArrayToJPG.html)
