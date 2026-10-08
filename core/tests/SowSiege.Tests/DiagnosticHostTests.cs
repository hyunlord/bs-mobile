using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class DiagnosticHostTests
{
    [Fact]
    public void LegacyS2ObserverDoesNotIntroduceGameplayTimelineSamples()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase0-r2", "data"));
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 301 } };
        var options = new RunOptions(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "people");
        var plain = SimulationFactory.Create(catalog, options);
        var observer = new DiagnosticObserver(new(DiagnosticVariant.Control));
        var observed = SimulationFactory.Create(catalog, options, observer);
        Assert.Equal(DiagnosticOutput.Digest(plain.Result()), DiagnosticOutput.Digest(observed.Result()));
        while (!plain.IsComplete) { plain.Tick(); observed.Tick(); }
        Assert.Equal(DiagnosticOutput.Digest(plain.Result()), DiagnosticOutput.Digest(observed.Result()));
    }

    [Theory]
    [InlineData("--diagnostic-variant", "control")]
    [InlineData("--diagnostic-output", "compact.json")]
    [InlineData("--diagnostic-raw-output", "raw.json")]
    public void PartialDiagnosticFlagsAreRejected(string key, string value) =>
        Assert.Throws<ArgumentException>(() => DiagnosticRequest.Parse(new Dictionary<string, string> { [key] = value }, 3));

    [Fact]
    public void UnknownVariantWrongRepeatCountAndOverlappingOutputAreRejected()
    {
        var args = new Dictionary<string, string> { ["--diagnostic-variant"] = "control", ["--diagnostic-output"] = "compact.json" };
        Assert.NotNull(DiagnosticRequest.Parse(args, 3));
        Assert.Throws<ArgumentException>(() => DiagnosticRequest.Parse(args, 2));
        args["--diagnostic-raw-output"] = "./compact.json";
        Assert.Throws<ArgumentException>(() => DiagnosticRequest.Parse(args, 3));
        args.Remove("--diagnostic-raw-output"); args["--diagnostic-variant"] = "invalid";
        Assert.Throws<ArgumentException>(() => DiagnosticRequest.Parse(args, 3));
        args["--diagnostic-variant"] = "control"; args["--output"] = "all.json";
        Assert.Throws<ArgumentException>(() => DiagnosticRequest.Parse(args, 3));
        Assert.Null(DiagnosticRequest.Parse(new Dictionary<string, string>(), 1));
    }

    [Fact]
    public void ControlObserverPreservesGameplayAndProvidesInitialPeriodicAndTerminalSamples()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "s4b-02");
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 601 } };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "people", "C", Movement: "circuit");
        var observer = new DiagnosticObserver(new(DiagnosticVariant.Control));
        var ordinary = SimulationFactory.Create(catalog, options);
        var observed = SimulationFactory.Create(catalog, options, observer);
        while (!ordinary.IsComplete) { ordinary.Tick(); observed.Tick(); }
        Assert.Equal(DiagnosticOutput.Digest(ordinary.Result()), DiagnosticOutput.Digest(observed.Result()));
        Assert.Equal(ordinary.World.NextId, observed.World.NextId);
        var report = observer.Result();
        Assert.Equal(new[] { 0, 300, 600, 601 }, report.Snapshots.Select(snapshot => snapshot.Tick));
        Assert.True(report.Snapshots[^1].Terminal);
        Assert.Equal(602, report.RngTrace.Count);
        Assert.Equal(observed.Result().RandomDraws, report.RngTrace[^1].Draws);
        Assert.Equal(report.AttackSources.Sum(source => source.AppliedHpDamage), observed.Result().WeaponDamage + observed.Result().AllyDamage + observed.Result().PerTool.Values.Sum(tool => tool.ActivationDamage + tool.GrowthDamage));
    }

    [Fact]
    public void CompactPacketPreservesRngAndRejectsDifferentObservationsDespiteMatchingGameplayHash()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 1 } };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "people");
        var observer = new DiagnosticObserver(new(DiagnosticVariant.Control));
        var simulation = SimulationFactory.Create(catalog, options, observer); simulation.Tick();
        var result = simulation.Result(); var diagnostics = observer.Result();
        var request = new DiagnosticRequest("control", "unused.json", null);
        var source = new { contentSha256 = "content", profileSha256 = "profile", tuningSha256 = "tuning" };
        var packet = DiagnosticOutput.Create(request, "s2-baseline", null, 1, source, CoreAssemblyMetadata.Read(), [result, result, result], [diagnostics, diagnostics, diagnostics]);
        var bytes = Convert.FromBase64String(packet["diagnostics"]!["rngTrace"]!.GetValue<string>());
        Assert.Equal(16, bytes.Length);
        Assert.Equal((ulong)result.RandomDraws, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(DiagnosticOutput.Digest(diagnostics), packet["diagnosticDigest"]!.GetValue<string>());
        Assert.DoesNotContain("perTool", packet.ToJsonString());
        var different = diagnostics with { Threat = diagnostics.Threat with { LiveHp = diagnostics.Threat.LiveHp + 1 } };
        Assert.Throws<InvalidOperationException>(() => DiagnosticOutput.Create(request, "s2-baseline", null, 1, source, CoreAssemblyMetadata.Read(), [result, result, result], [diagnostics, different, diagnostics]));
        Assert.Throws<InvalidOperationException>(() => DiagnosticOutput.Create(request, "s2-baseline", null, 1, source, CoreAssemblyMetadata.Read(), [result, result with { Food = result.Food + 1 }, result], [diagnostics, diagnostics, diagnostics]));
    }
}
