using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Tests;

// #483: new geometry must be reachable using the existing economy, not injected wallet balances.
public sealed class AccountEstateR8MapTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static AccountEstateConfig Load(string revision) => AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-" + revision);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
        (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
        (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;

    [Fact]
    public void NewVersionChangesOnlyClosedGeometryAndExplicitUnlockThresholds()
    {
        var old = Load("r7"); var current = Load("r8");
        AccountEstateConfig WithoutZones(AccountEstateConfig c) => c with { Version = "", Status = "", Zones = [] };
        Assert.Equal(Json(WithoutZones(old)), Json(WithoutZones(current)));
        Assert.Equal(Json(old.Zones.Single(z => z.OpenAtStart)), Json(current.Zones.Single(z => z.OpenAtStart)));
        Assert.Equal(old.Zones.Select(z => z.Id), current.Zones.Select(z => z.Id));
        Assert.Equal(old.Zones.Where(z => z.Unlock != null).Select(z => Json(z.Unlock!.Cost)), current.Zones.Where(z => z.Unlock != null).Select(z => Json(z.Unlock!.Cost)));
        Assert.Equal(old.Zones.Where(z => z.Unlock != null).Select(z => z.Unlock!.Seconds), current.Zones.Where(z => z.Unlock != null).Select(z => z.Unlock!.Seconds));
        var expected = new[] { (24, 24, 12, 12), (20, 20, 20, 4), (36, 24, 4, 12), (20, 36, 20, 4), (20, 24, 4, 12), (8, 20, 12, 20), (20, 8, 20, 12), (12, 10, 8, 10), (12, 40, 8, 9), (40, 10, 8, 10), (40, 20, 12, 20), (20, 40, 20, 12), (40, 40, 8, 9) };
        Assert.Equal(expected, current.Zones.Select(z => (z.X, z.Y, z.Width, z.Height)));
        foreach (var zone in current.Zones.Where(z => z.Unlock != null))
        {
            var historical = old.Zones.Single(z => z.Id == zone.Id).Unlock!;
            var threshold = zone.Id == "zone:inner-village" || zone.Id.StartsWith("zone:outskirts-", StringComparison.Ordinal) ? 2 : 3;
            Assert.Equal(Json(historical with { CompletedProductionSites = threshold }), Json(zone.Unlock));
        }
        AccountEstate.Validate(Catalog, current);
        var cells = new HashSet<(int, int)>();
        foreach (var zone in current.Zones)
        {
            for (int x = zone.X; x < zone.X + zone.Width; x++)
            {
                for (int y = zone.Y; y < zone.Y + zone.Height; y++)
                {
                    Assert.True(cells.Add((x, y)), zone.Id);
                }
            }
        }
        Assert.Equal(13, current.Zones.Length); Assert.Equal(1664, cells.Count);
        Assert.Equal((8, 8, 51, 51), (cells.Min(c => c.Item1), cells.Min(c => c.Item2), cells.Max(c => c.Item1), cells.Max(c => c.Item2)));
    }

    [Theory]
    [InlineData("r3", "E26FE4B679670C2CA0A5A01206AB3CE2FEC9ED62D98AA4E48C7D57C81EE5FE0D")]
    [InlineData("r4", "BE455034E9227A12BFE9BC8221EE0EE884E212BDB4D1EBABC34A69C64588C05B")]
    [InlineData("r5", "9200BD12075F0D763F43ADE84A3E83E0FDC1FAFB3900426C75C652287FF822BF")]
    [InlineData("r6", "4C725D02E7DF3FB1E2C8BCBBE81C2C1F19720D38E4BB45324F01F87C83A48604")]
    [InlineData("r7", "0780F8D5F1845735E480906D9AD4F74F46BBED37050CA6F6DE0F9BB41C716916")]
    public void HistoricalConfigBytesAndSavesRemainFrozen(string revision, string sha256)
    {
        var version = "m3a-prototype-" + revision;
        var bytes = File.ReadAllBytes(AccountEstateConfigLoader.PathFor(Data, version));
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes)));
        var config = Load(revision);
        var state = AccountEstate.NewPrototype(Catalog, config, 1000, 1_800_000_000);
        var save = MetaSaveCodec.Encode(state);
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Advance(Catalog, Load("r8"), state, 1000, 1_800_000_000));
        Assert.Equal(save, MetaSaveCodec.Encode(state));
        Assert.Equal(save, MetaSaveCodec.Encode(RoundTrip(state)));
        Assert.Equal(config.Version, RoundTrip(state).Estate!.ConfigVersion);
    }

    [Fact]
    public void RealProductionAndSequentialUnlockCommandsReachAllThirteenZones()
    {
        var config = Load("r8");
        var clock = new EstateClock(); var state = AccountEstate.NewPrototype(Catalog, config, clock.Mono, clock.Wall);
        foreach (var kind in new[] { "meta:granary", "m3a:field", "m3a:lumber_camp", "meta:forge", "m3a:ironworks" })
        {
            var building = state.Estate!.Buildings.Single(b => b.Kind == kind);
            var started = AccountEstate.StartConstruction(Catalog, config, state, clock.Mono, clock.Wall, building.Id);
            Assert.True(started.Done, kind + ": " + started.Reason);
            state = RoundTrip(started.State);
            Assert.NotNull(state.Estate!.Active);
            state = Pass(config, state, clock, state.Estate.Active!.RemainingSeconds);
        }
        Assert.Equal(3, state.Estate!.Buildings.Count(b => config.Buildings.Single(k => k.Kind == b.Kind).Produces != null && b.Level >= 1));
        int claims = 0, opened = 1;
        long paid = 0;
        while (opened < config.Zones.Length)
        {
            var candidate = config.Zones.First(z => !state.Estate!.OpenZones.Contains(z.Id) && config.Zones.Any(o => state.Estate.OpenZones.Contains(o.Id) && Adjacent(o, z)));
            var cost = candidate.Unlock!.Cost["meta:timber"];
            while (state.Wallet["meta:timber"] < cost)
            {
                Assert.True(clock.Mono - 1000 < 24 * 60 * 60, "Normal progress exceeded one simulated day.");
                state = Pass(config, state, clock, 60);
                var claim = AccountEstate.ClaimProduction(Catalog, config, state, clock.Mono, clock.Wall);
                Assert.True(claim.Done, claim.Reason); state = claim.State; claims++;
            }
            int before = state.Wallet["meta:timber"];
            var unlock = AccountEstate.StartZoneUnlock(Catalog, config, state, clock.Mono, clock.Wall, candidate.Id);
            Assert.True(unlock.Done, candidate.Id + ": " + unlock.Reason);
            state = RoundTrip(unlock.State);
            Assert.NotNull(state.Estate!.ZoneWork);
            Assert.Equal(before - cost, state.Wallet["meta:timber"]); paid += cost;
            state = Pass(config, state, clock, candidate.Unlock.Seconds);
            Assert.Contains(candidate.Id, state.Estate!.OpenZones);
            Assert.Null(state.Estate.ZoneWork); Assert.Equal(++opened, state.Estate.OpenZones.Length);
            output.WriteLine($"{opened}/13 {candidate.Id}: elapsed={clock.Mono - 1000}s timber={state.Wallet["meta:timber"]} claims={claims}");
        }
        Assert.Equal(720, paid); Assert.True(claims > 1);
        Assert.Equal(config.Zones.Select(z => z.Id).OrderBy(x => x), state.Estate!.OpenZones.OrderBy(x => x));
        Assert.Empty(MetaValidation.ValidateState(Catalog, state)); RoundTrip(state);
    }

    [Fact]
    public void ExportDefaultsOnlyNewEstatesToR8AndKeepsHistoricalRoutingOutsideRunHash()
    {
        var source = UnityExport.Generate(Data);
        Assert.Contains("(version ?? \"m3a-prototype-r8\") switch", source);
        foreach (var revision in new[] { "r2", "r3", "r4", "r5", "r6", "r7", "r8" })
        {
            Assert.Contains("\"m3a-prototype-" + revision + "\" => new global::SowSiege.Core.AccountEstateConfig", source);
        }
        Assert.DoesNotContain(UnityExportSnapshot.Capture(Data).Files, f => f.RelativePath.StartsWith("opt-in/", StringComparison.Ordinal));
    }

    private sealed class EstateClock { public long Mono = 1000, Wall = 1_800_000_000; }
    private static MetaState Pass(AccountEstateConfig config, MetaState state, EstateClock clock, long seconds)
    {
        clock.Mono += seconds; clock.Wall += seconds;
        var result = AccountEstate.Advance(Catalog, config, state, clock.Mono, clock.Wall);
        Assert.Equal(AccountEstateClock.Accepted, result.Clock); Assert.Equal(seconds, result.CreditedSeconds);
        return result.State;
    }
    private static MetaState RoundTrip(MetaState state)
    {
        var bytes = MetaSaveCodec.Encode(state); Assert.Equal(6, BitConverter.ToInt32(bytes, 4));
        var decoded = MetaSaveCodec.Decode(bytes); Assert.True(decoded.Valid, decoded.ErrorCode);
        Assert.Equal(bytes, MetaSaveCodec.Encode(decoded.State!)); return decoded.State!;
    }
}
