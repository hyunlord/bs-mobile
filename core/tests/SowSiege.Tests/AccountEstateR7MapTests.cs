using System.Security.Cryptography;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// #389: layout A changes only prototype geometry; historical settings and the r6 economy remain contracts.
public sealed class AccountEstateR7MapTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig R6 = AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-r6");
    private static readonly AccountEstateConfig R7 = AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-r7");
    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
    private static AccountEstateBuildingKind Building(string kind) => R7.Buildings.Single(b => b.Kind == kind);
    private static bool Inside(AccountEstateZone zone, (int X, int Y) cell) =>
        cell.X >= zone.X && cell.Y >= zone.Y && cell.X < zone.X + zone.Width && cell.Y < zone.Y + zone.Height;
    private static IEnumerable<(int X, int Y)> Cells(int x, int y, int width, int height) =>
        Enumerable.Range(x, width).SelectMany(cx => Enumerable.Range(y, height).Select(cy => (cx, cy)));

    [Fact]
    public void R7ChangesOnlyVersionStatusZoneGeometryAndBuildingPositions()
    {
        Assert.Equal("m3a-prototype-r7", R7.Version);
        AccountEstateConfig WithoutGeometry(AccountEstateConfig config) => config with
        {
            Version = "",
            Status = "",
            Zones = config.Zones.Select(z => z with { X = 0, Y = 0, Width = 0, Height = 0 }).ToArray(),
            Buildings = config.Buildings.Select(b => b with { X = 0, Y = 0 }).ToArray()
        };
        Assert.Equal(Json(WithoutGeometry(R6)), Json(WithoutGeometry(R7)));
        AccountEstate.Validate(Catalog, R7);
        var fresh = AccountEstate.NewPrototype(Catalog, R7, 1000, 1_800_000_000);
        Assert.Equal(6, fresh.SchemaVersion);
        Assert.Equal(1, fresh.Estate!.Buildings.Single(b => b.Kind == "m3a:home").Level);
        Assert.All(fresh.Estate.Buildings.Where(b => b.Kind != "m3a:home"), b => Assert.Equal(0, b.Level));
        Assert.DoesNotContain(UnityExportSnapshot.Capture(Data).Files, f => f.RelativePath.StartsWith("opt-in/", StringComparison.Ordinal));
    }

    [Fact]
    public void TwelveCellOpeningContainsTenSitesWithOneCellClearanceAndAnEmptyHomePlaza()
    {
        var start = Assert.Single(R7.Zones, z => z.OpenAtStart);
        Assert.Equal((24, 24, 12, 12), (start.X, start.Y, start.Width, start.Height));
        Assert.Equal(10, R7.Buildings.Length);
        var occupied = new HashSet<(int X, int Y)>();
        foreach (var b in R7.Buildings)
        {
            Assert.Equal((2, 2), (b.Width, b.Height));
            foreach (var cell in Cells(b.X, b.Y, b.Width, b.Height))
            {
                Assert.True(Inside(start, cell), b.Kind);
                Assert.True(occupied.Add(cell), $"Overlapping site at {cell}.");
            }
        }
        for (int i = 0; i < R7.Buildings.Length; i++)
        {
            for (int j = i + 1; j < R7.Buildings.Length; j++)
            {
                var a = R7.Buildings[i]; var b = R7.Buildings[j];
                int gapX = Math.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width));
                int gapY = Math.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height));
                Assert.True(Math.Max(gapX, gapY) >= 1, $"{a.Kind} touches {b.Kind}.");
            }
        }
        Assert.Equal(40, occupied.Count);
        Assert.Equal(104, start.Width * start.Height - occupied.Count);
        var home = Building("m3a:home");
        Assert.Equal((28, 27), (home.X, home.Y));
        Assert.All(Cells(home.X, home.Y + home.Height, 2, 2), c => Assert.DoesNotContain(c, occupied));
        var well = Building("m3a:well_shrine");
        Assert.Equal((30, 30), (well.X, well.Y));
        var library = Building("meta:scriptorium");
        Assert.Equal(home.X, library.X);
        Assert.Equal(1, home.Y - (library.Y + library.Height));
        Assert.All(new[] { "m3a:field", "m3a:lumber_camp", "meta:granary" }, kind => Assert.True(Building(kind).X < home.X, kind));
        Assert.All(new[] { "meta:forge", "meta:barracks", "m3a:watchtower", "m3a:ironworks" }, kind => Assert.True(Building(kind).X > home.X + home.Width, kind));
    }

    [Fact]
    public void EveryDoorCanReachTheHomeWithoutCrossingASiteOrClosedLand()
    {
        // Geometry feasibility only: the View tests separately inspect the actual automatically drawn road cells.
        var start = R7.Zones.Single(z => z.OpenAtStart);
        var blocked = R7.Buildings.SelectMany(b => Cells(b.X, b.Y, b.Width, b.Height)).ToHashSet();
        (int X, int Y) Door(AccountEstateBuildingKind b) => (b.X + b.Width - 1, b.Y + b.Height);
        var origin = Door(Building("m3a:home"));
        var reached = new HashSet<(int X, int Y)> { origin };
        var pending = new Queue<(int X, int Y)>(); pending.Enqueue(origin);
        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            foreach (var next in new[] { (cell.X - 1, cell.Y), (cell.X + 1, cell.Y), (cell.X, cell.Y - 1), (cell.X, cell.Y + 1) })
            {
                if (Inside(start, next) && !blocked.Contains(next) && reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }
        Assert.All(R7.Buildings, b => Assert.Contains(Door(b), reached));
    }

    [Fact]
    public void OnlyTheFourOutskirtsShrinkAndTheOldCentreStillHasFourHundredDistinctCells()
    {
        Assert.Equal(13, R7.Zones.Length);
        Assert.Equal(R6.Zones.Skip(5).Select(Json), R7.Zones.Skip(5).Select(Json));
        var centre = new HashSet<(int X, int Y)>();
        foreach (var z in R7.Zones.Take(5))
        {
            foreach (var cell in Cells(z.X, z.Y, z.Width, z.Height))
            {
                Assert.InRange(cell.X, 20, 39); Assert.InRange(cell.Y, 20, 39);
                Assert.True(centre.Add(cell), $"Zone overlap at {cell}.");
            }
        }
        Assert.Equal(400, centre.Count);
        Assert.Equal(new[] { 144, 80, 48, 80, 48 }, R7.Zones.Take(5).Select(z => z.Width * z.Height));
    }

    [Theory]
    [InlineData("r3", "E26FE4B679670C2CA0A5A01206AB3CE2FEC9ED62D98AA4E48C7D57C81EE5FE0D")]
    [InlineData("r4", "BE455034E9227A12BFE9BC8221EE0EE884E212BDB4D1EBABC34A69C64588C05B")]
    [InlineData("r5", "9200BD12075F0D763F43ADE84A3E83E0FDC1FAFB3900426C75C652287FF822BF")]
    [InlineData("r6", "4C725D02E7DF3FB1E2C8BCBBE81C2C1F19720D38E4BB45324F01F87C83A48604")]
    public void HistoricalConfigBytesStayFrozenAndR7CannotRewriteTheirSaves(string revision, string sha256)
    {
        var version = "m3a-prototype-" + revision;
        var bytes = File.ReadAllBytes(Path.Combine(Data, "opt-in", "account-estate", version + ".json"));
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes)));
        var historical = AccountEstateConfigLoader.Load(Data, Catalog, version);
        var state = AccountEstate.NewPrototype(Catalog, historical, 1000, 1_800_000_000);
        var saved = MetaSaveCodec.Encode(state);
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Advance(Catalog, R7, state, 1000, 1_800_000_000));
        Assert.Equal(saved, MetaSaveCodec.Encode(state));
    }
}
