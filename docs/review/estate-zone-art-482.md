# r8 남은10구역 콘셉트 r1

관문: 내부 시각 QA 통과 — 경작지92·휴경지91·폐허92·숲93·언덕90·주변91·시작92. 새 그림 사용자 승인 PENDING, 게임 코드·데이터 변경 없음.

7개3단계 비교판이10개 논리 구역을 다룹니다. 기존 승인 마을·풍차·강변은 보존합니다. 단계 순서는 어둠 / 되찾은 직후 / 개발 후이며, 밝아지는 순간 완성 시설이 생기는 것으로 표현하지 않습니다. 잉크·무광 과슈, 좌상단 빛, 낮은 채도의 세이지·황토 바닥과 남색 어둠 식생을 기준으로 합니다.

|세트|어둠→직후→개발 후|원화/후처리 후보|현재 QA|
|---|---|---|---|
|경작지|무너진 고랑→정리한 농지·기초→곡물·채소·작은 도구 헛간|fields r2|PASS92|
|휴경지|방치 초지→정리한 휴식 토양→클로버 목초·가축·지력 회복 소품|fallow r5 registered, 강·다리 문맥 유지|PASS91|
|폐허|묻힌 석조→노출·분류한 회수재→남긴 아치 주변 석재 회수 작업장|ruins r1|PASS92|
|숲|어두운 숲→작은 회복 공터→관리 벌목·목재·다시 심기|wood r2 어둠 수정|PASS93|
|언덕|풀이 덮인 바위→노출된 암반→얕은 채석장·기중 장치·가공 돌|hill r2 registered|PASS90, 약32px 모서리 잔차|
|주변 공통|방치 경계길→정리된 접근→생울타리·담·경계표|outskirts r3 registered-clean|PASS91|
|시작|10폐터→완성 집1+Lv0부지9→10개 구분 시설|start r2 registered|PASS92|

시작의 어두운 폐터는 과거 풍경을 위한 콘셉트이며 실제 게임의 시작 상태를 추가하는 제안이 아닙니다. 주변 공통판은 재료·분위기 공유용으로 북/남20×4와 동/서4×12의 바닥을 동일 그림으로 늘이거나 회전해 맞춘 자산이 아닙니다.

## 크기·출처·정밀도

기본 셀64×32,2×2바닥128×64는 정확한 설계 가이드 기준입니다. 검토판768×768/비교판2304×768은 fields·hill 배율1, fallow·ruins·wood 배율0.625, start 및 북/남 주변5/6을 적용합니다. 동/서 주변 가이드는 배율1입니다. 따라서 그림 검토판의 모든2×2가128×64인 것은 아닙니다.

생성 도구로 만든 원화와 거절 후보는 export/raw에 보존합니다. 각 세트 metadata는 원화 파일·SHA-256·크기·후처리 배율·등록 변환·QA·납품 파일 해시를 기록합니다. 등록된 파일을 최초 생성 원화라고 부르지 않으며 originalGeneratedSHA256과 sourceSHA256을 구별합니다. 기계적 crop·resample·명시된 투영 등록은 그림의 의미를 다시 칠하는 작업과 구별합니다.

언덕은 세 단계에 동일 균일 배율 약0.78155와 평행이동을 적용했고 비등방 찌그러뜨림이나 마스킹을 하지 않았습니다. 모서리 추정 불확실성 약15px, 최종 형상 잔차 약32px가 남아 콘셉트 수준으로만 통과했습니다. 정확한 runtime sprite·충돌·건물 기준점 정합으로 주장하지 않습니다. 휴경지는 세 단계에 공통 affine 카메라 보정을 적용해12×20바닥을 맞췄고 세로 디테일이 약5.29도 기울어집니다. 양·울타리·다리의 왜곡이 검토 크기에서 방해하지 않는 콘셉트로 PASS91 판정했습니다. 원래 r4와 변환 metadata를 보존하며 픽셀 단위 runtime 등록이라고 주장하지 않습니다. 시작은 공통 affine 등록 후 모서리 잔차 약5.3–6.2px이며 건물10역할의 상대 위치를 검토했습니다. 정확한 그림 밑동 좌표를 증명하지 않습니다. 주변은 등록 후 중앙 패널의 빈 오른쪽8px crop으로 떠돌던 조각을 제거했고 최종 비교판을 재검토했습니다. 변환된 밝은 패널 여백은 지형이 아닙니다.

## 납품·보존 계획

그림45PNG는 단계21·비교판7·가이드중첩7·정확한 네이티브가이드10개입니다. 세트별metadata7개가 별도입니다. 최종 허용 목록으로만 게시합니다. 그림45PNG·세트metadata7개·README/QA2문서의 총54개 추가 납품입니다. 기존 Release44파일의ID·digest 기준선을 보존합니다. 실패 기록을 지우거나 새 그림 사용자 승인을 내부 QA로 대체하지 않습니다.


## 파일 찾기

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-expansion-concept-468-20261028)에서 expansion-r8 접두어 파일을 확인합니다. 각 세트의 three-stages, dark/just-reclaimed/developed-later, guide-overlay, native-guide, metadata 파일을 함께 읽습니다. 주변은 북·동·남·서 개별native-guide4장입니다.

|세트|3단계 비교|
|---|---|
|경작지|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-fields-three-stages-r1.png)|
|휴경지|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-fallow-three-stages-r1.png)|
|폐허|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-ruins-three-stages-r1.png)|
|숲|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-wood-three-stages-r1.png)|
|언덕|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-hill-three-stages-r1.png)|
|주변|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-outskirts-common-three-stages-r1.png)|
|시작|[비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-expansion-concept-468-20261028/expansion-r8-start-three-stages-r1.png)|

## 검토 이력 — 실패 보존

|후보|판정|점수|주요 근거|
|---|---|---:|---|
|inner-fallow-r1|revise|77|Ground is enlarged near-symmetric diamond rather than strongly asymmetric12×20 guide; copied two-foundation windmill composition overrides zone geometry.|
|inner-fallow-r2|revise|88|Rear ground margin extends approximately 50–60 review pixels beyond the cyan guide in all stages, adding a substantial strip rather than only vegetation overhang.|
|inner-fallow-r3|revise|89|Developed shelter footing is now inside the guide, correcting the previous anchor issue.|
|inner-fallow-r4|revise|88|Rear floor still extends beyond the guide; overall corner mapping remains displaced.|
|inner-fallow-r5-registered|pass|91|Common affine registration slightly tilts vertical details (exporter reports about5.29 degrees); no visually distracting animal, fence or bridge deformation at concept review size.|
|inner-fields-r1|revise|78|Painted floor nearly fills each panel and rises above required576×288 guide;8×10 skew is weakened toward square.|
|inner-fields-r2|pass|92|Mature crop yellows remain stronger than the sage ground but appropriately identify the developed stage.|
|inner-northeast-wood-r1|revise|88|Dark-stage broadleaf canopies remain olive with lit cream/yellow highlights, visibly brighter than adjacent dark soil; they do not read as the required dark navy silhouettes.|
|inner-northeast-wood-r2|pass|93|Organic tree crowns overhang the logical floor, as expected.|
|inner-ruins-r1|pass|92|Painterly ground margins are approximate, not pixel-registered production geometry.|
|inner-west-hill-r1|revise|86|Floor lateral span is visually approximately710 review pixels versus576 required, about23% wider; each side corner is roughly60–75 pixels outside the cyan guide.|
|inner-west-hill-r2-registered|pass|90|Approximately32 review pixels of corner/shape residual remain after uniform registration; this is a contextual concept, not a production-registered sprite.|
|outskirts-north-r1|revise|88|Long narrow strip is offset relative to its guide, particularly at the left end; fence and tree ground anchors occupy the rear offset band.|
|outskirts-north-r3-registered-clean|pass|91|Representative20x4 strip shares material/mood with all outskirts, but cannot be stretched into the separate4x12 east/west footprint.|
|start-r2-registered|pass|92|Illustrated building silhouettes and positions approximate the fixed slots; this does not prove exact atlas or runtime anchor registration.|

공개 파일명 r1은 이번 세트의 첫 납품판이라는 뜻입니다. 원화 반복 번호와 다릅니다. 우선3종은 경작지 원화r2·휴경지r5-registered·폐허r1을 각각 공개r1으로 제공하며 metadata의 sourceRevision으로 구별합니다.

## 게시·범위 검증

실제 Release 읽기 결과 총98파일이다. 기존44파일ID·이름·크기·digest는 모두 그대로이며 새54파일(45PNG·7JSON·2MD)의SHA-256 및 추가 본문이 일치했다. 이번 커밋은 이 검토 문서1개만 포함한다. 게임 코드·데이터·기존 atlas 변경0이며 제품용 새 설정도 만들지 않았다. 내부 시각 QA와 자산 게시 검증은 통과했으나 사용자 최종 그림 승인과 실제 게임 적용은 별도다.
