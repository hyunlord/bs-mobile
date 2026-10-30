# 승인된 모바일 ASTC4 적용 계획 (#487)

1. 승인된 hero-knight / fallow-enemy-rigs / estate-decor의 PNG 동일성 확인 후 Android/iOS override만 변경한다. Mac/Default와 원본PNG는 보존한다.
2. 원본 padding 검증을 source PNG 임시 RGBA 읽기로 분리한다. 기존 허용alpha 규칙은 그대로, 실패 회귀검사를 둔다.
3. PR 변경 texture에만 before/after 메모리 추정 기록을 요구한다. 이전 LFS oid·size와 추정상태를 고정하고 현재 PNG/메타·산식을 확인한다. 전역예산 강제 없음.
4. Node 음성 대조·EditMode importer 계약검사·두 프로필을 검증한다. Unity 슬롯 승인 전은 정적 검사만.
5. 기존 실제 ASTC4 동일PNG 시안과 변경하지 않은 FX 대조군을 1x/3x로 제출하고 draftPR/최종CI까지 진행한다. 모바일 실기와 병합은 범위 밖.
