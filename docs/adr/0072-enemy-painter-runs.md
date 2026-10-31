# ADR0072: 적 한 층 안에서 Y 정렬과 연속 아틀라스 묶음

- 상태: 채택 — [#382](https://github.com/hyunlord/bs-mobile/pull/382) 병합(`02006ba`), C05 3:3 통과
- 날짜: 2026-10-25 (검증 결과 갱신 2026-10-27, [#420](https://github.com/hyunlord/bs-mobile/issues/420))
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
- **두 프로필 Unity 기록:** 있음. 기록 커밋은 `e81e44a`이다. 결과는 #382 본문 `## Unity tests`에 있다.
  - first-playable: EditMode 818 통과, PlayMode 48 통과.
  - wave-1a: EditMode 828 통과, PlayMode 73 통과.
  - 네 묶음 모두 실패 0이다. 처음 실행에서 설명 없는 skip 2개로 실패한 기록은 보존했고, 보충 XML 2개로 해소했다.
- **C05:** 3:3 통과. 사용한 판정 도구는 `tools/check-wave-regression.mjs`이다.
  - 측정 조건: uncapped, 1600×900, 같은 Mac.
  - 비교: 기준 main `d71be4d` 3회, #382 `e81e44a` 3회.
  - 밀도별 통합 p95와 증가율:

    | 적 수 | main | #382 | 증가 |
    |---|---|---|---|
    | 400–599 | 2.720 ms | 3.003 ms | +10.4% |
    | 600–799 | 3.711 ms | 4.009 ms | +8.0% |
    | 800+ | 4.412 ms | 4.808 ms | +9.0% |

  - 세 구간 모두 20% 기준 안이다. 스크립트 출력은 [#382 코멘트](https://github.com/hyunlord/bs-mobile/pull/382#issuecomment-6428355792)에 그대로 있다.
  - #382 쪽 3~8회째는 다른 프로세스 부하와 창 전면화로 무효가 됐다. 기록은 지우지 않았다.
- **리플레이 해시:** C05 6회 모두 `stateHash == expectedStateHash`이다. 해시는 `B056B090…8AEE8`이다.
- **동일 장면 전후 캡처:** #382의 [선정 표본 Release](https://github.com/hyunlord/bs-mobile/releases/tag/enemy-single-layer-339-20261025)에 있다.
  - first-playable 10쌍: 최종 해시가 일치한다. 수정 입력 stress, world-only 장면이다.
  - wave-1a 정지 렌더: 전체 묶음이 16→12로 줄었다.
  - 둘 다 정상 플레이 증거가 아니다.
- **판정 경위:** 2026-10-26에는 #382 쪽 유효 실행이 2회뿐이었는데 3:2 '통과'로 병합했다.
  - 이것은 무상한 3회 계약 위반이었다. 지시 쪽 실수로 정정했다.
  - 2026-10-27 야간에 유효 1회를 더 측정해 3:3으로 다시 판정했다.
- 작성 당시 읽은 공유 Mac 런북은 [PR #381의 파일](https://github.com/hyunlord/bs-mobile/blob/2d2ab66db34aa90d3e3ce6aafc367e31d36e4ccc/docs/runbooks/shared-mac.md)이다. 지금은 main의 [shared-mac.md](../runbooks/shared-mac.md)에 있다.
- **남은 확인 항목:**
  - 그리기 호출은 29→14(−52%)로 줄었는데 Mac p95는 +8~10%다. 원인은 미확인이다.
  - 특정 Mac 결과이며 모바일 성능으로 일반화하지 않는다.

## API 근거

Unity는 `worldBounds`를 instanced group의 culling/sorting에 사용한다고 명시한다. 정사영의 기본 투명 정렬은 시선 축 거리다.
[RenderMeshInstanced](https://docs.unity3d.com/ja/current/ScriptReference/Graphics.RenderMeshInstanced.html),
[TransparencySortMode](https://docs.unity3d.com/ja/6000.0/ScriptReference/TransparencySortMode.html).
이 API 계약 확인은 pinned URP2D에서 실제 실행한 증거를 대신하지 않는다.
