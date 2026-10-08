using System;
using System.Collections.Generic;

using System.Linq;
namespace SowSiege.Core
{
    internal sealed partial class CombatSystem
    {
        private void SpawnScheduledEnemies()
        {
            if (catalog.FirstPlayable is not { } definition || world.FirstPlayable is not { } state) { return; }
            foreach (var pair in definition.Enemies.Where(p => p.Value.Rank != "normal").OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var next = state.NextEnemySpawn.GetValueOrDefault(pair.Key, pair.Value.FirstSpawnTick);
                if (world.Tick < next) { continue; }
                if (world.Enemies.Count >= catalog.Tuning.World.Threat.EnemyCap)
                {
                    if (pair.Value.Rank != "boss")
                    {
                        continue;
                    }

                    var displaced = world.Enemies.Where(e => definition.Enemies[e.Definition].Rank != "boss").OrderByDescending(e => e.Id).FirstOrDefault();
                    if (displaced is null)
                    {
                        continue;
                    }

                    world.Enemies.Remove(displaced); state.Count("enemy:" + pair.Value.Rank + ":reserved-slot");
                }
                var enemy = catalog.Enemies[pair.Key]; var map = catalog.Tuning.World.Map;
                var entity = new EnemyState { Id = world.AllocateId(), Definition = pair.Key, Position = new(Math.Clamp(world.Lord.X + map.EstateRadius, 0, map.Width), Math.Clamp(world.Lord.Y + map.EstateRadius, 0, map.Height)), Health = enemy.Health };
                world.Enemies.Add(entity); world.SpawnedEnemies++; diagnostics?.SpawnRequest(1, 0); diagnostics?.Spawn(enemy.Health);
                state.NextEnemySpawn[pair.Key] = pair.Value.RepeatTicks > 0 ? checked(world.Tick + pair.Value.RepeatTicks) : int.MaxValue;
                state.BossEntities[pair.Key] = entity.Id; state.DefeatedBosses.Remove(pair.Key); state.Count("enemy:" + pair.Value.Rank + ":spawn");
                interactive?.Experience(world.Tick, PresentationKind.BossWarning, pair.Key, entity.Position, enemy.Health);
            }
        }
    }
}
