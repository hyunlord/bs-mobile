using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

// ADR0079 (c): estate advance is split at each effect change; the Core receives only capped values.
public sealed class EstateEffectSplitTests
{
    private static readonly EstateContent Content = EstateContent.Load(ContentEstateTests.Root());
    private static readonly EstateApplication App = new(Content);
    private const long T0 = 1_800_000_000_000;

    private static AccountEstateBuildingKind Site => Content.Config.Buildings.First(k => k.Produces != null);

    /// <summary>A stored account whose first production site and its parent are at level 1 (test setup only).</summary>
    private static StoredAccount Producing()
    {
        var account = App.Create(T0);
        var state = account.State;
        var buildings = state.Estate!.Buildings.Select(b => b.Kind == Site.Kind || b.Kind == Site.Parent ? b with { Level = 1 } : b).ToArray();
        return account with { State = state with { Estate = state.Estate with { Buildings = buildings } } };
    }

    private static AccountEstateBonuses Bonus(int permille) => new(new Dictionary<string, int> { [Site.Produces!.Resource] = permille }, 0, 0);

    private static MetaEstateBuilding SiteOf(StoredAccount a) => a.State.Estate!.Buildings.Single(b => b.Kind == Site.Kind);

    [Fact]
    public void NoTimelineOrAllZeroTimelineKeepsTheHistoricalBytes()
    {
        var start = Producing();
        var plain = App.Apply(start, null, T0 + 600_000).Account;
        var zero = App.Apply(start, null, T0 + 600_000, [(T0 + 300_000, AccountEstateBonuses.None)]).Account;
        Assert.Equal(MetaSaveCodec.Encode(plain.State), MetaSaveCodec.Encode(zero.State));
        Assert.Equal(plain.Clock, zero.Clock);
    }

    [Fact]
    public void ProductionUsesTheOldRateBeforeACompletionAndTheNewRateAfter()
    {
        var start = Producing();
        var split = App.Apply(start, null, T0 + 600_000, [(T0 + 200_000, Bonus(50))]).Account;
        // Same span by hand: 200 s without the bonus, then 400 s with it, remainder carried.
        long logical = start.Clock.LogicalSeconds;
        var manual = AccountEstate.Advance(Content.Meta, Content.Config, start.State, logical + 200, logical + 200).State;
        manual = AccountEstate.Advance(Content.Meta, Content.Config, manual, logical + 600, logical + 600, Bonus(50)).State;
        Assert.Equal(MetaSaveCodec.Encode(manual), MetaSaveCodec.Encode(split.State));
        var whole = App.Apply(start, null, T0 + 600_000, [(T0 - 1, Bonus(50))]).Account;
        Assert.True(SiteOf(whole).Stock * 60_000L + SiteOf(whole).Remainder >= SiteOf(split).Stock * 60_000L + SiteOf(split).Remainder);
        // Two completions inside one span both split it.
        var twice = App.Apply(start, null, T0 + 600_000, [(T0 + 200_000, Bonus(50)), (T0 + 400_000, Bonus(100))]).Account;
        var byHand = AccountEstate.Advance(Content.Meta, Content.Config, start.State, logical + 200, logical + 200).State;
        byHand = AccountEstate.Advance(Content.Meta, Content.Config, byHand, logical + 400, logical + 400, Bonus(50)).State;
        byHand = AccountEstate.Advance(Content.Meta, Content.Config, byHand, logical + 600, logical + 600, Bonus(100)).State;
        Assert.Equal(MetaSaveCodec.Encode(byHand), MetaSaveCodec.Encode(twice.State));
    }

    [Fact]
    public void OfflineCapAppliesOnceAcrossSplitSegments()
    {
        var start = Producing();
        long cap = Content.Config.MaximumIntervalSeconds;
        long now = T0 + (cap + 5_000) * 1000;
        var split = App.Apply(start, null, now, [(T0 + (cap + 1_000) * 1000, Bonus(50))]);
        Assert.Equal(cap, split.Reply.Estate.CreditedSeconds);
        Assert.Equal(start.Clock.LogicalSeconds + cap, split.Account.Clock.LogicalSeconds);
        // The change lies after the credited span, so production equals the unsplit capped advance.
        var plain = App.Apply(start, null, now).Account;
        Assert.Equal(MetaSaveCodec.Encode(plain.State), MetaSaveCodec.Encode(split.Account.State));
    }

    [Fact]
    public void CappedPercentDomainsMapToCorePermilleAndStorageSecondsPassThrough()
    {
        var bonuses = EstateEffects.ToBonuses(new Dictionary<string, int>
        {
            ["estate.grainProductionPercent"] = 5, ["estate.ironProductionPercent"] = 3, ["construction.speedPercent"] = 5, ["estate.storageSeconds"] = 600,
            ["training.speedPercent"] = 5,
        });
        Assert.Equal((50, 30, false), (bonuses.ProductionPermille["meta:grain"], bonuses.ProductionPermille["meta:iron"], bonuses.ProductionPermille.ContainsKey("meta:timber")));
        Assert.Equal((50, 600L), (bonuses.ConstructionSpeedPermille, bonuses.StorageSeconds));
        Assert.True(EstateEffects.ToBonuses(new Dictionary<string, int>()).IsNone);
    }

    [Fact]
    public void ResearchPublishesSnapshotsAtCompletionTimesIncludingUnsettledJobs()
    {
        var research = new ResearchDomain(ResearchContent.Load(ContentEstateTests.Root()));
        var state = JsonSerializer.SerializeToElement(new
        {
            nextSeq = 3,
            active = new[] { new { seq = 2, nodeId = "slg:research-wood-yield", level = 1, startedAtMs = 1000L, completesAtMs = 5000L } },
            levels = new Dictionary<string, int> { ["slg:research-field-yield"] = 1, ["slg:research-training"] = 1 },
            completedAtMs = new Dictionary<string, long> { ["slg:research-field-yield"] = 2000, ["slg:research-training"] = 3000 },
            proofs = Array.Empty<string>(),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var until = research.EstateEffects(state, 4999);
        // Training speed is not an estate effect, so its completion adds no snapshot.
        Assert.Equal(new[] { 2000L }, until.Select(s => s.FromUtc));
        Assert.Equal(5, until[0].Values["estate.grainProductionPercent"]);
        var after = research.EstateEffects(state, 5000);
        Assert.Equal(new[] { 2000L, 5000L }, after.Select(s => s.FromUtc));
        Assert.Equal(5, after[1].Values["estate.timberProductionPercent"]);
        var timeline = EstateEffects.Timeline([after]);
        Assert.Equal((50, 50), (timeline[1].Bonuses.ProductionPermille["meta:grain"], timeline[1].Bonuses.ProductionPermille["meta:timber"]));
    }
}
