관문: 통과 — Release 새 다운로드 자산 2개와 추출 파일 2,226개·4,144,928,329바이트의 크기·SHA-256·정확한 집합을 검증했다.
변경: 보정 2개 후보·A576/B288·기술 검사·CSV 재생성 원자료를 외부에 보존했다.
결정: 밸런스(c)는 실패이며 이 무결성 통과가 R2 밸런스 통과를 뜻하지 않는다. 후속 #61.

# R2 외부 증거와 재현

[Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r2-s4b-20261008)는 측정 소스 `bcaa00f763ec6950707635569befb558e8de1731`을 가리킨다. 운영상 덮어쓰지 않는다. GitHub 플랫폼의 WORM/immutable 기능을 활성화했다는 주장은 아니다.

| 자산 | 바이트 | SHA-256 |
| --- | ---: | --- |
| bs-mobile-R2-evidence-20261008.tar.gz | 300788194 | d9714b3d3a87f30ac4f55d6cf7b53a32bd4574a0b37fc517798c6c7b999f200e |
| R2-source-manifest.csv | 286489 | b4fb743845193726ed3a410efd48672b4494b2308930b82cbdef8cf71a6fcc36 |
| R2-assets-sha256.csv | 245 | 63946720b454cc8aae20724638745ac5e526fa65bf11386e9d2fdf5a2e6b4d01 |

소스 매니페스트는 payload 루트의 정확한 전체 파일 목록이다. 자산 매니페스트는 아카이브와 소스 매니페스트 두 파일을 검증하며 자기 자신은 제외한다. 위 별도 SHA와 Git에 보관된 바이트로 자산 매니페스트를 확인할 수 있다.

새 경로에 `gh release download phase0-r2-s4b-20261008 --dir DIR`로 내려받았다. `node tools/verify-evidence.mjs DIR/R2-assets-sha256.csv DIR` 결과는 asset-payloads-only, 2파일·301074683바이트 검증, 제외된 파일은 자산 매니페스트 자신뿐이었다. 새 추출 디렉터리에서 `tar -xzf DIR/bs-mobile-R2-evidence-20261008.tar.gz -C EXTRACTED` 후 `node tools/verify-evidence.mjs DIR/R2-source-manifest.csv EXTRACTED` 결과는 source-exact-set, 2226파일·4144928329바이트, 추가 파일0이었다.

payload의 calibration-01/02, evaluation-A/B에 열 개 CSV와 raw가 있다. `node tools/s4b-report.mjs EXTRACTED/evaluation-A NEW_REPORT_DIR`로 재생성한다. [A 재생성 대조](evaluation-A-replay.csv)·[B 재생성 대조](evaluation-B-replay.csv)는 각 12파일 바이트 일치다. CSV-only 재생성은 통계 재현이며 원본 JSON 해시 검증과 구분한다. technical/에는 후보별 전역 수치 감사, 실제 CI 로그, 더미·런타임 경계 검증이 있다. verification/에는 전체 보고 검토와 재생성 검증 기록이 있다.
