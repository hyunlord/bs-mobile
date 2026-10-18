# Unity 테스트 기록 (병합 전 필수)

`unity/`를 바꾸는 PR은 병합 전에 맥에서 두 콘텐츠 프로필의 Unity EditMode·PlayMode 전체를 실행하고, 그 결과를 PR 본문의 `## Unity tests` 절에 남긴다. CI에는 Unity 실행기가 없다(공개 저장소라 맥 러너를 붙이지 않는다, [ADR0068](../adr/0068-mac-unity-test-record-before-merge.md)). 대신 CI의 PR 정책 검사가 이 기록을 확인하고, 없거나 맞지 않으면 병합을 막는다.

## 실행

```sh
git switch <PR 가지>        # 추적 파일 변경, unity/·core/·data/의 추적 안 된 파일이 없어야 한다
./tools/unity-tests.sh
```

스크립트가 하는 일:
1. .NET으로 재생 fixture를 만든다(first-playable·wave-1a·메타).
2. 프로필마다 `prepare-unity.sh`와 `FoundationBuild.Configure`를 실행한 뒤 EditMode·PlayMode 전체를 돌린다.
3. 끝나면 first-playable로 되돌린다.

결과 XML과 로그는 `artifacts/unity-tests/`에 남는다. 마지막에 아래 형식의 기록을 출력한다(`artifacts/unity-tests/record.md`). 실패가 있으면 종료 코드 1이다.

```
## Unity tests
commit: <검사한 커밋 SHA>
first-playable EditMode: passed N, failed 0, skipped N
first-playable PlayMode: passed N, failed 0, skipped N
wave-1a EditMode: passed N, failed 0, skipped N
wave-1a PlayMode: passed N, failed 0, skipped N
```

출력을 그대로 PR 본문 템플릿의 `## Unity tests` 절에 붙인다. 제목이 두 번 나와도 검사는 같은 이름의 절을 모두 합쳐 읽는다. 손으로 숫자를 고치지 않는다.

## CI가 확인하는 것 (`tools/unity-test-record.mjs`)

- PR이 `unity/` 아래 파일을 바꾸면 `## Unity tests` 절이 있어야 한다.
- 네 줄이 모두 있고, 각 `failed`는 0이며 `passed`는 1 이상이어야 한다.
- `commit`은 저장소에 있는 커밋이어야 하고, PR head의 역사 안에 있어야 한다.
- 그 커밋 이후 `unity/` 아래가 바뀌었으면 기록이 낡은 것이다. 다시 실행하고 기록을 바꾼다. `unity/` 밖만 바뀐 뒤라면 기록은 그대로 유효하다. main을 가지에 병합해 `unity/`가 바뀐 경우도 다시 실행한다.
- PR 본문을 고치면(`edited`) CI가 다시 돈다.

## 주의

- 건너뜀(skipped)은 다른 프로필 전용 표시다. 두 프로필을 합치면 각 테스트가 한쪽에서 실행된다.
- 측정·녹화·다른 Unity 작업과 동시에 돌리지 않는다. 타이밍 테스트가 흔들릴 수 있다.
- Unity가 비정상 종료하면 XML이 없어 `missing`으로 기록되고 실패로 처리된다. 원인을 확인하고 다시 실행한다.
- 이 기록은 Mac 에디터 검사다. Android IL2CPP·실기 성능 관문을 대신하지 않는다.
