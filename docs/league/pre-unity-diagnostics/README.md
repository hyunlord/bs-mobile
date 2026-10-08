관문: 통과 — 동결 소스에서 864조건×3반복, 역사 control288개, 선택16사례×3반복 검증; compact 정본4,349,959bytes.

실행 소스는 `b067c3a7cc944c09f6117af432efcad13bb92104`이다. R3활성 people/weapon×ABC×32seed×4조건=768사례와 역사R2weapon control96사례를 포함한다. 반복3회는 결정론 검사이며 표본 수를 늘리지 않는다. 생존 효과는32개 대응seed블록으로 해석한다. 실제 진단 결과 해석은 [결과 보고서](../../review/pre-unity-diagnostics.md)에 둔다.

이 폴더의 CSV16개와 SCHEMA.txt는 로컬 canonical CSV13개에서 생성한 정본이다. 실제 전체1971개 정규격자 집계 표본을 보존하며 행을 예산 때문에 버리지 않았다. 생성된17파일의 합계는4,349,959bytes이다. 작은 selection.csv는 추가된 선택 재실행 증명으로, 같은16사례의 gameplay/diagnostic 해시와 출처가 원실행과 같음을 기록한다. README와selection.csv는 위 compact 크기에 포함하지 않는다.

재생성: `node tools/diagnostic-compact.mjs INPUT_CSV_DIR NEW_OUTPUT_DIR`. 검사: `node --test tools/test-diagnostic-*.mjs`. 입력파일 SHA는 source-csv.csv, 전체 실행의 사례별 packetSHA/inputHash는 runs.csv, 공유 소스·입력·DLL 출처는 provenance.csv에 있다. 입력 원본과 선택 상세 원자료의 보관·공개 범위는 [런북](../../runbooks/run-diagnostics.md)을 따른다.

- runs는 모든 사례의 결과와 식별자를 보존한다. 상세행의 caseId를 여기에 join한다. provenanceId는 공유tuple이며 서로 다른 inputHash를 동일 입력으로 취급하지 않는다.
- terminal-counters/target-damage/intercept-target-kinds는 격자 밖 사망까지 실제 종료 계측을 보존한다. attack-sources/population은 모든 관련 원본 수치를 유지한다.
- snapshots는 정규300틱 생존자 표본의 수치 합계다. nAlive/nDead/nAtRisk/nObserved를 함께 읽는다. 빈값은 미관측이며0은 관측값이다. sum_threat_nearLordRadius 등 상수도 원본 합계이므로 개별값으로 오독하지 않는다.
- weapon-equipment와weapon-card-summary는 무기별 실제 보유rank 및 제안·선택 횟수·시점·rarity횟수를 담는다. rank증분과피해의 인과효과를 추정하지 않는다.
- paired/interactions는 동일seed 대응 총효과와2×2상호작용이다. 피해억제량은실제피해에합산하지 않으며 가로채기횟수는막은Lord피해가 아니다.

전체 카드 chronology, 도구·헌장 장비 상세, 모든 성공packet은 로컬에 남는다. 따라서 이 자료는 전체 원본의 무손실 압축이 아니다. 선택 원자료 공개는 기본8+추가8=16사례 한도를 지킨다. 수치 보정·전체 정책 순위 재판정·Unity/IL2CPP·모바일 조작 검증은 이 정본 생성의 범위가 아니다.
