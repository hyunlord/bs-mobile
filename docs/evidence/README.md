관문: 통과 — 원본 Release 재다운로드·SHA-256·전체 파일 집합 검증 PASS.
변경: 대량 원자료를 Release로 이전하고 보고서·요약 CSV·해시 목록을 남겼다.
사용자 결정 필요: 없음. 원본 S4 FAIL 판정과 Git 히스토리는 보존한다.

# Phase 0 증거 보관

## 원본 받기

[고정 원본 Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-evidence-original-20261008)는 커밋 `b3f4830bb8c441343f61362e2b39e474f5709290`의 `docs/evidence/`와 `docs/league/` 전체 1,399개 파일을 보관한다. 파일별 원본 경로·크기·SHA-256은 [source manifest](phase0-source-manifest.csv), 다운로드 자산 해시는 [asset manifest](phase0-assets-sha256.csv)에 있다.

```sh
mkdir phase0-original
 gh release download phase0-evidence-original-20261008 --repo hyunlord/bs-mobile --dir phase0-original
shasum -a 256 phase0-original/phase0-evidence-original-b3f4830.tar.gz
mkdir phase0-original/extracted
tar -xzf phase0-original/phase0-evidence-original-b3f4830.tar.gz -C phase0-original/extracted
```

압축 파일의 예상 SHA-256은 `ee04370bc0c5a560238a1223efb18eaadd1ba0e2cbc61fc7f2e0dbc4b2465881`이다. `phase0-source-manifest.csv`의 모든 파일에 대해 추출 경로·크기·SHA-256을 대조하고 추가 파일도 없는지 확인한다. 압축 해시만 확인한 검증과 전체 파일 검증을 혼동하지 않는다. 검증된 이번 이전 결과는 [R1 archive verification](R1-archive-verification.md)에 기록했다.

기존 보고서의 스크린샷·JSON·원시 CSV는 Release를 추출하면 원래 상대 경로로 열린다. 현재 트리에 없는 파일을 온라인에서 확인하려면 [원본 커밋의 evidence](https://github.com/hyunlord/bs-mobile/tree/b3f4830bb8c441343f61362e2b39e474f5709290/docs/evidence)와 [league](https://github.com/hyunlord/bs-mobile/tree/b3f4830bb8c441343f61362e2b39e474f5709290/docs/league)를 사용한다. Release 자산을 덮어쓰지 않으며 수정본은 새 버전으로 발행한다.

Release의 GitHub `isImmutable` 값은 `false`다. 자산을 덮어쓰지 않는 운영 규칙이며 플랫폼 불변성 보장은 아니다. 해시 목록은 payload 2개(압축 원본·파일별 매니페스트)를 검증한다. 해시 목록 자체와 안내문은 별도 다운로드 후 업로드 원본과 바이트 비교했다. 목록 자체의 재귀적인 자기 해시는 주장하지 않는다.
