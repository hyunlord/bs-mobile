관문 결과: 부분 — 실행 관문 3/4 통과, 관리 관문은 라벨·마일스톤·템플릿 통과 / Projects 보드 권한 부족. 의도적 위반 PR 5/5 차단·미병합 종료.

# S0 개발 기반 보고

확인일 2026-10-08, 저장소 https://github.com/hyunlord/bs-mobile, 작업 경로 `~/bs-mobile`. 원문 3개를 첫 커밋 `7c4c4e7`로 보존했고 ZIP 원본과 바이트·SHA-256 동일함을 확인했다. Charter & Kin 저장소는 수정·커밋·푸시하지 않았다.

## 관문별 결과

| 관문 | 결과 | 원본 증거 |
|---|---|---|
| 깨끗한 클론에서 명령 하나 | 통과: `./tools/check.sh`, 경고/오류 0, xUnit 6/6, 콘텐츠 경계 20/20, 적합성 자체 30/30, 정책·seed 18조합×3회 | `docs/evidence/S0/clean-clone.log`, 기반 [CI run](https://github.com/hyunlord/bs-mobile/actions/runs/37679235067) |
| 의도적 위반 PR 5개 | 통과: 5/5 해당 검사 실패, 필수 체크로 BLOCKED, 모두 CLOSED·mergedAt=null | 아래 표와 `docs/evidence/S0/negative-prs/` |
| 관리 체계 | 부분: 요청 라벨 21종, 마일스톤 6개, 이슈 템플릿 5개, PR 템플릿 존재. Projects v2 생성 거부 | `labels.json`, `milestones.json`, `board-status.txt`, [결정 #2](https://github.com/hyunlord/bs-mobile/issues/2) |
| 새 에이전트 런북 | 통과: AGENTS만 진입점으로 더미 도구/테스트 영웅 2파일 변경, Core 수정 0, 원격 CI 통과·병합 | [이슈 #19](https://github.com/hyunlord/bs-mobile/issues/19), [PR #21](https://github.com/hyunlord/bs-mobile/pull/21), `docs/evidence/S0/runbook/` |

| 의도한 위반 | PR | 실제 차단 진단 |
|---|---|---|
| Core 엔진 참조 | [#5](https://github.com/hyunlord/bs-mobile/pull/5) | AG001, Core의 UnityEngine using |
| Core 영웅 ID | [#7](https://github.com/hyunlord/bs-mobile/pull/7) | AG002, concrete content ID |
| 스키마 위반 | [#9](https://github.com/hyunlord/bs-mobile/pull/9) | activation.damage 문자열의 JSON Schema type 위반 |
| 형식 위반 | [#11](https://github.com/hyunlord/bs-mobile/pull/11) | dotnet format WHITESPACE |
| 결정론 위반 | [#13](https://github.com/hyunlord/bs-mobile/pull/13) | static 순서 상태가 seed를 바꿈; SameSeedProducesSameHashThreeTimes 런타임 실패 |

각 JSON에는 변경 SHA, CI URL, 종료 전 BLOCKED 상태, 실제 진단 관찰, 종료 후 미병합 상태가 있다. 실패 PR의 코드를 main에 넣지 않았다. 테스트 원격 가지·로컬 작업 가지와 worktree도 삭제했다.

## 들어간 기반

`core/src/`(Core/Sim), `core/tests/`, `core/bench/`, `data/schema/` 및 `data/test/`, `tools/`, `docs/`, `unity/README.md`, `.github/`. Core는 BCL·데이터 모델/시뮬레이션만 사용하고 JSON I/O는 Sim에 있다. 초기 물리 경로의 의뢰서 차이는 ADR 0006으로 통일했다.

AGENTS가 정본이고 CLAUDE 및 5개 Claude 스킬은 정본·런북 포인터다. ADR 0001~0005는 승인된 방향, 0006~0008은 구조·증거 보관·커밋별 실행 결정을 기록한다. 커밋은 Conventional Commits 의도 제목 + 필요한 Lore trailer. 구조/스키마/빌드 변경 ADR, 이슈 연결, PR 증거 섹션, 이슈 없는 TODO를 CI에서 검사한다.

main 보호는 quality/secrets 성공 필수·최신 base·관리자 포함·force push/삭제 금지·대화 해결·선형 이력이다. 자동 squash 병합과 가지 삭제를 켰다. GitHub secret scanning/push protection, gitleaks, Dependabot NuGet/Actions/npm을 켰다. 운영 SDK와 Unity 프로젝트, DGX self-hosted runner는 설치하지 않았다.

## 실측과 해석의 한계

- Ubuntu CI의 seed42/mixed 기본 900틱 결과: 피해 8,884, 성장 1,800, 3회 해시 `43FD2B0BB0314D1EAA8ECB5249D82F6155F24FD9243ECF4274162CC6C2A82407`. macOS와 Ubuntu에서 같은 해시를 관찰했다. 모든 플랫폼에 대한 결정론 증명은 아니다.
- CI 틱 p95 **0.000046ms**, 2,700개 개별 틱 표본. 이는 S0 산술 합성 루프이며 S2의 1,000적·300땅·20건물·60사람 부하가 아니다. 모바일 성능 추정에 사용하지 않는다.
- 정책 피해 평균 CV **0.3748277841470601**은 `scaffoldDamageCv`다. 게임 밸런스 필드 `balanceDispersion`은 null이며 생존율·재미 주장을 하지 않는다.
- [야간 실제 실행](https://github.com/hyunlord/bs-mobile/actions/runs/37679521811): 6정책×128seed×3반복, 768조합. BenchmarkDotNet ShortRun 전체 900틱+결과 해시 평균 **11.67µs**, 표준편차 **0.019µs**, 할당 **912B**. 개별 틱 p95와 다른 측정이다. AMD EPYC 7763 / Ubuntu24.04.5 / .NET8.0.31; 전체 원본은 `nightly-raw.tar.gz`, 요약·BDN JSON/CSV는 `nightly/`.
- `docs/metrics/index.html`은 실제 CI JSON에서 재생성했다. 375/768/1280px 브라우저·키보드 원본 링크·오류/넘침 검사를 했다. 표는 좁은 화면에서 가로 스크롤한다. 스크린샷·원본 해시는 `docs/evidence/S0/metrics/`.

## 미완료 결정과 위험

1. **Projects 보드 미생성:** OAuth 토큰 `project`/`read:project` scope가 없다. 저장소 ADMIN만으로 해결되지 않았다. [#2](https://github.com/hyunlord/bs-mobile/issues/2)에 재현 오류·정확한 조치가 있다. S0 전체 통과로 바꾸지 않으며 독립 작업은 진행한다.
2. 영구 지표 경로는 신뢰된 main workflow가 성공 CI JSON만 release 자산으로 쌓는다. 첫 시도에서 gh pattern 다운로드 하위 폴더 차이로 실패했고, 실제 자료로 재현하여 [PR #25](https://github.com/hyunlord/bs-mobile/pull/25)에서 수정했다. 수정 후 [실행 37681128112](https://github.com/hyunlord/bs-mobile/actions/runs/37681128112)이 성공했고 metrics-history release의 JSON·HTML·ZIP 자산을 확인했다. 누적 경로는 다른 성공 커밋으로 다시 실행해 검증한다.
3. 적합성 검사는 컴파일·AST·런타임 경계이며 악의적인 검사기 자체 변경을 막는 보안 샌드박스는 아니다. 임의 런타임 문자열 조합까지 정적으로 증명하지 않는다. S2에서 행동 범위를 넓혀 검증한다.
4. 현재 보안/의존성 도구 버전은 SHA/lockfile 고정이며 Dependabot 제안은 이슈·ADR·CI를 거쳐 검토한다. 자동 생성된 PR이 정책 증거 없이 바로 병합되지는 않는다.
5. 사용자 선택이 필요한 기본 영웅·영지 정체는 S3 후보 단계에 남는다. 현재 core:founder/core:meadow는 임시 데이터이며 확정 콘셉트가 아니다.

단계 ZIP은 `tools/package-stage.sh S0 20261008`로 만든다. 루트 MANIFEST.json에 모든 파일의 크기·SHA-256·커밋을 남기고 CRC, 깨끗한 해제, 전체 해시·정확 파일 집합을 확인한다. build/cache/.git/자격증명은 제외한다.

## 최종 누적 확인

[보관 실행 37681523466](https://github.com/hyunlord/bs-mobile/actions/runs/37681523466) 성공 후 release에서 서로 다른 성공 커밋 `678e0b5`·`aafd7b6`의 JSON 두 개를 직접 내려받아 확인했다. 원본 첫 CI 기록까지 포함한 저장소 HTML도 다시 생성했다. 자산 목록은 `docs/evidence/S0/metrics-release-accumulated.json`이며, 한 번의 통과 수치가 아닌 커밋·시각별 기록의 누적을 실제로 검증했다. `metrics-history.zip`은 HTML과 상대 링크의 원본 JSON을 함께 제공한다.
