using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneSlowTests
{
    [Theory]
    [InlineData(300, 5)]
    [InlineData(600, 4)]
    [InlineData(1000, 2)]
    [InlineData(0, 5)]
    public void WetAndActiveSlowUseOriginalSpeedIndependently(int raw, int expected)
    {
        var f = HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0, SlowPermille = raw }));
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies);
        Assert.Equal(expected, f.State.MovementSpeed(enemy.Id, 10, 5));
        Assert.Equal(10, f.Options.WorldCombat!.Snapshot.Cohorts[0].Stats.Speed);
        Assert.Equal(Math.Min(raw, 800), f.State.CaptureSlow(enemy.Id).CappedPermille);
    }

    [Fact]
    public void StrongExpiryRevealsRetainedWeakSourceWithoutCompounding()
    {
        var f = HeroZoneStateTests.Fixture(2, configure: a =>
        {
            a = HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0, SlowPermille = 300, DurationTicks = 8 });
            return HeroZoneStateTests.Change(a, 1, p => p with { TellTicks = 0, SlowPermille = 600, DurationTicks = 3 });
        });
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies); Assert.Equal(2, f.State.Capture().Sources.Count);
        Assert.Equal(4, f.State.MovementSpeed(enemy.Id, 10, 10)); Assert.Equal(new HeroSlowFrame(600, 600, 3), f.State.CaptureSlow(enemy.Id));
        f.Phase(3); Assert.Single(f.State.Capture().Sources);
        Assert.Equal(7, f.State.MovementSpeed(enemy.Id, 10, 10)); Assert.Equal(new HeroSlowFrame(300, 300, 8), f.State.CaptureSlow(enemy.Id));
        f.Phase(8); Assert.Empty(f.State.Capture().Sources); Assert.Equal(5, f.State.MovementSpeed(enemy.Id, 10, 5));
    }

    [Fact]
    public void EqualStrengthKeepsSeparateSourcesAndMaximumExpiryNotSum()
    {
        var f = HeroZoneStateTests.Fixture(2, configure: a =>
        {
            a = HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0, SlowPermille = 600, DurationTicks = 3 });
            return HeroZoneStateTests.Change(a, 1, p => p with { TellTicks = 0, SlowPermille = 600, DurationTicks = 8 });
        });
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies);
        Assert.Equal(2, f.State.Capture().Sources.Count); Assert.Equal(8, f.State.CaptureSlow(enemy.Id).ValidUntilTick);
        Assert.Equal(4, f.State.MovementSpeed(enemy.Id, 10, 10));
        f.Phase(3); Assert.Single(f.State.Capture().Sources); Assert.Equal(8, f.State.CaptureSlow(enemy.Id).ValidUntilTick);
        enemy.Position = new(enemy.Position.X + 101, enemy.Position.Y); f.State.NotifyEnemyChanged(enemy);
        Assert.Equal(new HeroSlowFrame(0, 0, 0), f.State.CaptureSlow(enemy.Id));
    }

    [Fact]
    public void PositiveMinimumPreventsRootingWhileOriginallyStationaryEnemyRemainsZero()
    {
        var f = HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0, p => p with
        { TellTicks = 0, SlowPermille = 1000, SlowCapPermille = 1000 }),
            configureWorld: s => s with { Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Speed = 1 } } } });
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies);
        Assert.Equal(1, f.State.MovementSpeed(enemy.Id, 1, 1)); Assert.Equal(0, f.State.MovementSpeed(enemy.Id, 0, 0));
    }

    [Fact]
    public void InvalidWetInputCannotMasqueradeAsOriginalSpeed()
    {
        var f = HeroZoneStateTests.Fixture(); var id = Assert.Single(f.World.Enemies).Id;
        Assert.Throws<ArgumentException>(() => f.State.MovementSpeed(id, 5, 10));
        Assert.Throws<ArgumentException>(() => f.State.MovementSpeed(id, -1, 0));
        Assert.Throws<ArgumentException>(() => f.State.MovementSpeed(id, 10, -1));
    }

    [Fact]
    public void RemovingNarrowStrongSourceImmediatelyExposesWideWeakSource()
    {
        var f = HeroZoneStateTests.Fixture(2, configure: a =>
        {
            a = HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0, SlowPermille = 600, Radius = 10 });
            return HeroZoneStateTests.Change(a, 1, p => p with { TellTicks = 0, SlowPermille = 300, Radius = 100 });
        });
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies);
        enemy.Position = new(enemy.Position.X + 20, enemy.Position.Y); f.State.NotifyEnemyChanged(enemy);
        Assert.Single(f.State.Capture().Sources); Assert.Equal(300, f.State.CaptureSlow(enemy.Id).RawPermille);
        Assert.Equal(7, f.State.MovementSpeed(enemy.Id, 10, 10));
    }

    [Fact]
    public void CanonicalStateBytesIgnoreEnemyListOrderAndCaptureDoesNotAliasLiveSources()
    {
        var first = HeroZoneStateTests.Fixture(enemies: 2, configure: a => HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0 }));
        var second = HeroZoneStateTests.Fixture(enemies: 2, configure: a => HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0 }));
        second.World.Enemies.Reverse(); first.Phase(0); second.Phase(0); Assert.Equal(first.Bytes(), second.Bytes());
        var snapshot = first.State.Capture(); Assert.Equal(2, snapshot.Sources.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<HeroSlowSourceFrame>)snapshot.Sources).Clear());
        first.State.Clear(); Assert.Empty(first.State.Capture().Sources); Assert.Equal(2, snapshot.Sources.Count);
        Assert.NotEqual(first.Bytes(), second.Bytes());
    }
}
