using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    internal sealed class CombatSystem
    {
        private readonly DiagnosticObserver? diagnostics;
        private readonly ContentCatalog catalog;
        private readonly RunOptions options;
        private readonly WorldState world;
        private readonly TrackedRandom random;
        private readonly SpatialHash spatial;
        private readonly RuntimeSystem? runtime;
        private readonly ExperimentSystem? experiment;

        public CombatSystem(ContentCatalog catalog, RunOptions options, WorldState world, TrackedRandom random, SpatialHash spatial, RuntimeSystem? runtime = null, ExperimentSystem? experiment = null, DiagnosticObserver? diagnostics = null)
        {
            this.diagnostics = diagnostics;
            this.catalog = catalog;
            this.options = options;
            this.world = world;
            this.random = random;
            this.spatial = spatial;
            this.runtime = runtime;
            this.experiment = experiment;
            enemyTypes = catalog.Enemies.Values.OrderBy(enemy => enemy.Id, StringComparer.Ordinal).ToArray();
        }

        private readonly EnemyDefinition[] enemyTypes;

        public void MoveLord()
        {
            var map = catalog.Tuning.World.Map;
            if (world.Tick % map.WaypointPeriodTicks == 0)
            {
                var ripe = world.Farms.Where(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1)
                    .OrderBy(farm => farm.Position.DistanceSquared(world.Lord)).ThenBy(farm => farm.Id).FirstOrDefault();
                if (ripe is not null && options.Policy is not "weapon") { world.Destination = ripe.Position; }
                else
                {
                    var offsetX = random.Next(map.EstateRadius + 1) * ((random.Next(int.MaxValue) & 1) == 0 ? -1 : 1);
                    var offsetY = random.Next(map.EstateRadius + 1) * ((random.Next(int.MaxValue) & 1) == 0 ? -1 : 1);
                    world.Destination = new(Math.Clamp(world.Estate.X + offsetX, 0, map.Width), Math.Clamp(world.Estate.Y + offsetY, 0, map.Height));
                }
            }
            world.Lord = world.Lord.MoveToward(world.Destination, map.LordSpeed);
        }

        public void SpawnAndMoveEnemies()
        {
            var tuning = catalog.Tuning.World.Threat;
            if (world.Tick % tuning.SpawnPeriodTicks == 0 && options.Scenario != "load")
            {
                var prosperity = world.Food + world.Farms.Count(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1)
                    + world.Buildings.Count(building => building.Built && building.Health > 0);
                var count = (tuning.BaseSpawnCount + world.Tick / tuning.TimeRampTicks + prosperity / tuning.ProsperityDivisor)
                    * catalog.Tuning.World.Seasons[world.Season].SpawnMultiplier;
                diagnostics?.SpawnRequest(count, Math.Max(0, count - Math.Max(0, tuning.EnemyCap - world.Enemies.Count)));
                for (var index = 0; index < count && world.Enemies.Count < tuning.EnemyCap; index++) { Spawn(); }
            }
            foreach (var enemy in world.Enemies)
            {
                var definition = catalog.Enemies[enemy.Definition];
                var destination = Target(enemy, definition);
                enemy.Position = enemy.Position.MoveToward(destination, definition.Speed);
            }
        }

        private Position Target(EnemyState enemy, EnemyDefinition definition)
        {
            if (enemy.TargetRefreshTick > world.Tick) { return enemy.LastTarget == "lord" ? world.Lord : enemy.TargetPosition; }
            enemy.TargetRefreshTick = world.Tick + catalog.Tuning.World.Threat.ContactPeriodTicks;
            enemy.LastTarget = "lord";
            var bestDistance = long.MaxValue;
            if (definition.Target is "seed" or "ripe")
            {
                foreach (var farm in world.Farms)
                {
                    if (!(definition.Target == "seed" ? farm.Stage <= 1 : farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1)) { continue; }
                    var distance = farm.Position.DistanceSquared(enemy.Position);
                    if (distance >= bestDistance) { continue; }
                    bestDistance = distance;
                    enemy.LastTarget = definition.Target;
                    enemy.TargetPosition = farm.Position;
                    enemy.TargetId = farm.Id;
                }
            }
            if (definition.Target == "building")
            {
                foreach (var building in world.Buildings)
                {
                    if (!building.Built || building.Health <= 0) { continue; }
                    var distance = building.Position.DistanceSquared(enemy.Position);
                    if (distance >= bestDistance) { continue; }
                    bestDistance = distance;
                    enemy.LastTarget = "building";
                    enemy.TargetPosition = building.Position;
                    enemy.TargetId = building.Id;
                }
            }
            return enemy.LastTarget == "lord" ? world.Lord : enemy.TargetPosition;
        }

        public void Activate(EquipmentState equipment, Activation activation, ToolLedger? ledger)
        {
            equipment.ReadyTick = world.Tick + (runtime?.Modify("attack-cooldown", activation.CooldownTicks, new(world.Lord, equipment.Id), 1) ?? activation.CooldownTicks);
            if (ledger is not null) { ledger.Activations++; }
            var candidates = spatial.Query(world.Lord, activation.Range).OrderBy(enemy => enemy.Position.DistanceSquared(world.Lord)).ThenBy(enemy => enemy.Id).ToArray();
            var nearest = candidates.FirstOrDefault();
            var actor = ledger is null ? "weapon" : "tool-activation";
            diagnostics?.Attack(actor, "", equipment.Id, candidates.Length, equipment.ReadyTick - world.Tick);
            foreach (var enemy in candidates)
            {
                var hits = activation.Shape switch
                {
                    "projectile" => enemy == nearest,
                    "melee" => world.Destination.X >= world.Lord.X ? enemy.Position.X >= world.Lord.X : enemy.Position.X <= world.Lord.X,
                    "orbit" => enemy.Position.DistanceSquared(world.Lord) >= (long)(activation.Range >> 1) * (activation.Range >> 1),
                    "wave" => true,
                    _ => throw new InvalidOperationException($"Unknown attack shape {activation.Shape}")
                };
                if (!hits) { continue; }
                var requested = checked((activation.Damage * equipment.Level + random.Next(catalog.Tuning.DamageRollMax)) * catalog.Heroes[options.HeroId].DamageMultiplier);
                requested = runtime?.Modify("attack-damage", requested, new(world.Lord, equipment.Id, enemy)) ?? requested;
                var dealt = Math.Min(enemy.Health, requested);
                enemy.Health -= dealt;
                if (ledger is null) { world.WeaponDamage += dealt; } else { ledger.ActivationDamage += dealt; }
                var knockback = runtime?.Modify("attack-knockback", activation.Knockback, new(world.Lord, equipment.Id, enemy)) ?? activation.Knockback;
                var beforeKnockback = enemy.Position;
                if (knockback > 0)
                {
                    var map = catalog.Tuning.World.Map;
                    spatial.Move(enemy, new(Math.Clamp(enemy.Position.X + Math.Sign(enemy.Position.X - world.Lord.X) * knockback, 0, map.Width),
                        Math.Clamp(enemy.Position.Y + Math.Sign(enemy.Position.Y - world.Lord.Y) * knockback, 0, map.Height)));
                }
                diagnostics?.Hit(actor, "", equipment.Id, requested, dealt, knockback: Math.Abs((long)enemy.Position.X - beforeKnockback.X) + Math.Abs((long)enemy.Position.Y - beforeKnockback.Y));
            }
            runtime?.Emit("attack", new(world.Lord, equipment.Id, nearest));
        }

        public void ResolveEnemyAttacks()
        {
            foreach (var enemy in world.Enemies)
            {
                if (enemy.Health <= 0 || enemy.AttackTick > world.Tick) { continue; }
                var definition = catalog.Enemies[enemy.Definition];
                var defender = world.People.Where(person => person.Health > 0 && person.Position.DistanceSquared(enemy.Position) <= (long)definition.Range * definition.Range)
                                .OrderBy(person => person.Position.DistanceSquared(enemy.Position)).ThenBy(person => person.Id).FirstOrDefault();
                var cachedKind = diagnostics?.EnemyOpportunity(world, enemy, definition, defender) ?? "unknown";
                if (defender is not null && diagnostics?.InterceptionOff != true)
                {
                    var incoming = runtime?.Modify("worker-incoming-damage", definition.Damage, new(defender.Position, defender.Source, enemy, Person: defender)) ?? definition.Damage;
                    var prevented = Math.Max(0, definition.Damage - incoming);
                    var guarded = runtime is not null && runtime.Entity(defender.Id).ArrivalGuardUsed && runtime.Entity(defender.Id).HoldUntil > world.Tick;
                    if (guarded) { incoming = 0; }
                    var previousHealth = defender.Health; var previousMembers = defender.Members;
                    defender.Health = Math.Max(0, defender.Health - incoming);
                    defender.Members = Math.Min(defender.Members, (defender.Health + catalog.Tuning.World.People.VassalHealth - 1) / catalog.Tuning.World.People.VassalHealth);
                    diagnostics?.Intercept(cachedKind, previousHealth - defender.Health, guarded, prevented);
                    diagnostics?.PersonLoss(defender, previousHealth, previousMembers);
                    diagnostics?.TargetDamage("person", definition.Damage, previousHealth - defender.Health);
                    enemy.AttackTick = world.Tick + definition.AttackCooldownTicks;
                    continue;
                }
                var target = Target(enemy, definition);
                if (enemy.Position.DistanceSquared(target) > (long)definition.Range * definition.Range) { continue; }
                enemy.AttackTick = world.Tick + definition.AttackCooldownTicks;
                if (defender is not null && diagnostics?.InterceptionOff == true) { diagnostics.Rerouted(); }
                if (enemy.LastTarget is "seed" or "ripe")
                {
                    var farm = world.Farms.First(farm => farm.Id == enemy.TargetId);
                    if (!(enemy.LastTarget == "seed" ? farm.Stage <= 1 : farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1)) { enemy.TargetRefreshTick = 0; continue; }
                    diagnostics?.TargetDamage(enemy.LastTarget, definition.Damage, 0);
                    if (runtime?.ShieldFarm(farm, enemy) == true) { continue; }
                    experiment?.CloseRipe(farm, "destroyed");
                    RemainsSystem.Destroyed(world, farm);
                    farm.Stage = 0; farm.Progress = 0; farm.Fertility = 0;
                }
                else if (enemy.LastTarget == "building")
                {
                    var building = world.Buildings.First(building => building.Id == enemy.TargetId);
                    if (building.Health <= 0) { enemy.TargetRefreshTick = 0; continue; }
                    var previousHealth = building.Health;
                    building.Health = Math.Max(0, building.Health - (runtime?.BuildingDamage(building, enemy, definition.Damage) ?? definition.Damage));
                    diagnostics?.TargetDamage("building", definition.Damage, previousHealth - building.Health);
                    if (building.Health == 0) { world.Ruins++; }
                }
                else
                {
                    if (world.LordHealth <= 0) { continue; }
                    var previousHealth = world.LordHealth;
                    world.LordHealth = Math.Max(0, world.LordHealth - definition.Damage);
                    diagnostics?.TargetDamage("lord", definition.Damage, previousHealth - world.LordHealth);
                    if (world.LordHealth == 0) { world.DeathCause = definition.Id; }
                }
            }
        }

        public void ResolveDeaths()
        {
            foreach (var enemy in world.Enemies.Where(enemy => enemy.Health <= 0))
            {
                diagnostics?.Kill(catalog.Enemies[enemy.Definition].Health);
                var experience = catalog.Enemies[enemy.Definition].Experience;
                world.Experience += experience;
                world.KillExperience += experience;
                runtime?.Experience("weapon", experience);
                runtime?.Emit("kill", new(enemy.Position, Enemy: enemy));
                if (world.Remains is not null) { RemainsSystem.Create(world, enemy); continue; }
                var nearest = world.Farms.OrderBy(farm => farm.Position.DistanceSquared(enemy.Position)).ThenBy(farm => farm.Id).FirstOrDefault();
                if (nearest is not null) { nearest.Fertility += catalog.Tuning.World.Farms.FertilityPerKill; }
            }
            world.Enemies.RemoveAll(enemy => enemy.Health <= 0);
            world.People.RemoveAll(person => person.Health <= 0);
        }

        public void FillLoad()
        {
            world.Enemies.RemoveAll(enemy => enemy.Health <= 0);
            diagnostics?.SpawnRequest(Math.Max(0, catalog.Tuning.World.Load.Enemies - world.Enemies.Count), 0);
            while (world.Enemies.Count < catalog.Tuning.World.Load.Enemies) { Spawn(); }
        }

        private void Spawn()
        {
            var map = catalog.Tuning.World.Map;
            var inset = catalog.Tuning.World.Threat.SpawnInset;
            var definition = enemyTypes[random.Next(enemyTypes.Length)];
            Position position;
            if (options.Scenario == "load") { position = new(random.Next(map.Width), random.Next(map.Height)); }
            else
            {
                var x = random.Next(map.Width);
                var y = (random.Next(int.MaxValue) & 1) == 0 ? inset : map.Height - inset;
                position = new(x, y);
            }
            world.Enemies.Add(new() { Id = world.AllocateId(), Definition = definition.Id, Position = position, Health = definition.Health });
            world.SpawnedEnemies++;
            diagnostics?.Spawn(definition.Health);
        }
    }

}
