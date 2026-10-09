관문: 로컬 통과 — 전체 검사와 변경 전후6개 전체 길이 표본·36개 짧은 리그 조건 일치. 병합은 필수 CI 통과 후 진행.
변경: 개별 콘텐츠223개와 중첩 메타44개에 공통 설계 의미를 정리했다.
보존: 기존 ID·실행 값·배열 순서·프로필·Core 규칙과 역사 판정을 유지했다.
구분: 설계 의도와 실제 처리 operation을 분리하며 신규 메커니즘 구현으로 세지 않는다.
결정: #126 수치 조정과 Fold7 #98은 보류, Lattice 도구 제작은 별도 세션이다.
범위: 이 보고서는 데이터·host 호환성 검증이며 화면·재미·실기 검증이 아니다.

# D1 데이터 규약 정리

근거: [의뢰서](../design/09_ASTRA_GOAL_bs-mobile_phase2b-system-design_v2.md), [#133](https://github.com/hyunlord/bs-mobile/issues/133), [ADR0036](../adr/0036-content-metadata-without-runtime-redesign.md).
기준 소스는 `90c8ca34f9de6755afca01d28c8eff4f2eae20be`다. 종료 커밋과 CI는 이 문서를 포함한 PR에서 확인한다.

## 규약과 수량

| 구분 | 수량 | 변경 |
|---|---:|---|
| 정본 개별 콘텐츠 |219| 공통 kind와 effect.trigger/benefit/cost |
| 검사용 더미 |4| 같은 계약; 출시 콘텐츠에 포함하지 않음 |
| 중첩 메타 |44| 자재4·챕터10·장원4·가신6·도전20의 공통 필드 |
| 런타임 operation |14| [처리 위치·소비 경로·의미 표](../design/runtime-operations.md) |

역사 설계 풀216개와 영웅·영지·스킨을 포함한219개는 집계 대상이 다르다. first-playable의 실행 선택 수와도 다르다. 개별 파일 kind는 콘텐츠 종류로 통일하고 진화 subtype은 evolutionKind로 옮겼다. 장원의 기존 실행 effect enum은 manorEffect로 옮겨 공통 설계 설명과 충돌하지 않도록 했다. host에서 기존 Core 정의로 투영하며 Core 타입과 규칙은 바꾸지 않았다.

검사한 콘텐츠 ID 참조396개는 이미 유효했다. 이름·배열 인덱스로 된 잘못된 참조를 발견하거나 수정했다고 주장하지 않는다. target/growth.target의 대상 분류, chapter.index의 순서와 challenge.target의 목표 수치는 ID 참조가 아니므로 유지했다. operation별 subject와 condition별 value의 타입은 처리표에서 구분한다.

## 동작 보존 증거

독립 검토에서 변경된 비스키마 JSON224개(개별223+메타1)의 공통 메타데이터만 제거하고 두 필드 이동을 역변환했을 때 기존 값과 배열 순서는 기준 소스와 같았다. 중첩 메타의 실행 payload를 키 정렬한 비교 해시는 변경 전후 `5afeacac921932fee2de4879105f3f773531fe1d4a95fae5ba5b1e472f370e98`로 같다. 파일 바이트의 출처 해시는 메타데이터 추가로 달라지므로 상태 해시와 혼동하지 않는다.

아래 표본은 mixed 정책으로 실행했으며 결과 전체에서 출처 runMetadata만 제외한 깊은 비교도 일치했다.

| 프로필 | seed | tick | 변경 전후 공통 상태 해시 |
|---|---:|---:|---|
|s2-baseline|42|21600|8089ECC043DB12853020065C6B63FFE45FBF27694DB6648421064468A0B08442|
|s2-baseline|73|21600|FA111F36EE3EBF3EE1E46451ABEABB5A31D1DA38A51796007B25791D66037440|
|production|42|21600|E510C138AEDB05AA2A62342105B46524AF4D5DDDB74AF4019EF73EC5E7C3F8F6|
|production|73|21600|CE3B18024CCDA73B9A318043B0AFE69A779115C73891B45B9C4FD80501B8496B|
|first-playable|42|27000|F24B7B7DBFAAFEB9935895215491EAB9CAD1F0126D7C64E7509EBB1E41D130B8|
|first-playable|73|27000|5CB88B5D74891BE897E8A818ACC3349A5B58FB6AE87C17BFCA4DA3011F03D5AB|

seed42의 s2-baseline/first-playable은 보존한 변경 전 host와 새 host에서 각각2회 실행했다. 다른4개 표본은 하위 호환 host에서 변경 전 데이터 스냅샷과 새 데이터를 비교했다. 이 여섯 표본을 전체 seed 공간 검증으로 확대하지 않는다.

추가로 first-playable 900tick 상한에서 seed42/43 × 사람 규칙A/B/C × 정책6종의36조건을 변경 전후 각각3회 실행했다(216회). 각 조건의 반복 해시와 출처 정보만 제외한 전체 게임 결과가 모두 일치했다. 이는 짧은 통합 회귀 비교이며 생존율·재미 관문을 새로 판정한 실험이 아니다.

## 검사와 호환 한계

새 Node 음성 사례7개는 kind 누락/오류, 설계 trigger 누락, 진화 subtype 누락/잘못된 공통 kind, 다른 종류 ID·이름 참조를 거부한다. 중첩 메타8개 회귀 테스트와 Meta 전체63개가 통과했고, 스키마의 메타 변이45개 거부도 확인했다. 첫 전체 검사에서 MetaContentLoader의 using 정렬 하나가 실패하여 수정했다. 테스트를 제거하거나 규칙을 약화하지 않았다.

현재 파일은 CI에서 새 공통 필드를 필수 검사한다. CLI host는 역사 개별 테스트 카탈로그의 no-kind 형식을 제한적으로 읽는다. 중첩 메타의 원시 JSON은 새 형식으로 이행해야 한다. 플레이어 저장·리플레이 코덱은 변경하지 않았다. 실제 지원 범위는 ADR0036을 따른다.

`./tools/check.sh`가 exit0으로 완료됐다. 전체 검사에서 Node 콘텐츠 검사162개·.NET 테스트426개·실제 실행 동작156개가 통과했다. 기존/현재 Core 두 대상의16조건96회, first-playable5seed30회, 메타5seed30회 결정론 비교도 통과했다. 짧은 진단·리그는 통합 회귀 확인이며 밸런스 재판정이 아니다.

## 재현

`./tools/check.sh`는 스키마·참조·음성 사례·host/Core 테스트·형식·결정론 DLL 비교·짧은 리그를 포함한다. 변경 전후 비교는 기준 커밋의 데이터를 별도 경로에 꺼내고 같은 프로필·seed·정책으로 `SowSiege.Sim`을 실행해 stateHash와 runMetadata를 제외한 게임 결과를 비교한다. 메타의 전체 실행 payload 비교는 공통 설계 필드만 제거하고 manorEffect를 기존 effect로 되돌린 JSON을 대상으로 한다. 성공 원자료 전량이나 재생성 보고서는 Release에 올리지 않는다.

단일 표본의 현재 host 명령은 다음과 같다. 기준 데이터는 기준 커밋에서 추출한 별도 디렉터리이며 현재 데이터를 덮어쓰지 않는다. 동일 명령의 `--data`만 기준/현재로 바꾸고 출력 경로는 분리한다.

```sh
dotnet core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll \
  --data data --profile first-playable --seed 42 --policy mixed \
  --output artifacts/d1-first-playable-42.json \
  --metrics artifacts/d1-first-playable-42-metrics.json --iterations 2
```

짧은 리그 비교는 위 CLI에서 seed42/43, `--people-rule A|B|C`, `--policy weapon|land|building|people|mixed|random`의 모든 조합에 `--scenario normal --duration-ticks 900 --iterations 3`을 적용한다. 기준 데이터와 현재 데이터를 각각 실행하고, 각 결과 배열에서 runMetadata만 제거한 깊은 동등성과 반복 해시를 검사한다.
