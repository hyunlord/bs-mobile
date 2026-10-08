관문: 통과 — 에디터6000.6.4f1·Android 도구·CLI 라이선스 확인 완료.
실기: adb 연결0대. 기기 확인 전까지 U1과 독립 구현을 진행하며 실기 관문 통과로 취급하지 않는다.
범위: U0 환경 확인이며 Unity 프로젝트 기능·Android 빌드·IL2CPP 검증은 아직 미실행.

# U0 환경 확인 — 2026-10-08

|항목|실측|
|---|---|
|에디터|Info.plist CFBundleVersion `6000.6.4f1`|
|Android 모듈|에디터 루트의 `PlaybackEngines/AndroidPlayer` 존재, 에디터가 플랫폼 모듈 등록|
|OpenJDK|동봉 Java 실행 `17.0.18+8`|
|SDK|동봉 adb 실행 정상, platforms `android-34`, `android-36`, `android-37.0`|
|NDK|source.properties `27.2.12479018`, r27c|
|iOS|`PlaybackEngines/iOSSupport` 존재, 에디터 모듈 등록; 빌드 미검증|
|라이선스|batchmode entitlement 확인 후 `Exiting batchmode successfully`, exit0|
|기기|동봉 `adb devices -l` 결과 연결 목록 비어 있음|

에디터 위치는 `/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app`, 플랫폼 모듈은 Unity.app 내부가 아닌 같은 설치 루트의 `PlaybackEngines`에 있다. 카탈로그만으로 설치를 판정하지 않았다. U0 CLI 확인은 임시 `/tmp/bs-mobile-u0-license-probe`에만 생성했고 제품 `unity/` 구현은 U1 병합 이후다. 로컬 원로그는 `artifacts/phase1a-u0-unity.log`이며 계정 정보가 섞일 수 있어 공개하지 않는다.

```sh
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -createProject /tmp/bs-mobile-u0-license-probe \
  -logFile /Users/rexxa/bs-mobile/artifacts/phase1a-u0-unity.log
/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb devices -l
```

iOS와 Xcode는 이번 통과 조건이 아니다. 시스템 Git의 Xcode 라이선스 오류를 피할 때는 `DEVELOPER_DIR=/Library/Developer/CommandLineTools`를 해당 명령에만 지정한다. 라이선스를 대신 동의하거나 시스템 선택을 변경하지 않는다. 기기 연결은 실기 단계 전에 다시 확인한다.
