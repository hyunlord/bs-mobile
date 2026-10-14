# M1-5 UAL 걷기 시험

2026-10-14 · [#262](https://github.com/hyunlord/bs-mobile/issues/262). 사용자 결정으로 Mixamo 경로는 중단하고 파일만 보존한다. 로그인 없는 Quaternius UAL 무료판을 사용해 승인 (b) 메시의 걷기1동작·1방향 시험을 수행한다. 결과 수치와 정지 준비는 사용자 영상 승인과 구분한다.

## 실제 다운로드와 라이선스

[UAL1 공식 페이지](https://quaternius.com/packs/universalanimationlibrary.html)와 [UAL2 공식 페이지](https://quaternius.com/packs/universalanimationlibrary2.html)에서 연결된 itch.io 무료 Standard 다운로드를 로그인·결제 없이 받았다. 광고의120+/130+ 전체판 수량을 받은 무료판 수량으로 쓰지 않는다. 실제 GLB는 각각43클립이며 `A_TPose`를 포함한다.

원본 동봉 라이선스: [UAL1 License.txt](../../research/licenses/quaternius-ual/UAL1-License.txt), [UAL2 License.txt](../../research/licenses/quaternius-ual/UAL2-License.txt). 두 파일은 CC0 1.0 Universal/Public Domain Dedication을 명시한다. [CC0 법문](https://creativecommons.org/publicdomain/zero/1.0/legalcode.en), 확인2026-10-14. 원본 ZIP·README·공식/다운로드 페이지·해시·전체 inventory는 로컬 `ual-pilot/source/`에 보존한다. 새 전달 ZIP은 만들지 않는다.

원본 아카이브: UAL1 15,904,933바이트 SHA256 `cc73fc4e495b82958207316596317a3f40b9fa38065bde1027937452da537724`; UAL2 18,735,003바이트 SHA256 `4008ea208a604773a2b2177d965f0f5d3195498b5bf838c3f5785d68e95f2a68`. 실제 Blender import에서도 각각43actions/65bones, 이름·부모 관계 일치. 원본 `Walk_Loop`은1.333333초/30Hz41samples이며24fps import에서0..32프레임이다. `ual1-imported-donor.blend`는 무료 GLB를 로컬 변환한 파일로 유료 Source판이 아니다.

## 실제 클립 이름

| 용도 | UAL1 Standard | UAL2 Standard |
| --- | --- | --- |
| 걷기 | `Walk_Loop`, `Walk_Formal_Loop` | `Walk_Carry_Loop`, `Zombie_Walk_Fwd_Loop` |
| 달리기 | `Jog_Fwd_Loop`, `Sprint_Loop` | 일반 달리기 없음 |
| 대기 | `Idle_Loop`, `Sword_Idle`, `Idle_Talking_Loop`, `Idle_Torch_Loop` | `Idle_FoldArms_Loop`, `Idle_Lantern_Loop`, `Idle_No_Loop`, `Idle_Rail_Loop`, `Idle_Shield_Loop`, `Idle_TalkingPhone_Loop`, `Zombie_Idle_Loop` |
| 검·근접 | `Sword_Attack`, `Punch_Cross`, `Punch_Jab` | `Sword_Regular_A`, `Sword_Regular_A_Rec`, `Sword_Regular_B`, `Sword_Regular_B_Rec`, `Sword_Regular_C`, `Sword_Regular_Combo`, `Sword_Heavy_Combo`, `Sword_Dash`, `Sword_Block`, `Melee_Hook`, `Melee_Hook_Rec`, `Zombie_Scratch` |
| 피격 | `Hit_Chest`, `Hit_Head` | `Hit_Knockback` |
| 사망 | `Death01` | 없음 |

첫 시험은 UAL1 `Walk_Loop`. 목록을 사용자에게 먼저 보고한 뒤 리깅을 시작했다. 검/피격/사망 클립이 있어도 걷기 승인 전 제작 범위를 확대하지 않는다.

## 리깅과 검수 계약

승인 (b) 원본 GLB SHA `a8d65d591ae2da80829d409449b06eb33a065285a8268961b024e18c363a5cff`와4K 원본 텍스처를 보존한다. UAL 공통 사람형 뼈대 관절을 맞추고 Blender bone heat로 가중치를 만든다. 이름이 같아도 rest pose/축/비율 변경이 동작을 망가뜨릴 수 있어 원본 뼈대·행렬·클립을 보존하고 실제 재생으로 확인한다. 자동 가중치 실패는 숨기지 않으며 필요하면 UniRig 예비안의 라이선스·실행 환경을 확인한 후 UAL 리타깃으로 전환한다.

실제 오른손 본에 별도 검, 몸통 기준에 망토 보조 본을 결합한다. 제출은2배 영상·0.05초 간격 시트·팔/옆구리/겉옷 앞판/다리 사이 확대·100px 미리보기다. 늘어남·찢김·관통은 실패. 소품·겉옷과 몸의 관통도 확인한다. 색·툰/셀·외곽선·게임 크기 얼굴 보정은 다음 굽기 단계로 이관하고 이번 시험에 섞지 않는다.

## 실제 자동 가중치 실패

UAL1 뼈대를 맞춘 뒤 Blender `ARMATURE_AUTO`를 실행했지만 원본67,117정점 모두 무가중치였다. 원본 렌더 메시를 바꾸지 않고 정확히 같은 좌표의 정점만 임시 프록시로 합쳐 재시험했으나11,327정점/22,445면에서도 전부 무가중치였다. 두 시도의 `.blend`·로그·진단 JSON은 `ual-pilot/rig/`에 보존했다. 작은 분리 조각·비정상 표면은 원인 후보이지 확정된 단일 원인은 아니다.

자동 가중치 관문은 실패이며 실제 걷기 영상이 아직 없다. 사용자 승인 예비안인 UniRig 설치·가중치 생성·UAL 리타깃 검증을 진행한다. 무가중치 모델에 인위적인 걷기를 붙여 제출하지 않는다.
