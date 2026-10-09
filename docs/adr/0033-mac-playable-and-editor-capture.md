# ADR0033: 폰 연결과 독립된 맥 플레이판과 에디터 영상

- 상태: 채택, 2026-10-09
- 추적: [#113](https://github.com/hyunlord/bs-mobile/issues/113)
- 근거: [2단계 A 의뢰서 P1](../design/07_ASTRA_GOAL_bs-mobile_phase2a.md)

## 결정

P1은 Unity6000.6.4f1의 macOS Standalone ARM64 Mono Release로 배포한다. 현재 사용자의 Apple Silicon 맥에서 실행할 수 있는 가장 작은 추가 경로이며, 기존 Android IL2CPP 경로와 Core의 두 .NET 대상은 유지한다. Mac 실행을 Android 결정론·성능 검증으로 확대하지 않는다. `first-playable`의 콘텐츠·seed 규칙·15분 길이는 바꾸지 않는다.

기존 마우스 포인터의 떠다니는 조이스틱에 WASD를 더한다. 마우스가 활성화되면 우선하며 대각선은 정규화한다. 카드·설정·포커스 상실 이후 눌린 키는 놓았다가 다시 눌러야 이동한다. F1/F2/F3은 현재 화면 작업 영역에 맞춘 기본 세로/접힘/펼침 비율이다. 비율 전환은 실제 물리 접힘 증거가 아니다.

`BuildOptions.None`으로 개발 메뉴·기기 진단을 제외하고 패키징한 실제 관리 어셈블리를 검사한다. 에디터 전용 자동 입력·영상 수집 어셈블리는 플레이어에 포함하지 않는다. 사용자 지정 Unity Recorder5.1.7을 에디터 영상 도구로 추가한다. [Unity6000.6 공식 패키지 안내](https://docs.unity3d.com/6000.6/Documentation/Manual/com.unity.recorder.html)에 공개된 버전이며 확인일은 2026-10-09다. 이는 광고·결제·분석 SDK 설치 승인이 아니다.

## 검증과 한계

`tools/build-mac.sh`는 새 출력 폴더에 앱을 빌드하고 실제 Release 어셈블리 및 생성 데이터 일치를 검사한다. Unity 테스트는 포인터/키보드 우선순위·일시정지·화면 비율을 검사한다. 영상은 자동 입력 여부, 정상 속도, 실제 명령 기록, seed와 원본 빌드를 함께 기록한다. 무적·레벨 부여·위협 감소·시간 건너뛰기를 정상 영상에 쓰지 않는다.

깨끗한 OS 계정에서 압축 해제→타이틀→완주→재시작은 별도 실제 실행 관문이다. 현재 계정에서 새 저장 경로를 쓰거나 에디터 테스트를 돌린 결과로 대신 통과시키지 않는다. 관리자 인증과 macOS 입력/캡처 권한 제한은 [#114](https://github.com/hyunlord/bs-mobile/issues/114)에 남긴다. 배포 설명은 검증된 범위와 남은 관문을 구분한다. Fold7 [#98](https://github.com/hyunlord/bs-mobile/issues/98)은 사용자 재개 지시까지 보류한다.

## 배포

사용자가 명시한 `.app` 배포용 ZIP 하나, 지정된 영상2개, 플레이 목록과 선정 화면만 Release로 제공한다. 별도 보고서 ZIP·CRC·영수증은 만들지 않는다. 기존01·02 설계 원본은 변경하지 않는다. 공증·Developer ID 배포를 완료했다고 주장하지 않으며, 플레이 목록에 현재 macOS의 앱별 열기 절차를 안내한다.
