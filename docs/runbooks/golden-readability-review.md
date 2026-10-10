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

합성 입력은 가상 Mouse → InputSystem → EventSystem → 실제 버튼/조이스틱 경로를 사용한다. 실제 손 조작이나 물리 입력-광자 지연의 증거가 아니다. 캡처 모드, Core 예열, 프레임 상한 변경, 상태·자원 주입을 사용하지 않는다. 합성 입력 인자를 빼면 사용자가 직접 조작한 동일 좌표를 기록한다.

실제 저속 입력을 관측한 뒤 저속·정지·고속·정지 각4초를 시작하고 총18초를 기록한다. 기본 방향은 오른쪽이며 `--smoothness-oblique`는 같은 크기의 대각선 입력(저속0.2/0.15, 고속0.8/0.6)을 사용한다. 서로 다른 방향의 실행을 개선율로 직접 비교하지 않는다. 실제 일정 입력 구간 전체와 시작 과도 구간을 보존하고, 앞선0.5초 이상이 있는 마지막3초 이상을 별도 정상 구간 후보로 분석한다. 피격 화면 흔들림을 빼서 통과시키지 않는다.

`normal-frames.csv`는 실제 프레임 시각·입력·틱·누산 잔여·권위/예측 위치·카메라 위치/배율을, `normal-entities.csv`는 RenderedEntitySample의 영주·고정된 첫 적 5 ID·투사체/궤도 파편 좌표를 보존한다. 죽은 적을 다른 ID로 바꾸지 않으며 실제로 관측되지 않은 개체는 검증했다고 하지 않는다. `normal-trace.txt`에 commit/sourceHash와 합성 입력·드롭 수를 남긴다.

```sh
node tools/analyze-motion-trace.mjs /path/new-trace /path/analysis
node tools/plot-motion-comparison.mjs /path/pairs.json /path/comparison.svg
```

`pairs.json`은 `[{"label":"normal cardinal","before":"/path/before-analysis","after":"/path/after-analysis"}]` 형식이다. Node 표준 라이브러리만 사용하며 영상 디코딩에는 이미 설치된 ffmpeg/ffprobe를 사용한다. 저장소 의존성을 추가하지 않는다. `analyze-motion-video.mjs`는 영상·시작초·길이초·ROI의 x/y/폭/높이·출력폴더를 순서대로 받는다. 위상 상관의 ROI·PTS·신뢰도를 함께 보존한다. 입력·장면·ROI가 다른 영상의 수치를 원인 개선율로 해석하지 않는다. 영상 추정과 네이티브 좌표는 같은 그래프 패널에 섞지 않는다.

저속/고속 일정 입력 각각 3초 이상에서 이동량/실제 프레임 시간 CV ≤0.10, lag 2–12 자기상관 ≤0.3, 영주 화면 위치의 15프레임 이동평균 대비 RMS ≤0.5px(1080p 환산)를 확인한다. 정지·카메라 수렴 구간은 평균 속도 0의 CV를 억지로 계산하지 않고 별도 원자료와 RMS를 남긴다. 자동 분석은 사용자 직접 플레이 판정을 대체하지 않는다.

## 마스크와 경험치

`normal-fallow-xp.csv`는 장면 활성 조건·셰이더 지원·마스크 바인딩·전체 지도 CPU 마스크 점유와 성장 단계, 처치 경험치·성장 보상 생성/흡수를 기록한다. 처치 경험치는 즉시 지급되며 성장 경험치만 보상 흡수를 거친다. 누락 틱·사건·행 수를 확인한다. CPU 마스크 점유율은 실제 화면의 녹색 비율이나 GPU 출력 증명이 아니므로 같은 실행의 화면과 대조한다.

## 판독 증거

돌진 예고는 화면 안의 영주 대상 위협을 충돌 시각 순으로 합쳐 최대 세 경로만 보여야 한다. 실제 판정 영역을 덮는 얇은 경로·방향 화살표·예고 채움과 첫 두 안내를 확인한다. 같은 입력으로 새 분리 계약을 실행한 결과는 역사 해시 검증과 구별한다. 밀집 재생에서 비보스 중심 거리 < 두 몸 너비 평균 ×0.6인 상태가 1초 넘은 쌍은 0이어야 한다.

사용자 22:19 화면의 원본·해설은 로컬 증거로 보존한다. 앱 경로 또는 해당 실행의 기록 없이 화면만으로 실행 commit을 확정하지 않는다. 공개 증거에는 게임 부분만 사용한다. 보고 첫 이미지는 유사한 돌진·밀집 상황의 전후, 다음은 움직임 그래프이며 그래프에 서로 다른 입력의 비교 한계를 표시한다.
