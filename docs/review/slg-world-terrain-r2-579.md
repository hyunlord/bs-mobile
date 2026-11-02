# SLG 월드 지형 r2 · #579

상태: **월드맵 지형 r2 사용자 승인 · 2026-10-30**. 사용자는 CI 최종 통과 후 #580의 squash 병합을 승인했다. [#578](https://github.com/hyunlord/bs-mobile/pull/578)은 성·거점·몬스터·자원지·행군 깃발의 오브젝트 승인 범위로 병합했다. 지형 r1 불합격과 과거 파일은 보존한다. 이 문서는 승인 기록이며 게임 코드·데이터·원본 atlas를 변경하거나 게임 적용 완료를 뜻하지 않는다.

## 지형 변경과 계약

논리 월드 512×512와 오프라인 표본의 동일 오브젝트 좌표를 유지한다. 시각 셀 제안은 100%에서 64×32, 50%에서 32×16이다. 셀 마름모를 지형 경계로 자르지 않고 평원·숲·언덕·습지의 색 덩어리를 부드러운 마스크로 연결한다. 숲은 올리브색, 평원은 세이지·황토, 언덕은 바위 회색, 물은 깊은 청색으로 구분한다.

승인된 영지 강 r2의 연속 원판을 균일 확대·이동하여 사용한다. 원판 SHA-256은 `ef9a79c2c024b160deacce8d7e9b0863385fe58474a7bbd6158af86b17a73024`이다. 기존 굽이 조각은 원래 배치에서 잘라낸 문맥 의존 조각이므로 임의 접속 가능한 타일로 취급하지 않는다. 사용자가 채택하지 않은 영지 강 r3는 사용하지 않는다.

새 평원·숲·습지 패치와 모래톱·바위 해안은 승인 영지 그림을 참조하여 제작했고 이번 월드맵 지형 r2로 사용자 승인을 받았다. 특히 습지는 제작 이전의 승인 자산으로 소급하지 않는다. 승인 오브젝트는 원본 픽셀을 보존하며, 숲·산 장식은 이번 지형 검토에 포함했다. 지형 그림은 서버 통행·자원·소유권 규칙을 추가하지 않는다.

## 전달과 재조립

기존 [Release](https://github.com/hyunlord/bs-mobile/releases/tag/slg-world-assets-577-20261030)에 r2 파일을 추가한다. r1 파일을 덮어쓰지 않는다. `COMPOSITION-r2.json`과 같은 디렉터리의 PNG가 오프라인 조립 정본이며, 모든 배율은 동일 월드 원판에서 추출한다. 전체 512×512 서버 생성 지도나 최종 월드 배치가 아니라 동일 표본의 시인성 비교다.

### 검토 표본

- [동일 장면 100% / 50%](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-two-zooms-comparison-r2.png)
- [r1 / r2 두 배율 전후 비교](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-before-after-two-zooms-r2.png)
- [승인 영지 r8와 화풍 비교](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-estate-style-comparison-r2.png)
- [100% 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-terrain-r2-100-390x844.png), [50% 실제 크기](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/world-terrain-r2-50-390x844.png)
- [조립 JSON](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/COMPOSITION-r2.json)

이 표본은 r1에서 실패한 경계·강·해안과 50% 색 덩어리 판독을 확인하기 위해 선정했다. Unity 적용·atlas·필터링·그리기 호출·휴대폰 성능은 **NOT_RUN**이며, 오프라인 조립과 구분한다. 사용자 그림 승인은 완료되었으며 실제 게임 적용 검증은 별도다.

## 검증 기록

전달 집합은 재사용 오브젝트를 포함한 PNG 50장이다. [manifest](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/MANIFEST-r2.json), [크기·기준점 목록](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/ASSET-LIST-r2.md), [재조립 방법](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/README-r2.md)을 함께 제공한다. 패치 기준점은 좌상단이며 실제 표시 크기는 JSON의 폭·높이를 따른다. 오브젝트 기존 바닥 기준점은 유지한다.

| 검사 | 결과와 한계 |
|---|---|
| PNG 50장 해시·크기·RGBA | 오류 0 |
| 승인 오브젝트 15종 × 1/2배 | 30파일 원본 바이트 동일 |
| 동일 좌표·동일 월드 원판 | 100%와 50% 카메라만 변경 |
| 독립 재조립 | PNG와 JSON만 읽은 두 화면 모두 원본 비교판과 바이트 동일 |
| 최종 독립 시각 검토 | 내부 91/PASS, 사용자 승인과 별개 |
| 반복 수정 이력 | 83·87/revise와 중간 그림 보존. 큰 숲 바닥 질감·점무늬·대각 반복을 수정 |
| 남은 한계 | 풀잎 질감이 일부 두드러짐. 산은 넓은 50% 화면에서 확인하며 100%는 숲·강 중심 표본 |

[정적 검증](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/DELIVERY-VERIFICATION-r2.json), [시각 검토](https://github.com/hyunlord/bs-mobile/releases/download/slg-world-assets-577-20261030/INDEPENDENT-VISUAL-REVIEW-r2.json). 강 원판의 색은 유지하고 하류는 해안·바다 레이어로 연결한다. 이 강·해안은 문맥 조각이며 모든 방향의 임의 연결을 보장하지 않는다. 완전한 서버 지도 생성용 타일셋·성능 관문은 이번 검증 범위가 아니다.
