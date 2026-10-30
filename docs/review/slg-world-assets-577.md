# SLG 월드맵 게임용 그림 자산 r1 · #577

상태: **오브젝트 r1 사용자 승인, 지형 r1 불합격·r2 재작업**. 추적 [#577](https://github.com/hyunlord/bs-mobile/issues/577). 서버 프로토타입 연결 전에 월드 지형·목적지·행군 표식을 준비했다. 사용자는 성·거점·몬스터·자원지·행군 깃발을 승인하고 #578을 이 오브젝트 기준으로 병합하도록 결정했다. 이 PR은 기록 문서만 변경하며 게임 코드·기존 데이터·원본 atlas를 변경하지 않는다. 병합은 지형 r1 채택이나 게임 적용을 뜻하지 않는다.

## 사용자 검토 결정 · 2026-10-30

| 구분 | 판정 | 적용 범위 |
|---|---|---|
| 성 3단계·거점 2종·몬스터 3종·자원지 4종·행군 깃발 3진영 | **사용자 승인** | r1 오브젝트 원본과 1/2배 출력 유지 |
| 평원·숲·언덕/산·물 지형, 강·해안·하구 및 지형 조립판 | **r1 불합격, r2 재작업** | 단색 갈색 평원, 칸 계단 숲 경계, 칸을 따라 직각으로 꺾이는 가는 강, 직선 해안을 교체 |
| 숲·산 지형 오브젝트, 보조 방향 화살표 | 승인 범위 확대 해석 금지 | 위의 명시적 승인 목록과 구분하여 원본 보존. 숲·산은 지형 r2 조립 검토에 포함 |

지형 r2는 후속 [#579](https://github.com/hyunlord/bs-mobile/issues/579)에서 승인된 영지의 평원·숲·언덕·습지 패치를 섞고, 지형 경계를 부드러운 마스크로 번지게 한다. 강은 승인된 영지 r2 계열 조각으로 굽이와 폭 변화를 만들고, 해안은 모래톱·바위를 섞은 불규칙한 선으로 보강한다. 같은 100%/50% 배율 비교와 영지 r8 화풍 비교를 다시 제출하며, 50%에서도 숲·산·평원·물이 색 덩어리로 구분되어야 한다.

아래의 33종·66 PNG 전달 기록과 **91/PASS는 당시 내부 오프라인 검사 이력**으로 보존한다. 최신 사용자 판정이 지형에 대한 그 내부 판정보다 우선한다. r1 Release의 지형 PNG와 비교판을 보존하는 것은 채택 승인이 아니며, 지형 r2 사용자 승인 전 r1 지형을 승인 자산으로 적용하지 않는다.

## 근거와 규칙 경계

[GDD18](../design/gdd/18-slg-world-map-proposal.md)의 첫 출시 월드는 **논리 서버 하나의 512×512 타일 [가설]**이다. 512×512는 그림 해상도가 아니다. 계정 도시 표식은 논리 타일 하나이며 개인 영지 r8의 13구역·1,664칸과는 별도 좌표 공간이다. 평지·숲은 통과 가능, 산·물은 통과 불가, 고정 다리는 통과 가능하고 경로는 상하좌우 이동이다. 그림의 해안·길·장식이 새로운 통행 규칙이나 영토 소유권을 만들지 않는다.

승인된 [SLG 화면 시안 #563](https://github.com/hyunlord/bs-mobile/pull/563)과 [512 정합 기록](slg-ui-flow-561.md#r2--월드-크기-정합-사용자-승인--2026-10-30)은 흐름·화면 구성의 근거다. 해당 시안의 배경은 실제 서버 배치가 아니며 렌더러 셀 크기·피벗 승인으로 확대 해석하지 않는다.

## 이번 그림 범위

| 묶음 | 내용 | 규칙에 추가하지 않는 것 |
|---|---|---|
| 지형 | 기본 바닥 4종·강 6종·해안 1종, 숲·산 오브젝트 2종 | 새 지형 통행·지도 생성 규칙 |
| 자원지 | 곡물·목재·철재·석재 4종 | 석재 지갑·수급·가격·채집 경제 |
| 몬스터 무리 | 승인된 진드기·말벌 스프라이트를 조립한 2종, 새 골렘 1종 | r1 외형은 사용자 승인. 전투 수치·스폰·보상 추가 없음 |
| 거점 | 폐허 표식·강가 수비 거점 2종 | 영구 점령·영토 수익 |
| 다른 영주 성 | 목책 전당·석조 성채·다탑 성채 3단계 | 도시 레벨·성능·해금 규칙 |
| 행군 | 영웅과 병력 깃발, 아군·연맹·적 식별, 격자 네 방향 표시 | 행군 칸·속도·경로·동맹 판정 변경 |

석재 자원지는 사용자가 요청한 **그림 자산**으로 준비한다. 현재 GDD18 채집 경제의 곡물·목재·철재에 석재를 자동 추가하지 않는다. 성 3단계 역시 외형 단계이며 기존 영지 건물 레벨과 연결한 구현이 아니다. 새 골렘은 이번 r1 오브젝트 검토에서 승인된 외형이며, 제작 이전부터 승인된 전투 원화였다고 소급하지 않는다.

전달 집합은 **논리 자산 33종·1/2배 PNG 66장**이다. 지형 12종(기본 4·강 6·해안 1·하구 1), 오브젝트 14종(숲/산 2·자원 4·몬스터 3·거점 2·성 3), 행군 7종(진영 3·방향 화살표 4)으로 나눈다. 산은 낮은 돌 더미와 석재 자원지가 혼동되지 않도록 봉우리 실루엣을 새로 제작했다. 강의 내부 r3 및 원본 r4 포트 보정은 이번 월드 자산 제작 이력이며, 사용자가 채택하지 않은 과거 영지 강 r3를 재사용한 것이 아니다.

## 내보내기 계약 제안

다음은 이번 자산 검토를 위한 **신규 제안**이며 런타임 승인값이 아니다. Unity 적용 담당자가 최종 승인·실제 화면 검증 뒤 연결한다.

| 분류 | 1배 | 2배 | 기준점 |
|---|---|---|---|
| 지형 셀 | 64×32 등각 마름모 | 128×64 | 중앙 (32,16) / (64,32) |
| 목적지·지형 오브젝트 | 96×112 투명 PNG, 바닥 64×32 | 192×224, 바닥 128×64 | (48,88) / (96,176) |
| 행군 진영 표식 | 48×64 투명 PNG | 96×128 | 바닥 중심 (24,52) / (48,104) |
| 격자 방향 화살표 | 24×16 투명 PNG | 48×32 | 중앙 (12,8) / (24,16) |

고해상도 생성 원화와 정규화된 게임용 PNG를 구분한다. 접지 그림자 전체 bbox를 건물 크기로 간주하지 않고 의미 있는 바닥 기준점과 실루엣으로 정렬한다. 실제 셀당 그림 수·atlas 편성·그리기 호출 수는 이번 오프라인 그림 제작으로 검증되지 않는다.

기준점은 PNG 왼쪽 위 원점의 y-down 픽셀 좌표다. Unity 정규화 피벗으로 옮길 때는 `(x/width, 1-y/height)`를 사용한다. 방향 표시는 부대 원화를 돌리지 않고 별도 화살표로 겹친다. 아군 파랑·연맹 녹청·적 적갈색과 깃발 외곽의 구분은 이번에 승인된 행군 깃발 외형에 포함되며, 런타임 연결을 검증했다는 뜻은 아니다.

## 화풍과 판독성

승인된 영지의 잉크·과슈, 좌상단 광원, 따뜻한 세이지·황토·회색과 깊은 강물 톤을 따른다. 배경 바닥은 대비를 낮추고 자원·적·도시의 실루엣을 구분한다. 떠 있는 두꺼운 타일 받침, 테두리 마름모, 그림에 구워 넣은 제목·UI는 사용하지 않는다. 아군 문장은 씨앗 하나·휜 줄기·높이가 다른 잎 두 장이며 백합·사자·금색 왕가 장식을 추가하지 않는다.

오브젝트는 50%·100%·200% 실제 배율 판독 비교판으로 검토한다. 월드 조립은 같은 배치 fixture를 50%·100%로 보여 주는 390×844 두 화면을 사용한다. 배율마다 다른 배치를 그려 밀도나 크기를 숨기지 않는다. 색만으로 진영을 구분하지 않도록 깃발·방향·실루엣도 함께 확인한다. fixture는 오프라인 표본이며 실제 서버 월드·게임 실행·터치·성능 증거가 아니다.

오프라인 타일 조립은 nearest 필터를 사용한다. 이 조건의 이음새 검사는 Unity의 bilinear 샘플링 검증을 대신하지 않는다. 게임 적용 시 실제 texture filtering, atlas gutter/패딩·UV 경계, bilinear에서의 타일 이음새를 별도 확인해야 한다.

## 원본과 전달 기록

새 오브젝트는 내장 이미지 생성 도구로 개별 제작하고 생성 원본·프롬프트·출처 경로를 보존한다. 승인된 지형을 재사용한 경우 최종 자산 목록에 원본과 변환을 기록한다. 기존 파일을 덮어쓰지 않으며 실패·보류 후보도 보존한다.

- [Release · 월드 자산 33종](https://github.com/hyunlord/bs-mobile/releases/tag/slg-world-assets-577-20261030)
- [전체 조각 목록](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/ASSET-LIST-r1.md), [게임용 manifest](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/MANIFEST-r1.json), [PNG 66개 파일명](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/SELECTED-PNG-FILES.txt)
- [같은 배치의 두 배율 비교](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-two-zooms-comparison-r1.png), [100% · 390×844](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-local-fixture-100-390x844-r1.png), [50% · 390×844](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-local-fixture-50-390x844-r1.png)
- [자원 4종](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/resources-native-r1.png), [성 3단계](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/castles-native-r1.png), [몬스터 3종](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/monsters-native-r1.png), [석재와 산 구분](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/stone-versus-mountain-native-r1.png)
- [거점 2종](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/strongholds-native-r1.png), [행군 확대·축소](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/march-native-r1.png)
- [행군 진영·방향](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/march-factions-directions-r1.png), [강 6조각 연결](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/river-matching-port-chain-r1.png), [바다 반복](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/sea-repeat-r1.png)
- [내보내기와 검증 범위](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/EXPORT-NOTES-r1.md), [최종 시각 검토](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/independent-visual-review.json), [정적 재검사](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/root-final-validation.json)

PNG와 `MANIFEST-r1.json`을 같은 디렉터리에 놓으면 `variants`의 파일명이 그대로 연결된다. `sourcePathBase=production-root`는 보존된 제작 원본의 출처이며 Release에 원본 전체를 복제했다는 뜻이 아니다. 신규 ZIP은 만들지 않는다. 원본·실패 후보·중간 비교판은 제작 출력에 보존하고, Release에는 최종 자산과 위에 선정한 판독·접합 비교 표본을 전달한다.

## 검증 상태

| 항목 | 현재 상태 | 증명 범위 |
|---|---|---|
| GDD18·#563 화면 계약 대조 | 문서 확인 | 논리 월드 크기·통행·시안과 런타임 계약 구분 |
| 자원·거점·성 원화 9종 | 원화 생성 및 alpha 존재 확인 | 고해상도 원화의 투명 채널. 최종 출력 검사 아님 |
| 최종 PNG 66장·33종 집합, 크기·알파·기준점·조각 목록 | 통과 | 최종 집합 누락·중복·크기·기준점 오류 0, 원본 SHA 재검사 33건 오류 0 |
| 전체 실제 크기·줌·진영·조립판 시각 QA | 당시 내부 91/PASS 보존 | 최신 사용자 판정은 오브젝트 승인·지형 r1 불합격. 내부 점수를 지형 승인으로 사용하지 않음 |
| Release 파일명·업로드·문서 링크 | 게시 후 대조 | manifest와 게시 파일명 확인, 아트 채택과 별개 |
| Unity 게임 적용·atlas·draw call·filtering/gutter/bilinear 이음새 | NOT_RUN | nearest 오프라인 조립과 별도 검증 |
| 실제 서버 지도·경로·타일 배치 | NOT_RUN | 오프라인 조립은 서버 결과가 아님 |
| 휴대폰 판독·터치·성능 | NOT_RUN | 실기 실행 없음 |
| 최종 사용자 그림 승인 | 오브젝트 승인·지형 r1 불합격 | 위의 명시적 승인 목록만 채택. 지형 r2는 별도 사용자 검토 필요 |

강은 검사한 연결 경로에서 1배 연속 판독이 가능하지만 모든 조합의 수면 폭·색이 픽셀 단위로 일치한다고 주장하지 않는다. `castle-tier2`는 캔버스 가장자리의 매우 낮은 알파 잔여가 1배 1픽셀·2배 2픽셀 검출됐으며 건물 본체는 잘리지 않았다. 50% 행군은 색과 깃발 외곽으로 구분하고 새싹 내부 잎·씨앗까지 분해해 읽는 크기로 보지 않는다.

시각 검토 담당자는 행군 원화 제작에도 참여했으므로 해당 부분은 리더가 별도로 재확인했다. 조립판은 지형·소품을 판독하기 위한 고정 fixture이며 최종 월드 배치·밀도 승인본이 아니다. 정적·오프라인 검사 통과를 런타임 또는 사용자 승인으로 보고하지 않는다.
