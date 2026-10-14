# M1-5 (b) A-pose 입력 검토

2026-10-14 · [#262](https://github.com/hyunlord/bs-mobile/issues/262). **후속 결정: 사용자가 A-pose 후보 v2를 승인해 (b) 메시 생성을 허용했다.** 아래 입력 제출 시점의 기록은 보존한다. 생성 메시 검토 전 검·망토 부착과 Mixamo는 금지하며, 걷기 검수에는 겉옷 앞판이 다리 사이에서 늘어나는지 확대 확인을 추가한다. (a) 승인 메시와 정리 실패는 보존한다.

## 비교 입력

원본: `docs/design/gdd/hero-concepts-v1/A-knight.png` 1536×1024에서 xyxy(0,15,532,925)로 얻은532×910 정면. 원본 시트 SHA256 `b6f834a6cce6a73db14373cffd68b3cd90bff10c6aa4e3e13078b68e150cf9d5`.

후보: 이전에 준비하고 보류했던 `a-knight-apose-v2.png` 1024×1536. 새로 승인됐다고 해석하지 않는다. 팔을 약35–40도로 벌리고 검·긴 망토를 제거한 후보이며, 얼굴/머리/상아색 누비옷/파란 장식/몸 비율을 비교한다. 이번 비교 시트는 원본 이미지 픽셀을 재생성하지 않고 동일 인물 높이로 배치한다. 머리-키 비율은 눈으로 고른 랜드마크의 추정이며 해부학적 자동 판정이 아니다.

## 다음 생성의 색 보존 계약

목표20,000–30,000삼각형 GLB. 저밀도 정점색에 의존하는 감량은 금지한다. 원본 고밀도 메시/속성 볼륨을 보존하고 원본 색을 UV 텍스처로 옮긴 뒤 저밀도 GLB Base Color에 연결한다. 원본과 감량본의 얼굴·옷 문양·실루엣·UV 경계 투사 누락을 직접 비교한다.

고정 [O-Voxel 공식 exporter](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/o-voxel/o_voxel/postprocess.py)는 nvdiffrast import와 무조건적인 cuBVH 생성 때문에 `remesh=False`로도 이전 비상업 제한을 피하지 못한다. [설치된 nvdiffrast 라이선스](https://github.com/NVlabs/nvdiffrast/blob/253ac4fcea7de5f396371124af597e6cc957bfae/LICENSE.txt)의 §3.3 및 cubvh 포함 LICENSE_NVIDIA 제한은 유지한다. 공식 exporter를 실행했다고 보고하지 않는다.

허용 경로 권고: 격리된 [CuMesh MIT](https://github.com/JeffreyXiang/CuMesh/blob/12289e1062f0603f2f0d0771b02e1395d247f26f/LICENSE)의 simplify+xatlas UV → [Blender 원본 색 Emission 베이크](https://docs.blender.org/manual/en/latest/render/cycles/baking.html) → GLB 텍스처 포함. 이는 **별도 GLB 내보내기**이며 공식 PBR metallic/roughness/alpha와 같다고 주장하지 않는다. 아직 (b) 원본이나 GLB를 생성하지 않았다.

## 후속 범위

검은 원화 십자 손잡이·긴 날의 별도 단순 소품으로 제작해 오른손 본에 붙인다. 망토는 어깨~무릎의 원화 파랑 천 메시와 보조 본2–3개. 영웅 몸 전체 직접 제작은 승인되지 않았다. Mixamo 로그인·다운로드는 사용자 담당. 걷기1동작·1방향의2배 영상/0.05초 시트/관절 확대/100px 미리보기만 먼저 제출한다. 사용자 걷기 승인 전 베기·피격·8방향 확장 금지.

## 제출 표본

[원본과 후보 비교 시트](https://github.com/hyunlord/bs-mobile/releases/tag/m1-5-apose-b-review-v2). 같은 인물 높이900px로 전신을 배치하고 원본 픽셀의 얼굴 확대를 붙였다. 후보 SHA256 `1e43458a2a0b92009bd9576596db42cd596eb98af6598b6086e65fab824c9da4`. 비교용 얼굴·색 보정 없음.

수동 랜드마크(각 입력 이미지 기준): 원본 머리끝y15/턱123/발끝898 → 약8.2등신, 후보12/191/1499 → 약8.3등신. 몇 픽셀의 눈대중 오차가 있으며 자동 정밀 측정이 아니다. 후보는 원화보다 선명하고 파랑/금색 채도가 높고, 부츠·무릎 갑옷과 얼굴 세부가 달라 보인다. 이 차이를 통과로 단정하지 않고 사용자에게 그대로 제출한다.

입력 제출 당시 관문: **부분 — 비교 그림 준비, 사용자 입력 승인 대기**. 당시 (b) 생성 및 공식/대체 GLB 실행 모두 미실행이었다. 이후 v2 입력 승인은 받았으며 메시 품질 승인은 별도다. 게임 실행은 바꾸지 않는다.
