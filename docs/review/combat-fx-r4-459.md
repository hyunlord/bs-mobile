# 전투 FX r4 — 원본 공격 판독성 보존

관문: 부분 — r4 제작과 고정 비교 QA 94/100 통과. r4 검토 파일 게시·기존 16개 자산 보존 확인, 사용자 승인 대기입니다.
추적: [#459](https://github.com/hyunlord/bs-mobile/issues/459). 게임 코드·데이터·원본 atlas는 수정하지 않습니다.

## 사용자 판정과 수정 방향

사용자는 [이전 FX 시안](combat-fx-audit-456.md)의 ward r1·ember r1이 원본보다 작고 희미해졌다고 판정했습니다. 이전 정적 비교 QA 91/100은 당시 고립된 배치의 내부 판정이며 사용자 승인이나 혼잡한 전투 판독성 통과로 해석하지 않습니다. 기존 비교판·실패 후보·원본은 보존합니다.

r4는 화풍보다 공격 판독성을 우선합니다. 방어 구체와 불씨 발사체는 원본의 표시 크기·밝기·대비를 유지하고 가장자리만 잉크·과슈 표현으로 수정합니다. 최종 후보는 원본 중심부 RGBA를 유지하고, 2px 외곽 띠 중 알파 77 미만인 영역에만 생성 질감을 아래에 합성합니다. `final-core-preservation.json`에서 ward 7,376px·ember 3,268px 중심부 변경 0바이트와 알파 감소 픽셀 0을 확인했습니다. 초기 생성 후보의 미달 수치는 반려 이력으로 보존합니다. 이 기술 검사는 혼잡 장면의 시각 통과를 대신하지 않습니다.

포자는 r3 내부 알파 .10을 유지하면서 경계 고리만 요구 범위 .25~.35 안에서 강화합니다. 현재 최종 경계 배율은 .34이며 알파 내장 PNG 최대 알파는 약 .349입니다. 내부와 고리의 알파 차이는 그림 시안에서 검증하며, 게임 renderer·manifest를 변경했다는 뜻이 아닙니다.

## 검증과 납품 조건

실제 캡처 배율의 1배 조립 비교에 영웅과 적 4명을 함께 배치하여 공격이 보이면서 인물도 읽히는지 확인합니다. 원본 표시 크기·밝기·대비, 내부/경계 알파와 원본 보존 수치를 시각 검사와 함께 기록합니다. 조립 비교는 실제 통합 후 게임 캡처가 아니며 정상 플레이·움직임·실기 성능 증거를 대신하지 않습니다.

최종 후보 파일은 `ward-candidate-r4-final.png`, `ember-candidate-r4-final.png`, `spore-combined-effective-alpha-r4-final.png`이며 분리된 포자 내부·경계 PNG도 보존합니다. 포자 알파 내장 PNG는 추가 renderer 알파 1로 비교하며, 내부 .10을 다시 곱하지 않습니다. 고립 배치 계측에서 ward 알파8 이상 점유 픽셀은 1,082→1,088, 배경 대비 합 +3.48%; ember는 1,425→1,448, 대비 합 +12.60%입니다. 최대 밝기와 중심부 RGBA는 유지되지만 어두운 외곽 추가로 전체 알파 가중 평균 밝기는 ward 123.49→122.27, ember 117.67→112.76으로 낮아졌습니다. 모든 밝기 지표가 동일하다고 주장하지 않습니다. 포자 경계는 72/72 방사선에서 알파 .25098~.33725로 확인되었고 내장 PNG 최대 .34902, 내부 .10입니다.

이 수치는 인물 가림이 없는 고립 배치 계측입니다. `three-fx-overlap-original-vs-r4-final-native.png` 혼잡 비교판의 시각 QA는 94/100 통과이며 아래 범위에 한정됩니다. 최종 고정 혼잡·고립 비교 시각 QA는 94/100 통과입니다. 사용자 최종 승인은 대기이며 새 런타임 재생은 NOT_RUN입니다. 실제 인물 가림을 반영한 별도 혼잡 계측에서 ward 가시 픽셀 913→922·대비 합 +4.11%, ember 1,280→1,298·대비 합 +12.67%, spore 가시 픽셀 5,544→9,305를 확인했습니다. 이는 위의 인물 없는 고립 계측과 구분합니다. 움직임·여러 장판·다른 지형 판독성은 미검증입니다.

r4 검토 파일은 [기존 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-combat-fx-audit-456-20261028)에 r4 이름으로 추가했습니다. 기존 16개 자산 ID·SHA256 동일성을 확인했으며 원본을 덮어쓰지 않았습니다. 최종 그림 승인은 사용자 담당입니다.

설계 문서 영향: 없음 — 코드·규칙·실제 renderer를 바꾸지 않는 결정용 그림 수정과 검토 기록입니다.

## 비교 자료

최종 그림 승인은 사용자 담당이며 아래는 검토 자료입니다.

- [영웅·적 4명 혼잡 1배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/three-fx-overlap-original-vs-r4-final-native.png)
- [인물 없는 1배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/three-fx-clean-original-vs-r4-final-native.png)
- [방어 구체 원본 1배·3배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ward-source-1x-3x-original-vs-r4-final.png)
- [불씨 원본 1배·3배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-source-1x-3x-original-vs-r4-final.png)
- [r4 설명·자산 해석](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/README-r4.md) · [최종 QA](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/QA-r4.md)
