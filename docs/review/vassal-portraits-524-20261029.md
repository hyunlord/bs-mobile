# 가신 6인 초상 r1 사용자 승인 기록

관문: 통과 — 사용자가 2026-10-29 가신6인 r1 새싹 교체 그림을 승인했다. 원래 crop 크기의 상태 PNG18개와 별도 atlas 검증은 유지하며, 게임 적용은 별도 Codex 작업이다.

이슈: https://github.com/hyunlord/bs-mobile/issues/524

Release: https://github.com/hyunlord/bs-mobile/releases/tag/vassal-portraits-524-20261029

현행 `meta-vassals.png`의 가신6인 이름·얼굴·직업 소품을 유지하며 무광 잉크·과슈 화풍으로 다시 그렸다. 고랑 방패수 가슴·방패와 징집 조장 가슴·깃천의 기존 백합4곳을 승인 새싹 기준(씨앗 하나, 휜 줄기, 높이가 다른 두 잎)으로 바꾼 승인 그림이다. 전투 `person.vassal` 방패와 이 메타 초상은 별도 그림이므로 이전 #400 감사의 완료를 이6초상 교체 완료로 확대하지 않는다.

## 납품 계약

| 가신 | native crop 크기 | 상태 |
|---|---|---|
| 고랑 방패수 | 324×383 | normal / selected / locked |
| 황금밭 호위 | 355×396 | normal / selected / locked |
| 귀환 대장 | 294×375 | normal / selected / locked |
| 비옥도 측량관 | 314×381 | normal / selected / locked |
| 징집 조장 | 373×388 | normal / selected / locked |
| 수확 서기 | 294×375 | normal / selected / locked |

selected는 normal과 동일한 RGBA이며 실제 선택 표현은 기존 `ui.card.rare` 테두리다. 6×3 상태판에는 이 테두리를 표시했다. locked는 현행 shader 수식 `gray=dot(RGB,(.2126,.7152,.0722))`, `RGB=(.1RGB+.9gray)×.55`를 8bit로 기계 파생하고 alpha를 보존했다. GPU 실행 픽셀 일치를 주장하지 않는다.

별도 normal atlas는1254×1254이며 현행6crop좌표를 그대로 쓴다. 비해당 영역은 투명하다. 원본 atlas SHA256 `6356280357c4aec7aebb7810f653ca9d9b292bbade7639b1287b0e350e4bea53`를 보존했다.

## 검증과 범위

18PNG 크기 일치, normal/selected RGBA차이0, locked 수식 채널 오차0·alpha오차0, 새atlas6crop과normal의 RGBA차이0, 기존6crop·atlas SHA불변을 확인했다. 승인 가신UI의 상세108×172·목록48×53 영역만 교체하고5잠김을 유지했다. 초상7개 표시영역 밖 원본RGBA변경0이다. 이것은 오프라인 UI시안이며 runtime 상세126×165와 구별한다.

원화와 생성 프롬프트·SHA, 등방 크기 변환, 상태 파생, 정확 좌표를 Release 계약JSON에 기록했다. PNG23개(게임용19·검토4), JSON1개, 문서3개를 선별한다. 신규 ZIP을 만들지 않았다.

게임 코드·데이터·원본 atlas·기존 Release는 변경하지 않았다. Unity 실행·실기·성능·게임 적용은 NOT_RUN이다. 그림 승인은 이 문서PR의 병합이나 게임 적용 완료를 뜻하지 않는다. 이번 명시 병합 대상 #525와 별개인 PR526은 승인 기록을 반영한 준비 상태로 열어 두며 임의 병합하지 않는다.

## 승인 이력 보존

2026-10-29 사용자 결정: 가신6인 초상 r1 새싹 교체본 승인. 기존 #524 제작 기록과 #400 누락 보완 댓글은 보존하고 후속 승인 댓글을 추가한다. 공개 Release의 후보·승인 대기 표기는 당시 납품 상태의 역사 기록이며 원본 파일·SHA·원화·프롬프트·실패 기록을 덮어쓰지 않는다.

첫 로컬 전체 검사의 문서 HEAD 변경에 따른 provenance 불일치 실패와 고정 HEAD `60c9522` 재실행 exit0(809/809 테스트)도 보존한다. 이번 변경은 승인 상태 문서만 갱신하며 기존 아트 검증을 재분류하거나 게임 실행 증거로 확대하지 않는다.
