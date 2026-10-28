# r8 영지 밀도·두 카메라 아트 검토

관문: 통과 — 독립 시각 검토91점, 신규 자산120PNG·여섯390×844 화면·비교판3장 검증. 최종 미술 승인과 기본 배율 결정은 사용자 대기다. 게임 코드·데이터·실행 화면은 변경하지 않는다.

이 기록은 #493의 산출물이다. 사용자는 같은 세 장면을 집 표시 너비 약75px 기본 후보와50px 축소 보기로 모두 비교하기로 했다. 최종 기본 배율은 비교 후 사용자가 결정한다. 두 화면은 동일한 월드 배치를 사용하고 카메라 배율만 바꾼다.

## 화면과 자산의 범위

정본 r8의13개 사각 구역,1664셀, 시작 구역12×12, 기존10개 건물 좌표,100개 논리 강물 셀과3개 다리 기준점을 유지한다. 승인된 기존 건물·지형과 추가 소품을 기계적으로 합성한다. 영지 바깥의 어두운 숲과 안개는 비플레이 장식 배경이며 영지 셀·확장 구역을 늘리지 않는다. 바깥 나무의 수관은 경계를 자연스럽게 덮을 수 있다.

현재 건물 atlas256×320의 기준점은(128,280)이고 현재 렌더러의 atlas→native 배율은0.7이다. 집의 alpha>8 너비141px에0.7을 곱한98.7native 단위를 기준으로 카메라 배율75/98.7≈0.759878와50/98.7≈0.506586을 쓴다. 다른 건물의 폭은 원화마다 다르며 건물별로 임의 확대하지 않는다. 작은 농막·장작 차양은 소품으로서 별도 명시한 균등 배율을 사용한다.

## 측정 정의

깨끗한390×844 화면에서 실제 카메라 필터링을 적용한 각 PNG의 알파와 가림 순서로 보이는 픽셀을 계산한다. 앞쪽 물체를 통과한 알파 기여도가32/255 이상인 픽셀이8개 이상인 인스턴스만 센다.128/255도 함께 기록한다. 건물·지형 소품·길·배경 나무·안개를 분리하며 바닥 텍스처와 흙 조각은 소품 점유로 세지 않는다.

논리 영지 면적/전체 화면 비율, 강물 면적, 길 면적과 마른 영지의 빈 땅 비율을 별도로 기록한다. 나무 그림자·흙 가장자리처럼 소품에 포함된 배경 픽셀의 한계와, 바깥 나무 수관이 논리 영지를 가리는 경우도 공개한다. 숫자는 미술상 밀도 판정을 대체하지 않는다.

## 보존과 검증

이전 Release `estate-r8-terrain-488-20261028`의228개 자산과 모든 실패 시안을 보존한다. 신규 파일은 별도 이름으로 추가하고 원판을 덮어쓰지 않는다. 신규 소품6종은 밝음/어둠·1/2배24PNG, 새 강은92PNG, 안개2종은1/2배4PNG이다. 기존 건물과 승인된 길·풀 원화는 출처와 해시를 기록하여 재사용한다.

게임 실행·실기 성능·사용자 최종 그림 승인은 `NOT_RUN` 또는 대기로 남긴다. 문서 PR/CI가 미술 승인이나 게임 적용을 뜻하지 않는다.

## 납품과 관문 결과

[기존 Release에 r2 추가](https://github.com/hyunlord/bs-mobile/releases/tag/estate-r8-terrain-488-20261028): 기존228개 보존, 신규138개(제작120PNG + 검토9PNG + JSON7 + 문서2). ZIP 없음.

- [start-village 75px/50px 비교판](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/dense-r8-start-village-75-vs-50-r2.png)
- [river-fallow-riverside 75px/50px 비교판](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/dense-r8-river-fallow-riverside-75-vs-50-r2.png)
- [forest-hill 75px/50px 비교판](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/dense-r8-forest-hill-75-vs-50-r2.png)
- [조각 목록·기준점·출처](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/DENSE-PIECES-r2.md)
- [실제 배치·카메라·공개 원본 링크](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/DENSE-SCENES-r2.json)
- [밀도와 건물 크기 측정](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/DENSE-METRICS-r2.json)
- [검증 요약](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/DENSE-QA-r2.md) / [해시 provenance](https://github.com/hyunlord/bs-mobile/releases/download/estate-r8-terrain-488-20261028/DENSE-PROVENANCE-r2.json)

| 장면 | 카메라 | 지형·소품 수 | 실제 빈 땅 | 보수적 빈 땅 |
|---|---:|---:|---:|---:|
| start-village | 50px | 394 | 17.4% | 20.5% |
| start-village | 75px | 264 | 19.5% | 21.4% |
| river-fallow-riverside | 50px | 307 | 20.5% | 24.4% |
| river-fallow-riverside | 75px | 168 | 27.4% | 33.4% |
| forest-hill | 50px | 329 | 21.3% | 22.9% |
| forest-hill | 75px | 202 | 19.2% | 19.4% |

실제 빈 땅은 외부에 기준점을 둔 불투명 나무 수관이 논리 영지를 덮은 면적도 가려진 것으로 취급한다. 안개는 점유로 계산하지 않는다. 기존 보수적 값도 유지했다. 강변75px의27.4%는 내부 검토용20% 목표보다 높다. 시각 검토 후 수치를 맞추기 위한 추가 장식은 중단하고 사용자 검토용으로 동결했다. Astra B와 정확한 배치·밀도 동등성을 주장하지 않는다.

실패r1–r8와 원본,30개 최종 픽셀 마스크는 로컬 `output/estate-r8-dense-20261028`에 보존한다. 최종r9는 물 위로 튀어나온 텃밭2개만 제거했다. 독립 검사에서 소품 기준점·bbox,120제작PNG,6화면 크기,전체138파일 해시,기존228개와 이름 충돌0을 확인했다. 문서 전용 `./tools/check.sh`와 필수 CI 결과는 PR의 Gate result·Verification에 기록한다. 이 검사는 실제 게임 적용이나 사용자 승인 증거가 아니다.
