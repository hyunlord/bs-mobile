# Unity 경계 예약

Unity 프로젝트는 아직 생성하지 않았다. 사용자 결정은 **Unity 6.6 6000.6.4f1 ARM64 + Universal 2D(URP 2D Renderer)**이며, 이전 6.3 선택의 대체 이력과 실제 설치 상태는 [ADR 0018](../docs/adr/0018-unity-66-urp-baseline.md)에 기록한다. 에디터는 설치되어 있지만 Android/iOS 빌드 모듈은 확인한 설치 루트에 없다. Personal 활성화는 사용자 진술이며 독립 검증하지 않았다.

[착수 체크리스트](../docs/runbooks/Unity-phase1-checklist.md)에 따라 별도 Phase1 착수 요청 후 프로젝트를 생성한다. 실제 시험 기기와 Unity CI 방식은 [#65](https://github.com/hyunlord/bs-mobile/issues/65), 6.7 LTS 정식 출시 후 전환 검토는 [#70](https://github.com/hyunlord/bs-mobile/issues/70)에서 추적한다.

Unity 어댑터는 입력·표시·플랫폼 서비스를 맡고 순수 C# Core를 소비한다. 엔진 타입·시각·SDK를 Core에 역참조시키지 않는다. .NET Standard 2.1 계약의 .NET 8 호스트 검증과 Unity/IL2CPP·실기 검증은 별개다. Force Text·LFS·asmdef·CI의 구체 설정은 착수 범위에서 검증한다. 광고·결제·분석·크래시 SDK는 이 준비 작업에 포함하지 않는다.
