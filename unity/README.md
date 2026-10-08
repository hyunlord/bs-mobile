# Unity 1단계 A

**Unity 6.6 6000.6.4f1 ARM64 + Universal 2D(URP 2D Renderer)**를 사용한다. Android OpenJDK·SDK·NDK와 iOS 모듈, Personal 라이선스의 실제 batch 실행은 [U0 보고](../docs/review/phase1a-u0.md)에서 확인했다. 이전 설치 상태와 6.3 선택 대체 이력은 [ADR0018](../docs/adr/0018-unity-66-urp-baseline.md)에 보존한다.

저장소 루트에서 먼저 `bash tools/prepare-unity.sh`를 실행한 뒤 이 폴더를 Unity Hub로 연다. .NET 8 SDK와 Node가 필요하다. 생성된 Core DLL·데이터 브리지·빌드 식별자는 Git에서 제외되며 커밋이나 입력 소스·데이터가 바뀌면 다시 생성한다. `bash tools/prepare-unity.sh --verify`는 생성물을 수정하지 않고 오래된 연결을 거부한다.

`bash tools/check-unity.sh`는 생성·경계 검사, Editor 설정, EditMode·PlayMode, ARM64 Android IL2CPP 빌드를 순서대로 실행한다. 기본 출력은 `artifacts/unity/`이며 성공한 APK는 `sow-siege.apk`다. 같은 프로젝트를 연 Editor를 먼저 종료한다. `UNITY_EDITOR`와 `UNITY_CHECK_OUTPUT`으로 실행 경로와 출력 폴더를 지정할 수 있지만 에디터 버전은 고정한다. 로컬 원본 로그에는 라이선스 정보가 포함될 수 있으므로 공개하지 않는다.

Unity는 입력·표시·플랫폼 서비스를 맡고 `netstandard2.1` Core를 소비한다. `Game.App`이 Core 명령을 적용하며 View는 읽기 전용 스냅샷과 사건을 받는다. 정본 JSON은 빌드 때 패키지에 포함하고 실제 읽은 바이트를 시작 전에 검증한다. 연결 방식은 [ADR0024](../docs/adr/0024-generated-core-and-canonical-unity-bridge.md), 입력·재생은 [ADR0025](../docs/adr/0025-interactive-core-and-portable-replay.md), 화면 규칙은 [게임 디자인 시스템](../docs/design/05_phase1a-game-design-system.md)에 따른다.

개발 APK에는 기록 재생 검사와 개발 메뉴가 있다. 같은 검사 명령에서 별도로 컴파일한 Android 릴리스 스크립트에 개발 UI·검증 타입이 없는지도 확인한다. 릴리스 APK의 실기 실행까지 검증했다는 뜻은 아니다. [기기 기록 추출](../docs/runbooks/device-play.md)과 [폴드7에서 해 볼 것](../docs/runbooks/phase1a-playtest.md)을 함께 사용한다.

시험 기기는 Galaxy Z Fold7 (SM-F966N, Android 16), Unity 검사는 Personal 활성화된 맥북의 로컬 실행이다. GitHub CI는 Core 검사를 유지한다. 실제 기기 실행·물리 접힘·성능은 빌드 성공과 별도 관문이다. 현재 진행 상태와 실행 절차는 [1단계 체크리스트](../docs/runbooks/Unity-phase1-checklist.md)를 따른다. 6.7 LTS 정식 출시 후 전환은 [#70](https://github.com/hyunlord/bs-mobile/issues/70)에서 별도로 검토한다.
