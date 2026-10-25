using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class AccountEstateR6MapTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig R5 = AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-r5");
    private static readonly AccountEstateConfig R6 = AccountEstateConfigLoader.Load(Data, Catalog, AccountEstateConfigLoader.FirstLoginMapVersion);
    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
    private static bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
        (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
        (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;

    [Fact]
    public void ExportIncludesHistoricalConfigurationsAndExcludesPrototypeDataFromTheRunHash()
    {
        var source = UnityExport.Generate(Data);
        Assert.Contains("CreateAccountEstateConfig(string version = null)", source);
        Assert.Contains("(version ?? \"m3a-prototype-r7\") switch", source);
        foreach (var version in new[] { "r2", "r3", "r4", "r5", "r6", "r7" })
        {
            Assert.Contains("\"m3a-prototype-" + version + "\" => new global::SowSiege.Core.AccountEstateConfig", source);
        }
        Assert.Contains("_ => null", source);
        Assert.DoesNotContain(UnityExportSnapshot.Capture(Data).Files, f => f.RelativePath.StartsWith("opt-in/", StringComparison.Ordinal));
    }

    [Fact]
    public void R6ChangesOnlyTheStartAndOutskirtsWhileKeepingEveryBuildingAndEconomicValue()
    {
        Assert.Equal("m3a-prototype-r6", R6.Version);
        Assert.Equal(Json(R5 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }),
            Json(R6 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }));
        var start = Assert.Single(R6.Zones, z => z.OpenAtStart);
        Assert.Equal((26, 26, 8, 8), (start.X, start.Y, start.Width, start.Height));
        Assert.All(R6.Buildings, b => Assert.True(b.X >= 26 && b.Y >= 26 && b.X + b.Width <= 34 && b.Y + b.Height <= 34, b.Kind));
        var outskirts = R6.Zones.Where(z => z.Id.StartsWith("zone:outskirts-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, outskirts.Length);
        Assert.All(outskirts, z => Assert.True(Adjacent(start, z), z.Id));
        Assert.All(outskirts, z => Assert.Equal(Json(R5.Zones.Single(old => old.Id == z.Id).Unlock), Json(z.Unlock)));
        var cells = new HashSet<(int X, int Y)>();
        foreach (var z in outskirts.Append(start))
        {
            for (var x = z.X; x < z.X + z.Width; x++)
            {
                for (var y = z.Y; y < z.Y + z.Height; y++)
                {
                    Assert.InRange(x, 20, 39); Assert.InRange(y, 20, 39);
                    Assert.True(cells.Add((x, y)), $"Centre cell {x},{y} occurs twice.");
                }
            }
        }
        Assert.Equal(400, cells.Count);
        Assert.Equal(R5.Zones.Where(z => !z.OpenAtStart && !z.Id.StartsWith("zone:outskirts-", StringComparison.Ordinal)).Select(Json),
            R6.Zones.Where(z => !z.OpenAtStart && !z.Id.StartsWith("zone:outskirts-", StringComparison.Ordinal)).Select(Json));
    }

    [Fact]
    public void R6KeepsACompleteHomeAndConnectedProgressionWithoutMigratingHistoricalSaves()
    {
        var reached = new HashSet<string> { R6.Zones.Single(z => z.OpenAtStart).Id };
        for (bool grew = true; grew;)
        {
            var next = R6.Zones.Where(z => !reached.Contains(z.Id) && R6.Zones.Any(o => reached.Contains(o.Id) && Adjacent(o, z))).Select(z => z.Id).ToArray();
            grew = next.Length > 0; reached.UnionWith(next);
        }
        Assert.Equal(R6.Zones.Length, reached.Count);
        var fresh = AccountEstate.NewPrototype(Catalog, R6, 1000, 1_800_000_000);
        Assert.True(R6.Buildings.Single(b => b.Kind == "m3a:home").StartsComplete);
        Assert.Equal(1, fresh.Estate!.Buildings.Single(b => b.Kind == "m3a:home").Level);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, R6, fresh, 1000, 1_800_000_000, "zone:outskirts-east").Reason);
        Assert.Equal("not-adjacent", AccountEstate.StartZoneUnlock(Catalog, R6, fresh, 1000, 1_800_000_000, "zone:inner-village").Reason);
        var historical = AccountEstate.NewPrototype(Catalog, R5, 1000, 1_800_000_000);
        var before = MetaSaveCodec.Encode(historical);
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Advance(Catalog, R6, historical, 1000, 1_800_000_000));
        Assert.Equal(before, MetaSaveCodec.Encode(historical));
    }
}
