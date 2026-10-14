# 모듈 공장 · 제한된 자율 반복 기록

[사용자 계약·최종 판정](../../../review/m1-5/modular-autonomous.md). 이전 [2차 실패](../trial-2/README.md)의 파일을 덮어쓰지 않고 별도 후보를 생성한다. 최대10후보 또는6시간이며, 기계적 기준을 모두 통과하기 전에는 게임 굽기·외형 작업으로 넘어가지 않는다.

원본 입력은 승인된(b) GLB·4K 텍스처, CC0 UBC 뼈대와 UAL `Walk_Loop`, 기존 정렬용 rest, A 원화다. UAL 동작을 약하게 하거나 정지시켜 통과시키지 않는다. 변경된 조각·접합·주먹은 후보별 레시피와 저작 소스에 기록한다.

## 보존 위치

로컬 원자료 루트는 `/Users/rexxa/bs-mobile/.omo/evidence/m1-5-research/modular-autonomous`다.

- `candidates/NN/`: 회차별 동결 Blender 파일, 레시피, 입력·저작 소스 SHA와 소스 사본.
- `metrics/candidate-NN-v8/`: 실제80프레임 CSV, 요약, 부품별 변형, 대응점·파지 표본과 최악 사례.
- `metrics/test_geometry.py`: 측정기에 대한 분석 형상 검사. 후보 통과와 구별한다.
- `metrics/aggregate.py`: 같은 측정기 SHA와80프레임을 확인하고 회차 표를 재생성한다.

Python은 저장소에 넣지 않는다(RB003). 모델·전체 프레임·ZIP은 새로 게시하지 않는다. 필요한 실패 표본과 외부 저작·측정 소스만 개별 Release 파일로 보존하고, 원본 입력은 기존 승인 경로와 해시를 따른다. Release 소스만으로 원본 모델까지 확보된다고 표현하지 않는다.

**10회 상한 도달, 실패.** [정본 회차 CSV](attempt-metrics.csv), [10회차 프레임 CSV](best-10-frames.csv), [측정기 원시 요약](best-10-summary.json), [새 주먹 보충 측정](best-10-authored-grip.json), [레시피](best-10-recipe.json), [10회차 입력 검증](best-10-provenance.json), [회차별 저작 명령·SHA](attempt-ledger.json)를 보존한다. 원시 요약의 숨긴 원본 손 계보 수는 표시 메시의 파지 검증으로 읽지 않는다. CSV의 표시 원본 손 사용 여부와 보충 측정을 함께 읽는다.

[실패 소스 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-modular-autonomous)의 `candidate10--*`는 선택된10회차의 저작 소스, `metrics--*`는 측정기다. [소스 manifest](source-manifest.csv)의 원래 경로·해시로 복원한다. 원본 메시와 다른9회차의 전체 소스·측정 출력은 로컬에 보존한다. 새 모델 제작은 여기서 중단하며 이 Release에는 외형 처리나 게임 영상이 없다.

## 재현

기존 Blender4.5.14에서 측정기를 실행한다. 평가 대상 파일은 덮어쓰지 않는다.

```sh
Blender -b --python-exit-code 1 --python metrics/test_geometry.py
Blender -b --python-exit-code 1 --python metrics/evaluate.py -- candidates/NN/a-knight-shell.blend output-NN
python3 metrics/aggregate.py metrics
```

`Blender`는 이 실행에 사용한 `/Users/rexxa/bs-mobile/artifacts/m1-256/3d/blender-mount/Blender.app/Contents/MacOS/Blender`를 뜻한다. 배포된 Python 모듈은 원래의 `metrics/` 또는 후보 `source/` 폴더 구조로 복원해야 한다. 열린 몸 표면의 관통 깊이, 검사하지 못한 접합 경계, 파지 감싸기 여부는0으로 채우거나 통과로 바꾸지 않는다.
