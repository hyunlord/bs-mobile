# ADR0087 · OS 기기 키와 삭제 유예를 보존하는 클라이언트

상태: 제안 · 날짜: 2026-11-02 · 연결: [#639](https://github.com/hyunlord/bs-mobile/issues/639), [ADR0080](0080-release-login.md). 사용자 요청 범위의 구현 후보이며 플랫폼별 실행 승인을 의미하지 않는다.

## 맥락

서버 1단계는 P-256 SPKI 공개키를 등록하고 `sow-auth-v1\n{keyId:D}\n{nonce}`의 SHA-256 ECDSA/P1363 서명을 검증한다. 삭제 뒤 같은 기기 로그인은 삭제를 취소하므로 일반 재접속 처리만 붙이면 사용자가 요청한 삭제를 곧바로 취소할 수 있다. 같은 공개키 게스트 생성은 현재 멱등하지 않다.

## 결정

- 비밀키는 Apple Secure Enclave 또는 AndroidKeyStore에서 만들고, 호출 표면은 공개키·서명·보안 세션 저장뿐이다. private key export API를 제공하지 않는다. Apple은 device-only Keychain, Android는 별도 Keystore AES-GCM 키로 인증 암호화한 no-backup 파일에 토큰을 보관한다. 평문 파일/PlayerPrefs fallback은 없다.
- Apple 키 조회에는 사용자 상호작용이 금지된 `LAContext`를 쓴다. 생체 인식/OS 권한 창을 요구하지 않는다. Secure Enclave/보안 저장소/서명 entitlement가 없으면 실패를 표시한다.
- 삭제 요청 전에 `deletionPending`을 안전하게 저장하고 접근·갱신 토큰을 비운다. 응답 유실과 재시작에도 그 상태를 유지한다. 확인한 `purgeAt`만 표시하고, 명시적인 복구 버튼만 기기 로그인을 수행한다. 연맹 소속 복구를 주장하지 않는다.
- 게스트 생성 전 `creationPending`을 보존한다. 응답 유실 시 자동 재생성하지 않는다. 공개키 기준 복구/멱등 계약이 서버에 없으므로 이 상태는 미확인으로 남긴다. 후속 서버 계약 없이 새 계정 생성으로 우회하지 않는다.
- 접근 토큰은 메모리 getter로만 전달한다. 만료 뒤에는 명시 기기 서명 로그인을 사용한다. 갱신 토큰을 안전하게 보관하지만 자동 refresh 재시도는 하지 않는다. 서버가 회전 토큰 재사용을 감지하므로 응답 유실 재시도 정책을 정하지 않은 자동화를 넣지 않는다.
- endpoint는 `--account-server` 또는 `SOW_ACCOUNT_SERVER`로 명시하며 HTTP는 Editor/Development loopback만 허용한다. 출시 endpoint가 정해지지 않은 기존 오프라인 경로를 임의 온라인 서비스로 전환하지 않는다. 서버 guest creation도 현재 Development+Prototype에서만 열린다.
- iOS는 `.mm` 플러그인을 빌드한다. macOS는 같은 소스로 dylib를 생성하고 `tools/build-mac.sh`에서 Unity 빌드 전에 준비한다. 생성 dylib는 Git에 넣지 않는다. 서명 키·entitlement 발급은 사용자의 기존 배포 과정으로 남긴다.

## 결과·비용

Native 코드는 Android/iOS/macOS 각각 검증해야 한다. Mac 문법 검사나 Unity DTO 테스트는 실기 키 보관·IL2CPP·패키징 검증이 아니다. 로그인은 설정 진입과 첫 온라인 진입에서만 동작하며 매 프레임 암호화/할당을 추가하지 않는다. View 변경이므로 C05 3:3 결과 전 병합하지 않는다.

## 거절한 대안

- PlayerPrefs/평문 파일: 기기 키·토큰 비밀 보관 계약 위반.
- 삭제 뒤 자동 로그인: 사용자의 삭제 요청을 자동 취소함.
- 게스트 생성 무조건 재시도: 응답 유실 때 중복 계정을 만듦.
- SDK 추가: 사용자 승인 없는 의존성 및 소셜 SDK 범위 확대.

## 검증

- Apple `clang++ -fsyntax-only`: 수정 후 통과. 최초 미지원 메모리 정리 함수 오류는 volatile zeroing으로 수정했으며 숨기지 않는다.
- Android SDK36 `javac -Xlint:all`: 통과. 실제 Keystore 실행·휴대폰 설치는 NOT_RUN.
- Unity 검증 SHA `e60e09e91052897c77b2f9b9da6617224fa489e9`: first-playable Edit 951/0/25, Play 55/0/56; wave-1a Edit 961/0/15, Play 98/0/13(통과/실패/건너뜀). 보충 각 1건 통과, combined unexplained 0, listed 1, 추적 파일 변경 0.
- 같은 SHA의 최종 계정 화면 TestRunner 2건 통과. 실제 Editor PNG는 입력 유휴 8675초에서 시작해 약 36초 동안 유휴 하락 0·외부 CPU 100% 초과 기록 0으로 캡처했다. [선택 7장과 native 실패 사례](https://github.com/hyunlord/bs-mobile/releases/tag/client-device-login-639-20261102-r1). fixture 이름의 계정 상태는 합성이며 실제 서버 삭제/복구 증거가 아니다.
- 실제 native 인증·실서버 전주기는 아래 entitlement 제한으로 BLOCKED/NOT_RUN. Android/iOS 실기·C05는 NOT_RUN. 이전 `a5a76442` Unity 결과는 이후 문구/fixture 변경으로 대체된 역사 결과다.
- 출처: [Apple Secure Enclave](https://developer.apple.com/documentation/security/protecting-keys-with-the-secure-enclave), [Android Keystore](https://developer.android.com/privacy-and-security/keystore), [KeyGenParameterSpec](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec), 확인 2026-11-02.

## 2026-11-02 실제 Mac Editor 검증 제한

계정 화면 통합 가지 `fac11949fcd6ee56c1527a83513f401a286fb9a1`에서 실제 native/server probe를 1회 실행했다. `native-empty-dedicated-scope`는 PASS였고, 다음 `native-key-public`은 `InvalidOperationException`, native status `-34018`로 FAIL했다. 이후 서명·저장·서버 guest/challenge/device·삭제·복구는 **NOT_RUN**이다. 원자료는 해당 worktree의 `artifacts/account-ui/device-auth-runtime-r1.txt`와 `.log`에 보존했다. 이것은 로그인 가지 전체 Unity 검사 결과가 아니다.

로컬 Apple SDK `SecBase.h`는 `-34018`을 `errSecMissingEntitlement`로 정의한다. 실행한 Unity 6000.6.4f1 앱의 실제 codesign entitlements에는 `keychain-access-groups`와 `com.apple.application-identifier`가 없다. 두 가지에서 native 소스 SHA256은 `64da642b2694f30d648688106529026507765f45651142b220680e3d65b17529`로 같았다. [Apple DTS 설명](https://developer.apple.com/forums/thread/114456)에 따르면 data-protection keychain 조회에서 접근 그룹 생략은 wildcard이고, 추가 시에는 기본 접근 그룹을 사용한다. 따라서 빈 저장소 조회 성공은 새 영구 키 저장 권한을 증명하지 않는다.

현재 증거는 **Editor 호스트 entitlement 제한**을 강하게 지지한다. 정확히 어느 Security 하위 호출에서 오류가 반환됐는지는 **UNCONFIRMED**다. 공개 키 형식 검사·서버 통신 이전 실패이며 서버 인증 성공이나 안전한 저장 성공으로 해석하지 않는다. 같은 호스트의 동일 원인을 재시도하거나 Unity 재서명·시스템 설정·사용자 서명 키 변경·소프트웨어 키 fallback으로 우회하지 않는다. 실제 native 인증은 **BLOCKED**, Android/iOS 실기는 **NOT_RUN**으로 유지한다. 별도 Unity 단위 검사와 합성 UI 캡처는 이 차단을 해제하지 않는다.

## 개발용 저장 대체 승인 (2026-11-02)

[승인 범위] 사용자 요청으로 Editor/Development Build의 개발용 저장 대체를 추가한다. Editor/Development Build는 컴파일 시 개발 backend를 선택하고 계정 endpoint가 설정되면 별도 메모리 vault를 사용한다. native 실패 뒤 자동 fallback하는 동작이 아니다. Release는 기존 실제 Keychain/Keystore 실패를 그대로 유지한다.

개발 vault는 비영구 소프트웨어 P-256 키와 세션을 프로세스 메모리에만 보관한다. endpoint로 분리한 개발 전용 scope를 쓰고 실제 OS vault와 섞지 않는다. 파일·PlayerPrefs에 비밀을 저장하지 않는다. 같은 프로세스 안의 클라이언트 재생성은 지원하지만, 프로세스 종료 후 키·세션·삭제 tombstone은 사라져 해당 개발 계정에 다시 접근할 수 없다. 하드웨어 비추출성·디스크 영속성·앱 재시작 복구의 증거가 아니다. UI에 개발 전용 임시 저장과 종료 시 소실을 표시한다.

C# 경계는 `UNITY_EDITOR || DEVELOPMENT_BUILD`, native 경계는 별도 `SOW_DEV_AUTH` 컴파일 정의다. Mac Release 빌드에서는 이 정의를 켜지 않는다. 개발 API는 임시 공개키/서명/세션만 제공하며 private key export는 제공하지 않는다. 현재 Mac 개발 검증용이고 Android/iOS 실제 저장소는 서명된 휴대폰 빌드에서 별도 검증한다.

실행 순서: 개발 vault와 명시 선택 경계 → 상태 표시·격리 단위 검사 → 개발용 실제 서버 challenge/sign/삭제/복구 probe → 새 고정 SHA의 Unity 전체·보충 검사 → 별도 캡처/PR 기록. 기존 e60/fac11949 및 native 실패 기록은 보존하며 새 head의 C05를 기존 승인 측정 묶음에 포함된 것으로 간주하지 않는다.

## 개발 backend 실행 결과 (2026-11-02)

고정 source `8ae6ca79631ce80cae8abf9ef13359aa0fc493ef`에서 개발 vault 집중 EditMode 3/3 및 실제 로컬 서버 전주기 1/1이 통과했다. guest 생성 → fresh challenge/device(상태 connected와 접근 토큰 회전 확인) → 세션 client 재생성 → 삭제 → 자동 복구 차단 → 동일 키 명시 복구를 확인했다. 이 실행은 메모리 backend와 Development/Prototype 서버의 증거이며 OS 보안 저장소·프로세스 재시작 보관·출시 서버의 증거가 아니다. 기존 native -34018 실패는 유지한다.

같은 source의 first-playable Edit 954/0/25, Play 55/0/56; wave-1a Edit 964/0/15, Play 98/0/13, 보충 각 1/0/0(통과/실패/건너뜀). combined unexplained 0, listed 1, 추적 파일 변경 0. 실제 Release native 빌드에는 개발 함수가 없고 개발 빌드에만 있다. 비개발 player script 컴파일 및 기존 Release metadata 검사를 통과했다. 최초 checker 입력에는 외부 Core DLL이 빠져 실패했으며, 같은 프로필의 실제 generated DLL과 netstandard2.1 Release 원본 일치를 확인한 뒤 staging하여 통과했다. 검사기를 완화하지 않았다.

최종 guarded PNG는 외부 CPU 시작 기준 초과로 두 preflight에서 차단되어 Editor 실행 0회, NOT_RUN이다. 반복 조건에 따라 캡처를 중단했다. [r2 선택 참고 화면·개발 실행 요약·차단 사례](https://github.com/hyunlord/bs-mobile/releases/tag/client-device-login-639-20261102-r2)의 PNG는 full PlayMode 검사가 만든 합성 UI 참고 화면이며 유휴 감시 캡처·실제 서버 UI 조작 증거가 아니다. 이전 r1 guarded 화면과 판정은 별도 보존한다. 새 head는 이전 C05 측정 신청에 포함되지 않는다.
