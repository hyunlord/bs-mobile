관문: 통과 — A 576·B 288사례 ×3반복, 각12보고 파일 CSV 재현 및 잔재 원장 검증. 밸런스 관문(c)는 실패로 보존한다.
변경: 대량 원자료는 Release에, 요약과 해시 매니페스트는 이 경로에 보관한다.
결정: 추가 보정은 #61의 별도 사전 선언 실험으로 남기며 Unity는 시작하지 않는다.

# R3 증거 안내

측정 소스는 `d9653353f0c17fcea756910d9b1db28d400680b6`이다. [R3 보고](../../review/R3-report.md), [종료 보고](../../review/Phase0-close-report.md), [Release](https://github.com/hyunlord/bs-mobile/releases/tag/phase0-r3-sprout-march-20261008)를 연결한다.

`R3-assets-sha256.csv`는 압축 자산과 파일 매니페스트의 크기·SHA-256을 기록한다. 자기 자신의 해시는 포함하지 않는다. `R3-source-manifest.csv`는 깨끗하게 추출한 payload의 정확한 파일 집합·바이트·SHA-256이다. Release 자산은 덮어쓰지 않는 운영 규칙이며 GitHub의 WORM 불변 보장을 주장하지 않는다.

원자료의 `evaluation-A`, `evaluation-B`는 열 개 CSV와 해시 확인 가능한 원본 JSON, 세 반복 및 이동 원장을 담는다. `evaluation-*-report`는 일반 S4b 보고, `evaluation-*-remains-report`는 원본 JSON까지 읽어 검증하는 잔재 보고다. 잔재 보고를 CSV-only 재현이라고 부르지 않는다. `verification/`는 재현·보존식·대응 비교, `technical/`은 실제 기본/더미 CLI·전체 검사·fixture 및 숫자 동결 검증, `frozen-inputs/`는 실제 측정 데이터를 담는다.

다운로드한 두 자산을 먼저 검사하고 빈 디렉터리에 압축을 해제한 뒤 검사한다.

```sh
node tools/verify-evidence.mjs R3-assets-sha256.csv <자산 디렉터리>
node tools/verify-evidence.mjs R3-source-manifest.csv <깨끗하게 추출한 payload>
```

A/B의 대응 seed 블록은 각각32개다. 결정론 반복을 독립 표본으로 늘리지 않는다. R2 원본에 잔재 계측 필드가 없는 것은 `not-instrumented`이며 0개가 아니다. ZIP의 최종 커밋·파일 수·크기·SHA-256·검증 결과는 ZIP 밖의 `bs-mobile-phase0-close-delivery-20261008.json` 전달 영수증에 기록해 자기참조를 피한다.
