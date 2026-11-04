using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneMixedMovementTests
{
    [Theory]
    [InlineData(false, 300)]
    [InlineData(true, 300)]
    [InlineData(true, 800)]
    public void TauntApproachUsesMinimumOfWetAndZoneFromOriginalSpeed(bool wet, int slow)
    {
        var f = HeroTauntRuntimeTests.Fixture(configure: a =>
        {
            var taunt = a.Definitions[0] with { Taunt = a.Definitions[0].Taunt! with { StopRadius = 0 } };
            var enemy = taunt.Taunt!.EnemyPolicies[0].EnemyId;
            var zone = new HeroActiveDefinition("test:zone", 1, new string('A', 64), HeroActiveKind.ZoneSlow, null,
                Zone: HeroZoneStateTests.Parameters(enemy) with { TellTicks = 0, SlowPermille = slow });
            var definitions = new[] { taunt, zone };
            return a with { Definitions = definitions, Skills = definitions.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        }, configureWorld: s => s with { Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Speed = 10 } } } });
        var run = new HeroTauntRuntimeTests.Run(f); run.Step();
        var enemyState = Assert.Single(run.World.Enemies); var before = enemyState.Position;
        if (wet) { run.World.WaveRuntime!.EnemyActions[enemyState.Id].WetUntil = 20; }
        run.Step();
        Assert.Single(run.Session.CaptureHeroActiveEffects().Taunts);
        Assert.Single(run.Session.CaptureHeroZones().Sources);
        var wetSpeed = wet ? Math.Max(1, 10 / f.Catalog.WaveRuntime!.Behavior.WetSpeedDivisor) : 10;
        var expected = Math.Min(wetSpeed, Math.Max(1, 10 * (1000 - slow) / 1000));
        Assert.Equal(before.X - expected, enemyState.Position.X);
        Assert.Equal(before.Y, enemyState.Position.Y);
    }
}
