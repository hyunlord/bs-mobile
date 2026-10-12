# 10 브랜드·현지화

- 버전: v1.0 · 날짜: 2026-10-12
- 바뀐 점: 제목의 미정 상태, M1 ko/en 분리 계약과 시작 SHA의 한글 화면 문자열 실측을 기록한다.
- 상태: 정본 — 브랜드 이름·영문 번역은 [미정], 문자열 분리 구현은 M1 후속.
- 근거: [#236](https://github.com/hyunlord/bs-mobile/issues/236), [기존 시스템](../08_system-design-v1.md), [휴경의 왕국 A](../world-directions/A-fallow-kingdom.md), [화면 원본](../portrait-v0/README.md).

## 제목과 용어

제품명 최종 결정은 보류한다. 현재 타이틀의 **휴경의 왕국** 로고는 유지하며, `Sow & Siege`는 저장소·배포 식별에 쓰는 작업명이다. 최종 이름에는 한국어·영어 이름을 함께 정한다. 작업명을 최종 영어 제목으로 확정하거나 로고를 번역하여 교체하지 않는다. 기존 문서의 `씨앗과 공성`, `새싹 들판` 등 역사 표기는 원본에 남기고 현재 화면 용어와 구별한다. 챕터1의 현재 세계 표현은 **새봄의 터**다.

콘텐츠의 `core:*` ID와 `designRef`는 번역 대상이 아니다. 무기·도구·진화·가신 이름도 표시 이름만 언어별로 바꾸고 저장·해시의 식별자는 보존한다. 번역에서 실험 수치나 가격 `[n]`을 채우지 않는다.

## M1부터의 문자열 계약

[확정] 화면 문자열은 M1부터 **안정된 키 + ko/en 표**로 분리한다. 이 문서화 PR은 표·로더·UI 교체를 구현하지 않는다. 후속 구현 PR에서 문자열 분리와 관련 GDD를 함께 갱신한다.

| 열 | 계약 |
|---|---|
| key | 문구·배치가 바뀌어도 유지하는 화면 의미 키. 콘텐츠 ID와 별도 |
| ko | 현재 한국어 정본. 가격·미정 수치는 `[n]` 유지 |
| en | 검수된 영어. 미번역은 명시하고 출시 완료로 세지 않음 |
| parameters | `count`, `weapon`, `tool` 등 명명된 값의 타입·허용 범위 |
| context | 화면, 화자, 길이·줄바꿈·공간 제약, 관련 콘텐츠 ID |

언어마다 문장 순서가 다르므로 `"레벨 " + 숫자` 같은 조각을 직역하지 않고 완성 문장 템플릿으로 합친다. 동적 값이 바뀔 때만 포맷·HUD를 갱신하며, 언어 전환·폰트 사전 준비는 판 중 무제한 할당 경로로 만들지 않는다. 기존 소리·진동·화면 흔들림·피해 숫자 설정과 저장·리플레이 계약은 변경하지 않는다. 영어 장문·한국어 줄바꿈·390×844 및 긴 화면 안전 영역은 별도 시각 검증 대상이다. 테이블 파일 위치·로더 구체 방식·언어 전환 제공 시점은 [미정]이다.

## 시작 시점 문자열 재고

시작 main은 **9387b7669bdc3d01790246509ad8d66f212af0ad**다. [정본 CSV](../../metrics/gdd-236-screen-strings.csv)는 이 SHA에서 추출한 위치·토큰 종류·원문·제외 사유를 보존한다.

| 범위와 단위 | 수 |
|---|---:|
| `unity/Assets/Game/App/` + `View/`의 `.cs`, 한글 음절 포함 문자열 토큰 전체 | 519 |
| 화면 문자열 토큰 출현 수(같은 문구가 다른 위치에 있으면 각각) | **476** |
| 위 476개 중 `ValueText`가 정확히 같은 것을 합친 고유 조각 | **332** |
| 화면 문자열 토큰이 남은 파일 | 21 |
| 제외: 자동 조작의 버튼 검색 문자열 | 24 |
| 제외: 폰트·텍스트 워밍업 문자 집합/표본 | 16 |
| 제외: 캡처 출처·진단 설명 | 2 |
| 제외: GameObject 이름 | 1 |

**476은 완성된 문장이나 향후 번역 키의 수가 아니다.** C# 일반 문자열 토큰 또는 보간 문자열의 한글 포함 텍스트 토큰 하나를 한 출현으로 센다. 예를 들어 보간식 양옆의 한글 조각은 두 개이며, 같은 문자열의 재사용은 위치별로 센다. 각 CSV 행을 확인하여 화면 텍스트와 비표시 용도를 분리했다. 자동 도달 가능성·기본 프로필에서의 노출 빈도를 세지는 않으므로 역사 화면·설정·오류 화면도 포함한다.

주석·문자 리터럴·한글 없는 문자열·식 내부 숫자는 제외한다. Tests/Editor, Core, Host, JSON 데이터의 표시 이름, 이미지에 구운 문자, 영어 전용 문구는 이 범위 밖이다. 따라서 **전체 게임의 모든 하드코딩 문자열 수/번역 작업량을 이 숫자로 주장하지 않는다.** Unicode 한글 음절 U+AC00–D7A3가 기준이며 자모·이스케이프 표기 문자열의 확장은 후속 전수 조사 사항이다.

## 재현 방법

저장소 루트에서 .NET8 SDK에 포함된 Roslyn만 사용한다. NuGet 패키지를 추가하지 않는다. 다음은 `/tmp`에서 실행할 문서용 조사이며 제품 코드나 빌드에는 넣지 않는다. `dotnet`이 PATH에 없으면 설치 경로를 사용한다.

```sh
mkdir -p /tmp/gdd236-inventory
cat > /tmp/gdd236-inventory/inventory.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <Reference Include="Microsoft.CodeAnalysis"><HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.dll</HintPath></Reference>
    <Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference>
  </ItemGroup>
</Project>
XML
cat > /tmp/gdd236-inventory/Program.cs <<'CS'
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
const string sha="9387b7669bdc3d01790246509ad8d66f212af0ad";
if(args.Length==2) {
    string[][] Rows(string path) {
        using var p=new Microsoft.VisualBasic.FileIO.TextFieldParser(path);
        p.TextFieldType=Microsoft.VisualBasic.FileIO.FieldType.Delimited;
        p.SetDelimiters(",");p.HasFieldsEnclosedInQuotes=true;p.TrimWhiteSpace=false;
        var rows=new List<string[]>();p.ReadFields();
        while(!p.EndOfData)rows.Add(p.ReadFields());return rows.ToArray();
    }
    var raw=Rows(args[0]);var ledger=Rows(args[1]);
    if(raw.Length!=519||ledger.Length!=519||
        raw.Where((r,i)=>!r.SequenceEqual(ledger[i].Take(6))).Any())
        throw new Exception("Inventory differs from pinned source");
    var screen=ledger.Where(r=>r[6]=="screen-text").ToArray();
    if(screen.Length!=476||screen.Select(r=>r[5]).Distinct().Count()!=332)
        throw new Exception("Classification count differs");
    Console.WriteLine("519 raw / 476 screen token occurrences / 332 unique fragments");
    return;
}
string Git(params string[] args) {
    var p=new Process{StartInfo=new ProcessStartInfo("git"){
        RedirectStandardOutput=true,UseShellExecute=false}};
    foreach(var a in args)p.StartInfo.ArgumentList.Add(a);
    p.Start();var result=p.StandardOutput.ReadToEnd();p.WaitForExit();
    if(p.ExitCode!=0)throw new Exception("git failed");return result;
}
string Csv(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
var files=Git("ls-tree","-r","--name-only",sha,
    "unity/Assets/Game/App","unity/Assets/Game/View")
    .Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(f=>f.EndsWith(".cs"));
Console.WriteLine("source_sha,file,line,column,token_kind,text");
foreach(var file in files) {
    var tree=CSharpSyntaxTree.ParseText(Git("show",sha+":"+file));
    foreach(var t in tree.GetRoot().DescendantTokens()) {
        if(!(t.IsKind(SyntaxKind.StringLiteralToken)||
            t.IsKind(SyntaxKind.InterpolatedStringTextToken))||
            !t.ValueText.Any(c=>c>='가'&&c<='힣'))continue;
        var p=t.GetLocation().GetLineSpan().StartLinePosition;
        Console.WriteLine(string.Join(",",new[]{sha,file,
            (p.Line+1).ToString(),(p.Character+1).ToString(),
            t.Kind().ToString(),t.ValueText}.Select(Csv)));
    }
}
CS
dotnet run --project /tmp/gdd236-inventory --verbosity quiet > /tmp/gdd236-raw.csv
dotnet run --project /tmp/gdd236-inventory --verbosity quiet -- /tmp/gdd236-raw.csv docs/metrics/gdd-236-screen-strings.csv
```

제외 판단은 CSV의 `exclusion_reason`에 남겼으며, 재현 시 해당 SHA의 원문 위치에서 용도를 다시 확인할 수 있다.

## 대조한 모순·보류

- 옛 제목과 현재 제목이 코드에 공존한다. 최종 브랜드가 정해지기 전에 역사 문자열을 이번 PR에서 일괄 교체하지 않는다.
- ADR0050의 “실험 모두 기본OFF”는 후속 설정 이관으로 일부 변경됐다. 문자열 분리와 함께 기본값을 되돌리지 않는다. 현황은 [11 기술](11-technology.md)을 따른다.
- M1 번역 표 분리 승인은 번역 품질이나 모든 화면의 영어 지원이 완료됐다는 뜻이 아니다.
