# 콘텐츠 추가와 실행 프로필

먼저 AGENTS.md와 연결 이슈를 읽는다. `gh repo view --json nameWithOwner`가 `hyunlord/bs-mobile`인지 확인한 뒤 이슈 → 가지 → 데이터·검사 → PR → CI 순서로 진행한다. 아래는 S3 계약 기준이다. S0 독립 에이전트의 실제 더미 추가 증거는 [S0 보고서](../review/S0-report.md)와 당시 ZIP에 보존한다.

## 설계 후보

같은 종류의 정본 레코드와 `data/schema/<kind>.schema.json`을 함께 읽는다. 새로운 ID, 이름, 구별되는 개념과 비용, 태그, `candidate` 상태, 구현 한계, 정직한 순환 연결을 작성한다. 도구는 발동 형태·설계 피해 계수와 성장 대상·산출·주 경로·바닥값 근거를 함께 갖는다. 반시너지 ID와 설명은 일대일 대응시킨다.

S3 정본 수량은 의뢰서의 고정 관문이다. 수량 변경은 별도 이슈에서 범위를 명시하고 검증 계약·ADR도 갱신한다. 실험용 레코드는 `data/test/`에 두어 정본 수량을 임의로 늘리지 않는다. 후보는 실행 프로필에 넣지 않고 CI에서 전체 구조·참조를 검사한다.

## Core 변경 없는 새 도구 실험

기존 공통 발동·성장 규칙으로 표현할 수 있는 실험은 별도 프로필과 테스트 레코드로 실행한다. 기존 `s2-baseline`과 골든 해시를 바꾸지 않는다. 아래 예시는 레코드의 기존 필드를 복사하고 ID 참조만 바꾸므로 누락된 필수 필드를 만든 예제가 아니다.

```sh
node --input-type=module <<'JS'
import fs from 'node:fs';
const read = (p) => JSON.parse(fs.readFileSync(p));
const write = (p, value) => {
  if (fs.existsSync(p)) throw new Error(`Already exists: ${p}`);
  fs.writeFileSync(p, JSON.stringify(value, null, 2) + '\n');
};
const tool = read('data/test/tools/dummy_rake.json');
tool.id = 'test:extension_rake';
tool.name = '확장 검사용 갈퀴';
tool.concept = '기존 공통 공격과 경작 규칙만 사용하는 데이터 확장 시험.';
write('data/test/tools/extension_rake.json', tool);
const hero = read('data/test/heroes/scout.json');
hero.id = 'test:extension_scout';
hero.name = '확장 검사용 영주';
hero.startingTool = tool.id;
write('data/test/heroes/extension_scout.json', hero);
const profile = read('data/profiles/s2-baseline.json');
profile.id = 'test:extension_check';
profile.name = '기준 프로필을 보존하는 확장 시험';
profile.testSelection.tools.push(tool.id);
profile.testSelection.heroes.push(hero.id);
write('data/profiles/extension-check.json', profile);
JS
./tools/check.sh
dotnet run --project core/src/SowSiege.Sim -- --data data --profile extension-check --seed 42 --policy mixed --include-test --hero test:extension_scout --estate test:moor --output artifacts/extension.json --metrics artifacts/extension-metrics.json --iterations 3
```

출력 배열의 영주·영지·시작 도구, 프로필 ID·해시·선택 ID를 실제로 확인한다. 반복 상태 해시 세 개가 같아야 하고 새 도구의 직접 피해와 성장 산출을 원자료에서 확인한다. 프로필만 추가했다고 실행 효과가 생기는 것은 아니다. 새로운 고유 규칙이 필요한 후보는 해당 규칙의 구현 이슈·ADR·행동 검증이 먼저 필요하며 `s2-runtime`으로 위장하지 않는다.

## 검사와 PR

```sh
node tools/validate-content.mjs
node tools/content-report.mjs
node tools/content-report.mjs --check
./tools/check.sh
git diff --check
```

정본 후보 표는 테스트 레코드를 수량에서 제외한다. 원자료 전체를 무분별하게 커밋하지 말고 검증에 필요한 로그·요약을 증거 경로에 보존한다. 커밋 메시지는 Lore 규약을 사용한다. PR 제목은 `feat(data): ...` 등 Conventional Commits 형식이며 본문에는 실제 이슈와 다음 세 제목이 필요하다.

```markdown
Closes #실제이슈번호

## Gate result
실측한 관문 결과와 미검증 범위.

## Verification
명령, 원자료, 상태 해시와 참조 검사 결과.

## Screens
헤드리스 데이터 변경이면 해당 없음과 검토 문서 경로.
```

`gh pr checks <번호> --watch`로 원격 결과를 확인한 뒤 `gh pr merge <번호> --auto --squash --delete-branch`를 사용한다. Core 변경 없이 확장했다고 보고하려면 실제 diff와 새 프로필 실행을 모두 확인한다.
