# 봇 리그 실행·해석

최초 실행은 `./tools/check.sh`로 Release 빌드를 만든다. 이후 저장소 루트에서:

```sh
./tools/league.sh
```

직접 재현할 때:

```sh
dotnet run --project core/src/SowSiege.Sim -- --data data --seed 42 --policy mixed --output artifacts/smoke.json --metrics artifacts/metrics.json --iterations 3
```

S0 출력은 **synthetic scaffold**다. 실제 30Hz 지도·성장·전투가 구현되는 S2와 콘텐츠 기반 S4 전에는 생존·점수·정책 차이를 재미나 빌드 균형의 근거로 삼지 않는다. 같은 seed·설정·정책의 세 실행은 결정론 검사이며 독립 표본 세 개가 아니다.

원자료와 함께 commit SHA, 콘텐츠 버전, seed 목록, 정책, 사람 규칙, 실행 환경, 측정 반복 수를 보관한다. 시간축 HTML은 커밋별 지표 JSON을 소비한다. 틱 p95·리그 분포를 과거 실행과 비교하되 기계·빌드·설정이 다른 측정치를 동일 성능 변화로 단정하지 않는다. 측정 경로가 제외하는 I/O·화면·Unity 비용도 명시한다.

S4에서는 정책×A/B/C×seed별 원본 CSV에서 보고 수치를 재생성한다. 생존율·도달 시간·경험치 출처·시간별 도구/무기 피해·체류율·사망 원인을 비교한다. 모든 조건에서 한 정책이 1위면 실패라고 쓰고 원인 가설 세 개를 낸다. 식량·자재 보유량 자체를 점수로 보상하지 않는다.

DGX는 S4 대량 리그에만 별도 폴더·낮은 우선순위로 사용하며 다른 프로젝트 관문을 방해하면 즉시 중단한다. 공개 저장소의 Actions 자체 호스팅 러너로 연결하지 않는다.
