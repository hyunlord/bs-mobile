using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// #375: m3a-prototype-r4 keeps r3's economy and centre and lays the other zones along the portrait screen's vertical (grid u + v),
// like the approved key art A/C. Screen position of a grid point: x = (u - v) * 32, y = (u + v) * 16.
public sealed class AccountEstateVerticalMapTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig R3 = AccountEstateConfigLoader.Load(Data, Catalog, AccountEstateConfigLoader.WideMapVersion);
    private static readonly AccountEstateConfig R4 = AccountEstateConfigLoader.Load(Data, Catalog, AccountEstateConfigLoader.VerticalMapVersion);
    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static (double D, double S) Centre(AccountEstateZone z) => (z.X + z.Width / 2.0 - (z.Y + z.Height / 2.0), z.X + z.Width / 2.0 + z.Y + z.Height / 2.0);

    [Fact]
    public void TheVerticalMapKeepsTheR3EconomyCentreAndLayout()
    {
        Assert.Equal("m3a-prototype-r4", R4.Version);
        Assert.Equal(Json(R3 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }), Json(R4 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }));
        Assert.Equal(Json(R3.Zones.Single(z => z.OpenAtStart)), Json(R4.Zones.Single(z => z.OpenAtStart)));
        Assert.Equal(Json(R3.Zones.Single(z => z.Id == "zone:inner-village").Unlock), Json(R4.Zones.Single(z => z.Id == "zone:inner-village").Unlock));
    }

    [Fact]
    public void TheZonesStackAlongTheScreenVerticalWithinTheCentresWidth()
    {
        var corners = R4.Zones.SelectMany(z => new[] { (z.X, z.Y), (z.X + z.Width, z.Y), (z.X, z.Y + z.Height), (z.X + z.Width, z.Y + z.Height) }).ToArray();
        int minD = corners.Min(c => c.Item1 - c.Item2), maxD = corners.Max(c => c.Item1 - c.Item2);
        int minS = corners.Min(c => c.Item1 + c.Item2), maxS = corners.Max(c => c.Item1 + c.Item2);
        Assert.Equal((-20, 20), (minD, maxD));
        double width = (maxD - minD) * 32.0, height = (maxS - minS) * 16.0;
        Assert.True(height / width >= 1.5, $"the estate is {width}x{height}, not about 1:2");

        var start = Centre(R4.Zones.Single(z => z.OpenAtStart));
        (double D, double S) At(string id) => Centre(R4.Zones.Single(z => z.Id == "zone:inner-" + id));
        Assert.All(new[] { "northeast-wood", "west-hill", "windmill" }, id => Assert.True(At(id).S < start.S, id + " lies above the centre"));
        Assert.All(new[] { "village", "fields", "fallow", "ruins", "riverside" }, id => Assert.True(At(id).S > start.S, id + " lies below the centre"));
        Assert.True(At("northeast-wood").D > 0 && At("windmill").D < 0 && At("fields").D < 0 && At("village").D > 0 && At("riverside").D > 0);
    }

    [Fact]
    public void EveryZoneConnectsToTheCentreAndOnlyTheVillageUnlocksNow()
    {
        static bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
            (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
            (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;
        var reached = new HashSet<string> { "zone:start" };
        for (bool grew = true; grew;)
        {
            var next = R4.Zones.Where(z => !reached.Contains(z.Id) && R4.Zones.Any(o => reached.Contains(o.Id) && Adjacent(o, z))).Select(z => z.Id).ToArray();
            grew = next.Length > 0; reached.UnionWith(next);
        }
        Assert.Equal(R4.Zones.Length, reached.Count);
        Assert.True(Adjacent(R4.Zones.Single(z => z.OpenAtStart), R4.Zones.Single(z => z.Id == "zone:inner-village")));

        int productionKinds = R4.Buildings.Count(b => b.Produces != null);
        var closed = R4.Zones.Where(z => !z.OpenAtStart).ToArray();
        Assert.Equal("zone:inner-village", Assert.Single(closed, z => z.Unlock!.CompletedProductionSites <= productionKinds).Id);
        var state = AccountEstate.NewPrototype(Catalog, R4, 1000, 1_800_000_000);
        Assert.Equal(new[] { "zone:start" }, state.Estate!.OpenZones);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, R4, state, 1000, 1_800_000_000, "zone:inner-windmill").Reason);
        Assert.Equal("not-adjacent", AccountEstate.StartZoneUnlock(Catalog, R4, state, 1000, 1_800_000_000, "zone:inner-ruins").Reason);
    }
}
