using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatContractTests
{
    private static WorldCombatContent Content() => WorldCombatContent.Load(ContentEstateTests.Root());
    private static WorldCombatContractFactory Factory()
    {
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        return new(Content(), ContentLoader.Load(data, profileName: "wave-1a"), ContentLoader.Hash(data, false), "wave-1a");
    }
    private static readonly Guid Run = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static WorldMarch March(Dictionary<string, int>? levels = null) => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"),
        null, "target:field", 1, "attack", "core:frontier_knight", [new("slg:levy", 5)], [], 0, 1000, 1000, 1, "arrived", "",
        JsonSerializer.SerializeToElement(new { combatAccount = new { activeVassalIds = Array.Empty<string>() }, researchHash = Content().Research.Hash, domains = new { research = new { levels = levels ?? new() } } }));
    private static WorldCombatIssuedContract Issue(WorldMarch? march = null, Guid? run = null, WorldCombatMode mode = WorldCombatMode.DirectPve) =>
        Factory().Issue(march ?? March(), "slg:encounter_field_mites", run ?? Run, 1000, mode);

    [Fact]
    public void SameAuthoritativeSnapshotHasSameContractAndRealRuntimeProvenance()
    {
        var first = Issue(); var second = Issue();
        Assert.Equal(first.ContractHash, second.ContractHash);
        Assert.Equal(first.ContractHash, first.Direct!.ContractHash);
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        Assert.Equal(ContentLoader.Hash(data, false), first.Direct.Provenance.ContentHash);
        Assert.Equal(ReviewShowcaseCatalog.Hash(ContentLoader.Load(data, profileName: "wave-1a")), first.Direct.Provenance.ProfileHash);
        WorldCombatCodec.Validate(new(first.Direct));
        Assert.Equal(5, Assert.Single(first.Direct.Troops).N);
        Assert.Equal(80, first.Award.InjuryPercent);
        Assert.Equal(600000, first.Award.RespawnMilliseconds);
        Assert.Contains(first.Award.Credits, c => c.Material == "meta:grain" && c.Amount == 40);
    }
    [Fact]
    public void IdentityGenerationArmyAndModeAreBound()
    {
        var march = March(); string hash = Issue(march).ContractHash;
        Assert.NotEqual(hash, Issue(march with { Generation = 2 }).ContractHash);
        Assert.NotEqual(hash, Issue(march with { TargetId = "target:another" }).ContractHash);
        Assert.NotEqual(hash, Issue(march with { AccountId = Guid.NewGuid() }).ContractHash);
        Assert.NotEqual(hash, Issue(march with { Troops = [new("slg:levy", 6)] }).ContractHash);
        Assert.NotEqual(hash, Issue(march, Guid.NewGuid()).ContractHash);
        Assert.NotEqual(hash, Issue(march, mode: WorldCombatMode.AutoPve).ContractHash);
    }
    [Fact]
    public void IssuedCargoUsesDataPriorityAndDepartureCapacity()
    {
        var content = Content();
        var encounter = content.Encounters.Single(e => e.Id == "slg:encounter_field_mites");
        content = content with { Encounters = [encounter with { Credits = [new("meta:iron", 40), new("meta:timber", 40), new("meta:grain", 40)] }] };
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        var issued = new WorldCombatContractFactory(content, ContentLoader.Load(data, profileName: "wave-1a"), ContentLoader.Hash(data, false), "wave-1a")
            .Issue(March(), encounter.Id, Run, 1000, WorldCombatMode.DirectPve);
        Assert.Equal(new[] { new DomainCredit("meta:grain", 40), new DomainCredit("meta:timber", 10) }, issued.Award.Credits);
    }
    [Fact]
    public void SourceArmyMutationDoesNotChangeIssuedRoster()
    {
        var march = March(); var issued = Issue(march); march.Troops[0] = new("slg:guard", 99);
        Assert.Equal("slg:levy", Assert.Single(issued.Direct!.Troops).Id);
        Assert.Equal(5, Assert.Single(issued.Direct.Troops).N);
    }
    [Fact]
    public void FormationResearchIsFrozenWithStandingOnlyScope()
    {
        var issued = Issue(March(new() { ["slg:research-formation"] = 1 }));
        var attack = issued.Direct!.Effects.Single(e => e.Ability == MetaAbility.Attack);
        Assert.Equal(20, attack.Raw); Assert.True(attack.StandingEligible); Assert.False(attack.HeroEligible);
        Assert.Equal("slg:research-formation", Assert.Single(attack.Sources).SourceId);
    }
    [Fact]
    public void CoverResearchIncreasesActualStandingSlotHealthWithoutChangingHeroHealth()
    {
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        var catalog = ContentLoader.Load(data, profileName: "wave-1a");
        var executor = new WorldCombatExecutor(new(data, "wave-1a", 4 * 1024 * 1024, TimeSpan.FromSeconds(30), 1));
        var baseline = Issue().Direct!;
        var improved = Issue(March(new() { ["slg:research-cover"] = 1 })).Direct!;
        var effect = improved.Effects.Single(e => e.Ability == MetaAbility.Allies);
        Assert.Equal(20, effect.Bonus);
        Assert.True(effect.StandingEligible);
        Assert.False(effect.HeroEligible);
        var before = new InteractiveSession(catalog, executor.ExpectedOptions(baseline));
        var after = new InteractiveSession(catalog, executor.ExpectedOptions(improved));
        var beforeSlots = before.CaptureWorldCombat().Slots;
        var afterSlots = after.CaptureWorldCombat().Slots;
        Assert.Equal(5, afterSlots.Count);
        for (int i = 0; i < afterSlots.Count; i++)
        {
            Assert.Equal(beforeSlots[i].MaximumHealth * 1020 / 1000, afterSlots[i].MaximumHealth);
            Assert.True(afterSlots[i].MaximumHealth > beforeSlots[i].MaximumHealth);
            Assert.Equal(afterSlots[i].MaximumHealth, afterSlots[i].Health);
        }
        Assert.Equal(before.View.CaptureFrame().Lord.MaxHealth, after.View.CaptureFrame().Lord.MaxHealth);
    }
    [Fact]
    public void UnsupportedEffectScopesAndAutoUnitsFailClosed()
    {
        Assert.Throws<CommandConflictException>(() => Issue(March(new() { ["slg:research-formation"] = 1 }), mode: WorldCombatMode.AutoPve));
        Assert.Throws<CommandConflictException>(() => Issue(March(new() { ["slg:research-formation"] = 1, ["slg:research-hero-drill"] = 1 })));
        Assert.Throws<CommandConflictException>(() => Issue(March(new() { ["slg:unknown"] = 1 })));
        Assert.Throws<CommandConflictException>(() => Issue(March() with { HeroId = "slg:field_guide" }));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(513)]
    public void InvalidOrIneligibleArmyCannotObtainContract(int n) =>
        Assert.Throws<CommandConflictException>(() => Issue(March() with { Troops = [new("slg:levy", n)] }));
    [Fact]
    public void AutoPveUsesFrozenArmyAndNoDirectObjectiveProgress()
    {
        var issued = Issue(mode: WorldCombatMode.AutoPve);
        Assert.Null(issued.Direct); Assert.NotNull(issued.Auto);
        var result = ArmyAuto.Resolve(issued.Auto!);
        Assert.Equal(issued.ContractHash, result.Combat.ContractHash);
        Assert.Empty(result.Combat.Objectives);
    }
    [Fact]
    public void CityRequiresAuthoritativeDefenderEffectsAlongsideRoster()
    {
        var march = March() with { TargetAccountId = Guid.NewGuid() };
        Assert.Throws<CommandConflictException>(() => Factory().Issue(march, "city", Run, 1000, WorldCombatMode.CityPvp, new([], null, null)));
        var issued = Factory().Issue(march, "city", Run, 1000, WorldCombatMode.CityPvp, new([], null, null),
            JsonSerializer.SerializeToElement(new { research = new { levels = new Dictionary<string, int>() } }),
            JsonSerializer.SerializeToElement(new { activeVassalIds = Array.Empty<string>() }));
        Assert.Empty(issued.Award.Credits); Assert.Equal(60, issued.Award.InjuryPercent);
        Assert.Equal(WorldTerminalReason.AutoVictory, ArmyAuto.Resolve(issued.Auto!).Combat.Terminal);
    }
    [Fact]
    public void MissingDispatchSnapshotAndEquippedVassalsAreRejected()
    {
        Assert.Throws<CommandConflictException>(() => Issue(March() with { Snapshot = JsonSerializer.SerializeToElement(new { domains = new { } }) }));
        Assert.Throws<CommandConflictException>(() => Issue(March() with
        {
            Snapshot = JsonSerializer.SerializeToElement(new { combatAccount = new { activeVassalIds = new[] { "meta:vassal" } } })
        }));
    }
    [Fact]
    public void PolicyRejectsUnknownProperties()
    {
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        byte[] policy = File.ReadAllBytes(Path.Combine(data, "opt-in/slg/world-combat-server-v1.json"));
        using var parsed = JsonDocument.Parse(policy);
        var properties = parsed.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        properties["clientReward"] = JsonSerializer.SerializeToElement(999);
        Assert.Throws<JsonException>(() => WorldCombatContent.Parse(JsonSerializer.SerializeToUtf8Bytes(properties),
            File.ReadAllBytes(Path.Combine(data, RecruitPool.DraftPath))));
    }
}
