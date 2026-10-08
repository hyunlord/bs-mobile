# ADR 0018: 설치된 Unity 6.6과 URP 2D를 다음 단계 기준으로 고정

- 상태: 채택 — 2026-10-08 사용자 결정
- 결정일: 2026-10-08
- 관련: [#65](https://github.com/hyunlord/bs-mobile/issues/65), [#67](https://github.com/hyunlord/bs-mobile/issues/67)

### 배경과 대체 이력

2026-10-08의 앞선 선택은 Unity 6.3 LTS 6000.3.25f1이었다. 같은 날 사용자가 실제 설치 버전을 Unity 6.6으로 정정하고 URP Universal 2D를 선택했다. 이 결정은 앞선 6.3 선택만 대체한다. 원래의 선택 이력을 삭제하거나 당시 검증 결과를 6.6의 결과로 바꾸지 않는다.

### 결정

다음 단계의 에디터 기준은 **Unity 6.6 6000.6.4f1 ARM64**, 프로젝트 시작 방식은 **Universal 2D 템플릿의 URP 2D Renderer**다. 프로젝트 생성 시 승인 버전을 ProjectVersion.txt와 패키지 잠금 정보에 고정한다. 지금은 프로젝트 생성·에디터 실행·SDK 설치를 하지 않으며 Phase1 착수 승인을 뜻하지 않는다.

2026-10-08 읽기 전용 로컬 검사에서 `/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/Info.plist`의 CFBundleVersion은 `6000.6.4f1`, `Contents/MacOS/Unity`는 Mach-O ARM64였다. Hub metadata는 `Unity 6.6`/`arm64`다. `/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines`에는 `MacStandaloneSupport`, `WebGLSupport`만 존재했다. 다음 경로는 없었다.

- `PlaybackEngines/AndroidPlayer`
- `PlaybackEngines/AndroidPlayer/OpenJDK`
- `PlaybackEngines/AndroidPlayer/SDK`
- `PlaybackEngines/AndroidPlayer/NDK`
- `PlaybackEngines/iOSSupport`

modules.json의 Android/iOS 항목은 설치 가능 카탈로그이며 설치 증거가 아니다. 그 카탈로그의 OpenJDK 17.0.18+8, NDK r27c, SDK Build Tools 36.0.0 등의 버전을 설치 완료 버전으로 보고하지 않는다. 외부 경로에 별도 SDK가 있는지는 조사하지 않았다.

사용자는 **Unity Personal 라이선스를 활성화했다고 알렸다**. 에이전트는 활성화 상태·라이선스 자격을 독립 검증하지 않았다. 실제 기기 모델/OS와 Unity CI 방식·runner·라이선스 인증은 #65의 남은 결정이다. 필요한 비밀값은 승인한 저장소에만 등록하고 Git·채팅·납품 ZIP에 포함하지 않는다.

### 기술 경계

Unity 6.6도 C# 9 및 기본 .NET Standard 2.1을 사용한다. Core 호환성 관문은 바뀌지 않는다. Core에 Unity 타입·구체 영웅/영지 ID·새 패키지를 넣지 않으며, netstandard2.1 DLL을 .NET 8 테스트 호스트에서 실행한 결과는 API 계약 대상의 결과 동등성 증거다. 실제 Unity Mono/IL2CPP·stripping·기기 검증은 별도다. record에 필요한 IsExternalInit 보완과 Unity 직렬화 DTO 분리도 유지한다. [C# 9 및 records](https://docs.unity3d.com/6000.6/Documentation/Manual/csharp-compiler.html), [API 및 AOT 경계](https://docs.unity3d.com/6000.6/Documentation/Manual/dotnet-profile-support.html)

### 패치 위험과 검증 계획

6000.6.4f1의 공식 배포일은 2026-10-01이다. [공식 release note](https://unity.com/releases/editor/whats-new/6000.6.4f1)의 알려진 문제는 프로젝트에서 재현한 결과와 구분한다.

| 공식 알려진 문제 | 다음 단계 검증 |
|---|---|
| UUM-153744: Vulkan swapchain VK_TIMEOUT 처리 중 SIGSEGV | Android Vulkan 채택 시 실제 기기의 실행·전환·복귀 로그 확인 |
| UUM-149540: Player의 Resources.UnloadUnusedAssets 지연 | 해당 API를 쓰면 Player에서 호출 시간 측정 |
| UUM-142773: 여러 bee_backend 동시 실행 시 충돌 | 최초 빌드를 직렬 실행하고 로그 보존 |
| UUM-150566: 특정 프로젝트 시작 시 Mono 로그 충돌 | import·Editor 시작 성공을 별도 기록 |

초기 회색상자에서 필요 없는 조명·후처리를 추가하지 않는 방식을 권고한다. 이는 별도 사용자 결정이 아니다. 2D Renderer의 존재나 Editor 설치가 모바일 성능 통과를 뜻하지 않는다. [6000.6 URP 2D Renderer](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/2DRendererData-overview.html)

### 대안과 후속

Built-in은 6.5부터 deprecated이고 신규 프로젝트에 권장되지 않는다. 최신 2026-09-29 공식 발표는 Unity 7.0에서 제거, 6.7 LTS에서는 2028년 말까지 지원(Extended Support 2029년 말)을 명시한다. 따라서 오래된 전략 문서의 제거 시점 미정을 현재 결론으로 사용하지 않는다. [6.5 deprecation](https://unity.com/topics/render-pipelines-strategy-for-2026), [최신 공식 발표](https://discussions.unity.com/t/the-path-to-a-single-render-pipeline-in-unity-7/1737908)

[후속 이슈 #70](https://github.com/hyunlord/bs-mobile/issues/70)에서 추적한다. 6.7 LTS는 연말 계획이며 현재 beta다. 출시 후 별도 이슈→PR→CI와 실제 Unity 회귀 검증으로 전환 여부를 결정한다. 설치된 6.6.4f1을 지금 자동 업그레이드하지 않는다.
