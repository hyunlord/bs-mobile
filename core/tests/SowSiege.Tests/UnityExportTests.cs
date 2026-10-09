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
        Assert.Throws<InvalidDataException>(() => UnityExportExpression.Write((MetaAbility)int.MaxValue));
        Assert.Equal("global::SowSiege.Core.MetaAbility.Attack", UnityExportExpression.Write(MetaAbility.Attack));
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

    [Theory]
    [InlineData("production")]
    [InlineData("first-playable")]
    public async Task GeneratedConstructorsCompileAndReproduceTheCompleteProfileGraph(string profile)
    {
        var weaponFile = Path.Combine(Data, "weapons/iron_blade.json");
        var weapon = JsonNode.Parse(File.ReadAllText(weaponFile))!;
        weapon["growth"]!["levels"]![0]!["damage"] = 23;
        weapon["name"] = "Canonical \"blade\" \n 눈";
        File.WriteAllText(weaponFile, weapon.ToJsonString());
        var source = UnityExport.Generate(Data, profile);
        Assert.DoesNotContain("System.Reflection", source);
        Assert.DoesNotContain("SowSiege.Sim", source);
        File.WriteAllText(Path.Combine(root, "CanonicalContent.g.cs"), source);
        UnityExport.Verify(Data, Path.Combine(root, "CanonicalContent.g.cs"), profile);
        Assert.Throws<InvalidDataException>(() => UnityExport.Verify(Data, Path.Combine(root, "CanonicalContent.g.cs"), profile == "production" ? "first-playable" : "production"));
        var core = System.Security.SecurityElement.Escape(typeof(ContentCatalog).Assembly.Location);
        File.WriteAllText(Path.Combine(root, "ExportProbe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion><AssemblyName>ExportProbe-{profile}</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
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
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, $"bin/Release/net8.0/ExportProbe-{profile}.dll"));
        var type = assembly.GetType("Game.App.Generated.CanonicalContent", true)!;
        var actual = (ContentCatalog)type.GetMethod("CreateCatalog")!.Invoke(null, null)!;
        var expected = ContentLoader.Load(Data, false, profile);
        var actualMeta = (MetaCatalog)type.GetMethod("CreateMetaCatalog")!.Invoke(null, null)!;
        var expectedMeta = MetaContentLoader.Read(Data);
        Assert.Equal(JsonSerializer.Serialize(expectedMeta), JsonSerializer.Serialize(actualMeta));
        Assert.Equal(MetaSaveCodec.Encode(MetaEngine.NewGame(expectedMeta)), MetaSaveCodec.Encode(MetaEngine.NewGame(actualMeta)));
        Assert.Equal(profile, type.GetField("ProfileName")!.GetRawConstantValue());
        if (profile == "first-playable")
        {
            Assert.NotNull(actual.FirstPlayable);
            Assert.Equal(27000, actual.Tuning.DurationTicks);
            Assert.Equal(30, actual.Tuning.TickRate);
            Assert.Equal(10, actual.Weapons.Count); Assert.Equal(8, actual.Tools.Count); Assert.Equal(13, actual.Enemies.Count);
            Assert.Equal(8, actual.Runtime!.Charters.Count); Assert.Equal(30, actual.Runtime.Items.Count); Assert.Equal(8, actual.Runtime.Evolutions.Count);
        }
        else { Assert.Null(actual.FirstPlayable); Assert.Equal(21600, actual.Tuning.DurationTicks); }
        Assert.Equal(new CanonicalStateHasher().Compute(expected), new CanonicalStateHasher().Compute(actual));
        Assert.Equal(JsonSerializer.Serialize(expected, HostJson.CreateOptions()), JsonSerializer.Serialize(actual, HostJson.CreateOptions()));
        Assert.Equal(ContentLoader.Hash(Data, false), type.GetField("DataHash")!.GetRawConstantValue());
        Assert.Equal(ContentLoader.ProfileHash(Data, profile), type.GetField("ProfileHash")!.GetRawConstantValue());
        var displays = (Array)type.GetField("Displays")!.GetValue(null)!;
        var blade = Assert.Single(displays.Cast<object>(), item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "core:iron_blade");
        Assert.Equal(weapon["name"]!.GetValue<string>(), blade.GetType().GetProperty("DisplayName")!.GetValue(blade));
        Assert.Equal("weapon", blade.GetType().GetProperty("Category")!.GetValue(blade));
        Assert.Equal(23, actual.Weapons["core:iron_blade"].Activation.Damage);
        var options = new RunOptions(20001, expected.Tuning.DefaultHero, expected.Tuning.DefaultEstate, "random", "A", Movement: "circuit");
        Assert.Equal(SimulationTests.Finish(expected, options).Hash, SimulationTests.Finish(actual, options).Hash);
    }


    [Fact]
    public void ProductionReplayCannotBeRelabeledAsFirstPlayableDespiteSharedDataHash()
    {
        var production = ContentLoader.Load(Data, false, "production");
        var firstPlayable = ContentLoader.Load(Data, false, "first-playable");
        var dataHash = ContentLoader.Hash(Data, false);
        using var recording = new MemoryStream();
        InteractiveCli.RecordFixture(production, dataHash, 30000, recording);
        recording.Position = 0;
        var replay = ReplayCodec.Read(recording);
        Assert.Equal(dataHash, replay.Header.Options.DataHash);
        Assert.Equal(21600, ReplayRunner.Verify(production, dataHash, replay).Tick);
        Assert.Equal("Card is not offered.", Assert.Throws<ArgumentException>(() => ReplayRunner.Verify(firstPlayable, dataHash, replay)).Message);
        var forgedDuration = replay with { End = replay.End with { Tick = 27000 } };
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Verify(firstPlayable, dataHash, forgedDuration));
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
