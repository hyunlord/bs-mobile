# 실제 게임 데이터 진입점

정본은 `data/profiles/production.json`과 `data/tuning.json`이다. 생산 프로필은 선택된 무기·도구·적·영웅·영지, 런타임 효과와 적/경험치 규칙을 묶는다. 다음 명령은 한 판의 Core 실행이며 화면·기기 검증이 아니다.

```sh
PATH="$HOME/.dotnet:$PATH" dotnet run --project core/src/SowSiege.Sim -- \
  --data data --profile production --seed 20000 --policy weapon --people-rule C \
  --movement circuit --iterations 3 --output artifacts/production-example.json \
  --metrics artifacts/production-example-metrics.json
```

네 기본 무기의 성장 수치는 `data/weapons/<무기>.json`의 `growth.levels` 한 곳에서 편집한다. 숫자 `activation`을 다시 넣으면 검사에서 실패한다. 레벨1을 바꾸면 파생되는 Core 기본 activation도 같이 바뀐다. 성장표의 존재와 프로필의 전투 기능 활성화는 별개이며, 역사 프로필의 동작을 자동으로 바꾸지 않는다.

`data/experiments/`는 과거·실험 입력이다. 기존 CLI 기본 `s2-baseline`은 역사 기준 튜닝을 명시적으로 사용한다. `weapon-growth-79` 등 과거 이름 프로필은 호환 실행을 위해 유지하며 새 Unity 플레이의 기본으로 선택하지 않는다. 원래 출처 해시까지 같은 재현은 기록된 커밋을 별도 체크아웃하고 당시 명령을 사용한다.

```sh
npm run validate
npm test
./tools/check.sh
```

스키마와 호스트가 같은 계약을 검사해야 한다. 새로운 ID·규칙은 별도 이슈/ADR/행동 검증을 거치며 이 문서는 밸런스 재조정이나 리그 확대를 승인하지 않는다. 자세한 이전 방식과의 경계는 [ADR0023](../adr/0023-production-data-and-experiment-boundary.md)를 따른다.
