# r7 강둑·나무 군집 그림 세트

관문: 제작·내보내기 검사 통과, 사용자 그림 승인 대기. 게임 미적용.

- 강둑: 직선2·굽이 안쪽2·바깥2·다리 접속2, 밝음/어둠·1/2배 32 PNG.
- 군집: 침엽2·활엽2·관목/덤불2, 밝음/어둠·1/2배 24 PNG. 군집의 2배도 편의상 포함.
- 투명 배경, 좌상단 광원, 잉크·과슈, 세이지·황토 및 어둠 청회색. 작은 돌·흙·모래·갈대를 비정형으로 혼합.
- ASSET-LIST.md: 파일명·크기·바닥 기준점. PROMPTS.md: built-in image_gen 전체 프롬프트와 원본 경로.
- banks-contact-sheet.png, tree-clusters-contact-sheet.png: 밝음/어둠 2배 비교.
- keyart-C-r7-riverbank-clusters-comparison.png: 키아트 C / 실제 native 첫 진입 / 새 PNG 정적 합성. 게임 적용 화면이 아님.

## 검증과 한계

강둑32장·군집24장 알파 및 투명 외곽 검사를 통과했습니다. 대응 밝음/어둠은 동일 캔버스와 기준점입니다. 강둑 bbox 차이는 각 변 최대1px(2배). 잎끝·부분 알파가 픽셀 단위로 동일하지 않으므로 실제 조명 전환은 게임 적용 때 별도 확인해야 합니다.

원본은 Release `m3a-r7-feather-native-20261026`의 `native-first-0-1-30.png`입니다. frame0을 (0,32,834,1808)로 정확히 추출하고 비교판에390×844로 표시했습니다. 소스 commit8fcbcd7. `first-entry-3x.png`는 Editor 증거이며 native로 혼동하지 않았습니다.

물색 마스크(G-R≥10,B-R≥18,표시x>270 및2nativepx확장), HUD, 다리 보호영역은 원본 픽셀을 보존했습니다. 마스크 기반 검사는 의미론적 전체 물 영역의 자동 증명을 뜻하지 않습니다. 비교판을 함께 육안 확인했습니다. 기존 강둑 돌은 새 조각이 덮는 부분에서만 가려지고, 일부는 남습니다. 원본 물 텍스처 및 게임 코드·데이터는 변경하지 않았습니다.

다리 접속02 최초 원본에 물처럼 보이는 청색 면이 있어 dry-earth r2로 편집했습니다. 최초 후보는 로컬 originals에 보존했습니다. 밝음/어둠은 image_gen 편집, 크롭·리샘플·비교판은 Swift/CoreGraphics 기계적 처리입니다.

공사 세트 전체(건물3단계r2 포함)는 이번 사용자 지시에 따라 승인 상태로 기록했습니다. 이 새 강둑·군집 세트의 최종 그림 승인은 사용자 담당입니다.

## 전달

- [아트 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m3-riverbank-clusters-review-416-20261026)
- [키아트·실제 r7·조립 비교](https://github.com/hyunlord/bs-mobile/releases/download/m3-riverbank-clusters-review-416-20261026/keyart-C-r7-riverbank-clusters-comparison.png)
- [전체 조각 목록](https://github.com/hyunlord/bs-mobile/releases/download/m3-riverbank-clusters-review-416-20261026/ASSET-LIST.md)

로컬 생성 원본·반려 후보·Swift 내보내기·검사 기록은 `output/m3-riverbank-clusters/`에 보존합니다. 저장소 변경은 이 검토 문서뿐입니다. 런타임·실기 검사는 게임 미적용 범위여서 수행하지 않았습니다.
