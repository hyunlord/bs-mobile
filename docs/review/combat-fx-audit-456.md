# 전투 FX 감사와 잉크·과슈 시안 #456

관문: 부분 — 감사·결정용 시안 3종 게시 완료, 정적 비교 QA 91/100 통과. 사용자 승인·실전 판독성은 미확인입니다.
게임 코드·데이터·원본 atlas를 수정하지 않습니다. 새 그림의 최종 승인과 게임 적용은 별도입니다.
추적: [#456](https://github.com/hyunlord/bs-mobile/issues/456). [Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-combat-fx-audit-456-20261028): 자산 16개 게시 확인.

## 감사 결과와 선정 근거

실제 호출 가능한 주요 그림 31 binding 행과 공통 피드백 12행을 정리했습니다. 같은 그림을 서로 다른 source가 호출하는 행을 유지했으며, 전부 동시에 나타난다는 뜻은 아닙니다. FP의 기본 무기 10종·도구 8종, 특허장 8종·진화 8종을 프로필 override와 대조했습니다. 특허장 8종은 유효 operation이 모두 stat-add로 독립 공격 FX가 없습니다. 진화 8종도 자체 damage-pulse가 없고 기존 source 그림과 해금 공통 피드백을 사용합니다. manifest binding만 있는 진화를 실제 발생 FX로 세지 않았습니다.

일반 wave 번개는 절차 생성 경로이므로 atlas 교체만으로 바뀌지 않습니다. M1 리뷰 설정 전용 34 role과 기본 진화 목록에서 도달하지 않는 horn 2종도 일반 플레이 개선 대상으로 합산하지 않았습니다.

FP의 포자 장판·방어 구체·불씨 발사체는 광택과 매끈한 후광이 키아트 C의 잉크·과슈 질감과 어긋나므로 첫 결정용 시안으로 선정했습니다.

| FP 시안 대상 | 역사 576사례 중 기록 사례 | 역사 발동 시도 | 역사 캡처 배율에서의 비교 크기 |
|---|---:|---:|---|
| spore_fan 장판 | 300 | 1,017,083 | 156.80×156.80px × pulse, 알파 .10 |
| ward_orbit 구체 | 270 | 954,658 | 47.44×41.07px, 알파 .72 |
| ember_wand 발사체 | 243 | 728,221 | 72.88×41.07px, 알파 .72 |

발동 수는 [역사 봇 리그 진단](../league/first-playable-m1/README.md)의 합계이며 현재 사용자 등장 빈도나 렌더링된 입자 수가 아닙니다. 출현 범위로 보면 iron_blade가 576/576사례로 가장 넓지만, 이번 선정은 발동 시도 상위 3종과 화풍 불일치를 함께 고려했습니다. 일반 wave의 ward는 방패 조각으로, 이 표의 FP 파란 구체와 다릅니다.

원본 검사에서 `wave1a.wave.shield-fragment.default`에 옛 백합 문장이 남아 있음을 확인했습니다. 이전 승인된 새싹 방패의 통합 여부는 별도 확인 대상이며, 이번 시안으로 기존 방패까지 바뀌었다고 표현하지 않습니다.

## 출처와 1배 비교의 의미

정적 renderer·atlas 감사 기준은 game checkout `0537368a143fb7faf4da3698f9b7b5115f1498c9`입니다. 문서 checkout은 [PR #454](https://github.com/hyunlord/bs-mobile/pull/454)가 병합된 `8bffc4d52783dbe56fcb13e7c5360b56a994779a`를 기반으로 합니다. #454 병합은 비인간 걷기 그림 검토 기록의 이력이며 이번 FX 통합·런타임 검증을 뜻하지 않습니다.

실제 조립 비교판 배경은 보존된 FP Editor Mono/Metal 캡처 `stress-replay-30000-early-tick900-portrait.png`입니다. 감사에서 참고한 season tick 6751 캡처와 별도로, 조립에는 early tick 900을 사용합니다. source `373b43c16a0b0aa24a27d98497165a9479e82f96`, tick 900, 900×1600px이며 같은 manifest의 cameraHalfHeight는 4.285714149475098입니다. 따라서 1 world-unit은 186.6666726px입니다. 원본 PNG SHA256은 `10e676ad588fd192ab58467d466adbed5ea7cd7a9c7fb6930edf0cd2d9938137`이며 manifest와 일치합니다.

이 캡처는 무적·디버그 적 생성·레벨 지급을 사용한 스트레스 fixture로 정상 플레이·성능 증거가 아닙니다. 표의 크기는 현재 renderer 계약을 이 역사 캡처 배율에 환산한 값입니다. 시안을 배경에 올리는 비교판은 조립 이미지이며 실제 게임 교체 캡처가 아닙니다. 새 현재 런타임 재생은 NOT_RUN입니다. 화면의 실제 적 가림, 아군·적 구분 및 정상 플레이 빈도는 미측정으로 남깁니다.

## 납품 상태와 검증 경계

작업 원자료는 `output/combat-fx-audit-20261028/AUDIT.md` 및 `audit/runtime`, `audit/visual`, `audit/frequency`에 보존합니다. 상세 감사에는 source→state→role, crop, frame, world 크기, 알파·tint 경로와 위험 추정을 포함합니다. 최종 구성은 포자 r3·방어 구체 r1·불씨 r1입니다. 독립 정적 비교 QA 91/100 통과 후 다음 자산을 게시했습니다.

- [전체 감사](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/AUDIT.md) · [자산 목록](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ASSETS.md)
- [실제 캡처 배율 1배 조립 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/three-fx-native-calibrated-before-after-r3.png)
- [포자 원본 1배·3배 비교 r3](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/spore-source-1x-3x-before-after-r3.png)
- [방어 구체 원본 1배·3배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ward-source-1x-3x-before-after.png)
- [불씨 원본 1배·3배 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/ember-source-1x-3x-before-after.png)

포자 r1/r2는 실제 배율 판독성 미달(86/89점)로 보존했습니다. r3는 알파 .10과 크기를 유지한 채 넓고 짙은 6개 안료 덩어리로 둘레를 읽히게 했습니다. 고리가 더 규칙적이고 보라색 정체성이 약해지는 절충이 있으며, 움직임·중첩·다른 바닥에서의 판독성은 미검증입니다. [최종 QA](https://github.com/hyunlord/bs-mobile/releases/download/m3-combat-fx-audit-456-20261028/QA-r3.md)는 이 고립된 정적 비교만 통과시킵니다.

게임 수정이 없는 문서 작업입니다. 문서 내용·로컬 링크 직접 검사와 공백 검사는 오류 0입니다. 커밋 범위 문서 검사와 PR CI 결과는 PR에 기록합니다. 사용자 그림 승인, 실제 게임 통합, 새로운 Unity 실행 및 프레임 성능 측정은 이번 문서의 완료 조건에 포함하지 않습니다.

## 불씨 공병 도화선 제안

기존 메모의 도화선 불꽃 깜빡임은 미착수 상태를 유지합니다. 제안은 기존 도화선 끝 기준점을 고정한 작은 따뜻한 불꽃 2~4프레임으로, 끝 부분만 약하게 변형하고 몸·손·소품은 유지하는 것입니다. 큰 후광이나 강한 점멸을 피합니다. 이는 별도 제안이며 제작 완료·게임 적용·사용자 승인이 아닙니다.

설계 문서 영향: 없음 — 현재 구현을 감사하고 시안의 판단 자료를 제공하며 규칙이나 승인된 디자인 결정을 변경하지 않습니다.
