using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// #374: the map-scale prototype m3a-prototype-r3 keeps the r2 economy on the design r3 centre + inner ring grid.
public sealed class AccountEstateWideMapTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig R2 = AccountEstateConfigLoader.Load(Data, Catalog);
    private static readonly AccountEstateConfig Wide = AccountEstateConfigLoader.Load(Data, Catalog, AccountEstateConfigLoader.MapPrototypeVersion);
    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public void TheWideMapKeepsTheR2EconomyAndShiftsItsLayoutIntoTheCentre()
    {
        Assert.Equal("m3a-prototype-r3", Wide.Version);
        Assert.Equal(Json(R2.InitialWallet), Json(Wide.InitialWallet));
        Assert.Equal(Json(R2.WalletCaps), Json(Wide.WalletCaps));
        Assert.Equal(Json(R2.Queue), Json(Wide.Queue));
        Assert.Equal(Json(R2.Storage), Json(Wide.Storage));
        Assert.Equal(Json(R2.Production), Json(Wide.Production));
        Assert.Equal(Json(R2.Construction), Json(Wide.Construction));
        Assert.Equal(R2.ProtectedPath, Wide.ProtectedPath);
        Assert.Equal(Json(R2.Tasks), Json(Wide.Tasks));
        Assert.Equal(R2.Buildings.Length, Wide.Buildings.Length);
        for (int i = 0; i < R2.Buildings.Length; i++)
        {
            var a = R2.Buildings[i]; var b = Wide.Buildings[i];
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal((a.X + 26, a.Y + 26), (b.X, b.Y));
            Assert.Equal(Json(a with { X = 0, Y = 0 }), Json(b with { X = 0, Y = 0 }));
        }
    }

    [Fact]
    public void TheCentreZoneIsSurroundedByFourInnerZonesAndOnlyTheVillageUnlocksNow()
    {
        var start = Assert.Single(Wide.Zones, z => z.OpenAtStart);
        Assert.Equal((20, 20, 20, 20), (start.X, start.Y, start.Width, start.Height));
        var ring = Wide.Zones.Where(z => !z.OpenAtStart).ToArray();
        Assert.Equal(4, ring.Length);
        Assert.All(ring, z => Assert.True(z.Width == 20 && z.Height == 20 && Math.Abs(z.X - start.X) + Math.Abs(z.Y - start.Y) == 20, z.Id));
        Assert.All(Wide.Buildings, b => Assert.True(b.X >= 20 && b.Y >= 20 && b.X + b.Width <= 40 && b.Y + b.Height <= 40, b.Kind));
        // Only the village extension opens in this prototype; the exploration zones need more production sites than exist.
        int productionKinds = Wide.Buildings.Count(b => b.Produces != null);
        var unlockable = Assert.Single(ring, z => z.Unlock!.CompletedProductionSites <= productionKinds);
        Assert.Equal("zone:inner-village", unlockable.Id);
        Assert.Equal(Json(R2.Zones.Single(z => z.Unlock != null).Unlock), Json(unlockable.Unlock));
        Assert.All(ring.Where(z => z.Id != unlockable.Id), z => Assert.True(z.Unlock!.CompletedProductionSites > productionKinds, z.Id));

        var state = AccountEstate.NewPrototype(Catalog, Wide, 1000, 1_800_000_000);
        Assert.Equal(new[] { "zone:start" }, state.Estate!.OpenZones);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, Wide, state, 1000, 1_800_000_000, "zone:inner-riverside").Reason);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, Wide, state, 1000, 1_800_000_000, "zone:inner-village").Reason);
        var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
        Assert.Equal("moved", AccountEstate.Move(Catalog, Wide, state, 1000, 1_800_000_000, home.Id, 21, 21).Reason);
        Assert.Equal("outside-open-zone", AccountEstate.Move(Catalog, Wide, state, 1000, 1_800_000_000, home.Id, 19, 21).Reason);
    }
}
