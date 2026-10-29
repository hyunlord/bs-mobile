# 앱 아이콘·세로 대표 이미지 3안

[이슈 #534](https://github.com/hyunlord/bs-mobile/issues/534)의 방향 시안입니다. 사용자 최종 선택·승인은 대기 중이며 출시 자산 확정이나 스토어 등록이 아닙니다. 게임 코드·데이터·스토어 메타 설정은 변경하지 않았습니다.

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/store-art-534-20261029)에 A 새싹 방패, B 푸른 망토 영주, C 되찾는 땅·영지를 비교합니다. 각 안은 아이콘1024²·60²와 세로1290×2796이며, 제목은 포스터에만 `Sow & Siege`입니다. 기준 문장은 씨앗1·휜줄기·높이가 다른 잎2입니다.

- [실제60px 및4배 보조 아이콘 비교](https://github.com/hyunlord/bs-mobile/releases/download/store-art-534-20261029/store-icons-abc-r1-native60-and4x.png)
- [대표 이미지 동일1/3배율 비교](https://github.com/hyunlord/bs-mobile/releases/download/store-art-534-20261029/store-posters-abc-r1-430x932-panels.png)
- [README](https://github.com/hyunlord/bs-mobile/releases/download/store-art-534-20261029/README-r1.md), [QA](https://github.com/hyunlord/bs-mobile/releases/download/store-art-534-20261029/QA-r1.md), [출처·변환 기록](https://github.com/hyunlord/bs-mobile/releases/download/store-art-534-20261029/STORE-ART-PROVENANCE-r1.json)

내부 시각 검토 PASS92. A는60px에서 문장이 가장 선명하며, B는 얼굴·푸른 망토가 우선 읽히고 문장이 작습니다. C는 집·황금밭·깃발의 구성이 읽힙니다. 세 포스터의 제목과 문장은 잘리지 않았습니다. 복잡했던 C 첫 생성본은 미사용으로 로컬 보존하고 단순화한 내부r2를 공개 C r1에 사용했습니다.

생성 원본은 아이콘1254², 포스터 A/B853×1844·C852×1846입니다. 요청 규격은 기계적 정규화 결과이며 포스터는 확대본입니다. 균등 배율을 유지하고 포스터 좌우를 각 약1.689px(A/B)·0.231px(C)만 잘랐습니다. 내용 재그리기·워터마크 추가는 없습니다. 원본·실패 이력은 `output/store-art-concepts-20261029/art/raw/`에 보존했습니다.

공개 범위는11PNG와 짧은 README·QA·provenance JSON 총14파일입니다. 새 Release를 사용하며 ZIP을 만들지 않습니다. 스토어 심사·실기·게임 적용은 NOT_RUN입니다. 문서 PR은 사용자 승인 대기로 유지합니다.
