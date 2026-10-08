# ADR 0028: Universal 2D 인스턴스 도형 표시

- 상태: 채택
- 관련: #89, ADR 0018, ADR 0026

## 결정

Universal 2D Renderer를 유지하고 재사용 메시·재질·511개 이하 인스턴스 묶음으로 세계 도형을 제출한다. 적·사람·효과마다 GameObject를 만들지 않는다. 공격의 위치·방향·범위·관통 결과는 Core가 제공한 사건을 표시하며 Unity에서 피해나 충돌을 다시 계산하지 않는다.

6000.6.4f1의 실제 GPU 시험에서 카메라를 지정한 `Graphics.RenderMeshInstanced` 제출은 보이지 않았고, 같은 메시·셰이더의 일반 MeshRenderer는 보였다. 명시적 경계 확대와 다른 셰이더도 해결하지 못했으며 카메라 필터를 null로 바꾼 제출은 보였다. 따라서 [RenderParams.camera의 문서화된 null 동작](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/RenderParams-camera.html)을 사용한다. 이를 Unity 전체 버전의 결함이라고 일반화하지 않는다.

현재는 세계 카메라 하나와 Screen Space Overlay UI다. 세계 레이어는0이며 카메라의 해당 레이어 컬링을 유지한다. null은 모든 적합한 카메라에 그린다는 뜻이므로 보조 세계 카메라·미니맵·카메라 스택을 추가할 때 이 경계를 다시 검토해야 한다. Built-In이나 3D Renderer로 전환하지 않는다.

프로젝트의 Linear 색 공간에서는 Vector4 인스턴스 색에 sRGB 토큰을 그대로 넣으면 밝아진다. 색 공간에 따라 RGB를 변환하고 alpha는 유지한다. 실제 픽셀 검사로 서로 다른 인스턴스 색·반투명 혼합·적의 지정 색을 검증한다. 제출 개수 검사만으로 실제 렌더 성공을 주장하지 않는다.

## 한계

Editor GPU 출력과 Android IL2CPP GPU 출력은 별도 증거다. 화면 비율 변경은 물리 접기·펼치기 검증을 대신하지 않는다. 최대 개체 구간의 실제 프레임 성능은 기기 CSV로 판정한다.

참고: [Graphics.RenderMeshInstanced](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html).
