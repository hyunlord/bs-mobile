관문: 통과 — 재건 수정본 candidate-02의576조건·1728회와 (b)·c′·무기 하한·혼합 관문 모두 통과.
소스: `7e4dd4ad149722b1bb1b7df51438bd33d99e27e3`의 깨끗한 작업 트리에서 실행했다.
변경: 폐허 재건 문맥과 단계 재건 우회 결함을 고친 뒤 같은 seed로 전체 리그를 다시 실행했다.
검증: 전체343테스트, 구현 관찰156개, 두 실제 Core DLL의 신규30회·역사96회 재생 일치.
리그: seed40000–40031 × 사람규칙ABC × 정책6, 각27000틱·3반복 일치; 새 독립 holdout이 아닌 수정 전후 대응 비교다.
한계: 모든 정책32/32 생존, 수정 전후 리그의 게임플레이·진단·카드 해시는 동일했다. 실제 재건 효과는 별도 회귀 검사로 증명했다.
보존: candidate-01의 정본·당시 판정·발견 결함을 아래에 유지한다. 관문을 위한 수치 보정은 하지 않았다.
연결: [PR #99](https://github.com/hyunlord/bs-mobile/pull/99), [수정 소스 CI](https://github.com/hyunlord/bs-mobile/actions/runs/37828211351), [종합 진행표](../../review/phase1b-first-playable.md).

# M1 candidate-02

| 후보 | 실행 소스 | 조건·반복 | (b) / c′ / 무기 하한 | 혼합≥random | 생존율 |
|---|---|---|---|---|---|
| candidate-01 (보존) | aec84d2 | 576×3 | 모두 통과 | 3/3, 동률 | 18정책·규칙 셀 각각32/32 |
| candidate-02 (현재) | 7e4dd4a | 576×3 | 모두 통과 | 3/3, 동률 | 18정책·규칙 셀 각각32/32 |

수정은 실제4회 성장 발동의 재건 완료 시 폐허 문맥을 보존하고, 수호 고리 진화의 수동적 수선이 폐허를 즉시 되살려 단계를 건너뛰지 못하게 한다. 신규13회귀를 포함한343테스트가 통과했다. 이 수정의 효과를 리그가 관측했다고 확대하지 않는다. 대응576사례의 생존·틱·레벨·피해·경험치·식량·수확·RNG·게임플레이/진단/카드 해시에는 차이가 없었다. 실행 소스·DLL과 원본 packet 식별자는 새 provenance로 구분한다.

현재 관문 재계산의 정본은 `candidate-02/runs.csv`와 `candidate-02/provenance.csv`다. 무기·장비·진화·종료 상태 진단CSV4개는 candidate-01과 바이트 단위로 동일해서 복제하지 않고 기존 파일을 참조한다. 로컬의 상세coverage/runtime-effects CSV도 동일함을 확인했다. 진화8종658회·397사례 관측과 아래 진단 해석도 같으며, 구성 효과의 인과적 우위는 뜻하지 않는다.

```sh
node tools/first-playable-report.mjs docs/league/first-playable-m1/candidate-02 artifacts/phase1b/m1-candidate-02-recalculated full
```

현재 실행 출력과 CSV 재생성의 `condition-ranks.csv`, `gates.csv`, `mixed.csv`, `outcomes.csv`, `ratios.csv`, `report.md`6개가 바이트 단위로 동일하다. 전체 packet은 로컬 `artifacts/phase1b/m1-candidate-02/`에 보존한다. 표본 선정과 업로드 제한은 candidate-01과 동일하다.

# 보존: candidate-01 당시 결과

관문: 통과 — M1 정식576조건·1728회, (b)·c′·무기 하한·혼합 관문 모두 통과.
소스: `aec84d2a5097a001d2203dcd069f1e55ea267fa0`의 깨끗한 작업 트리에서 빌드·실행했다.
변경: 별도15분 first-playable 프로필의 실제 콘텐츠와 공격·영지·이벤트를 검증했다.
검증: 전체330테스트, 구현 관찰156개, 두 실제 Core DLL의 신규30회·역사96회 재생 일치.
리그: holdout40000–40031 × 사람규칙ABC × 정책6, 각27000틱·3반복 해시 일치.
한계: 모든 정책이32/32 생존해 생존율은 상한에 닿았다. 상대 난이도·재미·손조작·실기 성능의 증거가 아니다.
실패 후보: 없음. 이번 후보에서 리그 관문을 위한 후속 수치 보정은 하지 않았다.
연결: [PR #99](https://github.com/hyunlord/bs-mobile/pull/99), [실행 소스 CI](https://github.com/hyunlord/bs-mobile/actions/runs/37813381091), [종합 진행표](../../review/phase1b-first-playable.md).

# M1 candidate-01

| 정책 | A 생존 | B 생존 | C 생존 | 같은 묶음 random 대비 |
|---|---|---|---|---|
| 무기 | 32/32 | 32/32 | 32/32 | 1.00배 |
| 땅 | 32/32 | 32/32 | 32/32 | 1.00배 |
| 건물 | 32/32 | 32/32 | 32/32 | 1.00배 |
| 사람 | 32/32 | 32/32 | 32/32 | 1.00배 |
| 혼합 | 32/32 | 32/32 | 32/32 | 1.00배 |
| random | 32/32 | 32/32 | 32/32 | 기준 |

| 관문 | 실제 판정 |
|---|---|
| (b) 기존 지배 조건 | 통과; 기존 구현을 그대로 사용 |
| 무기 ≥ random ×0.6 | A/B/C 모두 통과 |
| c′ 전문4정책의 random 대비0.6–1.5배 | 12비교 모두1.00배, 통과 |
| 혼합 ≥ random인 묶음 ≥2/3 | 3/3 통과; 우월함이 아니라 동률 |
| 반복 결정론 | 576조건 각각3회 일치, 총1728실행 |

리그에서 진화8종 모두 실제 관측됐다. 총658회 활성화, 서로 다른397사례이며 희귀한 `core:warded_masonry`는1회였다. 이 빈도는 선택·조건 도달 결과이며 진화의 인과적 우위 증거로 해석하지 않는다. 특허 양축 효과는 통제된 실제 세계 회귀 검사로 별도 검증했다. 무기 과잉 타격·반경 후보 없음·완료 공격의 실제 허공 발동을 분리한 지표는 `weapon-sources.csv`에 유지한다.

## 정본과 재생성

`candidate-01/runs.csv`와 `provenance.csv`가 관문 재계산의 정본이다. `weapon-sources.csv`, `weapon-equipment.csv`, `evolutions.csv`, `terminal.csv`는 무기·진화·종료 상태의 작은 진단 정본이다. 기존 R2/R3 및 이후 역사 판정은 수정하지 않았다.

```sh
node tools/first-playable-report.mjs docs/league/first-playable-m1/candidate-01 artifacts/phase1b/m1-recalculated full
```

실제 실행 출력과 위 재생성의 `condition-ranks.csv`, `gates.csv`, `mixed.csv`, `outcomes.csv`, `ratios.csv`, `report.md` 6개가 바이트 단위로 동일함을 확인했다. 재생성 산출물은 Release에 중복 업로드하지 않는다.

전체 실행 재현은 기록된 소스의 깨끗한 작업 트리에서 수행한다. 출력 디렉터리는 새 경로여야 한다.

```sh
node tools/first-playable-league.mjs full artifacts/phase1b/m1-reproduced 4
```

전체576 packet과 상세coverage/runtime-effects CSV는 로컬 `artifacts/phase1b/m1-candidate-01/`에 보존한다. 전량 성공 원자료를 게시하지 않는다. 사전 선정된 정상 표본은 seed40000의 mixed A/B/C 세 사례이며, 공개가 필요할 때 Release 태그로만 제공한다. 이번 실행의 실패 표본은0개다. 원자료를 아직 Release에 게시했다는 뜻은 아니다.

## 해석 한계와 다음 관문

모든 정책의 생존율이100%이므로 이번 실행만으로 혼합 빌드의 우수성이나 난이도 적절성을 판정할 수 없다. 관문 기준을 사후 변경하거나 성공률을 낮추기 위한 재조정은 하지 않는다. 실제 화면·손맛·입력·15분 흐름은 M2/M3, 실제 폴드7 성능·플레이와 .NET/Mono/IL2CPP 교차 검증은 M4에서 확인한다.

### 실행 후 병합 리뷰에서 발견한 결함

[PR 리뷰](https://github.com/hyunlord/bs-mobile/pull/99#discussion_r4222779948)가 다회 발동 재건 중 폐허였다는 문맥을 잃어 `core:ruin_keystone`의 실제 재건 효과가 적용되지 않는 경로를 지적했다. candidate-01의 CSV·소스·당시 수치 판정은 그대로 보존한다. 이 리그 통과만으로 결함까지 해소됐다고 보지 않으며, 실제 재건 회귀와 수정 소스의 후속 검증 전에는 M1 전체를 병합하지 않는다.
