# ADR0072: 적 한 층 안에서 Y 정렬과 연속 아틀라스 묶음

- 상태: 제안 — 구현 후 두 프로필 GPU 검증 대기
- 날짜: 2026-10-25
- 연결: [#339](https://github.com/hyunlord/bs-mobile/issues/339)
- 번호: 작성 시 origin/main `717ead7`의 최대 번호 0071. 병합 전 다시 확인한다.

## 맥락

적을 50–69의 Y 구간으로 나누면 같은 아틀라스가 구간마다 별도 묶음이 된다.
#339의 기존 관측은 24마리에서 적 묶음 10개였다. 이 수치는 이번 수정본의 측정값이 아니다.
영웅 위층과 #171 발 주변 상체 반투명, 공격 효과의 stencil 제한을 유지해야 한다.

## 결정

- View만 변경한다. Core·데이터·리플레이 codec은 변경하지 않는다.
- 살아 있는 적, 피격 중 멈춘 적, 사망 표현을 모두 `EnemyLayer=50`에 제출한다.
- 재사용 버퍼에 발 위치 Y를 담아 내림차순 정렬한다. 같은 Y는 원래 제출 순서로 유지하여 몸/외곽선 순서를 보존한다. 몸의 절차적 출렁임은 정렬 키에 넣지 않는다.
- 같은 텍스처의 **연속 구간**만 합친다. A/B/A는 세 묶음이며 511개를 넘는 구간은 나눈다.
- 묶음 간 순서는 `RenderParams.worldBounds`의 깊이 중심으로 지정한다. 실제 정점은 z=0, shader는 기존 ZWrite Off/ZTest Always다. bounds는 회전·피벗을 적용한 네 모서리와 z=0을 포함한다.
- 현재 RunCamera의 정사영, -Z에서 +Z를 보는 방향, Renderer2D의 Default 투명 정렬을 전제로 한다. 카메라/투명 정렬을 바꾸면 GPU 회귀 검사를 다시 한다.
- renderQueue 3050은 유지한다. 영웅 3080 및 위험/공격 효과의 기존 층을 변경하지 않는다. upperOpacity 계산·예외도 그대로다.

## 결과·비용

하나의 아틀라스만 쓸 때 예상 구조는 24/411/511개=1묶음, 512/800개=2묶음, 1023개=3묶음이다.
다른 아틀라스가 앞뒤로 섞이면 더 나눠진다. 모든 장면에서 1묶음이라고 주장하지 않는다.
O(n log n) 정렬과 제출 bounds 계산이 추가된다. 버퍼·comparer·텍스처별 batch는 재사용한다.
실제 C05 비용과 화면 결과를 확인하기 전 성능 개선을 확정하지 않는다.

## 거절한 대안

- 텍스처별로만 모으기: A/B/A의 앞뒤 순서가 깨진다.
- 깊이 쓰기 켜기: 상체 반투명과 뒤에 보이는 적/땅의 합성 계약이 바뀐다.
- enemy에만 sortingOrder 지정: 기존 material renderQueue보다 우선하여 영웅/효과 계층을 깨뜨릴 수 있다.
- 별도 URP pass: 적 이후 효과와 영웅까지 순서를 다시 관리해야 하므로 이번 범위를 넘는다.

## 검증과 남은 관문

- 추가한 PlayMode 검사: 단일 atlas 구조 수, 같은 atlas 내부 Y 순서, A/B/A 앞뒤와 상체 반투명·영웅 위층, 511 경계와 동일 Y 순서.
- 2026-10-25 야간에는 실행하지 않았다. 두 프로필 EditMode/PlayMode 기록, 실제 동일 장면 전후 캡처, 리플레이 해시 검증은 미완료다.
- 읽은 공유 Mac 런북은 [PR #381의 파일](https://github.com/hyunlord/bs-mobile/blob/2d2ab66db34aa90d3e3ce6aafc367e31d36e4ccc/docs/runbooks/shared-mac.md)이다. main에는 아직 없었다.
- 허용 시간에 잠금 획득 후 Unity 기록을 만든다. C05 1회는 PR에서 야간 대기열을 요청하며 이 작업자가 직접 실행하지 않는다.

## API 근거

Unity는 `worldBounds`를 instanced group의 culling/sorting에 사용한다고 명시한다. 정사영의 기본 투명 정렬은 시선 축 거리다.
[RenderMeshInstanced](https://docs.unity3d.com/ja/current/ScriptReference/Graphics.RenderMeshInstanced.html),
[TransparencySortMode](https://docs.unity3d.com/ja/6000.0/ScriptReference/TransparencySortMode.html).
이 API 계약 확인은 pinned URP2D에서 실제 실행한 증거를 대신하지 않는다.
