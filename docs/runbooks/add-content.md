# 도구·무기 추가

먼저 AGENTS.md와 연결 이슈를 읽는다. 아래는 **S0 새 에이전트 관문용 더미 도구**의 재현 절차다. S0에는 무기 스키마가 아직 없으므로 무기 레코드를 도구로 위장하지 않는다. 무기는 S2/S3 스키마·공통 실행 계약을 만든 이슈와 ADR에서 추가하며, 아래의 이슈→데이터→검사→PR 절차를 그대로 따른다.

## 독립 에이전트 더미 도구 PR

1. main 최신 상태에서 시작한다. `gh repo view --json nameWithOwner`가 `hyunlord/bs-mobile`인지 확인한다. `gh issue create`로 제목 "Validate tool runbook with an independent dummy tool" 이슈를 만들고 `type/content`, `area/data`, `phase/0` 라벨을 붙인다. 본문에 "Core 변경 없이 더미 도구를 데이터로 추가하고 테스트 영웅으로 실행한다"는 관문을 쓴다. 출력된 번호를 아래 `ISSUE`에 넣는다.

```sh
ISSUE=123 # 실제 생성된 이슈 번호로 바꾼다.
git switch -c content/$ISSUE-dummy-tool
mkdir -p data/test/tools
cat > data/test/tools/dummy_rake.json <<'JSON'
{
  "id": "test:dummy_rake",
  "tags": ["land"],
  "activation": { "damage": 1 },
  "growth": { "target": "land", "yield": 2 },
  "floorRationale": "S0 extensibility fixture only; not a measured gameplay balance value.",
  "antiSynergy": []
}
JSON
```

2. `data/test/heroes/`에서 `id`가 `test:scout`인 레코드의 `startingTool`만 새 ID로 변경한다. 아래 명령은 매칭이 정확히 하나인지 확인하며 나머지 필드를 보존한다.

```sh
python3 - <<'PY'
import json
from pathlib import Path
matches = []
for path in Path('data/test/heroes').glob('*.json'):
    record = json.loads(path.read_text())
    if record.get('id') == 'test:scout':
        matches.append((path, record))
assert len(matches) == 1, f'Expected one test:scout, found {len(matches)}'
path, record = matches[0]
record['startingTool'] = 'test:dummy_rake'
path.write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')
PY
./tools/check.sh
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --include-test --hero test:scout --estate test:moor --output artifacts/runbook-dummy.json --metrics artifacts/runbook-dummy-metrics.json --iterations 3
```

3. 결과 JSON은 반복 실행 결과 배열이다. 직접 열어 `heroId`=`test:scout`, `estateId`=`test:moor`, `toolId`=`test:dummy_rake`, `damage`와 `growth`가 모두 양수인지, 세 항목의 `hash`가 동일한지 확인한다. 결과 필드가 누락되거나 입력이 반영되지 않으면 이 관문은 실패다. Core 코드나 생산 영웅·영지를 수정해서 맞추지 않는다. S0 산출은 연기 시험이며 도구의 실제 재미·바닥값·교차 시점을 증명하지 않는다.
4. `git diff --check`와 `git diff --stat`에서 더미 데이터만 바뀌었는지 확인한다. 검증 명령·실제 결과·원자료 위치를 PR에 기록한다. 큰 `artifacts/` 디렉터리를 통째로 커밋하지 않는다.

```sh
git add data/test/tools/dummy_rake.json data/test/heroes
git commit -m 'test(data): prove new tools work without core edits' -m 'Exercise the independent-agent runbook with a test-only tool.' -m 'Scope-risk: narrow'
git push -u origin HEAD
```

5. 아래 본문을 만들고 실제 관측 결과를 기록한다. `ISSUE`는 1단계에서 생성한 실제 번호를 유지한다. CI 정책이 검사하는 영문 제목 세 개를 그대로 둔다.

```sh
cat > /tmp/bs-mobile-dummy-tool-pr.md <<EOF
Closes #${ISSUE}

## Gate result
관문: 실제 check.sh 및 JSON 확인 결과를 기록한다.

## Verification
./tools/check.sh
별도 더미 실행의 heroId/estateId/toolId, damage/growth, 세 hash 확인 결과를 기록한다.

## Screens
해당 없음: 헤드리스 S0 데이터 연기 시험.
EOF
# 위 임시 파일의 결과 설명을 실제 측정으로 채운 뒤 생성한다.
gh pr create --title 'test(data): prove tool extension through the runbook' --body-file /tmp/bs-mobile-dummy-tool-pr.md
```

 `gh pr checks <PR번호> --watch`로 원격 결과를 확인한다. `gh pr merge <PR번호> --auto --squash --delete-branch`로 예약하고 실제 병합 여부를 확인한다. PR 링크·CI run 링크·SHA가 독립 에이전트 관문의 증거다.

## 실제 콘텐츠로 확장할 때

도구는 태그·발동 면·성장 면·바닥값 근거·반시너지를 갖는다. 성장 대상과 주 경로를 명시하고 기존 ID 참조·스키마·P1/P6/P8 검사를 통과시킨다. 무기는 해당 단계 무기 스키마를 사용한다. 새로운 실행 규칙이 필요하면 데이터 추가와 섞어 은밀히 Core에 분기를 넣지 말고 연결 이슈·ADR·검증을 가진 별도 변경으로 만든다. 최종 승인되지 않은 기본 영웅·영지 콘셉트나 확장 후보를 출시 데이터로 추가하지 않는다.
