# 걷기와 피격 실험 비교 (#207)

설정의 실험은 기본 OFF, 다음 출정에서 고정한다. B 외형은 별도 시안이다. 기존 저장을 사용하지 않는 앱 자체 입력·캡처로 검증한다. OS 화면 권한, Fold7 연결, C05 비용 측정을 요구하지 않는다.

```sh
open -n '/path/Sow and Siege.app' --args \
  --autoplay-capture --feel-comparison walk-a \
  --capture-output '/path/new-walk-a' --capture-seed 30001
```

`walk-b`는 같은 seed와 오른쪽 일정 입력으로0–5초를 기록한다. `rules-off`와 `rules-on`은 같은 seed와 기존 성장·회피 정책(`growth-evasive-policy-v1`)으로0초부터 실제 진행하여360–390초를 기록한다. 보상·익은 밭·건물을 향한 정상 이동에 적 회피를 적용하며 생존을 보장하지 않는다. 네 모드 모두 새 출력 경로를 쓴다. rules-on은 규칙 세 개와 피격 표현을 켜고 외형은 A로 유지한다. walk 비교는 규칙을 모두 끈다. 별도 시작·길이·가로·약한 빌드 옵션은 섞지 않는다. 이동 옵션은 아래 rules 전용 규칙을 따른다.

입력은 실제 UI 경로의 자동 조종이며 사람의 직접 조작 증거가 아니다. walk는 일정 방향이지만 UI 포인터는 프레임마다 전달하므로 누적 틱 처리에 따라 실제 틱별 입력도 달라질 수 있다. rules는 같은 반응 정책이며 실제 이동 명령은 상태에 따라 달라진다. 규칙이 달라지면 생존·카드·레벨·밀도도 달라질 수 있다. 카드 선택은 정상 허용 선택 중 기존 정책을 적용하고 선택 원장과 리플레이에 기록한다. 동일 seed/정책을 동일한 전체 명령·상태라고 표현하지 않는다.

초기 `rules-on-01`의30초 원형 입력 실패는 보존한다: seed30001, tick983 사망, level1, 발동45회 모두 빈 타격, 성장 완료5회이나 수확·처치 경험치0. 해당 입력은 성장물·보상 접근을 우회했고 원형 방향 적분이 지도 경계에 잘렸다. 정책 변경은 정상 입력만 바꾸며 규칙·수치·자원은 변경하지 않는다.

녹화 길이는 게임 틱 구간이다. 카드 정지가 있으면 실제 영상 시간이 늘어나므로 각 실행 manifest의 wall 시간과 PTS를 함께 확인한다. 사망, 미도달, 프레임 누락을 보존하고 가속 재생·무적·자원 주입·복제 프레임으로 보충하지 않는다. A/B 합성은 실제 녹화의 나란히 배치만 허용한다.

B 재생성은 `node tools/art-authoring/hero-b/bake.mjs` 또는 Editor 메뉴를 사용한다. [저작 명세](../../tools/art-authoring/hero-b/README.md)에 원본·수정 출처·관절·접지·프레임 계약이 있다. Runtime은 atlas 한 quad이며 뼈대 패키지가 필요 없다.

rules 모드에만 `--capture-movement default|evasive`를 허용한다. 생략하면 기존 회피 정책이며 `default`는 기존 성장 접근 정책만 사용한다. 각각 `growth-evasive-policy-v1`, `growth-policy-v1`로 기록한다. 다른 정책의 결과를 같은 입력 비교라고 표현하지 않는다.

`--feel-failure-preview`는 rules 전용의 명시적 진단 옵션이다. 가로1920×1080에서0–30초를 녹화하며6분 비교를 대체하지 않는다. 예: 위 명령의 모드를 `rules-on`으로 바꾸고 `--feel-failure-preview --capture-movement default`를 붙인다. 사망하면 기존 실패 상태와 실제 부분 프레임을 보존한다. manifest의 `failurePreview`와 `previewBoundary`로 정식 구간과 구별한다. 초기 `rules-on-02`도 tick426(14.2초),20회 모두 빈 발동·경험치0·raider9회 피격으로 사망한 실패로 보존한다. 미도달을 숨기기 위한 생존 보정은 하지 않는다.

필수 CI를 실제 성공으로 확인한 뒤 병합한다. 보호되지 않은 feature 대상의 `gh pr merge --auto`는 즉시 병합될 수 있으므로 대기 수단으로 사용하지 않는다. 최종 사용자의 A/B·손맛 선택은 자동 검사와 구별한다.
