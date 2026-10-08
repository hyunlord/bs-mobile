# ADR0024 — Unity와 Core·정본 데이터의 생성 연결

상태: 채택 — 1단계 A 구현 선택, 실행 관문은 PR의 측정 결과로 별도 판정
날짜: 2026-10-08
연결: [#87](https://github.com/hyunlord/bs-mobile/issues/87), [ADR0018](0018-unity-66-urp-baseline.md), [ADR0023](0023-production-data-and-experiment-boundary.md)

## 결정

Core의 Release/netstandard2.1 DLL을 생성 단계에서 `unity/Assets/Generated/Plugins/`로 빌드한다. Core 소스 정본은 `core/src/SowSiege.Core/`이며 Unity용 소스 복사본을 편집하지 않는다. DLL과 생성된 메타 파일은 Git에서 제외한다. Unity는 net8.0 Sim DLL을 참조하지 않는다.

호스트의 기존 ContentLoader가 `production`을 검증·해석하고, Unity App 아래 `Generated/CanonicalContent.g.cs`에 명시적 Core 생성자 호출을 만든다. 호스트의 reflection은 코드 생성에만 사용한다. 생성된 플레이어 코드에는 reflection 기반 JSON 해석기나 새 직렬화 의존성을 넣지 않는다. 알 수 없는 타입·순환 참조는 생성 실패로 처리한다. 배열과 사전의 삽입·열거 순서는 ContentLoader 결과 그대로 보존한다. 사전의 문자열 비교에는 ordinal comparer를 쓴다. 키를 다시 정렬하면 Core가 사전을 순회하는 선택/RNG 순서가 달라질 수 있으므로 생성 코드의 사전 순서와 상태 codec의 키 정렬을 혼동하지 않는다.

생성된 코드의 파일 목록은 정본 JSON의 상대 경로·바이트 길이·SHA256을 가진다. 전체 데이터 식별자는 기존 ContentLoader.Hash와 같은 정렬된 경로/NUL/원본 바이트/NUL 해시다. `test/`를 제외한 전체 JSON 트리 식별자이므로 선택된 전투 수치만의 해시로 부르지 않는다. 생성 전후 정본 해시가 달라지면 실패한다.

빌드 직전에 소스/DLL/브리지의 출처 일치를 검사한다. 빌드 프로세서가 목록의 원본 JSON 파일을 각각 StreamingAssets에 추가한다. 플레이어는 실제 패키지의 바이트를 읽고 길이·파일 해시·전체 해시를 확인한 뒤에만 생성된 catalog로 세션을 만든다. 불일치하면 화면에 원인을 표시하고 시작을 막는다. Editor의 직접 파일 읽기를 Android 패키지 읽기 검증으로 대신하지 않는다.

생성은 Unity 실행 전에 끝낸다. 빌드 콜백 안에서 C#을 다시 생성해 컴파일 시점과 엇갈리게 하지 않는다. `Game.App`만 세션 변경 권한을 소유하며 View에는 분리된 읽기 전용 값과 사건을 제공한다. 입력·개발 UI는 의도를 App에 전달한다.

## 대안과 검증

Core 소스를 로컬 패키지로 묶는 방식도 가능하지만 이번에는 생성 DLL로 .NET 타깃과 Unity 컴파일 경계를 명확히 한다. 손으로 유지하는 Core/data 복사본은 거절한다. 런타임 reflection 직렬화는 AOT/stripping 검증 범위를 불필요하게 넓혀 선택하지 않는다.

생성 코드 컴파일, 정본 훼손/생성물 오래됨 거부, 실제 EditMode·PlayMode·IL2CPP 빌드와 세 런타임 재생을 검사한다. 마지막 기기 실행 관문을 로컬 빌드 성공으로 대체하지 않는다. U0의 실제 설치·라이선스 결과는 [환경 보고](../review/phase1a-u0.md)에 있으며 ADR0018의 당시 미설치/미실행 기록을 새 결과로 덮어쓰지 않는다.

참고: [Unity StreamingAssets](https://docs.unity3d.com/6000.6/Documentation/Manual/StreamingAssets.html), [빌드별 파일 추가](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Build.BuildPlayerContext.AddAdditionalPathToStreamingAssets.html).
