# ADR0081 · CI는 LFS 객체를 캐시에서 복원하고 빠진 것만 받는다

상태: 승인 — 사용자 승인 2026-11-01(#601). 날짜: 2026-11-01. 연결: [#601](https://github.com/hyunlord/bs-mobile/issues/601).
번호: 작성 시 main의 최대 번호는 0080이다. 0079는 열린 #607이 쓰고 있어 0081을 쓴다. 병합 전 다시 확인한다.

## 맥락

`check.yml`의 quality와 `nightly.yml`은 `actions/checkout`의 `lfs: true`로 실행마다 저장소의 LFS 전체를 내려받았다. 2026-10-31 main 기준 224파일·고유 171객체·약 118MB다(로컬 `git lfs ls-files -l` + 포인터 size 합산). PR 갱신마다 이 양이 반복되어 2026-10-31 저장소 LFS 대역폭 예산이 소진됐고, 모든 PR CI가 checkout 단계에서 `This repository exceeded its LFS budget`로 실패했다(run 41169248335).

## 결정

- checkout은 `lfs: false`로 한다.
- 전체 게이트만 LFS를 받는다. `git lfs ls-files -l`의 객체 ID 목록 sha256을 키(`lfs-<hash>`)로 `actions/cache`가 `.git/lfs/objects`를 복원하고(복원 키 `lfs-` 접두사), `git lfs pull`이 캐시에 없는 객체만 받은 뒤 `git lfs checkout`으로 작업 트리를 채운다.
- 문서 전용 범위(`ci-scope` = docs), secrets, dependency-context는 LFS를 받지 않는다.
- nightly도 같은 캐시 단계를 쓴다.

## 결과·비용

- 캐시 적중 시 LFS 다운로드는 0이고, 그림을 바꾼 PR은 바뀐 객체만 받는다(실측은 #602 첫 miss 실행과 이후 hit 실행 로그로 기록).
- PR 가지에서 저장한 캐시는 다른 PR이 읽지 못한다. main push 실행이 저장한 캐시를 모든 PR이 공유한다.
- 캐시는 GitHub Actions 캐시 용량을 쓴다. 오래된 키는 GitHub 정책으로 만료된다.
- 저장소 내용·LFS 추적 규칙·검사 내용은 바뀌지 않는다.

## 거절한 대안

- LFS 전체 해제(이미지를 일반 git 객체로): 역사 재작성 또는 저장소 크기 급증이 필요하다.
- 문서 이미지만 Release로 이동: [#603](https://github.com/hyunlord/bs-mobile/pull/603)에서 별도로 하며 그것만으로는 Unity 자산 대역폭이 남는다.
- self-hosted runner: 공개 저장소에 붙이지 않는 규칙(AGENTS.md)에 어긋난다.

## 검증

YAML 문법 검사, #602 실행의 `actions/cache` miss 로그와 이후 실행의 hit 로그, quality 결과.
