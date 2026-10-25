using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// #379: m3a-prototype-r5 is r4 with a 14x14 lit start inside four outskirts zones, so the lit land is narrower than the estate.
public sealed class AccountEstateR5MapTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig R4 = AccountEstateConfigLoader.Load(Data, Catalog, AccountEstateConfigLoader.VerticalMapVersion);
    private static readonly AccountEstateConfig R5 = AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-r5");
    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
    private static bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
        (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
        (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;

    [Fact]
    public void R5KeepsR4ExceptTheSmallerStartAndItsOutskirts()
    {
        Assert.Equal("m3a-prototype-r5", R5.Version);
        Assert.Equal(Json(R4 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }), Json(R5 with { Version = "", Status = "", Zones = Array.Empty<AccountEstateZone>() }));
        var start = Assert.Single(R5.Zones, z => z.OpenAtStart);
        Assert.Equal((23, 23, 14, 14), (start.X, start.Y, start.Width, start.Height));
        Assert.All(R5.Buildings, b => Assert.True(b.X >= 23 && b.Y >= 23 && b.X + b.Width <= 37 && b.Y + b.Height <= 37, b.Kind));
        var outskirts = R5.Zones.Where(z => z.Id.StartsWith("zone:outskirts-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, outskirts.Length);
        Assert.Equal(400, start.Width * start.Height + outskirts.Sum(z => z.Width * z.Height));
        Assert.All(outskirts, z => Assert.True(z.X >= 20 && z.Y >= 20 && z.X + z.Width <= 40 && z.Y + z.Height <= 40 && Adjacent(start, z), z.Id));
        var village = R4.Zones.Single(z => z.Id == "zone:inner-village").Unlock;
        Assert.All(outskirts, z => Assert.Equal(Json(village), Json(z.Unlock)));
        var others = R5.Zones.Where(z => !z.OpenAtStart && !outskirts.Contains(z)).Select(Json).ToArray();
        Assert.Equal(R4.Zones.Where(z => !z.OpenAtStart).Select(Json).ToArray(), others);
    }

    [Fact]
    public void TheLitStartIsNarrowerThanTheEstateAndEveryZoneConnects()
    {
        var start = R5.Zones.Single(z => z.OpenAtStart);
        int minD = R5.Zones.Min(z => z.X - (z.Y + z.Height)), maxD = R5.Zones.Max(z => z.X + z.Width - z.Y);
        Assert.Equal((-20, 20), (minD, maxD));
        Assert.True(start.Width + start.Height < maxD - minD, "the lit land's diamond is narrower than the estate");
        var reached = new HashSet<string> { start.Id };
        for (bool grew = true; grew;)
        {
            var next = R5.Zones.Where(z => !reached.Contains(z.Id) && R5.Zones.Any(o => reached.Contains(o.Id) && Adjacent(o, z))).Select(z => z.Id).ToArray();
            grew = next.Length > 0; reached.UnionWith(next);
        }
        Assert.Equal(R5.Zones.Length, reached.Count);
        var state = AccountEstate.NewPrototype(Catalog, R5, 1000, 1_800_000_000);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, R5, state, 1000, 1_800_000_000, "zone:outskirts-east").Reason);
        Assert.Equal("not-adjacent", AccountEstate.StartZoneUnlock(Catalog, R5, state, 1000, 1_800_000_000, "zone:inner-village").Reason);
        var home = state.Estate!.Buildings.Single(b => b.Kind == "m3a:home");
        Assert.Equal("outside-open-zone", AccountEstate.Move(Catalog, R5, state, 1000, 1_800_000_000, home.Id, 21, 21).Reason);
    }
}
