using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// M3a-1 account estate Core (#361, design #360 section 6) with the opt-in prototype settings m3a-prototype-r2.
public sealed class AccountEstateTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig Config = AccountEstateConfigLoader.Load(Data, Catalog);
    private const string Grain = "meta:grain", Timber = "meta:timber", Iron = "meta:iron";

    // A host clock that moves monotonic and wall seconds together.
    private sealed class Clock
    {
        public long Mono = 1000, Wall = 1_800_000_000;
        public void Pass(long seconds) { Mono += seconds; Wall += seconds; }
    }
    private static int Id(MetaState s, string kind) => s.Estate!.Buildings.Single(x => x.Kind == kind).Id;
    private static MetaEstateBuilding B(MetaState s, string kind) => s.Estate!.Buildings.Single(x => x.Kind == kind);
    private static MetaState Start(MetaState s, Clock c, string kind, string expected = "started")
    {
        var r = AccountEstate.StartConstruction(Catalog, Config, s, c.Mono, c.Wall, Id(s, kind));
        Assert.Equal(expected, r.Reason);
        return r.State;
    }
    private static MetaState Pass(MetaState s, Clock c, long seconds) { c.Pass(seconds); return AccountEstate.Advance(Catalog, Config, s, c.Mono, c.Wall).State; }
    private static MetaState New(Clock c) => AccountEstate.NewPrototype(Catalog, Config, c.Mono, c.Wall);
    private static MetaState RoundTrip(MetaState s)
    {
        var bytes = MetaSaveCodec.Encode(s);
        var decoded = MetaSaveCodec.Decode(bytes);
        Assert.True(decoded.Valid, decoded.ErrorCode);
        Assert.Equal(bytes, MetaSaveCodec.Encode(decoded.State!));
        return decoded.State!;
    }

    [Fact]
    public void PrototypeSettingsMatchTheDesignTableAndProtectTheFirstProductionPath()
    {
        Assert.Equal("m3a-prototype-r2", Config.Version);
        var s = New(new Clock());
        var reserve = AccountEstate.ProtectedReserve(Config, s);
        Assert.Equal((30, 100, 25), (reserve[Grain], reserve[Timber], reserve[Iron]));
        Assert.Equal((720, 480, 240), (AccountEstate.StockCap(Config, s, Grain), AccountEstate.StockCap(Config, s, Timber), AccountEstate.StockCap(Config, s, Iron)));
        Assert.Equal(7200, AccountEstate.StorageSeconds(Config, s));
        Assert.Throws<ArgumentException>(() => AccountEstate.Validate(Catalog, Config with { WalletCaps = new() { [Grain] = 999_999 } }));
    }

    [Fact]
    public void NewPrototypeStartsWithTheR2WalletRuinsAndAlwaysLitHomeAndSavesAsVersionSix()
    {
        var s = New(new Clock());
        Assert.Equal((120, 180, 60, 0), (s.Wallet[Grain], s.Wallet[Timber], s.Wallet[Iron], s.Wallet["meta:charter"]));
        Assert.Equal(1, B(s, "m3a:home").Level);
        Assert.All(s.Estate!.Buildings.Where(x => x.Kind != "m3a:home"), b => Assert.Equal(0, b.Level));
        Assert.Equal(MetaSaveCodec.EstateVersion, s.SchemaVersion);
        Assert.Empty(MetaValidation.ValidateState(Catalog, s));
        var bytes = MetaSaveCodec.Encode(s);
        Assert.Equal(MetaSaveCodec.EstateVersion, BitConverter.ToInt32(bytes, 4));
        RoundTrip(s);
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.EncodeVersion(s, MetaSaveCodec.CurrentVersion));
    }

    // An account without an estate keeps writing version 5 with the same bytes as before (no silent extra fields).
    [Fact]
    public void AccountsWithoutAnEstateKeepTheirVersionFiveBytes()
    {
        var s = MetaEngine.NewGame(Catalog);
        var bytes = MetaSaveCodec.Encode(s);
        Assert.Equal(MetaSaveCodec.CurrentVersion, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(MetaSaveCodec.EncodeVersion(s, MetaSaveCodec.CurrentVersion), bytes);
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.EncodeVersion(s, MetaSaveCodec.EstateVersion));
        var decoded = MetaSaveCodec.Decode(bytes);
        Assert.True(decoded.Valid);
        Assert.Null(decoded.State!.Estate);
    }

    // The first observation only initialises; going back or a monotonic/wall gap credits nothing and re-bases; repeating the same
    // observation (a host replaying its last command after a crash) credits nothing a second time.
    [Fact]
    public void OnlyVerifiedIntervalsMoveConstructionAndProduction()
    {
        var c = new Clock();
        var s = New(c);
        s = Start(s, c, "meta:granary");
        c.Pass(30);
        var r = AccountEstate.Advance(Catalog, Config, s, c.Mono, c.Wall);
        Assert.Equal((AccountEstateClock.Accepted, 30L), (r.Clock, r.CreditedSeconds));
        Assert.Equal(30, r.State.Estate!.Active!.RemainingSeconds);
        var again = AccountEstate.Advance(Catalog, Config, r.State, c.Mono, c.Wall);
        Assert.Equal((0L, 30L), (again.CreditedSeconds, again.State.Estate!.Active!.RemainingSeconds));
        // The wall clock jumps ahead an hour without monotonic time: nothing is credited, construction is held too.
        var jump = AccountEstate.Advance(Catalog, Config, again.State, c.Mono + 1, c.Wall + 3600);
        Assert.Equal((AccountEstateClock.ClockMismatch, 0L), (jump.Clock, jump.CreditedSeconds));
        Assert.Equal(30, jump.State.Estate!.Active!.RemainingSeconds);
        // Turning the wall clock back is refused the same way; the next honest interval counts from the new base.
        var back = AccountEstate.Advance(Catalog, Config, jump.State, c.Mono + 2, c.Wall + 3600 - 600);
        Assert.Equal(AccountEstateClock.ClockMismatch, back.Clock);
        var on = AccountEstate.Advance(Catalog, Config, back.State, c.Mono + 32, c.Wall + 3600 - 570);
        Assert.Equal((AccountEstateClock.Accepted, 30L), (on.Clock, on.CreditedSeconds));
        Assert.Null(on.State.Estate!.Active);
        Assert.Equal(1, B(on.State, "meta:granary").Level);
        // A reboot resets monotonic time: refused, then counting resumes from it.
        var reboot = AccountEstate.Advance(Catalog, Config, on.State, 5, c.Wall + 3600);
        Assert.Equal(AccountEstateClock.ClockMismatch, reboot.Clock);
        // A host that crashed after saving restarts from the saved bytes and the same next observation: the same result.
        var saved = RoundTrip(reboot.State);
        var a = AccountEstate.Advance(Catalog, Config, reboot.State, 65, c.Wall + 3660);
        var b = AccountEstate.Advance(Catalog, Config, saved, 65, c.Wall + 3660);
        Assert.Equal(MetaSaveCodec.Encode(a.State), MetaSaveCodec.Encode(b.State));
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Advance(Catalog, Config with { Version = "other" }, s, c.Mono, c.Wall));
    }

    [Fact]
    public void OneBuildRunsAtATimeReservationsCostNothingUntilTheyStartAndAutoStartIsOptIn()
    {
        var c = new Clock();
        var s = New(c);
        s = Start(s, c, "meta:granary");
        Assert.Equal(150, s.Wallet[Timber]);
        s = Start(s, c, "meta:forge", "reserved");
        s = Start(s, c, "m3a:field", "reserved");
        Assert.Equal(150, s.Wallet[Timber]);
        s = Start(s, c, "m3a:lumber_camp", "queue-full");
        var cancel = AccountEstate.CancelReservation(Catalog, Config, s, c.Mono, c.Wall, Id(s, "m3a:field"));
        Assert.Equal("cancelled", cancel.Reason);
        s = cancel.State;
        Assert.Equal((120, 150, 60), (s.Wallet[Grain], s.Wallet[Timber], s.Wallet[Iron]));
        s = Pass(s, c, 60);
        Assert.Equal(1, B(s, "meta:granary").Level);
        Assert.Null(s.Estate!.Active);
        Assert.Single(s.Estate.Reservations);
        var started = AccountEstate.StartReserved(Catalog, Config, s, c.Mono, c.Wall);
        Assert.Equal("started", started.Reason);
        Assert.Equal((110, 40), (started.State.Wallet[Timber], started.State.Wallet[Iron]));
        // With auto start on, the next reservation starts when the current build ends.
        s = Start(started.State, c, "m3a:field", "reserved");
        s = AccountEstate.SetAutoStart(Catalog, Config, s, c.Mono, c.Wall, true).State;
        s = Pass(s, c, 120);
        Assert.Equal(1, B(s, "meta:forge").Level);
        Assert.Equal(Id(s, "m3a:field"), s.Estate!.Active!.BuildingId);
        Assert.Equal(30, s.Estate.Active.RemainingSeconds);
        s = Start(s, c, "meta:granary", "reserved");
        s = Start(s, c, "meta:granary", "already-queued");
        s = Start(s, c, "m3a:home", "max-level");
    }

    // Design section 6.1: forge, scriptorium, barracks, then watchtower would leave timber 20 and iron 0, so the granary and lumber
    // camp could never open. The protected reserve refuses the watchtower and the first production path still opens.
    [Fact]
    public void TheProtectedReserveKeepsTheFirstProductionPathOpen()
    {
        var c = new Clock();
        var s = New(c);
        s = Start(s, c, "meta:forge"); s = Pass(s, c, 120);
        s = Start(s, c, "meta:scriptorium"); s = Pass(s, c, 180);
        s = Start(s, c, "meta:barracks"); s = Pass(s, c, 180);
        Assert.Equal((100, 70, 20), (s.Wallet[Grain], s.Wallet[Timber], s.Wallet[Iron]));
        Assert.Equal((30, 60, 5), (AccountEstate.ProtectedReserve(Config, s)[Grain], AccountEstate.ProtectedReserve(Config, s)[Timber], AccountEstate.ProtectedReserve(Config, s)[Iron]));
        s = Start(s, c, "m3a:watchtower", "protected-reserve");
        foreach (string kind in new[] { "meta:granary", "m3a:field", "m3a:lumber_camp", "m3a:ironworks" }) { s = Start(s, c, kind); s = Pass(s, c, 60); }
        Assert.All(new[] { "meta:granary", "m3a:field", "m3a:lumber_camp", "m3a:ironworks" }, k => Assert.Equal(1, B(s, k).Level));
        Assert.Empty(AccountEstate.ProtectedReserve(Config, s));
        // Production sites need their parent first.
        var fresh = New(new Clock());
        Assert.Equal("parent-required", AccountEstate.StartConstruction(Catalog, Config, fresh, 1000, 1_800_000_000, Id(fresh, "m3a:field")).Reason);
    }

    private static MetaState ProducingField(Clock c)
    {
        var s = New(c);
        s = Start(s, c, "meta:granary"); s = Pass(s, c, 60);
        s = Start(s, c, "m3a:field"); s = Pass(s, c, 30);
        Assert.Equal(1, B(s, "m3a:field").Level);
        return s;
    }

    // Integer production with a remainder (6 grain a minute: 5 s is half a grain), the stock cap discards overflow without a hidden
    // remainder, and the storage time stops a full stock window.
    [Fact]
    public void ProductionKeepsAnExactRemainderAndStopsAtItsStockAndStorageCaps()
    {
        var c = new Clock();
        var s = ProducingField(c);
        s = Pass(s, c, 5);
        Assert.Equal((0, 30_000L), (B(s, "m3a:field").Stock, B(s, "m3a:field").Remainder));
        s = Pass(s, c, 5);
        Assert.Equal((1, 0L), (B(s, "m3a:field").Stock, B(s, "m3a:field").Remainder));
        s = Pass(s, c, 3 * 3600);
        var field = B(s, "m3a:field");
        Assert.Equal((720, 0L, 7200L), (field.Stock, field.Remainder, field.StoredSeconds));
        // A longer absence adds nothing: the window and the stock are both full.
        s = Pass(s, c, 5 * 3600);
        Assert.Equal(720, B(s, "m3a:field").Stock);
        // The interval is bounded by the config's maximum.
        c.Pass(30L * 24 * 3600);
        Assert.Equal(Config.MaximumIntervalSeconds, AccountEstate.Advance(Catalog, Config, s, c.Mono, c.Wall).CreditedSeconds);
    }

    // A claim moves only what fits under the estate wallet cap; the rest stays in stock; claiming again moves nothing more.
    [Fact]
    public void ClaimsMoveOnlyWhatFitsAndNeverPayTwice()
    {
        var c = new Clock();
        var s = ProducingField(c);
        s = Pass(s, c, 3600);
        Assert.Equal(360, B(s, "m3a:field").Stock);
        s = s with { Wallet = new Dictionary<string, int>(s.Wallet) { [Grain] = 1900 } };
        var claim = AccountEstate.ClaimProduction(Catalog, Config, s, c.Mono, c.Wall);
        Assert.Equal(100, claim.Moved[Grain]);
        Assert.Equal((2000, 260, 3600L), (claim.State.Wallet[Grain], B(claim.State, "m3a:field").Stock, B(claim.State, "m3a:field").StoredSeconds));
        var twice = AccountEstate.ClaimProduction(Catalog, Config, claim.State, c.Mono, c.Wall);
        Assert.Equal("nothing-to-claim", twice.Reason);
        Assert.Equal(2000, twice.State.Wallet[Grain]);
        // Room again: the rest moves and the storage window starts over.
        var room = twice.State with { Wallet = new Dictionary<string, int>(twice.State.Wallet) { [Grain] = 0 } };
        var rest = AccountEstate.ClaimProduction(Catalog, Config, room, c.Mono, c.Wall);
        Assert.Equal((260, 0, 0L), (rest.State.Wallet[Grain], B(rest.State, "m3a:field").Stock, B(rest.State, "m3a:field").StoredSeconds));
        // The first claim achieved its task once; it pays once.
        Assert.Contains("task:m3a-first-claim", rest.State.Estate!.AchievedTasks);
        var task = AccountEstate.ClaimTask(Catalog, Config, rest.State, c.Mono, c.Wall, "task:m3a-first-claim");
        Assert.Equal(task.State.Wallet[Timber], rest.State.Wallet[Timber] + 20);
        Assert.Equal("already-claimed", AccountEstate.ClaimTask(Catalog, Config, task.State, c.Mono, c.Wall, "task:m3a-first-claim").Reason);
        Assert.Equal("not-achieved", AccountEstate.ClaimTask(Catalog, Config, task.State, c.Mono, c.Wall, "task:m3a-first-move").Reason);
        // A reward that does not fit waits instead of being cut.
        var full = task.State with { Wallet = new Dictionary<string, int>(task.State.Wallet) { [Grain] = 1995 } };
        full = AccountEstate.Move(Catalog, Config, full, c.Mono, c.Wall, Id(full, "m3a:watchtower"), 4, 0).State;
        Assert.Equal("wallet-full", AccountEstate.ClaimTask(Catalog, Config, full, c.Mono, c.Wall, "task:m3a-first-move").Reason);
        Assert.Equal(1995, AccountEstate.ClaimTask(Catalog, Config, full, c.Mono, c.Wall, "task:m3a-first-move").State.Wallet[Grain]);
    }

    // Storage level and the long-away branch raise both caps; the forge supply branch adds 20 points to iron only.
    [Fact]
    public void BuildingLevelsAndBranchesChangeOnlyFutureProduction()
    {
        var c = new Clock();
        var s = ProducingField(c);
        s = Start(s, c, "meta:granary"); // level 2, 300 s
        s = Pass(s, c, 300);
        Assert.Equal(2, B(s, "meta:granary").Level);
        var before = B(s, "m3a:field").Stock;
        s = Pass(s, c, 60);
        Assert.Equal(before + 7, B(s, "m3a:field").Stock); // 6 x 125% = 7.5 a minute
        Assert.Equal((1080, 10800L), (AccountEstate.StockCap(Config, s, Grain), AccountEstate.StorageSeconds(Config, s)));
        Assert.Equal("level-required", AccountEstate.Specialize(Catalog, Config, s, c.Mono, c.Wall, Id(s, "meta:granary"), "granary:long-away").Reason);
        s = s with { Wallet = new Dictionary<string, int>(s.Wallet) { [Grain] = 500, [Timber] = 500, [Iron] = 500 } };
        s = Start(s, c, "meta:granary"); s = Pass(s, c, 1200);
        var specialized = AccountEstate.Specialize(Catalog, Config, s, c.Mono, c.Wall, Id(s, "meta:granary"), "granary:long-away");
        Assert.Equal("specialized", specialized.Reason);
        Assert.Equal((1800, 18000L), (AccountEstate.StockCap(Config, specialized.State, Grain), AccountEstate.StorageSeconds(Config, specialized.State)));
        Assert.Equal("unknown-branch", AccountEstate.Specialize(Catalog, Config, specialized.State, c.Mono, c.Wall, Id(s, "meta:granary"), "forge:supply-workshop").Reason);
        var plan = MetaEngine.BeginRun(Catalog, specialized.State, Catalog.Chapters[0].Id, 7);
        Assert.Equal("run-pending", AccountEstate.Specialize(Catalog, Config, plan.State, c.Mono, c.Wall, Id(s, "meta:granary"), "granary:expedition-supply").Reason);
    }

    [Fact]
    public void BarracksConstructionSupportShortensOnlyLaterBuilds()
    {
        var c = new Clock();
        var s = New(c) with { };
        s = s with { Wallet = new Dictionary<string, int>(s.Wallet) { [Grain] = 1000, [Timber] = 1000, [Iron] = 1000 } };
        foreach (int _ in new[] { 1, 2, 3 }) { s = Start(s, c, "meta:barracks"); s = Pass(s, c, 2400); }
        s = AccountEstate.Specialize(Catalog, Config, s, c.Mono, c.Wall, Id(s, "meta:barracks"), "barracks:construction-support").State;
        s = Start(s, c, "meta:scriptorium");
        Assert.Equal(162, s.Estate!.Active!.TotalSeconds); // 180 s - 10%
    }

    [Fact]
    public void MovingIsFreeInstantKeepsProgressAndRespectsZonesAndOtherBuildings()
    {
        var c = new Clock();
        var s = ProducingField(c);
        s = Pass(s, c, 600);
        var field = B(s, "m3a:field");
        var wallet = new Dictionary<string, int>(s.Wallet);
        var moved = AccountEstate.Move(Catalog, Config, s, c.Mono, c.Wall, field.Id, 2, 4);
        Assert.Equal("moved", moved.Reason);
        var after = B(moved.State, "m3a:field");
        Assert.Equal((2, 4, field.Level, field.Stock, field.Remainder), (after.X, after.Y, after.Level, after.Stock, after.Remainder));
        Assert.Equal(wallet, moved.State.Wallet);
        Assert.Contains("task:m3a-first-move", moved.State.Estate!.AchievedTasks);
        Assert.StartsWith("overlaps:", AccountEstate.Move(Catalog, Config, moved.State, c.Mono, c.Wall, field.Id, 0, 0).Reason);
        Assert.Equal("outside-open-zone", AccountEstate.Move(Catalog, Config, moved.State, c.Mono, c.Wall, field.Id, 8, 0).Reason);
        Assert.Equal("outside-open-zone", AccountEstate.Move(Catalog, Config, moved.State, c.Mono, c.Wall, field.Id, 7, 7).Reason);
        var building = Start(moved.State, c, "meta:forge");
        Assert.Equal("under-construction", AccountEstate.Move(Catalog, Config, building, c.Mono, c.Wall, Id(building, "meta:forge"), 2, 0).Reason);
    }

    [Fact]
    public void TheAdjacentZoneOpensAfterTwoProductionSitesTimberAndThirtySeconds()
    {
        var c = new Clock();
        var s = ProducingField(c);
        Assert.Equal("production-sites-required", AccountEstate.StartZoneUnlock(Catalog, Config, s, c.Mono, c.Wall, "zone:adjacent-1").Reason);
        s = Start(s, c, "m3a:lumber_camp"); s = Pass(s, c, 30);
        Assert.Contains("task:m3a-two-production-sites", s.Estate!.AchievedTasks);
        var timber = s.Wallet[Timber];
        var unlock = AccountEstate.StartZoneUnlock(Catalog, Config, s, c.Mono, c.Wall, "zone:adjacent-1");
        Assert.Equal("started", unlock.Reason);
        Assert.Equal(timber - 60, unlock.State.Wallet[Timber]);
        Assert.Equal("busy", AccountEstate.StartZoneUnlock(Catalog, Config, unlock.State, c.Mono, c.Wall, "zone:adjacent-1").Reason);
        s = Pass(unlock.State, c, 30);
        Assert.Contains("zone:adjacent-1", s.Estate!.OpenZones);
        Assert.Equal("moved", AccountEstate.Move(Catalog, Config, s, c.Mono, c.Wall, Id(s, "m3a:watchtower"), 9, 1).Reason);
        Assert.Equal("already-open", AccountEstate.StartZoneUnlock(Catalog, Config, s, c.Mono, c.Wall, "zone:adjacent-1").Reason);
    }

    private static MetaState OldAccount()
    {
        var s = MetaEngine.NewGame(Catalog) with { SchemaVersion = MetaSaveCodec.CurrentVersion };
        s = s with
        {
            HighestClearedChapter = 2,
            ManorPriority = "meta:granary",
            Wallet = new Dictionary<string, int> { [Grain] = 400, [Timber] = 300, [Iron] = 200, ["meta:charter"] = 50 },
            ManorLevels = new Dictionary<string, int> { ["meta:forge"] = 4, ["meta:granary"] = 2, ["meta:scriptorium"] = 0, ["meta:barracks"] = 1 },
            NextRunSequence = 9,
            CompletedRuns = 8,
            LastMonotonicSeconds = 500,
            LastWallSeconds = 1_799_000_000,
            IdleRemainderSeconds = 120,
            NextClaimSequence = 3,
            PendingClaims = new[] { new MetaPendingClaim(2, MetaClaimKind.Idle, "idle", new Dictionary<string, int> { [Grain] = 6 }, 1200) }
        };
        return MetaSaveCodec.Decode(MetaSaveCodec.Encode(s)).State!;
    }

    // Steps 2-6: a pending run is refused; manor levels become completed buildings at no cost (capped at the prototype's last level);
    // wallet, claims, idle remainder and the rest stay; auto start waits for consent; the old automatic growth stops; once only.
    [Fact]
    public void MigrationKeepsTheOldAccountExactlyAndAddsTheEstateOnce()
    {
        var old = OldAccount();
        var plan = MetaEngine.BeginRun(Catalog, old, Catalog.Chapters[0].Id, 3);
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Migrate(Catalog, Config, plan.State, 10, 1_800_000_000));
        var migrated = AccountEstate.Migrate(Catalog, Config, old, 10, 1_800_000_000);
        Assert.Equal(MetaSaveCodec.EstateVersion, migrated.SchemaVersion);
        Assert.Equal(AccountEstate.MigratedOrigin, migrated.Estate!.Origin);
        Assert.Equal((3, 2, 0, 1), (B(migrated, "meta:forge").Level, B(migrated, "meta:granary").Level, B(migrated, "meta:scriptorium").Level, B(migrated, "meta:barracks").Level));
        Assert.Equal(1, B(migrated, "m3a:home").Level);
        Assert.False(migrated.Estate.AutoStart);
        Assert.Equal(old.Wallet, migrated.Wallet);
        Assert.Equal(old.ManorLevels, migrated.ManorLevels);
        Assert.Equal(old.PendingClaims.Single().Rewards, migrated.PendingClaims.Single().Rewards);
        Assert.Equal((old.IdleRemainderSeconds, old.NextRunSequence, old.CompletedRuns, old.ManorPriority), (migrated.IdleRemainderSeconds, migrated.NextRunSequence, migrated.CompletedRuns, migrated.ManorPriority));
        // Everything but the schema number and the estate block equals the old save.
        Assert.Equal(MetaSaveCodec.Encode(old), MetaSaveCodec.Encode(migrated with { SchemaVersion = MetaSaveCodec.CurrentVersion, Estate = null }));
        Assert.Throws<InvalidOperationException>(() => AccountEstate.Migrate(Catalog, Config, migrated, 11, 1_800_000_001));
        var saved = RoundTrip(migrated);
        Assert.Equal(MetaSaveCodec.Encode(migrated), MetaSaveCodec.Encode(saved));
        // Old automatic growth would spend the wallet on manor levels; with the estate it no longer runs.
        var rich = saved with { Wallet = new Dictionary<string, int>(saved.Wallet) { [Grain] = 9000, [Timber] = 9000, [Iron] = 9000, ["meta:charter"] = 2000 } };
        var idle = MetaEngine.AdvanceIdle(Catalog, rich, 20, 1_800_000_010);
        Assert.Empty(idle.BuildingsGrown);
        Assert.Equal(rich.ManorLevels, idle.State.ManorLevels);
        var legacy = MetaEngine.AdvanceIdle(Catalog, rich with { SchemaVersion = MetaSaveCodec.CurrentVersion, Estate = null }, 20, 1_800_000_010);
        Assert.NotEmpty(legacy.BuildingsGrown);
        // The migration observation only initialises the estate clock: no back-filled production or construction.
        var first = AccountEstate.Advance(Catalog, Config, saved, 10, 1_800_000_000);
        Assert.Equal(0, first.CreditedSeconds);
    }
}
