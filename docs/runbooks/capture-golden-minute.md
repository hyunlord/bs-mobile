# 휴경의 왕국: 실제 Mac 첫 1분 캡처

관련 이슈: #197. 시각 관문은 사용자가 기준 그림과 실제 화면을 비교해 판단한다.
이 절차는 자동 정상 입력의 네이티브 실행 증거이며 사람의 조작이나 완주 증거가 아니다.

## 실행

`wave-1a` Mac Release 실행 파일에 다음 인자를 전달한다. 출력은 새 폴더여야 한다.

```sh
'/path/Sow & Siege.app/Contents/MacOS/Sow & Siege' \
  --autoplay-capture --golden-minute \
  --capture-output '/path/new-golden-capture' --capture-seed 30000
```

`--golden-minute` 단독으로는 자동화를 켜지 않는다. 900×1600 창의 실제 프레임버퍼를
기록하며 OS 화면 기록·손쉬운 사용 권한을 요구하지 않는다. 기존 타이틀→설정→출정
UI 버튼 및 가상 스틱 입력을 재사용한다. 카드 선택 대기는 2초이며 영상에 그대로 남는다.
성장·적·카드를 주입하지 않고 시뮬레이션 속도는 항상 1이다. 60게임초 전에 죽으면
실패로 기록하며 파일을 보존한다. 정상 종료 지점은 60게임초이므로 카드 정지를 포함한
영상의 실제 길이는 60초보다 길 수 있다. 이를 60초로 압축하거나 정지를 지우지 않는다.

## 결과

- `01-native-title.png`: 실제 타이틀.
- `05-native-golden-battle.png`: 55게임초 이후 첫 전투 프레임.
- `frames.ffconcat`, `frames/*.jpg`, `frames.csv`: 최대 30fps 실제 화면과 벽시계 시간.
- `audio.wav`: AudioListener의 실제 혼합 출력. `golden-minute.json`의 audio가 true인지 확인.
- `golden-minute.json`: 빌드 commit/sourceHash/dataHash, 실제 해상도·마지막 틱·자동 입력 표시.
- 기존 `capture-result.json`, `capture-ledger.jsonl`, 격리 프로필의 입력 재생: 원래 실행 출처.

녹화는 렌더 프레임을 동기식으로 읽으므로 성능 측정으로 사용하지 않는다. 타임스탬프
간격으로 인코딩해 캡처 자체로 생긴 지연도 보존한다. 오디오는 새로 합성하지 않는다.

```sh
cd '/path/new-golden-capture'
ffmpeg -f concat -safe 0 -i frames.ffconcat -i audio.wav \
  -map 0:v:0 -map 1:a:0 -c:v libx264 -preset medium -crf 18 \
  -pix_fmt yuv420p -fps_mode vfr -c:a aac -b:a 192k golden-minute.mp4
```

오디오가 기록되지 않았으면 성공으로 숨기지 말고 원인을 확인한다. 영상·PNG는 직접
열어 타이틀, 이동, 성장, 수확, 카드 창 및 실제 소리를 확인한다. 기준 A-battle/A-title와
같은 비율로 나란히 비교하되 기준 그림을 실제 화면에 합성하지 않는다. Release에는
영상·선정한 비교 캡처만 올리고 재생성 가능한 JPEG 전체나 별도 증거 ZIP을 올리지 않는다.
