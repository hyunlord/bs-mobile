# First Playable 아트 출처와 사용 근거

기록 기준: 2026-10-09. 아래 아트는 이 프로젝트를 위해 **내장 `image_gen` 이미지 생성 도구**로 만들었다. 도구 이름은 생성 모델명이 아니며, 확인되지 않은 모델명은 기록하지 않는다. 외부 스톡 아트나 CC0 자산으로 분류하지 않는다.

## 사용 근거

적용되는 OpenAI 약관은 법이 허용하는 범위에서 OpenAI가 보유한 출력 권리를 사용자에게 이전한다. 공식 도움말은 약관·정책을 준수하는 출력의 상업적 사용을 설명한다. 이 근거가 결과의 독점성이나 제3자 권리 비침해를 보증하지는 않는다. 생성 아트를 사람이 직접 그린 원화라고 표시하지 않는다. [Terms of Use](https://openai.com/policies/terms-of-use/), [상업적 사용 설명](https://help.openai.com/en/articles/6783457-what-is-chatgpt). 비즈니스 서비스 계약이 적용되는 계정은 해당 계약의 출력 조항을 따른다. [Services Agreement §4](https://openai.com/policies/services-agreement/)

프롬프트의 화풍·배치 지시는 실제 생성 출처 기록이며, 투명도·칸별 분리·역할 판독성이 통과했다는 증거는 아니다. 수정본은 앞선 프로젝트 생성본의 배치·배경 수정 또는 스타일 참조로 만들었다. 선택된 원본의 알파를 코드로 지우거나 재가공하지 않았다. 투명도 문제는 이미지 생성 도구의 재생성으로 해결하고, 사용하지 않을 불합격 영역은 정확한 채택 사각형에서 제외하거나 별도 생성한 대체 스프라이트로 연결했다.

## 같은 장면의 화풍 후보

아래 경로는 저장소 루트 기준 로컬 원자료 위치다. 후보 이미지는 실제 플레이 화면이 아닌 비교용 콘셉트다. 후보 1을 채택하고, 후보 2·3은 비교 기록으로 보존한다.

|후보|생성 이미지|프롬프트 원본|선택|
|---|---|---|---|
|1 잉크·과슈 동화풍|`artifacts/phase1b/m2-candidates/candidate-01-storybook.png`|`artifacts/phase1b/m2-candidates/candidate-01-prompt.txt`|채택 화풍|
|2 픽셀풍|`artifacts/phase1b/m2-candidates/candidate-02-pixel.png`|`artifacts/phase1b/m2-candidates/candidate-02-prompt.txt`|비교용|
|3 목판화풍|`artifacts/phase1b/m2-candidates/candidate-03-woodcut.png`|`artifacts/phase1b/m2-candidates/candidate-03-prompt.txt`|비교용|

## 가져온 아틀라스

**상태: 선택 영역의 원본 알파 0 여백 검사와 11개 UnityArtPreparation 가져오기 검사 통과. 실제 월드 GPU 표시 QA는 대기 중이며, M2 전체 통과를 뜻하지 않는다.** [아트 manifest](../../unity/Assets/Art/first-playable-manifest.json)는 345개 역할과 329개 바인딩(16개 UI 포함)을 정의한다. 전체 PNG의 모든 영역을 채택했다는 의미는 아니다.

선택 프롬프트 사본은 아래 링크에 보존한다. 이전 생성본과 비교 후보 원자료는 로컬 `artifacts/phase1b/`에 있으며, 모든 과거 프롬프트를 저장소에 포함했다고 주장하지 않는다.

|정본 PNG|선택 생성본|정확한 프롬프트 사본|용도·수정 맥락|
|---|---|---|---|
|[actors.png](../../unity/Assets/Art/actors.png)|actors v2|[actors-atlas-v2-prompt.txt](prompts/actors-atlas-v2-prompt.txt)|영웅·가신·일꾼·적; v1 배치 수정|
|[crops-people.png](../../unity/Assets/Art/crops-people.png)|crops-people v1|[crops-people-atlas-v1-prompt.txt](prompts/crops-people-atlas-v1-prompt.txt)|성장 단계 작물·사람 역할|
|[buildings.png](../../unity/Assets/Art/buildings.png)|buildings v2|[buildings-atlas-v2-prompt.txt](prompts/buildings-atlas-v2-prompt.txt)|건설·완성·손상·폐허; v1 여백 확대|
|[objects.png](../../unity/Assets/Art/objects.png)|objects v2|[objects-atlas-v2-prompt.txt](prompts/objects-atlas-v2-prompt.txt)|상인·성소·수레·상자·경계·계절 지면; v1 배치 수정|
|[equipment.png](../../unity/Assets/Art/equipment.png)|equipment v2|[equipment-atlas-v2-prompt.txt](prompts/equipment-atlas-v2-prompt.txt)|무기·도구 아이콘·타격 재료; v1 배치 수정|
|[items-a.png](../../unity/Assets/Art/items-a.png)|items-a v3|[items-a-atlas-v3-prompt.txt](prompts/items-a-atlas-v3-prompt.txt)|장신구 첫 묶음; 투명 컷아웃 스타일 재생성|
|[items-b.png](../../unity/Assets/Art/items-b.png)|items-b v1|[items-b-atlas-v1-prompt.txt](prompts/items-b-atlas-v1-prompt.txt)|장신구 둘째 묶음·문양|
|[charters-evolutions.png](../../unity/Assets/Art/charters-evolutions.png)|charters-evolutions v2|[charters-evolutions-atlas-v2-prompt.txt](prompts/charters-evolutions-atlas-v2-prompt.txt)|칙령·진화·경고 문양; v1 배치 수정|
|[fx.png](../../unity/Assets/Art/fx.png)|fx v2|[fx-atlas-v2-prompt.txt](prompts/fx-atlas-v2-prompt.txt)|무기·도구·수확·경험치 효과; v1 방향·형태·여백 수정|
|[repair.png](../../unity/Assets/Art/repair.png)|repair v1|[repair-atlas-v1-prompt.txt](prompts/repair-atlas-v1-prompt.txt)|불합격 건물·아이템·UI 영역을 대신하는 별도 생성본|
|[ui.png](../../unity/Assets/Art/ui.png)|ui v1|[ui-atlas-v1-prompt.txt](prompts/ui-atlas-v1-prompt.txt)|패널·버튼·카드·게이지·조이스틱|

배치 수정본은 같은 이름의 직전 v1 생성 아틀라스를 입력으로 사용해 정체성·순서를 유지하도록 요청했다. `items-a v3`는 앞선 불합격 배경 제거본의 채택이 아니라 투명 컷아웃 참조를 이용한 재생성이다. `repair v1`은 원본 PNG 청소 작업이 아니라 필요한 12개 대체 대상을 새로 생성한 아틀라스다. 프롬프트의 요구사항과 최종 채택 영역 검증 결과는 구분한다.

## 폰트와 오디오

현재 `FontProvider`는 운영체제에 설치된 폰트를 런타임에 참조한다. Apple 폰트 바이너리를 프로젝트에 복사하는 방식이 아니다. macOS의 Apple SD Gothic Neo와 Android의 Noto/Gothic 후보는 선택 우선순위이며, 실제 선택 폰트는 기기별로 달라질 수 있다. 최종 패키지의 폰트 파일 미포함 여부는 패키징 검사에서 확인한다. [Unity 6.6 OS 폰트 API](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Font.CreateDynamicFontFromOSFont.html)

M3에서 프로젝트 원본 합성 효과음7개와 반복 음악1개를 제작했다. 저작 소스·사용 근거·PCM 검사와 실제 Editor 출력 검증 범위는 [오디오 기록](assets.md)에 별도로 남겼다. 외부 음원이나 CC0 파일을 사용하지 않았다.
