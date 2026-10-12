![보조 버튼과 도전 받기 — 실제 Editor](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-d12db4a/cream-and-claim.png)

관문: 부분 — 활성 크림 버튼의 글자·횟수 표시와 실제 도전 받기를 검토하는 후속 변경이다. 사용자 이미지 승인은 아직 필요하다.
깨끗한 `d12db4a6e49474db17e579764f2ea46dea760fc9`의 재촬영 검사19/19가 통과했고, 활성 크림 버튼3종의 글자 중심부 대비는10.09–11.05:1이다.
#223 성능은 PR235 관문에서 제외됐으며 해결됐다는 뜻이 아니다. 이미지 승인 전에는 초안 해제·병합하지 않는다.

## 이번 확인 기준

| 항목 | 요구 사항 | 현재 증거 상태 |
| --- | --- | --- |
| 활성 크림 버튼 | 글자색 `#3B2A18`, 대비≥4.5:1 | 다시 뽑기10.5911·금지11.0467·고정10.0917:1, 선택한 글자 중심부 표본에서 기준 충족 |
| 횟수 표시 | 해당 버튼 안에 횟수·한도 표시 | 실제390×844 및1080×2520 재촬영 원본과 비교 이미지 공개 |
| 도전 받기 | 실제 달성·미수령 상태의 받기 버튼 | 실제 미수령 받기 상태의 재촬영·포인터 검사 포함; 화면만으로 지급 검증을 대체하지 않음 |
| 후보 검사 | 직접 후보19/19 통과 기록 | 최종 소스의 깨끗한 재촬영 실행과 구분 |
| 깨끗한 재촬영 | `artifacts/portrait-234/contrast235-d12db4a-playmode.xml` 및 해당 캡처 | 19/19 통과, XML 종료2026-10-12 03:19:44 UTC |
| CI·Mac 빌드 | [CI38199243626](https://github.com/hyunlord/bs-mobile/actions/runs/38199243626), d12db4a 앱 | CI 진행 중; Mac ARM64 Mono Release 빌드·개발 기능 제외 검사 통과 |
| 사용자 관문 | 실제 이미지 확인·승인 | 미완료; PR235 초안 유지 |

[대비 CSV](../metrics/portrait-235-cream-contrast.csv)는 실제 원본 PNG의 글자 중심부와 바로 인접한 크림 배경을 표본으로 측정했다. 안티앨리어싱된 경계 픽셀은 제외했다. 이 세 표본의 기준 충족을 모든 글자·텍스처 위치·비활성 상태의 자동 합격으로 확대하지 않는다. 캡처 내용은 합성하거나 가짜 상태로 그리지 않았다. Editor 캡처·포인터 검사는 실기 터치나 사용자 시각 승인을 대신하지 않는다.

재촬영 sourceHash는 `371A9D3045A9C249505F937AA5B4737C2982DA363CA94CD8432644E3E37CB5DC`, sourceDirty=false다. 직접 후보19/19와 깨끗한 재촬영19/19는 별도 실행으로 보존한다.

[Release 표본](https://github.com/hyunlord/bs-mobile/releases/tag/portrait-v0-d12db4a) · [긴 화면 비교](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-d12db4a/cream-and-claim-long.png) · [Mac 앱](https://github.com/hyunlord/bs-mobile/releases/download/portrait-v0-d12db4a/Sow-and-Siege-portrait-d12db4a-mac.zip). Mac ARM64 Mono Release 빌드와 개발 기능 제외 검사를 통과했고 배포 파일98,058,608바이트 게시를 확인했다. 일반 실행의 사람 클릭 판정은 사용자 담당이다.

## 성능 조사와 과거 기록

#223은 PR235의 병합 관문에서 명시적으로 분리한다. 끊김 해결·성능 통과로 닫지 않는다. 2026-10-13 01:00–06:00(KST) 일정은 진단 전용이며 깨어 있기 유지(caffeine)를 사용한다. 방해 금지는 설정되지 않았고, 이 일정 자체는 전경 유지나 계측 완료 증거가 아니다. 메모리 계측은 별도 가지에서 수행한다.

이전 fa6c71a의 화면·성능 실패·재생·출처는 [기존 검토 보고](portrait-234-reference-skin.md)에 보존한다. 이번 문서가 과거 원판정이나 미확인을 덮어쓰지 않는다. #126 경제·#98 Fold7·#224 생산 부활·1b 확장 보류를 유지한다.

[PR235](https://github.com/hyunlord/bs-mobile/pull/235) · [미해결 #223](https://github.com/hyunlord/bs-mobile/issues/223)
