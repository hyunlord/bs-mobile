# 콘텐츠 검사

필수 환경은 AGENTS.md를 따른다. 저장소 루트에서 실행한다.

```sh
./tools/check.sh
```

이 명령이 빌드·xUnit·형식·스키마·아키텍처·결정론·짧은 리그의 공통 진입점이다. 실패 출력의 파일과 필드를 고친 후 재실행한다. 스키마만 빠르게 검사하려면:

```sh
npm ci --ignore-scripts
npm run validate
```

검증기는 잠금 파일로 고정한 Ajv를 사용한다. `npm test`는 검증기의 음성·양성 사례를 검사한다. 전체 관문은 여전히 `./tools/check.sh`다. 형식만 자동 교정하려면 해당 .NET 솔루션의 `dotnet format`을 쓰되 다른 에이전트 변경까지 임의 수정하지 않는다.

S0 콘텐츠는 `data/tools/`, `data/heroes/`, `data/estates/`, `data/tuning.json`; 계약은 `data/schema/`다. `data/test/`는 명시적 `--include-test` 실행에만 포함한다. 네임스페이스 ID와 참조를 일치시키고, 도구의 activation·growth 양면을 채운다. 수치의 근거는 가설/측정 여부를 밝혀 기록한다.

기본·더미 조합을 따로 관찰하는 실행 예:

```sh
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --output artifacts/base.json --metrics artifacts/base-metrics.json --iterations 3
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --include-test --hero test:scout --estate test:moor --output artifacts/dummy.json --metrics artifacts/dummy-metrics.json --iterations 3
```

결과 JSON의 해시와 실행 설정을 확인한다. S0 계약 검사가 통과해도 S3 수량·태그 분포·진화 참조·반시너지·스킨 관문 전체를 통과했다는 뜻은 아니다. 입력 변경이 결과에 반영되는지도 관찰하고 파일 존재만으로 성공 판정하지 않는다.
