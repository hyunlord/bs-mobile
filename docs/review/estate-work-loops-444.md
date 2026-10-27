# 영지 주민 제자리 작업 루프 #444

[작업 이슈](https://github.com/hyunlord/bs-mobile/issues/444) · [그림 검토 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-estate-work-loops-444-20261027)

승인된 주민·일꾼 원화를 기반으로 걷기와 구분되는 제자리 작업 동작을 만든다. 게임 코드·데이터·atlas와 기존 아트 파일은 변경하지 않는다. 기록소·병영 문장 r3와 제철소 r2는 사용자 승인되었으며 게임 적용은 별도다.

## 실제 제작·검증 범위

Unity6000.6.4f1 / 2DAnimation16.0.1의 실제 SpriteSkin을 별도 제작 프로젝트에서 변형하고 Camera.Render로 캡처한다. 원화의 양손과 도구·짐 연결을 유지하고, 팔·허리 또는 고정 발 IK를 사용해 작업 동작을 만든다. Swift는 크기 조절과 atlas·접사판 조립을, ffmpeg는 반복 영상 인코딩을 담당한다. 실제 게임 배치와 기기 실행을 검사한 것으로 표현하지 않는다.

각 동작의 전 8프레임 개별 전신 접사, 전체판, 원본 크기 영상 및 3배 영상을 직접 확인한다. 발 표본·손 파지 거리·접촉·루프 닫힘 수치를 시각 검사와 함께 기록한다. 일부 중간 리그는 수치가 맞아도 팔 늘어짐·옷 겹침·몸통 빈 부분이 보여 내부 불합격 처리했다. 실패 캡처와 당시 판정은 로컬 제작 기록으로 보존하며 최종 후보와 혼동하지 않는다.

## 괭이질

8프레임,1.6초 주기. 원래 주민의 괭이·양손을 같은 강체 관절에 연결하고 허리·팔 가중치로 들어 올리기→흙에 닿기→짧게 당기기→복귀를 만든다. 네이티브 셀64×64, 사람 높이는 약52px다. 도구 끝은 약8.05px 올라가며 접촉 구간에서 흙 평면을0.33px 이내로 통과한다. 실제 SpriteSkin의 불투명 삼각형 뒤집힘0, 양발 표본 이동0, 손 사이 거리 오차0.000096sourcepx 이하, PNG 닫힘 차이0이다.

[전신 접사](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-all8-source-3x.png) · [1배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-loop-1x.mp4) · [3배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/peasant-hoe-loop-3x.mp4). 영상은 각16초,10회 반복한다. 마지막 내부3차가 첫 공개r1 후보이며 내부1·2차 불합격은 보존한다.

## 망치질

8프레임,1.2초 주기. 승인된 정지 그림의 몸·작업대는 고정하고 위팔과 전완·손·망치를 별도 SpriteSkin 조각으로 움직인다. 원래 움직이는 팔 뒤에 가려졌던 어깨·가슴만 imagegen으로 보완했으며 복원 마스크 밖 RGBA 변경0을 실제 캡처 cutout과 대조했다. 원본 파일은 보존한다. 망치를 들어 올려 목재를 타격하고 되돌아오며, 반대 손은 작업대에 고정한다.

네이티브64×76, 기준점(22,66), 사람 높이51.41px. 고정 정점 이동0, PNG 닫힘 동일,8개 서로 다른 프레임이다. 손·손잡이·망치머리는 동일 강체 관절을 사용한다. 전신 확대에서 목·어깨·팔꿈치·손목·허리·다리의 빈 부분이나 늘어진 조각이 없는지 확인했다.

[전신 접사](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-hammer-all8-seams-r1.png) · [1배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-hammer-loop-1x-r1.mp4) · [3배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-hammer-loop-3x-r1.mp4). 영상은 각14.4초,12회 반복한다. 마지막 내부4차가 첫 공개r1 후보이며 앞선 실패3차례는 로컬에 보존한다.

## 포대 내려놓기·다시 들기

8프레임, 2.4초 주기. 승인된 짐 나르는 일꾼의 상체·양손·포대를 연결된 원화로 유지하고, 허리를 숙이며 고정 발 IK로 쪼그려 앉는다. 포대가 표시 기준 약18.32px 내려가며 3·4프레임에서 땅에 닿고 다시 든다. 손은 접지 중에도 포대를 잡는다.

네이티브 셀66×66, 서 있는 사람 높이53.37px이며 숙이는 자세는35.58px다. 기준점은 셀 좌상단 기준(31.453125,59.5546875)로 고정한다. atlas528×66과 개별8프레임의 픽셀 일치를 확인했다. 실제 변형 표본에서 발 이동≤0.00003capturepx, 손/짐 거리 오차≤0.000108capturepx, 접지 오차0, 표본 닫힘 오차0이다. PNG 닫힘은 반올림으로1픽셀·채널차1이다. 깊은 쪼그림에서는 일부 허벅지·먼발이 가려지므로 보이지 않는 내부 관절을 시각 검사했다고 주장하지 않는다.

[전신 접사](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-sack-work-all8-source-3x.png) · [1배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-sack-work-loop-1x.mp4) · [3배 영상](https://github.com/hyunlord/bs-mobile/releases/download/m3-estate-work-loops-444-20261027/worker-sack-work-loop-3x.mp4). 영상은 각24초, 10회 반복한다.

최종 그림 승인은 사용자 담당이다. 이전 #435 전체 검사의 메모리 할당 테스트 실패 기록은 이번 아트 작업으로 해결했다고 주장하지 않는다.

검수는 PNG 시퀀스 전 프레임과 MP4에서 디코딩한 주요 동작 표본을 대상으로 했다. 연속 재생을 직접 시청한 것으로 확대하지 않는다. 괭이질 기준점은(13.25,55.5)이며 실제 고정 발 표본에서 산출했다. 최종3종 모두 내부 독립 검수 통과, 사용자 승인 대기다.
