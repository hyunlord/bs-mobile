# 강둑·나무 r2 검토

관문: 사용자 승인 대기. 밝음 나무6종은 사용자 승인, 이번 어둠 나무·강둑과 조립은 재검토 대상입니다.

## 수정

- 어둠 나무6종·강둑8종을 짙은 남색으로 image_gen 편집. 1·2배28PNG, 기존 캔버스·바닥 기준점 유지.
- 기존 둥근 강둑 돌을 생성 편집으로 지운 바탕에 새 조각을 배치. 제거 전/후 증거판을 별도 제공.
- 키아트 C / 실제 r7 native 첫 화면 / r2 정적 조립 비교. 평균 RGB와 Y′를 표기.
- 승인된 밝음 나무 및 모든 r1 파일 보존. 게임 코드·데이터·원본 atlas 수정 없음.

## 밝기 측정

기존 어둠 땅: native 좌표 (300,280,120,80), 평균 sRGB (61.44,66.70,76.31), Y′66.28. 다른 땅 표본2개도 Y′66.94·67.01로 비슷했습니다.

새 나무12PNG의 평균 Y′33.40–37.15, 강둑16PNG는20.76–28.81로 모두 땅보다 낮습니다. Y′=0.2126R+0.7152G+0.0722B, 비선형 sRGB 기준입니다. 스프라이트 RGB는 투명 픽셀을 검정으로 넣지 않은 알파 가중 비프리멀티플라이 평균이며 물리 휘도가 아닙니다. 그림자 속 윤곽은 약하게 남기되 순수 단색으로 만들지는 않았습니다.

## 출처와 경계

원본: Release `m3a-r7-feather-native-20261026`, `native-first-0-1-30.png`의 frame0, crop(0,32,834,1808). 원본 source8fcbcd7. 비교판은390×844 표시입니다. 조립은 정적 그림 검토용이며 런타임 적용 화면이 아닙니다.

built-in image_gen으로 색 편집과 기존 돌 제거 바탕을 제작했습니다. Swift/CoreGraphics는 크롭·리샘플·마스크 합성·수치 측정만 담당했습니다. 프롬프트는 PROMPTS-r2.md, 조각 크기·기준점·밝기는 ASSET-LIST-r2.md에 기록했습니다. 원본 생성물과 검사자료는 로컬 r2 하위에 보존합니다.

- [同 Release r2](https://github.com/hyunlord/bs-mobile/releases/tag/m3-riverbank-clusters-review-416-20261026)
- [r2 비교판](https://github.com/hyunlord/bs-mobile/releases/download/m3-riverbank-clusters-review-416-20261026/keyart-C-r7-riverbank-clusters-comparison-r2.png)
- [r2 조각 목록](https://github.com/hyunlord/bs-mobile/releases/download/m3-riverbank-clusters-review-416-20261026/ASSET-LIST-r2.md)

물·다리·HUD 보호 영역은 원본 그대로 유지합니다. 반투명 하단 HUD 뒤에 비치는 옛 바위 흔적은 HUD 픽셀 보존 때문에 유지하며, 화면에 드러난 강둑5구간은 제거 후 교체합니다. 보호영역 외 강둑만 생성 편집 바탕으로 바꾸며 게임 미적용입니다.
