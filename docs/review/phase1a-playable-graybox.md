관문: 비실기 통과 / 실기 대기. 사용자 요청에 따라 비실기 범위를 병합하고 기기 연결 후 나머지를 검증한다.

# Phase1A 회색 상자와 기록 검증

1. #89의 한 해 조작·카드·결산·사망/재시작과 #90의 개발 도구·재생·프레임 기록을 구현했다.
2. Core와 정본 데이터는 변경하지 않았다. Unity는 입력·표현·플랫폼 기록을 맡는다.
3. 실제 Unity 6000.6.4f1에서 EditMode 19/19, PlayMode 8/8, host 검사 17/17이 통과했다.
4. 전체 Core 검사 262개와 두 .NET 대상 결정론 실행 96개, 기존 역사/밸런스 검사가 통과했다.
5. 동일한 5개 전체 해 기록은 .NET과 실제 Editor Mono에서 일치했다. Android 실행은 대기다.
6. 실제 App의 한 해 종료·자연 사망·중도 종료 기록을 CLI로 다시 실행해 세 최종 해시 모두 일치했다.
7. 세로·정사각형 화면의 GPU 출력, 한국어 카드·HUD·결산·개발 메뉴 스크롤을 직접 확인했다.
8. 실제 Android release 스크립트 어셈블리에서 개발 도구 제외를 확인했다. 전달 APK는 개발용 ARM64 IL2CPP 빌드다.
9. 프레임 구간의 카드/종료 전환·긴 프레임을 보존하고, 중단/부분 구간을 원본에 남기되 적격 지표에서 구분한다.
10. 실기 관문 3·4·5 및 관문 2의 Android 부분은 대기이며 Android 글꼴·터치·접힘·성능·재미를 통과로 주장하지 않는다.

| 검사 | 관측 결과 | 범위 |
|---|---|---|
| 에디터 한 해 | seed 30000, 21,600 tick, 정상 결산 | 기록된 개발 명령을 사용하는 전체 경로 검사 |
| 자연 사망 | seed 30000, 5,057 tick, 사망 후 재시작 | 무적 없이 북동 이동·첫 카드 선택; Core 보정 없음 |
| 중도 종료 | 실제 App 종료 기록 CLI 일치 | Unity가 작성한 동일 파일 사용 |
| GPU/UI | 세로·정사각형 모두 통과 | 실제 URP 2D 출력; 물리 접힘 대체 아님 |
| APK | ARM64 IL2CPP 빌드·서명·패키지 검사 통과 | 정본 JSON 246개와 재생 fixture 5개 원본 일치 |
| 실기 3·4·5 | 대기 | 후반 p95, 접기/펼치기 각 3회, 기기 기록 CLI 재생 |

## 재현과 증거

`bash tools/check.sh`, `bash tools/check-unity.sh`가 검사 진입점이다. 출처 검증은 커밋·소스 해시·dirty 상태를 별도로 묶고, 커밋 후 생성물을 다시 만들고 검증한다. 최종 전달 커밋·PR·CI와 APK는 PR 및 Release에 연결한다.

로컬 검사 자료는 `artifacts/phase1a-u3-core-check-frozen.log`, `artifacts/phase1a-u3-unity-check-final.log`, `artifacts/unity/{EditMode,PlayMode}.xml`, `artifacts/unity/app-recordings/`, `artifacts/unity/screenshots/`에 있다. 원본 계정/라이선스 로그는 공개하지 않는다. CSV에서 재생성 가능한 출력과 ZIP·CRC·영수증은 올리지 않는다.

최종 APK는 수정 사항 없는 커밋 `92415c5d09d612f485daf0d4212de4aabfbafbbb`에서 다시 검증했다. `artifacts/phase1a-u3-unity-check-clean.log`의 전체 검사와 새 App 기록 3개의 CLI 검증이 통과했다. APK는 40,823,623바이트이며 소스 해시는 `985AE8271403F6E12FFFF79BC696CF5145C42C4764833D404384F5A2E455DCF5`다. 이 후속 문서 기록은 APK의 빌드 커밋을 바꾸지 않는다.

[PR92](https://github.com/hyunlord/bs-mobile/pull/92)의 최초 CI는 Conventional Commits 접두사 없는 PR 제목을 거부했다. 제목을 수정한 [CI](https://github.com/hyunlord/bs-mobile/actions/runs/37798407124)는 전체 통과했다. 최초 실패는 삭제하지 않으며, 이 문서 커밋을 포함한 최종 HEAD도 병합 전 CI를 통과해야 한다.

[플레이 목록](../runbooks/phase1a-playtest.md)과 [기기 기록 런북](../runbooks/device-play.md)에 설치·한 손 이동·조준 비교·도구/무기 판·기록 추출 순서를 적었다. #89는 비실기 구현 완료로 닫고 Android 비교를 포함하는 #90과 전체 #83은 유지한다. #85 조준 기본값은 사용자 플레이 판단을 기다린다.

## 구조 결정과 남은 위험

[ADR0027](../adr/0027-player-build-identity-and-frame-metrics.md)은 빌드 출처와 지표 축을, [ADR0028](../adr/0028-urp2d-instanced-graybox-rendering.md)은 실제 URP 2D 인스턴싱 검증과 단일 월드 카메라 조건을 설명한다. OS 글꼴은 에디터에서 한국어를 확인했으나 Android 확인이 남았다. 잘못된 데이터/글꼴은 표시 가능한 오류로 멈추며 원인을 고친 뒤 재실행한다. 실기 성능·발열·hinge 안전 영역은 아직 측정하지 않았다. 1단계 B는 시작하지 않았다.
