# ADR 0007: 커밋별 지표를 소스와 분리해 영구 보존

상태: 채택, 2026-10-08. 연결 #20.

PR마다 품질·비밀 검사를 필수로 실행한다. 성공한 CI의 지표 JSON은 main의 신뢰된 workflow_run 생성기로 검증하고, metrics-history prerelease 자산으로 누적한다. 데이터만 읽으며 PR 코드를 특권 토큰으로 실행하지 않는다. SHA·실행 환경·설정·seed·시각과 원본을 함께 보존한다. Actions artifact는 90일 보조 증거, release JSON/ZIP은 명시 삭제 전까지 이력이다. HTML의 로컬 원본 링크는 metrics-history.zip을 풀어 연다.

실측 API 확인: gh run download --pattern은 이름별 하위 폴더를 만든다. Actions API에서 정확한 artifact 이름을 얻어 --name으로 내려받으면 지정 폴더에 metrics.json이 존재한다. 이 차이를 실제 CI artifact로 재현하고 수정했다.

S0의 도구 발동·성장 합계는 합성 모델이다. scaffoldDamageCv는 정책별 합성 피해 평균의 변동계수이며 게임 생존율/밸런스가 아니다. balanceDispersion은 S0에서 null을 강제한다. tick p95와 게임 밸런스는 S2/S4에서 별도 모델·시리즈로 이어간다.

소스 트리는 issue→PR→CI로만 변경한다. 지표 생성으로 main 직접 푸시나 재귀 빌드를 만들지 않는다. 실패 CI는 합격 측정으로 축적하지 않고 Actions 실패 로그로 보존한다. 의도적인 위반 PR은 절대 병합하지 않는다.
