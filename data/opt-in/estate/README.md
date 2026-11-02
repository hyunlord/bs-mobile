# r8 승인 영지 배치

`terrain-layout-r8.json`은 승인된 75px 미리보기의 명시적 배치 데이터다. 게임 적용 전 검토용 opt-in이며 `runtimeEnabled:false`다. 자동 산포·확률 배치·빈 공간 채우기 규칙은 없다. 기존 설정·게임 로더·저장 형식을 바꾸지 않는다.

## 그리기 계약

- `zones`: 현재 r8의 13구역 ID/격자 사각형과 바닥 자산. 총 1,664칸.
- `assets`: PNG 크기, 좌상단 기준 앵커, Release URL과 SHA-256. atlas crop 및 강 조각 합성도 명시한다.
- `instances`: **실제 지형 배치**. `assetId`, `zoneId`, `grid:[x,y]`, `sizeVariant`, `scale:[sx,sy]`, `flipX`, `rotationRadians`, `opacity`, `drawOrder`.
- 정밀 배치는 소수 격자 좌표다. 정수로 반올림하거나 임의 재산포하지 않는다. 1칸 투영은 `u=32*(x-y), v=16*(x+y)`이다. 원본 PNG 크기에 `scale`을 곱하고, 투영점에서 스케일 적용 앵커를 빼 좌상단을 구한다. 회전은 스케일된 캔버스 중심 기준이다.
- 바닥을 먼저 그리고 `drawOrder` 오름차순으로 그린다. `layer`나 y값만으로 다시 정렬하지 않는다. PNG는 원본 RGBA를 유지하고 `opacity`를 곱한다. `flipX`는 좌우 반전 여부다(현재 승인본은 모두 false).
- 바닥 텍스처는 512px 원본 평면을 반복하고 구역 사각형에 클립한다. 화면 기준으로 반복 시작점을 다시 잡지 않는다. 정확한 변환은 별도 오프라인 렌더러에 있다.
- `zoneId`는 앵커 귀속이며 구역 경계를 가로지르는 강·길·다리의 전체 가시성 ID가 아니다. 전체 회복 배치를 담고 있으며 잠긴 구역의 클립/어둠 처리는 로더 후속 검증 대상이다.
- `background`: 구역 밖 어두운 숲 바탕. `zoneId:exterior` 배치는 영지 외곽 숲·안개다. 구역에 다시 끼워 넣지 않는다.
- `phase:start-adjacent`는 시작+외곽4방향+inner-village의 **우선 추출 범위**다. 초기 계정 개방 상태나 새로운 해금 규칙이 아니다. 전체 배치는 `instances` 전부다.

## 보호 영역과 리뷰 데이터

`reservations`는 건물/Lv0 10부지, 회전한 길, 다리와 강의 그림 보호 영역이다. 지형 소품의 `footprint`는 명목 접지 면적으로, 수관·그림자의 전체 픽셀 경계나 실제 게임 충돌체가 아니다. 보호 영역과 접지 면적이 겹치는 소품은 최종 배치에서 제외했다. 소품끼리 겹치는 군집은 원본대로 보존한다. 바닥 텍스처·얕은 흙 데칼은 바닥층이다.

`reviewOnly.buildings`는 기존 건물 10채의 비교용 참조다. **게임 지형 로더에서 그리지 않는다.** 기존 건물 렌더러가 계속 소유한다. `reviewOnly.excludedInstances`도 게임에서 그리지 않는다. 이를 원래 순서에 합치면 승인 원본을 검증할 수 있으나, 최종 보호 배치는 `instances`만 사용한다. 원본의 일부 투명 건물 여백 장식 허용과 최신 보호칸 요구 차이를 숨기지 않는다.

## 원본 작업 폴더 없는 재조립

Node.js 및 macOS Swift/CoreGraphics가 필요하다. 새 의존성 패키지는 없다. 빈 캐시에서 Release 자산을 받고 SHA를 검증한다. 결과는 오프라인 비교이며 Unity 구현/성능 검증이 아니다.

```sh
mkdir -p artifacts/terrain-layout-review/reference
node tools/art/cache-terrain-layout-assets.mjs data/opt-in/estate/terrain-layout-r8.json --remote artifacts/terrain-layout-review/cache
gh release download estate-r8-terrain-488-20261028 -R hyunlord/bs-mobile --pattern 'dense-r8-*-75px-r2-390x844.png' --dir artifacts/terrain-layout-review/reference
swiftc tools/art/render-terrain-layout.swift -o artifacts/terrain-layout-review/render
artifacts/terrain-layout-review/render data/opt-in/estate/terrain-layout-r8.json artifacts/terrain-layout-review/cache artifacts/terrain-layout-review/output artifacts/terrain-layout-review/reference
npm run validate
node --test tools/test-terrain-layout.mjs
```

렌더러의 승인 PNG 입력은 비교에만 쓰며 장면을 만드는 입력이 아니다. 배치는 이 JSON 하나만 읽는다. 재출력은 새 폴더에 하여 이전 결과를 보존한다. 전체 지도 축소판은 자료 개관용이고 기본 게임 배율은 승인된 75px다.

검증·제외 수·공개 비교판은 [검토 기록](../../../docs/review/estate-terrain-layout-572.md)을 참조한다. 사용자 승인 전 병합하지 않는다.
