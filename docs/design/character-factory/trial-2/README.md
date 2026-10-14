# 원본 메시 분할 레시피 · 2차 실패 후보

[실제 결과와 중단](../../../review/m1-5/modular-factory-2.md). [1차 공장](../README.md)의 기본 몸·레시피·부품·UAL 조립 구조를 유지하되, 보이는 몸과 갑옷은 승인(b) 메시를 분할한 표면으로 바꾼다. 원본 몸을 숨기므로 기본 몸의 전문가 가중치를 보이는(b) 메시의 가중치라고 표현하지 않는다.

`a-knight-shell.json`의 version2는 원본·정렬용 rest·각 영역의 뼈·원본 옷 분리 경계·겹침·좁은 혼합 띠·망토 원화 패치를 소유한다. version1과 입력 형태가 다르며 첫 시도는 그대로 보존한다. 원본 메시의 삼각형과 UV를 잘라 부품으로 조립하고, 원본 텍스처를 다시 그리거나 감량 정점색으로 바꾸지 않는다.

25mm 겹침은 원본 표면을 자르는 공간의 설정값이다. 이번 구현은 부품별 정렬 후 연결을 보장하지 못해 실패했다. 모든 캐릭터를 자동으로 성공시키는 제작기로 표현하지 않는다. 정렬용 rest 뼈대도 별도 입력이며 임의 원화를 자동 해부학 정렬하는 기능은 없다.

## 외부 저작 소스

Python은 저장소에 넣지 않는다(RB003). 실패 후보 Release에 개별 파일로 보존하고 `source-manifest.csv`의 경로·SHA로 확인한다. 원본 GLB·기본 몸·UAL·정렬용 rest는 로컬의 승인 입력이며 Release에 모델을 재게시하지 않는다. 망토 원화 입력은 로컬 아트 체크아웃 `/Users/rexxa/bs-mobile/docs/design/gdd/hero-concepts-v1/A-knight.png`(SHA `b6f834a6cce6a73db14373cffd68b3cd90bff10c6aa4e3e13078b68e150cf9d5`)를 레시피가 지정한 외부 경로에 복사한다. 이 문서 가지에는 원화 PNG가 없으므로 새 체크아웃만으로 모든 원본 입력이 마련됐다고 표현하지 않는다.

프레임을 다시 생성할 때도 실패 동결본을 덮어쓰지 않는 별도 출력 디렉터리를 사용한다. 추가 제작·부품 보정·유료 모델 전환은 새 사용자 결정 전까지 중단한다.

[실패 Release](https://github.com/hyunlord/bs-mobile/releases/tag/m1-modular-factory-2)의 `factory--*.py`, `review--*.py`를 [manifest](source-manifest.csv)의 원래 경로로 복원한다. `review--README.md`에 실행 명령과 외부 입력 제약을 기록했다. 레시피는 이 디렉터리의 JSON을 외부 작업 폴더 `recipes/a-knight-shell.json`에 복사한다. 스크립트만 내려받아도 원본 모델이 함께 제공되는 것은 아니다.
