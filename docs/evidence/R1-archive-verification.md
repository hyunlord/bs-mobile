관문: 통과 — Release 재다운로드·자산 해시·1,399개 파일 해시·정확한 파일 집합 PASS.
변경: 원본에서 1,363개 대량 파일(265,292,205 bytes)을 현재 트리에서 제거했다.
보존: 원본 S4 FAIL 보고서는 바이트 동일하며 Git 히스토리를 재작성하지 않았다.
사용자 결정 필요: 없음.

# R1 원본 증거 이전 검증

## 원본과 검증

- 원본: `b3f4830bb8c441343f61362e2b39e474f5709290`의 `docs/evidence/` + `docs/league/` 전체. `git archive`로 생성했으며 작업 트리 파일을 섞지 않았다.
- [Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-evidence-original-20261008): `phase0-evidence-original-b3f4830.tar.gz`, 39,324,629 bytes, SHA-256 `ee04370bc0c5a560238a1223efb18eaadd1ba0e2cbc61fc7f2e0dbc4b2465881`.
- 별도 다운로드 디렉터리에서 네 자산을 새로 내려받았다. archive·source manifest는 [자산 해시 목록](phase0-assets-sha256.csv)과 크기/SHA-256을 대조했고, asset manifest·Release README도 업로드 원본과 바이트 대조했다.
- 빈 디렉터리에 추출 후 [파일별 매니페스트](phase0-source-manifest.csv)의 **1,399개 / 265,931,147 bytes**를 전부 SHA-256·크기 대조했다. 재귀 열거한 실제 파일 집합과 매니페스트 경로 집합이 정확히 같았다. 검증 시각: `2026-10-08T01:54:12.536Z`.
- 이 검증이 끝난 후에만 현재 트리 파일을 제거했다. 재다운로드·추출 사본은 `/Users/rexxa/Downloads/bs-mobile-r1-original/`에 남겼다.

## 크기 산정

[원본 Git blob 크기 원장](R1-tree-sizes.csv)은 기준 커밋의 `git ls-tree -r -l` 출력으로 산정했다. `.git`, 빌드 출력, 파일시스템 블록 크기는 포함하지 않는다.

| 범위 | 원본 파일 수 / bytes | 제거 파일 수 / bytes | 남은 원본 blob bytes |
|---|---:|---:|---:|
| 저장소 전체 | 1,785 / 267,845,523 | 1,363 / 265,292,205 | 2,553,318 |
| docs/evidence | 1,374 / 240,701,673 | 1,351 / 240,491,953 | 209,720 |
| docs/league | 25 / 25,229,474 | 12 / 24,800,252 | 429,222 |

마지막 열은 **남긴 원본 blob의 합**이다. 이번 R1이 추가한 해시 매니페스트·문서·다른 구현 수정은 포함하지 않으므로 최종 R1 트리 크기는 통합 커밋에서 별도로 측정한다.

남긴 evidence는 짧은 Markdown 검토·측정 방법, BenchmarkDotNet 요약 CSV, 교차 플랫폼 요약 CSV 및 리그 실행 메타데이터 CSV다. 사례별 결과·타임라인·스크린샷·원시 JSON·압축 파일은 Release에서 복원한다. `docs/league/`의 집계 CSV와 `S4-report.md`는 원본을 유지했다. 후속 실험 결과로 원본 FAIL을 바꾸지 않았다.

Release의 GitHub `isImmutable` 값은 `false`다. 자산을 덮어쓰지 않는 운영 규칙이며 플랫폼 불변성 보장은 아니다. 해시 목록은 payload 2개(압축 원본·파일별 매니페스트)를 검증한다. 해시 목록 자체와 안내문은 별도 다운로드 후 업로드 원본과 바이트 비교했다. 목록 자체의 재귀적인 자기 해시는 주장하지 않는다.
