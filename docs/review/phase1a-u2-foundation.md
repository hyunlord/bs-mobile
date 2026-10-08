관문: 통과(U2 기반) — 전체 Core 검사, EditMode8/8·PlayMode2/2, 5seed .NET↔Mono 해시 일치, ARM64 IL2CPP APK 빌드. 실기 관문은 미검증이다.
변경: Unity Universal2D 기반, 생성 Core DLL·정본 데이터 연결, 수동 입력·카드·개발 명령·이식 가능한 재생을 추가했다.
검증: 실제 .NET/Mono에서 seed30000~30004의 21,600틱 기록과 checkpoint·종료 해시가 일치했다.
한계: 실제 Android 재생·폴드8 조작/접힘/성능과 완성된 게임 화면은 후속 #89·#90·#83 관문이다.

# 실행 범위

U1 [PR88](https://github.com/hyunlord/bs-mobile/pull/88) 병합 뒤 #86·#87을 함께 연결했다. Unity는6000.6.4f1, URP17.6 Universal2D, 세 장면 Boot/Meta/Run, ForceText/VisibleMeta, ARM64 IL2CPP다. 이 단계의 화면은 정본 검증 상태만 표시한다. URP2D 실제 RenderTexture 출력도 확인했으나 회색 상자 게임의 판독성 통과로 해석하지 않는다.

Core의 수동 경로는 별도 명령·RNG 상태·완전한 상태 codec을 갖고 기존 봇/역사 해시 경로를 유지한다. 사전 열거 순서, 최초/현재 catalog, 숨은 cooldown·카드·런타임 상태, RNG를 해시에 묶는다. 공격 사건은 타격 전 위치와 빗나감 궤적을 제공한다. 관찰은 상태나 RNG를 소비하지 않는다.

생성기는 ContentLoader의 전체 catalog 값과 열거 순서를 보존한 명시적 생성자 코드를 만들고, Core DLL·브리지·소스·정본 파일의 출처를 검사한다. 실제 패키지에서 읽는 JSON 바이트가 다르면 시작을 막는다. 생성된 소스/DLL은 Git에 넣지 않는다. 카메라 표현 설정의 추가로 전체 데이터 식별자가 바뀌었으며 밸런스 수치 변경은 없다.

| 식별자 | 값 |
|---|---|
| 프로필 | production |
| 전체 데이터 SHA256 | `1AA2CCF4FBA7555013F8356E53CC99C9181B1C1289EA263F4E800C712EEB0C3A` |
| 프로필 SHA256 | `B03C5B18A01711ADB45178CDDC12BC3C09F94A76F8204BEC22C714DBC5A3CC4D` |
| correctness 기록 | seed30000~30004, 각21,600틱; 기록된 무적/카드/조준/출현량 명령 포함 |

무적 correctness 기록은 4계절 경로와 런타임 일치를 검증하는 도구이며 일반 생존율이나 재미를 증명하지 않는다. 별도 Core 테스트가 사망·무적·카드 정지·거부 명령의 원자성·유해·공격 사건·잘못된 재생을 검사한다.

# 재현과 보관

| 실행 검사 | 실제 결과 |
|---|---|
| `bash tools/check.sh` | exit0; xUnit262/262, 콘텐츠155/155, ArchitectureGuard0, 형식 검사 통과 |
| 역사 두 타깃 비교 | 16사례 ×3회 ×2타깃 =96실행 일치 |
| 진단/무기 연기 검사 | 27사례·18사례 통과; 기존 S2/S4/S4b 연기 검사 통과 |
| `bash tools/check-unity.sh` | exit0; 생성 도구 Node14/14, EditMode8/8·PlayMode2/2, Android IL2CPP ARM64 성공 |
| 실제 APK | 37,471,042바이트; BuildReport 전체 산출물 크기와 구분 |
| APK 정적 검사 | `com.hyunlord.sowsiege`, min26/target36, arm64-v8a 전용, 개발 서명 v2 유효; 246개 패키지 정본 파일의 길이·해시와 전체 데이터 해시 일치 |

중간 전체 검사에서 서식 위반을 발견해 수정했다. 수정 전 Android 빌드는 출처를 섞지 않으려고 중단했고, 수정·재생성 후 두 전체 명령을 다시 통과했다. 테스트나 실패 기준을 삭제하지 않았다.

저장소 루트의 `bash tools/check.sh`는 Core·콘텐츠·역사 fixture/리그와 Unity 연결 도구의 엔진 독립 검사를 실행한다. `bash tools/check-unity.sh`는 새 생성물/기록을 만든 뒤 실제 Editor 테스트와 Android 빌드를 실행한다. 절차는 [런북](../runbooks/Unity-phase1-checklist.md), 결정은 [ADR0024](../adr/0024-generated-core-and-canonical-unity-bridge.md)·[0025](../adr/0025-interactive-core-and-portable-replay.md)·[0026](../adr/0026-portrait-fold-layout-and-local-unity-ci.md)에 있다.

로컬 원자료는 `artifacts/phase1a/replays/`, `artifacts/unity/`, `artifacts/phase1a-u2-check.log`다. 성공 기록 전체와 재생성 가능한 집계는 업로드하지 않는다. 원본 Editor 로그는 라이선스 정보를 포함할 수 있어 공개하지 않는다. 최종 연결 커밋·PR·CI는 병합 보고에 남긴다.
