using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WavePrimitiveFixtureTests
{
    [Fact]
    public void DataOnlyAttackAndEventSubscriptionProbesProduceTheirDeclaredEffects()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures/wave-primitives/probes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        using var input = File.OpenRead(path);
        var catalog = WavePrimitiveFixtures.Load(data, input);
        var records = document.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(2, records.Length);
        var weapon = records.Single(r => r.GetProperty("kind").GetString() == "weapon").GetProperty("id").GetString()!;
        var item = records.Single(r => r.GetProperty("kind").GetString() == "item").GetProperty("id").GetString()!;
        var original = ContentLoader.Load(data, false, "wave-1a");
        Assert.Equal(28, WavePrimitiveSupport.Resolve(original.WaveRuntime!).Count);
        Assert.Equal(30, WavePrimitiveSupport.Resolve(catalog.WaveRuntime!).Count);
        Assert.NotEqual(PortableStateCodec.HashCatalog(original), PortableStateCodec.HashCatalog(catalog));
        var enemyId = catalog.Enemies.Keys.First();
        (WaveRuntimeSystem System, WorldState World, EnemyState Front, EnemyState Rear) Arena(string equipment)
        {
            var world = new WorldState { Lord = new(5000, 5000), LordHealth = 1000 };
            world.Equipment.Add(new() { Id = equipment });
            var front = new EnemyState { Id = 900, Definition = enemyId, Position = new(5200, 5000), Health = 10000 };
            var rear = new EnemyState { Id = 901, Definition = enemyId, Position = new(4800, 5000), Health = 10000 };
            world.Enemies.Add(front); world.Enemies.Add(rear);
            return (new(catalog, world, new(30000, true), null), world, front, rear);
        }
        var slam = Arena(weapon);
        slam.System.Tick(slam.World.Lord);
        Assert.True(slam.Front.Health < 10000);
        Assert.Equal(10000, slam.Rear.Health);
        Assert.Empty(slam.World.WaveRuntime!.Work);
        Assert.Contains(slam.World.WaveRuntime.Events, e => e.Kind == "attack" && e.Source == weapon);

        var linked = catalog.WaveRuntime!.Items[item].EquipmentIds.Single();
        var owned = Arena(linked); owned.World.WaveRuntime!.Items.Add(item);
        var unowned = Arena(linked);
        for (var tick = 0; tick < 100; tick++)
        {
            owned.World.Tick = tick; unowned.World.Tick = tick;
            owned.System.Tick(owned.World.Lord); unowned.System.Tick(unowned.World.Lord);
            if (owned.World.WaveRuntime.Detours.Count == 0) { continue; }
            var events = owned.World.WaveRuntime.Events;
            var routed = events.First(e => e.Kind == "hit-detour");
            Assert.Contains(events, e => e.Kind == "hit" && e.SubjectId == routed.SubjectId && e.Id < routed.Id);
            Assert.Equal(events.Count(e => e.Kind == "hit"), events.Count(e => e.Kind == "hit-detour"));
            Assert.Empty(unowned.World.WaveRuntime!.Detours);
            var route = owned.World.WaveRuntime.Detours[0];
            var pursuer = catalog.WaveRuntime.Enemies.Keys.First(id => catalog.WaveRuntime.Programs![id].Is("unit:enemy-pressure", "movement", "pursue"));
            var followerPosition = new Position(route.Position.X + route.Radius + 1000, route.Position.Y);
            var following = new EnemyState { Id = 999, Definition = pursuer, Position = followerPosition, Health = 10000 };
            var direct = new EnemyState { Id = 999, Definition = pursuer, Position = followerPosition, Health = 10000 };
            owned.World.Enemies.Add(following); unowned.World.Enemies.Add(direct);
            new WaveEnemySystem(catalog, owned.World, new(30000, true), null).Tick();
            new WaveEnemySystem(catalog, unowned.World, new(30000, true), null).Tick();
            Assert.NotEqual(direct.Position, following.Position);
            return;
        }
        Assert.Fail("The data-defined subscription never observed an actual projectile hit.");
    }
}
