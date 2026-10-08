# ADR 0017: 같은 규칙을 .NET 8과 Unity API 계약에서 검증한다

- 상태: 채택, #67
- 대상: Core net8.0 + netstandard2.1, C# 9. CLI·검사·벤치는 net8.0 유지.

## 결정

Unity 6.6의 공식 API 계약은 .NET Standard 2.1이며 C# 9를 지원한다. Core만 두 타깃으로 빌드하고 Unity 타입, I/O, 외부 패키지를 넣지 않는다. 언어 변환은 파일 범위 namespace·primary constructor·collection expression·required·record struct의 의미를 보존한 C# 9 표현으로 제한한다. netstandard2.1의 init 접근자용 IsExternalInit을 내부 조건부 shim으로 제공한다. 기존 데이터·숫자·정책·seed·역사 fixture와 기대 해시는 변경하지 않는다.

System.Text.Json은 net8 공유 프레임워크에 있지만 netstandard2.1 BCL 자체의 일부가 아니다. Core에 새 패키지를 추가하는 대신 `IStateHasher.Compute(object)`를 생성자로 주입한다. Core는 종전과 같은 익명 snapshot을 전달하고 net8 호스트는 기존 정렬·배열 순서·필드 포함·이스케이프·숫자·대문자 SHA-256 규칙을 유지한다. `OmitWhenNullAttribute`는 Core의 선택적 메타데이터이며 호스트 resolver가 표시된 null 멤버만 생략한다. 전역 null 생략은 과거 해시를 바꾸므로 금지한다. Simulation 생성자 변경은 의도적인 API 변경이며 각 호스트가 codec을 명시적으로 책임진다. 전역 가변 기본값과 묵시적 대체 codec은 없다.

별도 net8 비교 실행기는 netstandard2.1 산출 DLL을 명시적으로 참조한다. Core ProjectReference를 다시 통해 net8 대상으로 결합하지 않는다. 두 프로세스의 실제 로드된 Core TargetFramework·MVID·경로·SHA-256을 검사하고 빌드 산출물과 일치시킨 후 동일 seed의 결과를 비교한다. 다른 타깃인 것처럼 이름만 바꾼 net8 DLL은 실패해야 한다.

## 검증과 한계

변환 전 03b6b2145d4ea408ef52ec8194e632cc3b402dd5에서 15가지 전체 길이 조건을 각 3회 실행했다. 역사 S2의 기본/더미 × A/B/C 6골든, 역사 S4, 현재 S2 기본/더미, 현재 S4b 사람/무기 × A/B/C를 포함한다. 자연 사망은 종결로 인정하며 사망 후 틱을 채우지 않는다. 변환 후 각 타깃 3회 실행을 기준선 게임 해시·의미 결과와 대조한다. 역사 fixture·기대 해시 갱신으로 실패를 덮지 않는다. 원래 전체 결과 digest는 보존하고, 비교용 gameplay digest는 변경 전 원자료에서 최상위 runMetadata만 제외해 계산한다. 커밋·소스·DLL 식별자는 변환과 타깃마다 달라야 하므로 별도로 검증하며 다른 게임 결과 필드는 제외하지 않는다.

이 검증은 서로 다른 계약 DLL을 net8 호스트에서 실행한 증거다. Unity Editor·Mono·IL2CPP·AOT·모바일에서 실행한 증거가 아니다. Unity용 codec·콘텐츠 역직렬화·Unity 직렬화 가능한 DTO는 1단계에서 별도 설계·검증한다. Unity serializer의 record 제한을 Core 내부 데이터 모델의 사용 금지로 확대하지 않는다.

Position의 기존 record struct가 생성하던 동등성·Deconstruct·문자열·해시 동작을 명시적 struct에서 보존한다. 컴파일러의 정수 해시 승수는 아키텍처 상수 목록에 좁게 기록하며 밸런스 수치로 취급하지 않는다.

## 기각

- Core에 System.Text.Json 패키지 추가: 순수 BCL 경계와 요청 없는 의존성 금지에 어긋난다.
- 자체 JSON 작성기로 해시 전면 재구현: 기존 canonical 표현을 복제하며 필요 없는 호환성 위험을 만든다.
- net8만 실행하고 타깃 이름 비교: 실제 다른 DLL의 결정론을 입증하지 않는다.

## 근거

- [Unity 6000.6 API 호환성](https://docs.unity3d.com/6000.6/Documentation/Manual/dotnet-profile-support.html)
- [Unity 6000.6 C# 컴파일러](https://docs.unity3d.com/6000.6/Documentation/Manual/csharp-compiler.html)
- [System.Text.Json 배포 범위](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)

확인일 2026-10-08. 6.3 기반 초기 조사에서 6.6으로 참조 기준을 교체했으며 API·언어 선택은 동일하다. 에디터·렌더 파이프라인 고정 결정은 ADR0018에서 추적한다.
