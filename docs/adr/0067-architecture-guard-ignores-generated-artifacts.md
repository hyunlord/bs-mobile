# ADR0067: ArchitectureGuard는 저장소 루트 artifacts/의 생성 사본에서 콘텐츠 ID를 모으지 않는다

- 상태: 승인 — #297, 사용자 요청(2026-10-18)
- 날짜: 2026-10-18
- 연결: [#297](https://github.com/hyunlord/bs-mobile/issues/297), [ArchitectureGuard](../../tools/ArchitectureGuard/README.md)
- 번호: main 최대 0064, 열린 #266 스택이 0065·0066을 쓰므로 0067.

## 맥락

AG002는 Core 상수 문자열이 저장소 JSON의 콘텐츠 ID와 같으면 거부한다. ID는 경로 어딘가에 `data`·`tests` 폴더가 있는 모든 JSON에서 모았다. 로컬 `node tools/prepare-wave-benchmark.mjs --data-only`는 `artifacts/benchmarks/c05/data/`에 데이터 사본(gitignore된 생성물)을 남긴다. 그래서 그 사본의 문자열까지 ID로 잡혀 로컬에서만 AG002 181건이 났다(2026-10-17 관측, 사본을 지우면 0건). CI에는 `artifacts/`가 없어 드러나지 않았다.

## 결정

저장소 루트 바로 아래 `artifacts/`의 JSON은 ID 수집에서 뺀다. `data/`와 `tests` 아래 정본 JSON, `bin`/`obj`/`node_modules` 제외 규칙은 그대로다. 저장소 테스트에 두 사례를 더한다. `artifacts/` 사본의 ID는 AG002를 만들지 않고, 같은 ID가 `data/`에 있으면 AG002가 난다.

## 결과와 대안

- 로컬 실행이 CI와 같은 입력을 본다. 생성 출력에 정본 데이터를 두지 않는다는 기존 저장소 규칙(`artifacts/`는 gitignore)과 일치한다.
- 거절: `benchmarks/` 경로만 빼기 — 다른 생성 사본이 생기면 같은 오탐이 다시 난다.
- 거절: 검사 전에 `artifacts/`를 지우는 스크립트 — 다른 증거를 지울 위험이 있다.

## 검증

`dotnet run --project tools/ArchitectureGuard -- --self-test` 통과(새 사례 포함, 수정을 끄면 첫 사례 실패). 사본이 있는 로컬 저장소에서 0건. 필수 CI.
