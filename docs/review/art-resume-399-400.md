# 아트 대기 작업 재개 — #399 / #400

관문: 부분 — 새싹 문장 감사·교체안 완료, 두 적16프레임 리그는 원본 준비 후 Unity 점유로 차단.

## 사용자 결정

2026-10-26 사용자 승인: r4바닥 세트(풀r4·흙6종·길패치). 미리보기 반복 종료. 게임 적용은 별도 Codex작업이다. [기존 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-lv0-sites-review-20261026)의 과거파일은 보존하고 승인 결정을 본문에 추가했다. 이 작업에서는 게임 코드·데이터·atlas를 수정하지 않았다.

## 적16프레임 리그 #399

약탈병r2의 원본 `/Users/rexxa/character-forge/experiments/enemy-raider-r2-20261023/`를 확인했다. 실제 Unity6000.6.4f1 SpriteSkin·Camera.Render 방식이며, 당시 방패/멧돼지 작업은 미착수였다. 현재 요청의 충차는 `core:ram_runner`의 인간형 `charger-parts.png`로 확인했고 멧돼지와 구분했다.

로컬 `output/enemy-walk-resume/rigs/`에 방패/충차 원화·원본 부품 사각형·앵커·뼈 설정·현재8프레임역할·피벗·worldSize·캡처계약·16행 NOT_RUN검사표·Unity 소스골격을 준비했다. r2 코드복사·원화 SHA256, JSON,16행 검사표 확인 완료. r2의 발바닥 contour/뼈 길이·26ms·목0px 결과를 다른 종의 검증값으로 재사용하지 않았다.

**종별 Scene/Gait/Capture 작성·보정·Unity컴파일·캡처는 미완료다. 두 종 모두 신규16칸 캡처0/16, 프레임별 전신 접사판정0/16.** 실행가능 완성리그나 새 그림 납품이 아니다. 방패는 목/몸/어깨/팔/검 파지/방패고정, 충차는 목/몸/어깨/팔꿈치/갈퀴 손을 전신1배·3배로 검사하도록 계약했다. 충차에는 별도무기가 없어 파지는N/A다.

차단 근거: 2026-10-26 10:42KST 공유Mac `stack-move/artifacts/main-verify/agent-busy.lock`은 다른 검증작업 pid44116소유였고 프로세스가 실제 살아 있었다. 런북에 따라 남의 lock삭제·Unity동시실행을 하지 않았다. 소유권이 정상 해제된 뒤 주간시간에 자기lock을 획득하고 실제 종별 리그를 보정·캡처해야 한다. #399는 열린상태로 유지한다.

## 새싹 문장 감사 #400

[감사 Release](https://github.com/hyunlord/bs-mobile/releases/tag/sprout-emblem-audit-400-20261026)에 AUDIT.md, 접사A/B, 원본해시·crop·manifest매핑을 게시했다. 원본기준 e088545. 게임 실행장면이 아니라 실제PNG와 정적사용경로 감사다.

- 백합/금색사자 원본잔존8role: wave binding5개, first-playable전용 구형건물3개. 8개가 모두 현행wave화면에 등장한다고 주장하지 않는다.
- 추가 새싹정합성 후보7항목: 여러잎/곡식문장 등은 백합과 별도로 분류했다.
- 교체안은 승인된 씨앗1·휘어진줄기1·서로다른높이의잎2실루엣을 쓰며 원근·주름·손상·재질을 유지한다.
- 실제 전투draw참조인 levy_banner·ward방패를 우선한다. 금속재질·적소속문양은 자동치환하지 않는다.
- 작은 measured_volley메달 식별과 seed_crown기능왕관 변경범위는 판단보류다. 교체목록/제안작성은 완료했으며 실제그림교체는 이번 범위가 아니다.

접사22개·보드2장·매핑을 육안/정적으로 확인했다. 현행영웅/병사 첫프레임 대표crop은 전체동작의 프레임별감사로 확대해 주장하지 않는다.
