using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// ADR0079 (#606): opt-in estate inputs. Absent or zero inputs must keep the historical arithmetic and saved bytes.
public sealed class AccountEstateBonusesTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly MetaCatalog Catalog = MetaContentLoader.Read(Data);
    private static readonly AccountEstateConfig Config = AccountEstateConfigLoader.Load(Data, Catalog, "m3a-prototype-r8");
    private const long Mono = 1000, Wall = 1_800_000_000;

    private static AccountEstateBuildingKind Site => Config.Buildings.First(k => k.Produces != null);

    /// <summary>A new r8 account with one production site and its parent at level 1 (test setup, not a play path).</summary>
    private static MetaState Producing()
    {
        var state = AccountEstate.NewPrototype(Catalog, Config, Mono, Wall);
        var buildings = state.Estate!.Buildings.Select(b => b.Kind == Site.Kind || b.Kind == Site.Parent ? b with { Level = 1 } : b).ToArray();
        return state with { Estate = state.Estate with { Buildings = buildings } };
    }

    private static AccountEstateBonuses Production(int permille) =>
        new(new Dictionary<string, int> { [Site.Produces!.Resource] = permille }, 0, 0);

    private static byte[] Scenario(AccountEstateBonuses? bonuses, bool passArgument)
    {
        var state = Producing();
        AccountEstateResult Step(Func<MetaState, AccountEstateResult> withArgument, Func<MetaState, AccountEstateResult> without) =>
            passArgument ? withArgument(state) : without(state);
        int granary = state.Estate!.Buildings.First(b => b.Kind == "meta:granary").Id;
        state = Step(s => AccountEstate.Advance(Catalog, Config, s, Mono + 600, Wall + 600, bonuses), s => AccountEstate.Advance(Catalog, Config, s, Mono + 600, Wall + 600)).State;
        state = Step(s => AccountEstate.StartConstruction(Catalog, Config, s, Mono + 700, Wall + 700, granary, bonuses), s => AccountEstate.StartConstruction(Catalog, Config, s, Mono + 700, Wall + 700, granary)).State;
        state = Step(s => AccountEstate.ClaimProduction(Catalog, Config, s, Mono + 4000, Wall + 4000, bonuses), s => AccountEstate.ClaimProduction(Catalog, Config, s, Mono + 4000, Wall + 4000)).State;
        state = Step(s => AccountEstate.Advance(Catalog, Config, s, Mono + 30000, Wall + 30000, bonuses), s => AccountEstate.Advance(Catalog, Config, s, Mono + 30000, Wall + 30000)).State;
        return MetaSaveCodec.Encode(state);
    }

    [Fact]
    public void AbsentOrZeroInputsKeepSavedBytesIdentical()
    {
        byte[] historical = Scenario(null, false);
        Assert.Equal(historical, Scenario(null, true));
        Assert.Equal(historical, Scenario(AccountEstateBonuses.None, true));
        Assert.Equal(historical, Scenario(new(new Dictionary<string, int> { ["meta:grain"] = 0, ["meta:timber"] = 0 }, 0, 0), true));
        Assert.True(AccountEstateBonuses.None.IsNone);
        var state = Producing();
        Assert.Equal(AccountEstate.StorageSeconds(Config, state), AccountEstate.StorageSeconds(Config, state, AccountEstateBonuses.None));
    }

    [Fact]
    public void ProductionBonusAddsAShareOfTheCurrentRateWithTheSameRemainderUnit()
    {
        var state = Producing();
        int Stock(MetaState s) => s.Estate!.Buildings.Single(b => b.Kind == Site.Kind).Stock;
        long Remainder(MetaState s) => s.Estate!.Buildings.Single(b => b.Kind == Site.Kind).Remainder;
        var plain = AccountEstate.Advance(Catalog, Config, state, Mono + 600, Wall + 600).State;
        var boosted = AccountEstate.Advance(Catalog, Config, state, Mono + 600, Wall + 600, Production(50)).State;
        long permille = Config.Production.ParentLevelPermille[1];
        long effective = permille + permille * 50 / PlayerInput.Scale;
        long numerator = 600L * Site.Produces!.PerMinute * effective;
        Assert.Equal((numerator / 60_000, numerator % 60_000), ((long)Stock(boosted), Remainder(boosted)));
        Assert.True(Stock(boosted) >= Stock(plain));
        // Splitting the interval at an effect boundary continues the remainder: no retroactive or double production.
        var split = AccountEstate.Advance(Catalog, Config, state, Mono + 300, Wall + 300).State;
        split = AccountEstate.Advance(Catalog, Config, split, Mono + 600, Wall + 600, Production(50)).State;
        long first = 300L * Site.Produces.PerMinute * permille, second = 300L * Site.Produces.PerMinute * effective;
        long carried = first % 60_000 + second;
        Assert.Equal((first / 60_000 + carried / 60_000, carried % 60_000), ((long)Stock(split), Remainder(split)));
        // Without a bonus, two half intervals equal one interval byte for byte.
        var halves = AccountEstate.Advance(Catalog, Config, AccountEstate.Advance(Catalog, Config, state, Mono + 300, Wall + 300).State, Mono + 600, Wall + 600).State;
        Assert.Equal(MetaSaveCodec.Encode(plain), MetaSaveCodec.Encode(halves));
    }

    [Fact]
    public void ConstructionSpeedUsesTheCeilingSpeedFormAtStart()
    {
        var state = Producing();
        int granary = state.Estate!.Buildings.First(b => b.Kind == "meta:granary").Id;
        var plain = AccountEstate.StartConstruction(Catalog, Config, state, Mono + 1, Wall + 1, granary).State.Estate!.Active!;
        var fast = AccountEstate.StartConstruction(Catalog, Config, state, Mono + 1, Wall + 1, granary, new(new Dictionary<string, int>(), 300, 0)).State.Estate!.Active!;
        long expected = (plain.RemainingSeconds * PlayerInput.Scale + 1300 - 1) / 1300;
        Assert.Equal(expected, fast.RemainingSeconds);
        Assert.True(fast.RemainingSeconds < plain.RemainingSeconds);
    }

    [Fact]
    public void StorageExtraSecondsStillEndAtTheConfiguredMaximum()
    {
        var state = Producing();
        long baseSeconds = AccountEstate.StorageSeconds(Config, state);
        Assert.Equal(Math.Min(baseSeconds + 600, Config.Storage.MaximumSeconds), AccountEstate.StorageSeconds(Config, state, new(new Dictionary<string, int>(), 0, 600)));
        Assert.Equal(Config.Storage.MaximumSeconds, AccountEstate.StorageSeconds(Config, state, new(new Dictionary<string, int>(), 0, 1_000_000)));
    }

    [Fact]
    public void InvalidInputsAreRejected()
    {
        var state = Producing();
        Assert.Throws<ArgumentException>(() => AccountEstate.Advance(Catalog, Config, state, Mono + 1, Wall + 1, Production(-1)));
        Assert.Throws<ArgumentException>(() => AccountEstate.Advance(Catalog, Config, state, Mono + 1, Wall + 1, new(new Dictionary<string, int> { ["meta:unknown"] = 5 }, 0, 0)));
        Assert.Throws<ArgumentException>(() => AccountEstate.Advance(Catalog, Config, state, Mono + 1, Wall + 1, new(new Dictionary<string, int>(), -1, 0)));
        Assert.Throws<ArgumentException>(() => AccountEstate.Advance(Catalog, Config, state, Mono + 1, Wall + 1, new(new Dictionary<string, int>(), 0, -1)));
    }
}
