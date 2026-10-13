# M1-258: 독립 8방향 프레임 후보 v2

이 기록은 `artifacts/m1-258/hero-frames/candidate-40-v2`를 고정한다. 40클립·232포즈이며, 좌우 반전 없이 각각 그렸다. idle4 / walk8 / slash-light8 / hit3 / death6을 방향마다 가진다. 명목 비압축 RGBA는 120,456,280바이트(114.876MiB)다.

- `manifest.json`: 실제 compact 런타임 manifest의 바이트 그대로.
- `freeze.json`: 그 manifest와 선택된 런타임 PNG 40개의 SHA-256 및 파일 이름. PNG는 Git에 넣지 않는다. Release에 **같은 이름의 개별 에셋**으로 전달할 대상이며, 이 기록 자체는 업로드 완료를 주장하지 않는다.
- `authoring.json`: sourceRects·수동 발/바닥 기준점·고정 서기 높이·클립별 접촉 프레임이 있는 조립 입력.
- `source-hashes.json`: 조립에 사용한 원본 그림 40개의 경로와 SHA-256. 원본은 로컬에 보존하며 성공 실행 전체 로그를 배포하지 않는다.
- `north-*` / `west-*`: 정확한 생성 프롬프트와 출력 식별자. 개발자 홈 경로와 도구 안내문은 제거했다. 파일 내부 과거 `paused` 등은 당시 기록이며 현재 후보 상태가 아니다. 서쪽 최초 제작 일부는 프롬프트가 별도로 보존되지 않고 세션 참조만 있으므로 정확한 재생성을 주장하지 않는다.
- E/S/SE 프롬프트는 상위 `generation-provenance-m1-258.json`에 있다. 생성 프롬프트를 다시 실행해 같은 픽셀이 나오는 것은 보장하지 않는다. 픽셀 재현에는 해시가 일치하는 원본이 필요하다.

## 재조립

원본 PNG를 `source-hashes.json`의 root 및 상대 경로에 복원한 뒤, 이 디렉터리의 `authoring.json`을 그 root에 복사한다. 저장소 루트에서 다음을 실행한다.

```sh
swift tools/art-authoring/hero-a-frames/assemble-production.swift artifacts/m1-258/hero-frames
swift tools/art-authoring/hero-a-frames/compact-production.swift artifacts/m1-258/hero-frames
```

첫 명령은 전체 자세를 정수 이동으로 배치한다. 둘째는 클립별 공통 배율로 서기 높이 최대256px까지 줄인다. 자세마다 늘이거나 머리만 변형하지 않는다. 런타임 출력은 root의 `runtime/`이다. 동결된 v2 폴더를 덮어쓰지 않는다.

## 검증 범위

로컬에서 40개 원본 및 40개 런타임 PNG 해시, manifest SHA, 232포즈 구성을 확인했다. 조립 알파 손실은 0이다. N/NE 10클립은 턱이 가려져 머리 높이를 측정할 수 없으므로 null과 `headMeasurementUnavailable:true`를 유지한다. 나머지의 수동 대표 측정도 전 프레임 비율 인증을 뜻하지 않는다.

네이티브 사전 검토는 부모가 실행한 `0bd300c` 빌드의 제한된 전투 표본에서 로딩과 동작을 확인한 결과다. 새 차단 결함은 보고되지 않았으나 작은 색 차이는 남았다. 전 방향·전 동작의 사용자 시각 승인과 성능 관문은 완료되지 않았다. v1의 null 메타데이터 로딩 실패는 보존하며, v2는 플래그만 추가했고 40개 PNG는 동일하다.
