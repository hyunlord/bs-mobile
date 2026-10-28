# ADR0074: 변경된 텍스처의 플랫폼별 예상 메모리 기록

- 상태: 승인된 사용자 요청의 구현 제안 (PR 검토 중)
- 날짜: 2026-10-28
- 연결: #487, #460, #465

## 맥락

원본 PNG 파일 크기는 GPU 텍스처 비용이 아니다. 모바일 ASTC4 적용이 승인됐지만 Mac 표시는 유지해야 하며 새 그림 PR의 비용을 추적할 필요가 있다. 실기 측정 없이 예상 바이트를 실제 메모리로 보고하면 안 된다.

## 결정

PR merge-base부터 변경된 runtime PNG 또는 PNG.meta마다 같은 PR의 `docs/metrics/texture-memory-deltas/*.json`에 before/after를 기록한다. 각 값은 ESTIMATE, PNG SHA256/파일 크기, 치수, mip 여부, Mac/Android/iOS 형식과 예상 바이트, 변경 이유를 포함한다. 신규·삭제 그림의 없는 쪽은 null이다. 미변경 역사 파일에는 소급 요구하지 않는다.

검사는 실제 PR 경로를 사용한다. 현재 PNG 헤더·플랫폼 메타·산식을 확인하고 git LFS 포인터도 oid/size로 검증한다. 원판이 같으면 이전 플랫폼 메타도 확인한다. 변경 전 LFS 원판을 내려받을 수 없는 경우 이전 치수는 포인터에 연결된 작성자 추정값으로 남긴다. ASTC는 각 mip의 `ceil(width/block) × ceil(height/block) × 16`을 더한다. 지원하지 않는 자동 압축·크기 축소 설정은 추정하지 않고 실패한다.

이번 적용은 승인된 hero-knight, fallow-enemy-rigs, estate-decor의 Android/iOS ASTC4만이다. Default/Standalone과 원본 PNG는 유지한다. 원본 alpha-padding 검사는 임포트 후 압축 결과 대신 PNG를 임시 RGBA로 디코드해 수행하며 기존 허용 수치는 바꾸지 않는다.

## 결과·비용

CI에 작은 Node 검사와 변경 기록이 추가된다. 플랫폼별 모든 형식, 드라이버 할당, ASTC 미지원 기기 fallback, 실제 기기 RAM은 이 산식의 범위가 아니다. 새 형식은 명시적으로 검사를 확장해야 한다. 전역 메모리 예산 상한은 도입하지 않는다.

## 거절한 대안

- PNG 파일 크기를 메모리로 사용: 디코드·블록 압축 크기와 다르다.
- 모든 역사 텍스처에 기록을 요구: 승인 범위 밖 소급 차단이다.
- 모바일 압축 픽셀로 원판 padding 판정: 압축 오차를 원본 결함으로 오판한다.
- 모든 플랫폼 ASTC 적용: Mac 표시 유지 계약에 어긋난다.

## 검증

Node 음성 대조(누락, stale identity, 산식 오류), 실제 git PR diff, 1MiB 초과 비LFS 원본을 검사한다. Unity EditMode는 세 atlas의 모바일 override와 Mac RGBA 유지, padding alpha0 허용·alpha1 거절을 검사한다. 두 프로필 실행 기록과 실제 ASTC 시험 출처는 PR 검토 문서에 남긴다. 실기 성능을 증명하지 않는다.
