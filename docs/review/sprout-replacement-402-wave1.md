# 새싹 문장 교체 1차 후보 — #402

관문: 부분 — 깃발/방패의 새싹 형태와 동일 crop 투명 PNG는 준비했으나 원본 외형 보존은 미통과다. 게임 적용 승인본이 아니다.

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/sprout-emblem-replacement-402-20261026)에 `icon.core.levy_banner.default` 159×165와 `fx.ward_orbit.shield` 128×160 후보, 각각 원본/후보1배·3배 비교판, 실패 이력, 프롬프트와 원본 provenance를 게시한다. 원본은 e088545의 equipment.png / wave1a-weapon-fx.png이며 감사 때 확보한 crop을 사용했다.

승인된 seedling-emblem.png를 참조해 씨앗1·휜줄기1·높이가다른잎2장의 크림색 문장으로 편집했다. 깃발의 여러잎과 방패의 곡식다발은 제거했고 금속 테두리는 유지했다. built-in image_gen 편집이며 기준 이미지를 픽셀 그대로 붙인 합성은 아니다. Swift는 알파보존 축소·비교판 조립만 수행했다.

## 남은 결함과 검사

- 깃발 r2도 원본보다 깃대가 약6px 짧고 주름 차이가 남았다. 보정 전후 alpha≥128 실루엣 IoU는0.81663→0.81847로 개선이 미미했다. 같은 문제가 두 시도에서 반복돼 추가 생성은 중단했다.
- 방패는 테두리 폭·위치 차이가 남았다. 실루엣 IoU0.87017. 문장 외 픽셀 동일을 주장하지 않는다.
- 동일 crop 크기·RGBA·투명영역 확인, 원본/후보1배·3배 육안 확인, export.swift 실행 완료. 최종 그림승인과 실제 게임 표시 검증은 미완료다.
- 원본 atlas·게임 코드·데이터 수정 없음. 원본·생성 원본·깃발 r1 실패를 보존했다. #402는 외형 보존 및 사용자 검토 대기로 유지한다.

## 후속 범위

1차 결과를 먼저 보고하며 2·3차는 미착수다. 2차는 ruin_keystone/rally_boundary/heirloom_service/rally_kitchen 네 아이콘과 person.vassal 방패, 3차는 carpenter_hammer 완성/손상/폐허와 harvest-flag/meal_oath/crop_guard_signet/wind_seed_flag다. 씨앗왕관은 사용자 결정 이후 왕관 형태 유지·왕가 금장/보석 제거·곡식고리 중앙새싹 방향으로 대기한다. measured_volley는 원판 확대 후 판단한다.
