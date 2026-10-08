관문: 진행 중 — U0 실제 설치·batch 실행과 U1 정본 이관은 통과, U2 Unity 기반 검증 중. 실기 관문은 별도다.

# 현재 실행 절차 — 1단계 A

사용자의 [1단계 A 의뢰서](../design/04_ASTRA_GOAL_bs-mobile_phase1a.md)가 이전 미착수 제한을 대체한다. 추적은 [#83](https://github.com/hyunlord/bs-mobile/issues/83), Core 명령·재생은 [#86](https://github.com/hyunlord/bs-mobile/issues/86), Unity 기반은 [#87](https://github.com/hyunlord/bs-mobile/issues/87)이다. 시험 기기는 **Galaxy Z Fold8**, Unity CI 방식은 **Personal 활성화된 맥북의 로컬 검사**로 결정되어 #65를 닫았다.

| 관문 | 현재 확인과 완료 조건 |
|---|---|
| U0 | 6000.6.4f1, Android OpenJDK·SDK·NDK와 iOS 모듈, 실제 라이선스 batch 실행 통과. [보고](../review/phase1a-u0.md) |
| U1 | production 정본과 실험 경계 이관 완료. [PR88](https://github.com/hyunlord/bs-mobile/pull/88), 병합 `1385eae0255a2bb187e7bd2b5ef6c03ae102cd55`, [CI](https://github.com/hyunlord/bs-mobile/actions/runs/37782971456) 통과 |
| U2 | 아래 명령으로 생성 연결·실제 Editor 테스트·ARM64 IL2CPP APK를 검증하고 연결 PR에 결과 기록 |
| U3/U4 | 한 해 수동 조작·카드·결산·표현·개발 도구·재생·프레임 지표를 구현하고 실제 화면에서 검증 |
| 실기 | 같은 5개 기록의 .NET/Mono/IL2CPP 일치, 기기 기록 CLI 재생, 마지막 계절 p95, 접기·펼치기 각3회와 두 화면 캡처 |

저장소 루트에서 실행한다. Unity 프로젝트를 사용하는 다른 Editor 프로세스는 종료한 뒤 검사한다.

```sh
bash tools/prepare-unity.sh
bash tools/prepare-unity.sh --verify
bash tools/check-unity.sh
```

생성 DLL·브리지와 패키지 데이터의 출처/해시가 맞지 않으면 실패한다. 생성물을 수동 수정하지 않는다. `check-unity.sh`는 실제 테스트 수가 양수이고 실패가 없는지 결과 XML을 검사하며 IL2CPP/ARM64 BuildReport와 APK를 확인한다. 출력은 `artifacts/unity/`다. 실제 기기 설치·실행 성공은 별도이며 빌드 결과로 대신하지 않는다.

재생 파일은 Core 명령과 checkpoint를 가진 한 파일이다. .NET CLI에서 다음과 같이 검증한다. fixture 생성은 현재 정본 데이터에 맞춘 correctness 검사이며 밸런스 측정이 아니다.

```sh
dotnet run --project core/src/SowSiege.Sim -- interactive-fixtures data artifacts/phase1a/replays
dotnet run --project core/src/SowSiege.Sim -- interactive-replay data artifacts/phase1a/replays/30000.ssreplay
```

Unity는 `production`을 선택한다. U1 이전 S2 CLI 기본값을 Unity 설정으로 대체 사용하지 않는다. 과거 R2/R3 판정과 이후 무기 holdout 통과는 각각 보존하고, 새 수치 보정은 이 단계에 포함하지 않는다. [생성 연결 ADR0024](../adr/0024-generated-core-and-canonical-unity-bridge.md), [재생 ADR0025](../adr/0025-interactive-core-and-portable-replay.md), [세로 폴드·검사 ADR0026](../adr/0026-portrait-fold-layout-and-local-unity-ci.md)를 따른다.

보고는 커밋·PR·CI와 실제 로컬 검사 결과로 한다. ZIP·CRC·전달 영수증은 만들지 않는다. 원본 로그의 Unity 계정/라이선스나 기기 식별자는 공개하지 않는다. 최종 APK와 해 볼 것 목록을 전달하며, 1단계 B는 시작하지 않는다.

---

## 아래는 1단계 승인 전 조사 기록

다음의 미설치·미활성화·기기 미정·미착수 문구는 당시 상태다. 현재 실행에는 위 절차와 U0 결과를 적용하며 과거 조사·판정 자체는 보존한다.

관문(당시): 부분 — 0건의 Unity 실행·기기 검증. 1단계는 미착수이며 이 문서는 착수 체크리스트다.
변경: 공식 문서 조사로 에디터 고정안·호환성 관문·사람이 처리할 계정 작업·첫 회색 상자 범위를 정리했다.
결정: 사용자 정정(2026-10-08)에 따라 설치된 Unity 6.6 6000.6.4f1 ARM64와 Universal 2D(URP 2D Renderer)를 채택했다. 시험 기기·Unity CI 방식은 미정이다.
근거: 확인일 2026-10-08. 아래 공식 자료는 그 시점의 확인이며 절대 최신 패치를 보증하지 않는다.
한계: 에디터 설치는 읽기 전용 확인했다. Personal 활성화는 사용자 진술이며 독립 검증하지 않았다. 에이전트는 프로젝트 생성·SDK 설치·활성화·비밀값 등록을 하지 않았다.

# 1단계 Unity 착수 체크리스트

이전 Unity 6.3 LTS 6000.3.25f1 선택은 사용자 정정으로 대체했다. [ADR 0018](../adr/0018-unity-66-urp-baseline.md)에 이력을 보존한다. 시험 기기·Unity CI 방식은 사용자 답변 전 확정하지 않는다. 남은 계획 결정은 [needs-decision #65](https://github.com/hyunlord/bs-mobile/issues/65), Phase0 종료 정리는 [#64](https://github.com/hyunlord/bs-mobile/issues/64)에서 추적한다.

## 에디터와 플랫폼 고정안

- [x] 설치된 **Unity 6.6 `6000.6.4f1` ARM64**를 확인했다. 경로는 `/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app`이며 Info.plist 버전과 실행파일 아키텍처를 대조했다. 공식 출시일은 2026-10-01이다. [패치·알려진 문제](https://unity.com/releases/editor/whats-new/6000.6.4f1)
- [ ] 착수할 때 Universal 2D 템플릿의 URP 2D Renderer를 사용하고 ProjectVersion.txt와 패키지 잠금 파일을 고정한다. 아직 프로젝트를 생성하지 않았다. [URP 2D Renderer](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/2DRendererData-overview.html)
- [ ] Android Build Support·OpenJDK·SDK·NDK 및 iOS Build Support를 필요한 대상에 맞춰 준비한다. 설치 루트의 PlaybackEngines에는 MacStandaloneSupport/WebGLSupport만 있고 AndroidPlayer와 그 OpenJDK/SDK/NDK, iOSSupport는 없다. modules.json 카탈로그를 설치 완료로 해석하지 않는다. [Android 환경](https://docs.unity3d.com/6000.6/Documentation/Manual/android-sdksetup.html), [iOS 환경](https://docs.unity3d.com/6000.6/Documentation/Manual/ios-environment-setup.html)
- [ ] 알려진 Vulkan 충돌(UUM-153744), Player의 UnloadUnusedAssets 지연(UUM-149540), 복수 bee_backend 충돌(UUM-142773)을 적용 대상에서 확인한다. 공식 알려진 문제이며 이 프로젝트에서 재현한 결과가 아니다. 처음에는 빌드를 직렬 실행하고 불필요한 후처리를 피하는 방식을 권고한다. [공식 release note](https://unity.com/releases/editor/whats-new/6000.6.4f1)
- [ ] 연말 계획인 6.7 LTS는 현재 beta다. 정식 출시 후 [#70](https://github.com/hyunlord/bs-mobile/issues/70)에서 별도 전환 검증한다. Built-in은 6.5부터 deprecated이고 최신 공식 발표상 7.0에서 제거된다. [초기 전략](https://unity.com/topics/render-pipelines-strategy-for-2026), [최신 공식 발표](https://discussions.unity.com/t/the-path-to-a-single-render-pipeline-in-unity-7/1737908)

## Core 연결 선행 관문

- [ ] [#67](https://github.com/hyunlord/bs-mobile/issues/67)의 Core 호환성 결과와 실제 산출 타깃을 확인한다. Unity 6.6의 API 수준은 .NET Standard 2.1 또는 .NET Framework 4.8이며 .NET Core 대상 관리 플러그인은 지원 대상이 아니다. net8.0 DLL을 그대로 넣으면 된다고 가정하지 않는다. [Unity .NET 호환성](https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)
- [ ] C# 9/.NET Standard 2.1 계약, 사용하는 BCL API·직렬화·IL2CPP/AOT 제약을 확인한다. .NET 8 CLI·역사 fixture를 유지한다. netstandard2.1 Core를 .NET 8 테스트 호스트에서 실행한 동등성 결과를 실제 Unity/IL2CPP 실행 검증과 구분한다. [C# 9 및 record 제약](https://docs.unity3d.com/6000.6/Documentation/Manual/csharp-compiler.html)
- [ ] Unity 어댑터는 입력·카메라·표시·플랫폼 처리를 맡고, Core에는 Unity 타입·시각·SDK나 구체 영웅/영지 ID를 추가하지 않는다. 같은 데이터·seed·입력 순서의 결과를 .NET 실행과 Editor·기기 실행에서 대조한다. IL2CPP는 AOT를 사용하므로 Editor 통과만으로 기기 호환성을 선언하지 않는다. [Unity API·AOT 설명](https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)

## 사람이 직접 처리할 계정·권한 작업

아래 항목은 필요한 시점에만 처리한다. 코딩·빌드 스크립트 작성·일반 검증은 에이전트가 수행하고, 사람의 계정 인증·약관 동의·조직 권한·기기 승인을 대신 추정하지 않는다.

| 필요한 시점 | 사용자가 직접 확인하거나 처리할 일 | 근거·경계 |
|---|---|---|
| 에디터를 사용하기 전 | 사용자가 Personal 활성화를 알렸다. 독립 검증은 하지 않았으며 추가 계정·좌석 확인이 실제로 필요한 경우에만 처리한다. | Personal 소개 페이지는 최근 12개월 매출·투자금 $200K 미만 자격을 설명한다. 이 문서는 사용자의 자격을 판정하지 않는다. [Unity Personal](https://unity.com/products/unity-personal) |
| Unity CI를 켜기 전 | 사용할 runner/서비스의 라이선스 방식과 비용·조직 권한을 확인한다. 필요한 인증만 승인한 비밀 저장소에 등록한다. | 6.6 매뉴얼은 Personal 활성화·반납에 Hub만 지원한다고 명시한다. 무료 라이선스 파일을 복사하면 CI가 된다는 전제를 두지 않는다. 실제 batch 빌드 통과 전에는 CI 준비 완료가 아니다. [활성화 방식](https://docs.unity.com/en-us/engine/6000.6/manual/get-started/install-and-upgrade/licenses-and-activation/license-activation-methods) |
| Android 기기 시험 | 사용할 기기를 준비하고 잠금 해제·연결·디버깅 승인을 처리한다. | 개발용 설치와 스토어 배포를 분리한다. 배포가 필요해질 때 keystore·alias·암호의 소유·보관 방식을 확정한다. [키 로드·서명 설정](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/android/getting-started/keystore/load) |
| iOS 기기 시험·배포 | Apple ID·개발 팀·기기 신뢰/등록을 처리한다. 배포 경로에 맞는 회원 자격과 서명 권한을 확인한다. | Unity 환경 문서는 무료 Apple ID의 기기 시험과 Apple Developer Program의 App Store 배포 등을 구분한다. [iOS 환경 설정](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/getting-started/ios-environment-setup) |
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
- [ ] 기기 모델·OS·빌드 커밋·에디터 버전·설정·seed와 함께 프레임 시간·할당·발열/지속 실행 조건을 기록한다. Core 틱 p95와 전체 프레임 시간은 다른 지표다. Unity도 Editor와 대상 플랫폼 양쪽 프로파일링을 권고한다. [Unity 플랫폼별 프로파일링 경계](https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support)

## 완료 판정과 이번 범위 밖

착수 이후 이슈 → PR → CI 흐름을 유지한다. 컴파일·빌드, Core 결과 대조, 실제 기기 조작, 사람이 이해하는 순환·재미를 각각 판정하고 미검증을 그대로 남긴다. 원본 S4 밸런스 실패와 이후 실험 결과는 별개 기록으로 보존한다. 기기 증거·로그는 저장소 예산과 외부 증거 보관 정책에 따른다.

광고·결제·분석·UA·크래시 SDK 설정, 상점 상품·가격, 아트 완성, 출시 배포는 첫 회색 상자 범위가 아니다. 이 체크리스트 작성으로 Unity 설치나 1단계 구현을 시작하지 않는다.
