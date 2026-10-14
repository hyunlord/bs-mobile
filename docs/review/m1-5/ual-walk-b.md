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

Blender bone heat 관문은 실패이며 그 결과를 보존한다. 사용자 승인 예비안인 UniRig로 실제 가중치를 생성했으며, 아래 결과와 실제 걷기 품질은 구분한다. 무가중치 모델에 인위적인 걷기를 붙여 제출하지 않는다.


## UniRig 실제 추론

격리 DGX 환경에서 UniRig `6793c6640ff01c8fb389f3993434124bb43d2933`와 공식 모델 revision `36842e2b5947e9e60f89275b83208c8e74071c63`을 사용했다. 원본 license와 source/model SHA, 설치 로그·실패 로그·최소 호환 패치·재현 명령은 로컬 `ual-pilot/unirig/`에 보존한다. [라이선스 경계](../../research/m1-5-free-trellis.md): 본체/모델 MIT, 포함 Michelangelo GPLv3. 게임 런타임에 포함하지 않는다.

- ARM64/sm_121에서 cumm·spconv·torch-scatter·torch-cluster를 빌드했다. 실제 GPU sparse convolution·scatter sum·segment CSR·farthest-point sampling 기준값 검사가 통과했다. PyTorch의 일반 capability 경고를 전체 GPU 호환성 보장으로 해석하지 않는다.
- 승인된 정확 좌표 프록시11,327정점/22,445삼각형과 이미 맞춘 UAL65뼈대를 입력했다. seed42 공식 skin model/transform으로 실제 추론 exit0, 약48초. 학습된 표면 가중치를 공식 reskin으로 원본 정점 순서에 보간했으며 임의 nearest-bone 배정이 아니다.
- 실제 결과11,327×65, NaN/음수0, 행합0.99999988–1.00000012. 정점 순서·좌표 정규화 잔차6.04e-8. 결과 NPZ SHA `dfb57e37ca4e36540f485c457e472db23e1eeccf3533ad686d951e16fb59c5df`.
- 기존 bone heat 시험에서 deform=false였던 손가락/leaf28개에도 전체 가중치의2.13%가 있다. 실제 적용 때 이 기여를 버리거나 손 본으로 합치지 않고 원래 관절에서 변형하도록 보존한다.
- 첫 checkpoint 로딩은 최신 PyTorch weights-only 기본값 때문에 실패했다. 공식 SHA와 정적 검사를 확인한 뒤 필요한 `box.box.Box` 하나만 허용해 해결했고 임의 pickle 전체 허용으로 풀지 않았다.

실제67,117렌더 정점에 정확 인덱스로 적용했다. 무가중치0, geometry/UV 완전 동일, packed4K 텍스처 SHA 불변을 확인했다. UAL `Walk_Loop`을60fps80프레임으로 실제 재생·렌더했다. 숫자상 유효한 가중치는 팔·겉옷·다리의 늘어남/찢김/관통 없음이나 사용자 영상 승인을 증명하지 않는다.

첫 실제 몸체 렌더는 **실패**다. 장갑/손가락이 갈퀴처럼 길고 납작하게 변형되며, 앞자락이 다리 사이에서 찢긴 듯 접히고 무릎과 교차한다. 0/20/40/60프레임의 같은 시점 확대와 전체80프레임 영상을 보존한다. 최대 edge비율만으로 판정하지 않고 실제 렌더를 확인했다. 손의 rest 상태 비교와 fitted finger joint 검증은 완료하지 못해 손 변형의 원인은 미확인이다. 소품 없는 실패 증거를 유지한다. 최대 상대 늘어남50.97은0.111mm→5.677mm인 짧은 앞자락 변이다. 최대 절대 늘어남은 뒤쪽 옷자락18.687mm→196.717mm이며 인접 정점의 pelvis/thigh/calf 가중치 차이가 확인됐다. 수치가 작거나 큰 것만으로 실제 관통 판정을 대체하지 않는다.


## 실제 걷기 제출 표본

표본 기준은 수정/가림 없는 원래 UAL 첫 루프 전체와0.05초 등간격, 변형 비교는0/20/40/60프레임이다. 실제 루프는60fps80프레임(1.333333초), 영상은 같은 루프4회 반복/320프레임/5.333초이며 무음 저작 시험이다. 게임 영상·실시간 성능·발 미끄러짐 통과 증거가 아니다.

| 제출물 | 확인 범위와 결과 |
| --- | --- |
| `body-1x-2x-walk-failure.mp4` | 같은 몸체1배/2배, 실제 걷기 전체 루프. 손가락/앞자락 변형 실패 |
| `walk-005s-sheet.png` | 실제0,3,…78프레임을0.05초 간격으로 배치 |
| `walk-defect-closeups.png` | 같은 시점의 팔·옆구리·앞판·무릎·다리 사이 확대. 앞자락 교차 실패 |
| `body-100px-walk.mp4` | 같은 루프의 약100px 전신 높이 표본 |
| `props-actual-walk-failure.mp4` | 별도 검은 실제hand_r, 망토는spine_03 추종. 정상 쥐기와 망토 관통 관문 실패 |
| `cape-shoulder-penetration.png` | 동일 걷기40프레임 후면. 어깨/상완이 망토를 뚫는 실패 표본 |

검·망토 시험은 리뷰용 사본만 수정했다. 망토 보조 본3개는 있지만 천 시뮬레이션이나 별도 흔들림 애니메이션은 이번 실패 표본에 추가하지 않았으며 동적 망토 통과로 쓰지 않는다. 소품으로 몸체 결함을 가리지 않도록 몸체 단독 영상도 유지한다. 원본 FBX/GLB/학습 가중치/atlas를 Release에 올리지 않고 선정된 실패 영상·시트만 제출한다.

결론: 로그인 없는 다운로드와 실제 가중치/걷기 재생은 실행했으나 **늘어남·관통 관문 실패**, 사용자 걷기 승인 없음. 정지 메시 승인과 구별한다. 소스 모델 재조형·재토폴로지·수작업 가중치 보정은 이번 결과를 숨기는 임의 수정으로 추가하지 않으며, 베기·피격·8방향으로 확장하지 않는다.

[선정된 실패 영상·시트 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-5-ual-walk-trial).
