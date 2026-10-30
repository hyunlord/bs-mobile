# 영지 실제 렌더 밀도 측정

상태: 도구·정적 검사. 사용자 화면 판정이나 Unity 검증을 대신하지 않는다.

`EstateMapFlowTests`의 R8 반/전체 복원 캡처는 PNG 옆에 `.density.json`을 내보낸다. 실제 CanvasRenderer 메시·UV·페인트 순서·상속 알파·활성 상태·마스크 교집합을 읽는다. UI 바와 런타임 밤/조명은 그림 구성 측정에서 제외하며, PNG에는 그대로 보인다. 카메라 값과 입력 파일 SHA-256을 결과에 보존한다. 캡처와 동일한 atlas를 사용해야 한다.

```sh
node --test tools/measure-estate-render-density.test.mjs
node tools/measure-estate-render-density.mjs artifacts/portrait-234/editor-meta/estate-r8-full-13-of-13-start-village-75px-390x844.png.density.json > artifacts/estate-density.json
```

기본은 캡처의 논리 강 셀을 물로 제외한다. 승인 Astra 시안의 물 영역과 비교할 때는 원본 알파 마스크를 별도로 지정한다. `estate-r8-terrain-488-20261028` Release의 DENSE-SCENES-r2·DENSE-METRICS-r2·DENSE-PROVENANCE-r2와 같은 원본 제작 마스크 `dense-river-r2-water-occupancy-1x.png`를 사용한다. 이 마스크가 Release에 없으면 제작 원본에서 제공받아야 하며, RGB로 물을 추측하거나 다른 그림의 마스크로 대체하지 않는다. 마스크가 없으면 기본 논리 셀 결과만 보고하고 시안과 같은 분모라는 주장은 하지 않는다.

```sh
node tools/measure-estate-render-density.mjs artifacts/portrait-234/editor-meta/estate-r8-full-13-of-13-start-village-75px-390x844.png.density.json \
  --reference-water-mask artifacts/estate-zone-terrain/reference/dense-river-r2-water-occupancy-1x.png \
  --water-origin=-256,448 --water-size=1152,1152 --grid-cell=64,32 \
  --zone-config data/opt-in/account-estate/m3a-prototype-r8.json \
  > artifacts/estate-density-reference-water.json
```

원점과 크기는 화면 좌표가 아닌 투영된 월드 좌표다. 도구는 실제 캡처의 모든 구역 꼭짓점으로 카메라 변환을 검증한 뒤 물 마스크를 재투영한다. 승인 원본은 CoreGraphics.high, 이 도구는 bilinear sampling이므로 경계 픽셀까지 동일하지 않다. 기존 여섯 카메라에서 물 픽셀 차이가 최대 49px였으며, 이는 전체 화면 비율·색상 등 임의 기준 변경 근거가 아니다. 결과는 논리 셀과 원본 물 마스크 두 값을 별도로 보존한다.

## 지표와 한계

- 실제 메시 삼각형을 픽셀 중심에서 샘플링한다. 앞쪽 그림부터 남은 투과율을 곱한 알파 기여도 32/255 이상이면 점유다. 같은 Graphic의 두 삼각형 경계를 두 번 합성하지 않는다.
- 외부 숲은 점유 기준 128/255이며 소품 수에서 제외한다. 원본 안개는 뒤 그림을 가리지만 빈 땅을 채우거나 소품 수를 늘리지 않는다. 런타임 어둠·빛·지면·흙 패치는 점유로 세지 않는다.
- 최소 8개 픽셀에서 기여도 32/255 이상인 Graphic만 보이는 수에 포함한다. `visibleCountsBySource`와 `visibleCountsByRole`은 실제 Graphic 인스턴스 수이며 의미상 건물·나무 개수나 면적 밀도가 아니다. 건물·군집·소품은 구분한다.
- `emptyDryLandPercent`: 화면에 잘린 논리 구역의 마른 땅 중 점유되지 않은 비율. `strictEmptyDryLandPercent`는 외부 숲의 채움을 빼며, `emptyDryLandExcludingPathsPercent`는 길 자체도 분모에서 뺀다.
- `largestConnectedEmptyComponentPercent`: 같은 빈 땅 마스크에서 상하좌우 4방향으로 연결된 가장 큰 영역 / 전체 마른 땅 픽셀. 대각선 접촉은 연결하지 않고, 길은 점유로 취급한다. 이 지표는 원래 Astra 표에 없던 추가 측정이다.
- 반 복원 강 장면은 이동 경계 때문에 (45,34)가 (41.5,34)로 제한될 수 있다. 카메라 중심·75/50px 설정·UI 영역·완성 건물 수가 다른 비교를 같은 조건으로 표현하지 않는다.
- 알파 shader, 부드러운 clipping, stencil, texture sampling 등 export가 지원하지 않는 조건은 `UNKNOWN`, `values:null`, 종료 코드 2로 남긴다. 요청한 참조 마스크가 없거나 불완전해도 2로 종료한다. 진단용 부분 값은 통과 수치가 아니다.

도구만으로 재미·화풍·실제 화면 판독성·그리기 호출·성능을 판정하지 않는다. Unity 두 프로필 및 사용자 영상 관문은 별도다.
