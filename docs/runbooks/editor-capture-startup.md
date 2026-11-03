# Editor 자동 캡처 시작 순서 (#661)

상태: 2026-11-03 코드 후보. Unity 컴파일·재현 대조·회귀 테스트 실행 **NOT_RUN**(야간 23:00–07:00 실행 금지). 이 문서는 Unity 내부 버그가 수정됐거나 캡처 오류가 사라졌다는 판정이 아니다.

## 원인과 범위

Unity6000.6.4f1 기존 account/chat 로그와 설치 QuickSearch DLL/PDB 분석에서 시작 인덱싱 delayCall보다 빠른 PlayMode 요청을 확인했다. 기본 DB가 없는 상태에서 PlayMode 분기는 기본 인덱스 준비를 건너뛰며, 이후 Unity 내부 목록의 첫 항목 접근이 `ArgumentOutOfRangeException`을 냈다. `351`은 소스 행 번호이며 배열 인덱스가 아니다. 기존 실패 로그·판정은 [#661](https://github.com/hyunlord/bs-mobile/issues/661)에 보존한다.

일반 TestRunner는 Search 시작 인덱싱 설정 기본값이 다른 경로를 사용하므로 테스트 통과만으로 executeMethod 캡처 시작 오류가 사라졌다고 판단하지 않는다.

## 프로젝트 가드

`Game.Editor.EditorCaptureStartup.WaitUntilReady(ready, failed, timeoutSeconds: 120)`를 사용한다. 호출 assembly는 `Game.Editor`를 참조한다. 준비 callback 안으로 Active 플래그·캡처 자체 deadline·설정 변경·씬 열기·해상도·PlayMode 요청을 모두 옮긴다. output/인자 유효성 검사만 앞에서 수행한다. helper는 PlayMode 진입이나 종료를 소유하지 않는다.

1. 정상 Editor `delayCall` 두 경계가 처리될 때까지 기다린다. `isCompiling/isUpdating == false`만으로 시작 콜백 처리를 대신하지 않는다.
2. EditMode·import/compile 유휴 상태에서 공개 API `SearchService.EnumerateDatabases()`를 순회한다. 비어 있으면 준비 완료가 아니며 제한 시간까지 대기한다. `[0]` 접근은 없다.
3. 비어 있지 않은 목록에서 `IsIndexReady(null)`도 참이어야 준비 callback을 실행한다. 해당 Unity API는 내부 `All` 판정 때문에 빈 목록만으로 참일 수 있다.
4. 시간 초과, Search 오류 로그/열거 예외, 외부 PlayMode 전환, assembly reload에는 실패 callback을 실행하고 등록한 이벤트를 해제한다. 오류를 성공으로 바꾸거나 같은 실패를 자동 재시도하지 않는다. caller는 실패 기록/설정 복원/필요한 batch 종료를 담당한다.

예시:

```csharp
EditorCaptureStartup.WaitUntilReady(BeginCapture, FailStartup);
// BeginCapture: 기존 설정 및 씬 준비 뒤 EditorApplication.isPlaying = true.
// FailStartup: 원래 예외를 기록하고 변경한 설정을 복원한 뒤 batch 실행이면 Exit(1).
```

Search 설정 변경·비활성화, 임의 인덱스 파일 생성, 설치 DLL 패치, 내부 API reflection은 사용하지 않는다. 정상 공개 API가 수행하는 Editor 초기화만 사용한다. 준비 시간은 게임 시간이나 캡처 성공 시간에 포함하지 않는다.

## 낮 검증 순서

- 고정 source에서 새 EditMode 회귀 검사와 두 프로필 전체/보충 검사를 수행한다. 정책 상태 검사는 실제 Search 초기화나 executeMethod 재현을 대신하지 않는다.
- 정본 설정을 보존하고 같은 깨끗한 조건의 캡처에서 `Start Indexing on Editor startup`/`CAPTURE_STARTUP_READY`가 PlayMode보다 먼저 나타나는지 확인한다. 의도적으로 캐시를 지우거나 설정을 바꾸지 않는다.
- 기존 같은 Search 예외의 재발 여부, 실제 캡처 결과, 환경 유휴 guard, 추적 파일 변경0을 별도 확인한다. 반복 실패 제한을 지키며 이전 실패를 보존한다.
- AccountCapture/AllianceChatCapture는 해당 통합 가지의 공통 helper 호출까지 포함한 최종 head에서 검증한다. 이 main 기반 가지에는 아직 그 캡처 파일이 없다.

## 공식 근거

- [Unity6000.6 SearchInit: 시작 인덱싱 delayCall 등록](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.6/Modules/QuickSearch/Editor/SearchInit.cs)
- [Unity6000.6 SearchService: EnumerateDatabases와 IsIndexReady](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.6/Modules/QuickSearch/Editor/SearchService.cs)
- [공개 EnumerateDatabases API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Search.SearchService.EnumerateDatabases.html)

공식 branch는 이동할 수 있으며 정확한 기존 설치 빌드 판정은 #661의 DLL SHA/PDB 기록을 우선한다. 설계 문서 영향: 없음 — 제품 화면·Core 규칙은 바꾸지 않는 Editor 캡처 준비 절차다.
