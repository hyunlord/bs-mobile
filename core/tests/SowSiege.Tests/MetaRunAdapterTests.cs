using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;

public sealed class MetaRunAdapterTests
{
    [Fact]
    public void AbsentMetaReturnsExactHistoricalCatalogAndHash()
    {
        var catalog = InteractiveTests.Catalog();
        Assert.Same(catalog, MetaRunAdapter.ProjectCatalog(catalog, null, null, null));
        Assert.Equal("B404B93190CCB5A9BDDC23F99CC952A796B7AF3127F253A14F49646CC9796D13", FirstPlayableTests.Session(catalog).ComputeStateHash());
    }
}

public sealed class MetaRunProjectionTests
{
    private static ContentCatalog Base() => SowSiege.Sim.ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "first-playable");
    private static (MetaCatalog Meta, MetaState State, MetaRunPlan Plan) Inputs(ContentCatalog c, MetaAbility ability, int bonus, bool all = true)
    {
        var ids = all ? c.Weapons.Keys.Concat(c.Tools.Keys).Concat(c.Runtime!.Items.Keys).ToArray() : c.Heroes.Values.Select(h => h.StartingTool).Append(c.Tuning.World.Progression.StartingWeapon).ToArray();
        var chapter = new MetaChapter("test:chapter", 1, "Test", "Test", 1100, 900, 7, 450, 17, 1200, 1300, 1100,
            c.FirstPlayable!.Enemies.Where(p => p.Value.Rank == "normal").Take(2).Select(p => p.Key).ToArray(), c.FirstPlayable.Enemies.Single(p => p.Value.Rank == "boss").Key, [], new());
        var vassal = new MetaVassal("test:vassal", "Test", "Test", "test:art", ability, true, bonus, 0, 0, 20, 1, 100, [10], new(), new());
        var economy = new MetaEconomy(500, 300, 0, 100, 60, 3600, 600, 7200, new(), 10, 5, 5, 1, 1, 6, 1, 0, []);
        var meta = new MetaCatalog(1, [], [chapter], [], [vassal], [], ids, economy);
        var state = new MetaState(3, 0, "", new(), new(), new() { [vassal.Id] = new(1, 0, 0, true) }, new(), [], ids, [vassal.Id], 2, new(1, chapter.Id, 30000), -1, -1, 0, 0);
        return (meta, state, new(state, state.PendingRun!));
    }
    private static ContentCatalog Project(ContentCatalog c, MetaAbility ability, int bonus, bool all = true)
    {
        var input = Inputs(c, ability, bonus, all);
        return MetaRunAdapter.ProjectCatalog(c, input.Meta, input.State, input.Plan);
    }
    [Fact]
    public void ChapterAndUnlocksAffectActualRunWithoutMutatingBase()
    {
        var c = Base(); var initialHash = FirstPlayableTests.Session(c).ComputeStateHash();
        var projected = Project(c, MetaAbility.Attack, 0, false); var s = FirstPlayableTests.Session(projected);
        Assert.Equal(7, s.Simulation.World.Buildings.Count);
        Assert.Equal(projected.Tuning.World.Map.Width, s.View.CaptureFrame().MapWidth);
        Assert.NotEqual(c.Tuning.World.Map.Width, s.View.CaptureFrame().MapWidth);
        Assert.Empty(projected.Runtime!.Items);
        Assert.All(projected.Runtime.Evolutions.Values, e => Assert.All(e.InputIds, id => Assert.True(projected.Weapons.ContainsKey(id) || projected.Tools.ContainsKey(id))));
        s.Apply(new(0, 0, ReplayCommandKind.GrantLevel));
        Assert.All(s.View.CaptureCards().Cards, id => Assert.True(projected.Weapons.ContainsKey(id) || projected.Tools.ContainsKey(id) || projected.Runtime.Charters.ContainsKey(id)));
        s.Apply(new(1, 0, ReplayCommandKind.ChooseCard, CardId: s.View.CaptureCards().Cards[0]));
        s.Apply(new(2, 0, ReplayCommandKind.Advance));
        Assert.All(s.View.CaptureFrame().Enemies, e => Assert.Contains(e.DefinitionId, projected.Enemies.Keys));
        Assert.Equal(initialHash, FirstPlayableTests.Session(c).ComputeStateHash());
    }
    [Fact]
    public void SelectedBossScheduleRunsAndFactsCountLivingComposition()
    {
        var c = Project(Base(), MetaAbility.Health, 0); var s = FirstPlayableTests.Session(c); var w = s.Simulation.World;
        var boss = c.FirstPlayable!.Enemies.Single(p => p.Value.Rank == "boss");
        w.Tick = boss.Value.FirstSpawnTick;
        s.Apply(new(s.NextSequence, w.Tick, ReplayCommandKind.Advance));
        Assert.Contains(s.View.CaptureFirstPlayable()!.Bosses, b => b.DefinitionId == boss.Key);
        var frame = s.View.CaptureFrame(); var fp = s.View.CaptureFirstPlayable()!;
        var summary = new RunSummary(30000, frame.Tick, false, "abandoned", frame.Level, 0, 0, 0, 0, 0, 0, 0, s.ComputeStateHash());
        var facts = MetaRunAdapter.FromInteractive(summary, frame, fp, true);
        Assert.True(facts.Abandoned); Assert.False(facts.Survived);
        Assert.Equal(frame.People.Sum(p => p.Members), facts.People);
        Assert.Equal(frame.Buildings.Count(b => b.Built && b.Health > 0), facts.Buildings);
        Assert.Equal(fp.Kills, facts.Kills); Assert.Equal(frame.DurationTicks, facts.DurationTicks);
    }
    [Theory]
    [InlineData(MetaAbility.Attack)]
    [InlineData(MetaAbility.Health)]
    [InlineData(MetaAbility.Movement)]
    [InlineData(MetaAbility.Growth)]
    [InlineData(MetaAbility.Allies)]
    [InlineData(MetaAbility.Experience)]
    public void EveryAbilityChangesItsActualSimulationOutcome(MetaAbility ability)
    {
        var c = Base(); var normal = Arena(Project(c, ability, 0), ability); var boosted = Arena(Project(c, ability, 1000), ability);
        Assert.True(boosted > normal, $"{ability}: normal={normal}, boosted={boosted}");
    }
    private static long Arena(ContentCatalog c, MetaAbility ability)
    {
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World;
        if (ability == MetaAbility.Health)
        {
            return s.View.CaptureFrame().Lord.Health;
        }

        if (ability == MetaAbility.Movement)
        {
            var x = w.Lord.X; s.Apply(new(0, 0, ReplayCommandKind.Advance, new(1000, 0))); return w.Lord.X - x;
        }
        w.People.Clear(); w.Buildings.Clear(); w.Farms.Clear();
        if (ability == MetaAbility.Growth)
        {
            w.Equipment.Clear(); var farm = new FarmState { Id = w.AllocateId(), Position = w.Lord, Source = c.Tools.Keys.First() }; w.Farms.Add(farm);
            var ticks = 0;
            while (farm.Stage == 0 && ticks++ < 10000)
            {
                s.Apply(new(s.NextSequence, w.Tick, ReplayCommandKind.Advance));
            }

            return 10000 - ticks;
        }
        var enemy = c.Enemies.Values.First();
        w.Enemies.Add(new() { Id = w.AllocateId(), Definition = enemy.Id, Position = new(w.Lord.X + 10, w.Lord.Y), Health = ability == MetaAbility.Experience ? 1 : 1000000 });
        if (ability == MetaAbility.Allies)
        {
            w.Equipment.Clear(); w.People.Add(new() { Id = w.AllocateId(), Position = w.Lord, Destination = w.Lord, Role = "vassal", Health = 1000 });
        }
        for (var i = 0; i < 10 && s.Status == RunStatus.Running; i++)
        {
            s.Apply(new(s.NextSequence, w.Tick, ReplayCommandKind.Advance));
        }

        return ability == MetaAbility.Allies ? w.AllyDamage : ability == MetaAbility.Experience ? w.KillExperience : w.WeaponDamage;
    }
    [Fact]
    public void SeparateProjectionsReplayIdentically()
    {
        var a = FirstPlayableTests.Session(Project(Base(), MetaAbility.Attack, 70)); var b = FirstPlayableTests.Session(Project(Base(), MetaAbility.Attack, 70));
        for (var i = 0; i < 200; i++)
        {
            var command = a.Status == RunStatus.AwaitingCard ? new ReplayCommand(a.NextSequence, a.View.CaptureFrame().Tick, ReplayCommandKind.ChooseCard, CardId: a.View.CaptureCards().Cards[0]) : new ReplayCommand(a.NextSequence, a.View.CaptureFrame().Tick, ReplayCommandKind.Advance, new(1000, 0));
            a.Apply(command); b.Apply(command);
        }
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }
}
