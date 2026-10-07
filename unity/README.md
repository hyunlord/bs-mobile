# Unity 경계 예약

Unity 프로젝트는 1단계 회색 상자에서 생성한다. 현재 디렉터리에는 이 문서만 둔다. S0~S5는 Unity 라이선스나 에디터 없이 .NET 8에서 검증한다.

추후 Unity 어댑터는 입력·표시·플랫폼 서비스를 맡고 순수 C# Core를 소비한다. 엔진 타입·시각·SDK를 Core에 역참조시키지 않는다. LTS 고정, Force Text, LFS, asmdef 경계, GameCI 및 CLI 채택은 S0 기반 조사와 후속 ADR을 확인한 뒤 적용한다. 광고·결제·분석·크래시 SDK는 2단계 전 운영 스택 조사 대상이다.
