# 다음 전투 FX 3종과 도화선 불꽃 검토 #464

관문: 통과 — FX 3종·도화선 6프레임 내부 납품 QA 92/100. 사용자 그림 승인과 게임 적용은 별도입니다.
추적: [#464](https://github.com/hyunlord/bs-mobile/issues/464). 게임 코드·데이터·원본 atlas를 수정하지 않습니다.

## 이전 승인과 이번 범위

사용자는 [FX r4](combat-fx-r4-459.md)의 포자·방어 구체·불씨 발사체를 승인했습니다. [기존 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-combat-fx-audit-456-20261028) 본문에 이 결정을 추가했고 이전 본문과 31개 자산의 ID·digest 보존을 확인했습니다. 게임 적용은 별도이며 이 승인으로 새 작업까지 승인된 것은 아닙니다.

다음 FX는 [감사표](combat-fx-audit-456.md) 순서에서 이미 처리한 세 가지를 제외하여 선정합니다. 확정 대상은 iron_blade·harvest_scythe·canal_bow입니다. 이는 감사표에서 남은 첫 세 행의 순서이며 발동 빈도 순위가 아닙니다. 역사 발동 시도는 각각 674,548 / 209,860 / 280,623회입니다. 화풍보다 공격 판독성을 우선하고, 원본의 크기·밝기·대비와 실제 배율의 혼잡 비교를 확인합니다.

불씨 공병의 작은 도화선 불꽃은 이번에 6프레임 제작 대상으로 요청되었습니다. 이전 미착수 메모를 보존하면서 새 제작 상태를 별도로 기록합니다. 승인된 걷기 본체·손·소품 연결과 도화선 끝 기준점은 유지합니다. 큰 후광이나 전신 재생성을 범위에 넣지 않습니다.

## 실제 표시 계약 정정

FP 철날 검·수확 낫은 이번 비교 대상인 transient area 경로에서 Lv1 각각 1.7×1.7 / 2×2 world 단위의 정사각형으로 표시됩니다. 기존 역사 캡처 배율에서는 317.33×317.33 / 373.33×373.33px이며, 최고 알파 .26에서 .18초 동안 fade합니다. 이전 감사의 일반 sector 알파 .72 표기는 이 실제 transient 경로에는 적용되지 않으므로 이번 수치로 정정합니다. 운하 활은 86.63×41.07px, 알파 .72입니다. 세 FX는 모두 layer 40으로 적 50·영웅 80 아래에 그립니다.

불씨 공병 승인 r1 메타데이터의 112px 표시는 리뷰용 크기입니다. 게임 world 크기·pivot 통합 매핑이 확인되지 않았으므로 게임 1배라고 부르지 않습니다. 도화선 결과는 256px 네이티브 프레임과 112/336px 리뷰 미리보기로 구분합니다.

## FX 수치 검사와 남은 판정

세 FX 모두 원본 핵심부 RGBA 변경 0px, 알파 감소 0px, 허용 가장자리 밴드 바깥 변경 0px입니다. 인물 가림을 포함한 정적 혼잡 조립의 대비 합은 다음과 같습니다.

| 대상 | 원본 대비 합 | 후보 대비 합 |
|---|---:|---:|
| iron_blade |475,778|488,762|
| harvest_scythe |669,821|684,747|
| canal_bow |16,478|17,423|

운하 활에는 예외가 있습니다. 낮은 임계값의 네이티브 coverage는 1,603→1,575px(-1.75%), 혼잡 조립의 변경 픽셀은 858→857px(-1px)입니다. 알파≥32 coverage는 1,222→1,357px, 알파 질량은 663→755로 증가하지만, 모든 면적 지표가 늘었다고 해석하지 않습니다. 감소 원인은 미확인이며 리샘플링 때문이라고 단정하거나 Unity 실행과 동등하다고 주장하지 않습니다.

위 수치는 정적 조립·원본 보존 검사입니다. 도화선 6프레임을 포함한 최종 묶음 시각 QA는 92점으로 통과했으며, 새 런타임 통합 검증은 NOT_RUN입니다. 사용자 최종 승인은 별도입니다.

## 검증·납품 상태


현재 내부 시각 QA는 완료했으며 사용자 승인은 대기 중입니다. 실제 배율 조립 비교와 원본 보존 증거를 남기며 이를 새 게임 통합 캡처나 정상 플레이·성능 증거로 표현하지 않습니다.

같은 Release에 확정된 새 고유 파일명으로 추가했습니다. 기존 31개 자산·원본 atlas·과거 반려 기록은 덮어쓰지 않습니다. 새 그림의 최종 승인과 게임 적용은 별도입니다.

설계 문서 영향: 없음 — 규칙·renderer·데이터를 바꾸지 않는 그림 제작과 검토 기록입니다.

FX·도화선을 포함한 최종 묶음 내부 QA는 92점으로 완료했으며 사용자 승인은 미확정입니다. 도화선은 6프레임×80ms=0.48초이고 고정 마스크 바깥 변경은 6장 모두 0px입니다. 112/336px 정지 포즈 리뷰 영상은 4.8초·60프레임, 승인된 걷기 위 합성 영상은 5.76초·144프레임입니다. 이 크기는 게임 표시 크기를 입증하지 않습니다.

## 납품 파일 링크

아래는 같은 Release에 추가한 검토 파일 주소입니다.

- [three-fx-clean-original-vs-r1-native.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/three-fx-clean-original-vs-r1-native.png)
- [three-fx-crowd-original-vs-r1-native.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/three-fx-crowd-original-vs-r1-native.png)
- [iron-source-original-vs-r1-1x3x.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/iron-source-original-vs-r1-1x3x.png)
- [harvest-source-original-vs-r1-1x3x.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/harvest-source-original-vs-r1-1x3x.png)
- [canal-source-original-vs-r1-1x3x.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/canal-source-original-vs-r1-1x3x.png)
- [iron-edge-candidate-r1.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/iron-edge-candidate-r1.png)
- [harvest-edge-candidate-r1.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/harvest-edge-candidate-r1.png)
- [canal-edge-candidate-r1.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/canal-edge-candidate-r1.png)
- [ember-sapper-fuse-r1-contactsheet.png](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-sapper-fuse-r1-contactsheet.png)
- [ember-sapper-fuse-r1-review-1x.mp4](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-sapper-fuse-r1-review-1x.mp4)
- [ember-sapper-fuse-r1-review-3x.mp4](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-sapper-fuse-r1-review-3x.mp4)
- [ember-sapper-fuse-r1-approved-walk-overlay-review-1x.mp4](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-sapper-fuse-r1-approved-walk-overlay-review-1x.mp4)
- [ember-sapper-fuse-r1-approved-walk-overlay-review-3x.mp4](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-sapper-fuse-r1-approved-walk-overlay-review-3x.mp4)
- [README-next3-r1.md](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/README-next3-r1.md)
- [QA-next3-r1.md](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/QA-next3-r1.md)
- [FX-NEXT3-R1-METRICS.json](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/FX-NEXT3-R1-METRICS.json)

독립 저장 PNG 재검사에서 도화선 6장 모두 고정 마스크 [190,75,32,40] 바깥 RGBA 변경 0px를 확인했습니다. 초기 미리보기는 색 변환으로 마스크 밖 16,538px가 달라져 내부 QA 84점으로 반려되었으며 해당 실패 자료를 보존했습니다. 수정본은 저장된 RGBA를 직접 재검사했습니다. 6개 위상은 서로 다르고 종료 프레임 06은 시작 00과 바이트 단위로 같습니다. 영상 검토는 디코딩 프레임과 타이밍 메타데이터에 근거하며 실시간 재생 검증을 뜻하지 않습니다.

게시 구성: 기존 31개 + 새 26개(그림·영상·메타데이터·QA 25개와 이 README 1개) = 57개입니다. 최종 readback에서 새 26개 원격 SHA-256 전체 일치, 기존 31개 ID·이름·digest 동일, Release 본문 일치와 이전 본문 보존을 확인했습니다.
