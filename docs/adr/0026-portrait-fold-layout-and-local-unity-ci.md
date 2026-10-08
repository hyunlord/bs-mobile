# ADR0026 — 세로 폴드 배치와 맥북 로컬 Unity 관문

상태: 채택 — 사용자 1단계 A 범위
날짜: 2026-10-08
연결: [#83](https://github.com/hyunlord/bs-mobile/issues/83), [#87](https://github.com/hyunlord/bs-mobile/issues/87)

## 세로 화면과 같은 판 유지

이번 빌드는 세로 방향을 요청한다. 화면 크기·안전 영역 변화에 맞춰 카메라와 HUD를 다시 배치하며 세션을 재생성하지 않는다. 긴 외부 화면과 정사각형에 가까운 내부 화면에서 같은 입력·카드·결산 기능을 제공한다. 카메라 줌 범위는 정본 표현 데이터에서 가져온다.

Screen.safeArea만으로 접힘부를 전부 처리했다고 주장하지 않는다. 플랫폼의 실제 화면/안전 영역과 실기 관찰로 접힘부·카메라 구멍 침범을 확인한다. Android의 큰 화면 방향 정책과 game category를 확인하고 병합된 manifest를 검사한다. 물리 접기·펼치기 각3회와 두 화면의 막바지 캡처는 별도 실기 관문이다. 합성 화면 크기 테스트는 실기 관문을 대신하지 않는다.

## 검사 위치

Unity6000.6.4f1 Personal 라이선스가 활성화된 맥북에서 `tools/check-unity.sh`를 실행한다. 생성물 준비 → EditMode → PlayMode → ARM64 Android IL2CPP 빌드를 직렬 실행한다. 테스트 실행에는 `-quit`를 붙이지 않고 결과 XML의 양수 테스트 수/실패 없음과 프로세스 종료 상태를 함께 확인한다. 빌드는 BuildReport 성공과 실제 APK를 확인한다.

GitHub Actions는 기존 Core 검사만 수행한다. 공개 저장소에 개인 기기/라이선스 비밀을 올리거나 새 자체 호스팅 러너를 연결하지 않는다. PR에 로컬 Unity 명령·정확한 결과·관련 커밋을 기록한다. 원본 라이선스/기기 식별 로그는 공개하지 않는다.

실기 프레임은 1배속 마지막 계절의 전체 표본을 유지한다. nearest-rank p95와 최대 프레임 시간·최대 개체 수·가능한 thermal status를 기록한다. 이른 사망으로 마지막 계절이 없으면 불완전한 성능 관측이다. 별도 무적 부하 판을 쓰면 일반 플레이와 구분해 기록한다. 16.7ms 목표를 넘으면 실제 병목과 개선안을 보고한다.

참고: [Unity 테스트 CLI](https://docs.unity3d.com/6000.6/Documentation/Manual/test-framework/reference-command-line.html), [safeArea](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Screen-safeArea.html), [Android 큰 화면 Unity 지침](https://developer.android.com/games/engines/unity/unity-large-screen?hl=en).
