# ADR0085 · OS 기기 키와 삭제 유예를 보존하는 클라이언트

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
- Unity·실제 서버 연결·실제 화면·C05: NOT_RUN. 전용 기록이 생기기 전 통과로 바꾸지 않는다.
- 출처: [Apple Secure Enclave](https://developer.apple.com/documentation/security/protecting-keys-with-the-secure-enclave), [Android Keystore](https://developer.android.com/privacy-and-security/keystore), [KeyGenParameterSpec](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec), 확인 2026-11-02.
