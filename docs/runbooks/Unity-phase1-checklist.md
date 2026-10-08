관문: 부분 — 0건의 Unity 실행·기기 검증. 1단계는 미착수이며 이 문서는 착수 체크리스트다.
변경: 공식 문서 조사로 에디터 고정안·호환성 관문·사람이 처리할 계정 작업·첫 회색 상자 범위를 정리했다.
결정: 사용자 승인(2026-10-08)으로 Unity 6.3 LTS 6000.3.25f1 고정 후보를 채택했다. 시험 기기·Unity CI 방식은 답변 대기이며 설치하지 않았다.
근거: 확인일 2026-10-08. 아래 공식 자료는 그 시점의 확인이며 절대 최신 패치를 보증하지 않는다.
한계: 프로젝트 생성·에디터/SDK 설치·라이선스 활성화·비밀값 등록을 수행하지 않았다. Phase0 헤드리스 통과는 모바일 품질 통과가 아니다.

# 1단계 Unity 착수 체크리스트

버전 후보 승인은 #65에 기록했다. 시험 기기·Unity CI 방식은 사용자 답변 전 확정하지 않는다. 남은 계획 결정은 [needs-decision #65](https://github.com/hyunlord/bs-mobile/issues/65), Phase0 종료 정리는 [#64](https://github.com/hyunlord/bs-mobile/issues/64)에서 추적한다.

## 에디터와 플랫폼 고정안

- [ ] **Unity 6.3 LTS `6000.3.25f1`, changeset `e1dba0a9aba4`**를 후보로 검토한다. 공식 출시일은 2026-09-24이며 macOS ARM64와 Android/iOS Build Support를 제공한다. 이는 공식 페이지로 확인한 구체적인 후보이지, 더 새로운 패치가 없다는 주장이 아니다. 실제 착수 시 알려진 문제를 다시 확인하고 `ProjectVersion.txt`와 패키지 잠금 파일에 승인한 버전을 고정한다. [공식 패치·모듈·알려진 문제](https://unity.com/releases/editor/whats-new/6000.3.25f1)
- [ ] 6.3 LTS의 공식 지원 기한은 2027년 12월이다. 해당 패치의 2D Renderer/Bloom 검은 화면 등 알려진 문제를 고려해 첫 회색 상자는 후처리 없이 시작하는 안을 권고한다. 실제 프로젝트에서 안전하다는 검증은 아직 없다. [지원 정책](https://unity.com/releases/unity-6/support), [패치 알려진 문제](https://unity.com/releases/editor/whats-new/6000.3.25f1)
- [ ] Android는 선택 에디터의 Android Build Support·SDK·NDK·OpenJDK 조합을 설치·기록한다. 의존성 버전은 해당 에디터 지원 조합을 확인하며 임의의 최신 버전으로 대체하지 않는다. [Unity Android 환경 설정](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/getting-started/sdksetup)
- [ ] iOS는 iOS Build Support와 Xcode 또는 Unity Build Automation 경로를 선택한다. 로컬 최종 빌드에는 macOS/Xcode가 필요하다. 프로젝트 생성과 최종 서명·기기 설치를 별도 관문으로 기록한다. [Unity iOS 환경 설정](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/iphone/getting-started/ios-environment-setup)

## Core 연결 선행 관문

- [ ] 현재 저장소의 `Directory.Build.props`는 `net8.0`을 대상으로 한다. Unity 6.3의 API 수준은 .NET Standard 2.1 또는 .NET Framework 4.8이며 .NET Core 대상 관리 플러그인은 지원 대상이 아니다. 현재 DLL을 그대로 넣으면 된다고 가정하지 않는다. [Unity .NET 호환성](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)
- [ ] 별도 이슈·ADR에서 Unity 호환 타깃, 사용하는 BCL API·C# 문법·직렬화·IL2CPP/AOT 제약을 확인한다. .NET 8 CLI·역사 fixture를 유지하면서 공통 Core를 연결하는 최소 경로를 검증한다. 멀티타깃 또는 소스 공유는 검토안이며 이미 성공한 구현이 아니다.
- [ ] Unity 어댑터는 입력·카메라·표시·플랫폼 처리를 맡고, Core에는 Unity 타입·시각·SDK나 구체 영웅/영지 ID를 추가하지 않는다. 같은 데이터·seed·입력 순서의 결과를 .NET 실행과 Editor·기기 실행에서 대조한다. IL2CPP는 AOT를 사용하므로 Editor 통과만으로 기기 호환성을 선언하지 않는다. [Unity API·AOT 설명](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)

## 사람이 직접 처리할 계정·권한 작업

아래 항목은 필요한 시점에만 처리한다. 코딩·빌드 스크립트 작성·일반 검증은 에이전트가 수행하고, 사람의 계정 인증·약관 동의·조직 권한·기기 승인을 대신 추정하지 않는다.

| 필요한 시점 | 사용자가 직접 확인하거나 처리할 일 | 근거·경계 |
|---|---|---|
| 에디터를 사용하기 전 | 본인/조직의 재무·계약 관계에 맞는 라이선스와 좌석을 확인하고 계정 로그인·인증·동의를 처리한다. | Personal 소개 페이지는 최근 12개월 매출·투자금 $200K 미만 자격을 설명한다. 이 문서는 사용자의 자격을 판정하지 않는다. [Unity Personal](https://unity.com/products/unity-personal) |
| Unity CI를 켜기 전 | 사용할 runner/서비스의 라이선스 방식과 비용·조직 권한을 확인한다. 필요한 인증만 승인한 비밀 저장소에 등록한다. | 6.3 매뉴얼은 Personal 활성화·반납에 Hub만 지원한다고 명시한다. 무료 라이선스 파일을 복사하면 CI가 된다는 전제를 두지 않는다. 실제 batch 빌드 통과 전에는 CI 준비 완료가 아니다. [활성화 방식](https://docs.unity.com/en-us/engine/6000.3/manual/get-started/install-and-upgrade/licenses-and-activation/license-activation-methods) |
| Android 기기 시험 | 사용할 기기를 준비하고 잠금 해제·연결·디버깅 승인을 처리한다. | 개발용 설치와 스토어 배포를 분리한다. 배포가 필요해질 때 keystore·alias·암호의 소유·보관 방식을 확정한다. [키 로드·서명 설정](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/getting-started/keystore/load) |
| iOS 기기 시험·배포 | Apple ID·개발 팀·기기 신뢰/등록을 처리한다. 배포 경로에 맞는 회원 자격과 서명 권한을 확인한다. | Unity 환경 문서는 무료 Apple ID의 기기 시험과 Apple Developer Program의 App Store 배포 등을 구분한다. [iOS 환경 설정](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/iphone/getting-started/ios-environment-setup) |
| iOS 자동 서명 빌드가 필요할 때 | `.p12`·해당 암호·`.mobileprovision`과 필요한 기기/앱 식별자를 준비해 승인한 CI 저장소에 등록한다. | Unity Build Automation 서명 문서가 이 입력을 명시한다. 인증서·키·암호는 채팅·Git·전달 ZIP에 넣지 않는다. 첫 로컬 회색 상자에 모든 배포용 비밀값을 선행 요구하지 않는다. [Unity iOS 서명](https://docs.unity.com/en-us/build-automation/sign-build-artifacts/sign-an-ios-application) |

## 측정 프로필을 명시적으로 인계

- [ ] 동결 R2/R3 밸런스 평가는 **`s4b-02`와 연결된 catalog/tuning**을 사용한다. 비선형 XP 계수 30/15/5, 영주 체력 1800 등은 이 동결 설정의 값이다. 최종 측정 소스·프로필·tuning 해시를 [동결 기록](../league/S4b-freeze.md) 및 [종료 보고](../review/Phase0-close-report.md)와 대조해 승인한 조합을 선택한다. R3 전체 재실행은 완료됐지만 밸런스 관문(c)는 실패다. 종료 보고의 기술 완료와 밸런스 실패를 함께 인계한다.
- [ ] `s2-baseline`·`s4-stage-one`은 이전 tuning을 사용하는 기술/역사 실행 경로다. 수동 기본 CLI에서 관측한 레벨 80을 R3 밸런스 결과로 인계하지 않는다. 루트 `data/tuning.json` 또는 현재 CLI 기본값이 자동으로 S4b 보정 수치를 선택한다고 가정하지 않는다.
- [ ] Unity 어댑터의 데이터 로드에 승인한 프로필·catalog를 명시하고 실제 로드된 ID·위협·XP·잔재 설정과 해시를 기록한다. Editor·기기 비교에는 같은 조합을 사용한다. 새 수치 선택이나 재보정은 별도 이슈이며 이 체크리스트가 승인하지 않는다.

## 첫 회색 상자 범위 — 선택한 A안

사용자 선택은 **새싹 변경 / 개척 기사**, 기본 영웅 1·영지 1이다. 선택한 콘셉트와 실제 구현·검증 완료를 구분한다. R2 측정과 R3 최소 고리의 결과·남은 실패를 인계한 뒤 1단계 작업 이슈에서 시작한다.

- [ ] 한 장면에 영주·적·농지·성장 단계·백성·최소 건물을 회색 도형으로 표시한다. 실제 콘텐츠·규칙은 승인된 데이터를 읽고 구체 ID로 동작을 분기하지 않는다.
- [ ] **한 손 이동 → 자동 전투·도구 발동 → 처치 잔재 → 비옥도 → 작물 성장 → 수확 식량·경험치 → 다음 전투**를 화면에서 추적할 수 있게 연결한다. 직접 건설·탭 배치·백성 수동 배분 조작은 넣지 않는다. 선택은 레벨업 카드로 제공한다.
- [ ] 씨앗 자루의 직접 전투 효과와 성장 결과를 구별하고 첫 도구가 두 역할을 한다는 점을 사람이 이해하는지 관찰한다. 처치·잔재·비옥도·성장의 변화는 실제 Core 사건에서 표시한다. 영상 연출로 미구현 고리를 대신하지 않는다.
- [ ] 최소 HUD는 체력·시간/계절·레벨·카드 선택·종료/재시작을 제공한다. 잔재 운반 같은 후보 능력은 구현·검증한 범위만 표시한다. 영웅 선택 화면이나 다중 영지 제작으로 범위를 넓히지 않는다.
- [ ] Android/iOS의 지정 실제 기기에서 터치 이동, 카드 선택 중 입력, 화면 가림·가독성, 안전 영역, 앱 일시정지·복귀, 사망·재시작을 관찰한다. 지원 대상 전체를 검증했다고 확대하지 않는다.
- [ ] 기기 모델·OS·빌드 커밋·에디터 버전·설정·seed와 함께 프레임 시간·할당·발열/지속 실행 조건을 기록한다. Core 틱 p95와 전체 프레임 시간은 다른 지표다. Unity도 Editor와 대상 플랫폼 양쪽 프로파일링을 권고한다. [Unity 플랫폼별 프로파일링 경계](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)

## 완료 판정과 이번 범위 밖

착수 이후 이슈 → PR → CI 흐름을 유지한다. 컴파일·빌드, Core 결과 대조, 실제 기기 조작, 사람이 이해하는 순환·재미를 각각 판정하고 미검증을 그대로 남긴다. 원본 S4 밸런스 실패와 이후 실험 결과는 별개 기록으로 보존한다. 기기 증거·로그는 저장소 예산과 외부 증거 보관 정책에 따른다.

광고·결제·분석·UA·크래시 SDK 설정, 상점 상품·가격, 아트 완성, 출시 배포는 첫 회색 상자 범위가 아니다. 이 체크리스트 작성으로 Unity 설치나 1단계 구현을 시작하지 않는다.
