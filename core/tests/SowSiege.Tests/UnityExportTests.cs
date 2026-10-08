using System.Diagnostics;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class UnityExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "unity-export-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");

    public UnityExportTests()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "data");
        foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Data, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public void ManifestMatchesCanonicalHashAndExcludesOnlyTestJson()
    {
        var snapshot = UnityExportSnapshot.Capture(Data);
        Assert.Equal(ContentLoader.Hash(Data, false), snapshot.DataHash);
        Assert.Contains(snapshot.Files, file => file.RelativePath == "profiles/production.json");
        Assert.Contains(snapshot.Files, file => file.RelativePath.StartsWith("experiments/", StringComparison.Ordinal));
        Assert.Contains(snapshot.Files, file => file.RelativePath.StartsWith("schema/", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Files, file => file.RelativePath.StartsWith("test/", StringComparison.Ordinal));
        Assert.Equal(snapshot.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal), snapshot.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void SnapshotRejectsSourceChanges()
    {
        var snapshot = UnityExportSnapshot.Capture(Data);
        File.AppendAllText(Path.Combine(Data, "tuning.json"), "\n");
        Assert.Throws<InvalidDataException>(() => snapshot.AssertUnchanged(Data));
    }

    [Theory]
    [InlineData("unsafe\\name.json")]
    [InlineData("unsafe name.json")]
    public void UnsafeBundlePathsAreRejected(string name)
    {
        File.WriteAllText(Path.Combine(Data, name), "{}");
        Assert.Throws<InvalidDataException>(() => UnityExportSnapshot.Capture(Data));
    }

    [Fact]
    public void SymlinkCannotEnterTheBundle()
    {
        File.CreateSymbolicLink(Path.Combine(Data, "alias.json"), Path.Combine(Data, "tuning.json"));
        Assert.Throws<InvalidDataException>(() => UnityExportSnapshot.Capture(Data));
    }

    [Fact]
    public void ExpressionRejectsUnsupportedTypesAndCycles()
    {
        Assert.Throws<InvalidDataException>(() => UnityExportExpression.Write(DateTime.UtcNow));
        var cycle = new object[1]; cycle[0] = cycle;
        Assert.Throws<InvalidDataException>(() => UnityExportExpression.Write(cycle));
        Assert.Throws<InvalidDataException>(() => UnityExportExpression.Write(new Dictionary<int, int> { [1] = 2 }));
    }

    [Fact]
    public void VerifyRejectsStaleBridgeWithoutRewritingIt()
    {
        var file = Path.Combine(root, "CanonicalContent.g.cs");
        UnityExport.Write(Data, file);
        UnityExport.Verify(Data, file);
        File.AppendAllText(file, "// stale\n");
        var modified = File.ReadAllText(file);
        Assert.Throws<InvalidDataException>(() => UnityExport.Verify(Data, file));
        Assert.Equal(modified, File.ReadAllText(file));
    }

    [Fact]
    public async Task GeneratedConstructorsCompileAndReproduceTheCompleteProductionGraph()
    {
        var weaponFile = Path.Combine(Data, "weapons/iron_blade.json");
        var weapon = JsonNode.Parse(File.ReadAllText(weaponFile))!;
        weapon["growth"]!["levels"]![0]!["damage"] = 23;
        weapon["name"] = "Canonical \"blade\" \n 눈";
        File.WriteAllText(weaponFile, weapon.ToJsonString());
        var source = UnityExport.Generate(Data);
        Assert.DoesNotContain("System.Reflection", source);
        Assert.DoesNotContain("SowSiege.Sim", source);
        File.WriteAllText(Path.Combine(root, "CanonicalContent.g.cs"), source);
        var core = System.Security.SecurityElement.Escape(typeof(ContentCatalog).Assembly.Location);
        File.WriteAllText(Path.Combine(root, "ExportProbe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup><Compile Include="CanonicalContent.g.cs"/><Reference Include="SowSiege.Core"><HintPath>{core}</HintPath></Reference></ItemGroup></Project>
            """);
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "build", "ExportProbe.csproj", "--configuration", "Release", "--nologo" }) { info.ArgumentList.Add(arg); }
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "bin/Release/net8.0/ExportProbe.dll"));
        var type = assembly.GetType("Game.App.Generated.CanonicalContent", true)!;
        var actual = (ContentCatalog)type.GetMethod("CreateCatalog")!.Invoke(null, null)!;
        var expected = ContentLoader.Load(Data, false, "production");
        Assert.Equal(new CanonicalStateHasher().Compute(expected), new CanonicalStateHasher().Compute(actual));
        Assert.Equal(JsonSerializer.Serialize(expected, HostJson.CreateOptions()), JsonSerializer.Serialize(actual, HostJson.CreateOptions()));
        Assert.Equal(ContentLoader.Hash(Data, false), type.GetField("DataHash")!.GetRawConstantValue());
        Assert.Equal(ContentLoader.ProfileHash(Data, "production"), type.GetField("ProfileHash")!.GetRawConstantValue());
        var displays = (Array)type.GetField("Displays")!.GetValue(null)!;
        var blade = Assert.Single(displays.Cast<object>(), item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "core:iron_blade");
        Assert.Equal(weapon["name"]!.GetValue<string>(), blade.GetType().GetProperty("DisplayName")!.GetValue(blade));
        Assert.Equal("weapon", blade.GetType().GetProperty("Category")!.GetValue(blade));
        Assert.Equal(23, actual.Weapons["core:iron_blade"].Activation.Damage);
        var options = new RunOptions(20001, expected.Tuning.DefaultHero, expected.Tuning.DefaultEstate, "random", "A", Movement: "circuit");
        Assert.Equal(SimulationTests.Finish(expected, options).Hash, SimulationTests.Finish(actual, options).Hash);
    }


    [Theory]
    [InlineData("minHalfHeight", 6001)]
    [InlineData("worldUnitsPerUnityUnit", 0)]
    [InlineData("zoomMilliseconds", 60001)]
    public void InvalidPresentationIsRejected(string field, int value)
    {
        var file = Path.Combine(Data, "presentation.json");
        var document = JsonNode.Parse(File.ReadAllText(file))!;
        document["camera"]![field] = value;
        File.WriteAllText(file, document.ToJsonString());
        Assert.Throws<InvalidDataException>(() => UnityExport.Generate(Data));
    }

    public void Dispose() => Directory.Delete(root, true);
}
