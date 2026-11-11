# #223 야간 메모리·용량 진단 준비

관문: 부분 — 계측47/47·Development 빌드는 통과했지만 야간 자료와 원인 판정, 수정 후 Release ABBA는 아직 없다. 최대50ms 관문을 통과했다고 보고하지 않는다.

- 사용자 창: 2026-10-13 01:00–06:00 KST. 화면 PR235와 별개다.
- 계측 빌드: `d0986ef27cccc539b186e42363a69467f7968bed`, sourceHash `0D971759F851598C058ADC681A7E4DFCD79CE2B3689E81181159CBCFAAC3841A`, sourceDirty=false, Unity6000.6.4f1 Development Mono ARM64. Core/data 규칙 변경 없음.
- 관련 Editor 검사: `DevelopmentLongFrameRecorderTests`·`DevelopmentProfilerTraceTests`47/47. 독립 코드 리뷰 CLEAR. Release 전처리 경로 동일성 검사. 새 관측 기능은 Development/Editor 전용이다.
- 예약: 로컬 LaunchAgent `com.hyunlord.bs-mobile.stutter223.20261013`; 날짜·중복 실행 제한, 소유 프로세스만 종료하는06:00watchdog, caffeinate. 앱577파일·입력재생SHA고정. 전경 앱 이름 관찰/복귀 재개. 방해 금지는 자동 설정 수단이 없어 **미설정·미검증**이다.
- 진단 범위: C05 같은 프로세스2회 ×3묶음. 전경 이탈 구간을 제외하고 입력 재생을 이어 간다. 출력은 `artifacts/stutter-223/night-20261013`. Release ABBA를 진단 결과로 대체하지 않는다.

| 자료 | 기록 | 해석 경계 |
|---|---|---|
| memory.csv | 최대1Hz Stopwatch시각·프레임·틱·적 수·Mono사용/힙·Unity할당/예약 | 18001표본 상한·누락수 명시. 0/미지원은 메모리0 증거가 아니다. Unity allocator는 RSS나 순수GPU/native 메모리가 아니며 Mono와 합산하지 않음 |
| capacity-growth.csv | 실제14추적 컬렉션 초기Count/Capacity, 이후 증가 | kind=baseline/growth. 전체 버퍼·모든 리스트나 바이트 크기라고 확대하지 않음 |
| raw Profiler | >50ms 트리거, 이전 구간 및 후속4프레임 | 초기 제외·계측/파일저장 비용 표시. 원시 프레임과 Update구간 경계는 다름 |
| foreground | 앱 이름·bundle·PID·UTC | NSWorkspace; OS 화면 기록/손쉬운 사용 권한 없음 |
| 상관 그래프 | 프레임·실경과/틱·적 수·메모리·용량 | 모든 네이티브 측정 종료 후 생성. 최근접1Hz 표본의 시차 표시; 상관은 원인 증명 아님 |

진단 전용 명령은 기존 하네스의 `--wave-benchmark benchmarks/wave-c05/run.ssreplay --wave-benchmark-mode capped --wave-benchmark-profile --wave-benchmark-stall-trace --wave-benchmark-repeat 2 --diagnostic-long-frames`다. 원인 판독·수정·새 빌드 확인 뒤 A=8f446e5와 B의 Release ABBA각3회(411/C05전체/C05800+)를 별도로 실행한다. A의 비활성 중단 제한과 이전 실패는 보존한다.

#126/#98/#224/1b 및 SDK 미도입 유지. [계측 PR242](https://github.com/hyunlord/bs-mobile/pull/242) · [미해결 #223](https://github.com/hyunlord/bs-mobile/issues/223).
