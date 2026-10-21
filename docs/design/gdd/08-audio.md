# 08 사운드

버전: v1.1 · 날짜: 2026-10-12 · 바뀐 점: M1 음높이 변주 구현과 청감·실측 미검증 상태를 분리했다.
상태: 정본(현재 계약); 새 타격음 변주·감성 품질은 적용/사용자 확인 대기.

## 역할과 현재 실행

소리는 타격·처치·수확·진화·위험을 구분한다. 소리를 끄고도 맞았다는 것을 느끼게 해야 하므로 시각 부족을 음량으로 숨기지 않는다. 확인 기준은 `9387b7669bdc3d01790246509ad8d66f212af0ad`다.

| 층 | 현재 구현 | 혼잡 제어 |
|---|---|---|
| 공통 | hit, kill, harvest, level, evolution, hurt, boss, frontier-loop 음악 | 효과 전용 7음원; cue별 cooldown; 음악 별도 |
| wave-1a | 무기·도구·진화·적 예고·성장 완료 cue, 공통 hit/kill/hurt/harvest/level | 6음원 풀, cue priority/cooldown, 중요 cue를 위한 앞쪽 슬롯 보호 |
| 사건 연결 | 실제 presentation/wave 사건을 한 번 받아 재생 | 사건 ID 중복 재생 방지, 공통/wave 중복 cue 회피 |
| 진동 | 영주 피격·레벨업·진화·보스 | 최소 1초, pause/설정 OFF에서는 호출하지 않음 |

[FirstPlayableAudio](../../../unity/Assets/Game/View/FirstPlayableAudio.cs), [WaveAudio](../../../unity/Assets/Game/View/WaveAudio.cs), [wave 사건 연결](../../../unity/Assets/Game/App/RunCoordinator.WaveAudio.cs)가 실행 근거다. 공통 cooldown은 hit 0.09초, kill 0.14초, harvest 0.12초, hurt 0.4초이며 wave 공통 cue에는 별도 0.14초 정책이 있다. 이 값은 현재 사실이지 최종 청감 균형의 승인 수치가 아니다.

[RunPreferences](../../../unity/Assets/Game/App/RunPreferences.cs)의 음악 0.55·효과 0.8·진동 ON이 새 환경설정 기본값이다. 음원 클래스의 초기 필드 값과 사용자 설정 적용 후 값은 구분한다. 일시정지에서 볼륨·진동 끄기를 유지하고 pause 시 재생을 중단/일시정지한다. 네이티브 기기 진동 느낌은 코드 검사로 통과 처리하지 않는다(#98 보류).

## M1 타격음과 연출 요구

[06 타격감 기준표](06-feedback.md)의 일반 타격음 음높이 ±5%는 [가설]이다. 위 기준 커밋의 `Play`에는 변주가 없었고, #246에서는 사건 ID에서 산출한 표현용 변주를 추가한다. Core RNG는 사용하지 않는다. 별도 처치음은 이미 있지만 새 시각 터짐과 함께 청감 검수가 필요하다. 큰 기술·진화는 짧은 진동을 쓰되 기존 최소 간격과 사용자 끄기를 유지한다. 무작위 소리 선택을 넣어도 Core RNG·상태·재생 명령에 영향을 주지 않는 표현 계층에 둔다.

새 효과음은 무기 유형별 리듬과 재질을 구분해야 한다. 동시에 수백 번 울리는 hit 때문에 예고·피격·레벨업이 묻히지 않도록 우선순위와 풀을 재사용한다. 동시 음원 수를 늘리는 변경은 실측 후 검토한다. #246은 기존 음원 풀을 재사용하고, 매 재생에서 pitch를 다시 설정해 앞선 타격 변주가 다음 처치·수확음에 남지 않도록 한다. 표현 기본 ON과 비교용 baseline OFF를 분리한다. 제작 음원·생성 스크립트·출처는 [기존 음원 제작 도구](../../../tools/first-playable-audio.mjs)를 보존하며 새 외부 음원은 라이선스를 원장에 기록한다.

## 검증과 보류

소리 켠/끈 동일 seed 영상으로 판독과 피로도를 구분한다. mute에서 타격이 안 읽히면 시각 관문 미달이다. 첫 로딩·첫 재생 비용은 Development의 `AudioResourceLoad`·`AudioSourceCreate`·`AudioFirstPlayback` 마커로 추적하지만 #223의 멈춤 원인이 소리라고 확정하지 않는다. 본 문서 작성은 새 음원 추가·모바일 실기·새 성능 통과 증거가 아니다. 광고·분석·오디오 외부 SDK는 추가하지 않는다.

M1 음원 구현의 집중 테스트와 실제 실행·청감·C05 비용 확인은 별개다. 구현 담당의 정적 점검만으로 실제 영상의 소리가 존재하거나 변주가 적절하다고 판정하지 않는다. 앱 내부 비교 녹화는 `GoldenMinuteAudio.BeginMixedOutput`의 혼합 음원을 기록하며, 무음 오프라인 영상으로 타격음 관문을 대체하지 않는다. 진동 최소 간격 1초와 사용자 끄기는 유지한다.
