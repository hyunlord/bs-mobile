using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public sealed class DiagnosticObserver
    {
        private sealed class AttackCounter
        {
            public string Actor = "", Role = "", Source = "";
            public long Attempts, NoTarget, Hits, Requested, Applied, Suppressed, Candidates, Eligible, Cooldown, Knockback;
            public DiagnosticAttackSource Read() => new(Actor, Role, Source, Attempts, NoTarget, Hits, Requested, Applied, Suppressed, Candidates, Eligible, Cooldown, Knockback);
        }
        private sealed class TargetCounter
        {
            public long Attempts, Requested, Applied;
        }
        private readonly DiagnosticOptions options;
        private readonly SortedDictionary<string, AttackCounter> attacks = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, TargetCounter> targets = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> memberTicks = new(StringComparer.Ordinal), deaths = new(StringComparer.Ordinal), membersLost = new(StringComparer.Ordinal), interceptedKinds = new(StringComparer.Ordinal);
        private readonly List<DiagnosticSnapshot> snapshots = new();
        private readonly List<DiagnosticRngPoint> rng = new();
        private long eligible, candidates, intercepts, inRange, unknown, refreshDue, personDamage, guardZero, modifierPrevented, rerouted;
        private long workers, boosted, labor, drafts, returns, weaponExcluded, toolExcluded, charterExcluded, weaponChoices, toolChoices, charterChoices, ranks;
        private long spawnRequested, spawnAdmitted, capRejected, admittedHp, killed, removedHp;
        private WorldState? world;
        private ContentCatalog? catalog;
        internal bool OffenseOff => options.Variant is DiagnosticVariant.OffenseOff or DiagnosticVariant.BothOff;
        internal bool InterceptionOff => options.Variant is DiagnosticVariant.InterceptionOff or DiagnosticVariant.BothOff;

        public DiagnosticObserver(DiagnosticOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (!Enum.IsDefined(typeof(DiagnosticVariant), options.Variant)) { throw new ArgumentException("Unknown diagnostic variant."); }
        }
        internal void Attach(ContentCatalog content, WorldState state)
        {
            if (world is not null) { throw new InvalidOperationException("Diagnostic observer can observe only one simulation."); }
            catalog = content; world = state;
        }
        internal void Trace(WorldState state, long draws, bool completedTick)
        {
            rng.Add(new(state.Tick, draws));
            if (completedTick)
            {
                foreach (var person in state.People.Where(person => person.Health > 0))
                { memberTicks[person.Role] = memberTicks.GetValueOrDefault(person.Role) + person.Members; }
            }
        }
        private AttackCounter Counter(string actor, string role, string source)
        {
            var key = actor + "\0" + role + "\0" + source;
            if (!attacks.TryGetValue(key, out var counter)) { counter = new() { Actor = actor, Role = role, Source = source }; attacks.Add(key, counter); }
            return counter;
        }
        internal void Attack(string actor, string role, string source, int candidateCount, int cooldown)
        {
            var counter = Counter(actor, role, source); counter.Attempts++; counter.Candidates += candidateCount; counter.Cooldown += cooldown;
            if (candidateCount == 0) { counter.NoTarget++; }
        }
        internal void Hit(string actor, string role, string source, int requested, int applied, int suppressed = 0, long knockback = 0)
        {
            var counter = Counter(actor, role, source); counter.Eligible++; counter.Hits++; counter.Requested += requested;
            counter.Applied += applied; counter.Suppressed += suppressed; counter.Knockback += knockback;
        }
        internal string EnemyOpportunity(WorldState state, EnemyState enemy, EnemyDefinition definition, PersonState? candidate)
        {
            eligible++; if (candidate is not null) { candidates++; }
            if (enemy.TargetRefreshTick <= state.Tick) { refreshDue++; }
            Position? target = null;
            if (enemy.LastTarget == "lord") { target = state.Lord; }
            else if (enemy.LastTarget is "seed" or "ripe")
            {
                var farm = state.Farms.FirstOrDefault(farm => farm.Id == enemy.TargetId);
                if (farm is not null && (enemy.LastTarget == "seed" ? farm.Stage <= 1 : farm.Stage == catalog!.Tuning.World.Farms.StageTicks.Length - 1)) { target = enemy.TargetPosition; }
            }
            else if (enemy.LastTarget == "building" && state.Buildings.Any(building => building.Id == enemy.TargetId && building.Health > 0)) { target = enemy.TargetPosition; }
            if (target is null) { unknown++; }
            else if (target.Value.DistanceSquared(enemy.Position) <= (long)definition.Range * definition.Range) { inRange++; }
            return target is null ? "unknown" : enemy.LastTarget;
        }
        internal void Intercept(string cachedKind, int actualDamage, bool guarded, int prevented)
        {
            intercepts++; personDamage += actualDamage; if (guarded) { guardZero++; }
            modifierPrevented += prevented;
            var kind = cachedKind.Length == 0 ? "unknown" : cachedKind;
            interceptedKinds[kind] = interceptedKinds.GetValueOrDefault(kind) + 1;
        }
        internal void Rerouted() { rerouted++; }
        internal void TargetDamage(string kind, int requested, int applied)
        {
            if (!targets.TryGetValue(kind, out var counter)) { counter = new(); targets.Add(kind, counter); }
            counter.Attempts++; counter.Requested += requested; counter.Applied += applied;
        }
        internal void PersonLoss(PersonState person, int previousHealth, int previousMembers)
        {
            membersLost[person.Role] = membersLost.GetValueOrDefault(person.Role) + previousMembers - person.Members;
            if (previousHealth > 0 && person.Health <= 0) { deaths[person.Role] = deaths.GetValueOrDefault(person.Role) + 1; }
        }
        internal void Labor(int workerCount, int boostedCount) { workers += workerCount; boosted += boostedCount; }
        internal void LaborGrowth(long amount) { labor += amount; }
        internal void Draft() { drafts++; }
        internal void Return() { returns++; }
        internal void SlotExcluded(string kind) { if (kind == "weapon") { weaponExcluded++; } else if (kind == "tool") { toolExcluded++; } else { charterExcluded++; } }
        internal void Choice(string kind, int increment) { if (kind == "weapon") { weaponChoices++; } else if (kind == "tool") { toolChoices++; } else { charterChoices++; } ranks += increment; }
        internal void SpawnRequest(int requested, int rejected) { spawnRequested += requested; capRejected += rejected; }
        internal void Spawn(int health) { spawnAdmitted++; admittedHp += health; }
        internal void Kill(int maximumHealth) { killed++; removedHp += maximumHealth; }
        private DiagnosticInterception Interception() => new(eligible, candidates, intercepts, inRange, unknown, refreshDue, personDamage, guardZero, modifierPrevented, rerouted, new SortedDictionary<string, long>(interceptedKinds, StringComparer.Ordinal));
        private DiagnosticGrowth Growth() => new(workers, boosted, labor, drafts, returns, weaponExcluded, toolExcluded, charterExcluded, weaponChoices, toolChoices, charterChoices, ranks);
        private DiagnosticThreat Threat()
        {
            if (world is null || catalog is null) { throw new InvalidOperationException("Observer is not attached."); }
            var tuning = catalog.Tuning.World.Threat;
            var live = world.Enemies.Where(enemy => enemy.Health > 0).ToArray();
            var radius = catalog.Enemies.Values.Max(enemy => enemy.Range);
            var prosperity = world.Food + world.Farms.Count(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1) + world.Buildings.Count(building => building.Built && building.Health > 0);
            return new(spawnRequested, spawnAdmitted, capRejected, admittedHp, killed, removedHp, live.Length, live.Sum(enemy => (long)enemy.Health),
                live.Count(enemy => enemy.Position.DistanceSquared(world.Lord) <= (long)radius * radius), radius, tuning.EnemyCap, tuning.BaseSpawnCount,
                world.Tick / tuning.TimeRampTicks, prosperity, prosperity / tuning.ProsperityDivisor, catalog.Tuning.World.Seasons[world.Season].SpawnMultiplier);
        }
        private DiagnosticAttackSource[] Attacks() => attacks.Values.Select(counter => counter.Read()).ToArray();
        private DiagnosticTargetDamage[] Targets() => targets.Select(pair => new DiagnosticTargetDamage(pair.Key, pair.Value.Attempts, pair.Value.Requested, pair.Value.Applied)).ToArray();
        internal void Sample(bool terminal)
        {
            if (world is null || catalog is null) { throw new InvalidOperationException("Observer is not attached."); }
            var roles = new[] { "peasant", "militia", "vassal", "guard", "returning" };
            var population = roles.Select(role => new DiagnosticPopulation(role, world.People.Count(person => person.Role == role && person.Health > 0),
                world.People.Where(person => person.Role == role && person.Health > 0).Sum(person => person.Members), memberTicks.GetValueOrDefault(role), deaths.GetValueOrDefault(role), membersLost.GetValueOrDefault(role))).ToArray();
            var equipment = world.Equipment.Select(item => new DiagnosticEquipment(item.Id, catalog.Tools.ContainsKey(item.Id) ? "tool" : "weapon", item.Level))
                .Concat(world.Runtime?.Charters.Select(pair => new DiagnosticEquipment(pair.Key, "charter", pair.Value)) ?? Enumerable.Empty<DiagnosticEquipment>()).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            snapshots.Add(new(world.Tick, terminal, world.LordHealth > 0, !terminal ? "none" : world.LordHealth <= 0 ? "death" : "duration", world.Lord.X, world.Lord.Y,
                world.LordHealth, world.Level, world.Food, world.Harvests, world.KillExperience, world.HarvestExperience, world.TaxExperience, world.WeaponDamage,
                world.Tools.Values.Sum(tool => tool.ActivationDamage), world.Tools.Values.Sum(tool => tool.GrowthDamage), world.AllyDamage,
                catalog.Runtime?.Tuning.WeaponSlots ?? catalog.Tuning.World.Progression.WeaponSlots, catalog.Runtime?.Tuning.ToolSlots ?? catalog.Tuning.World.Progression.ToolSlots,
                catalog.Runtime?.Tuning.CharterSlots ?? 0, equipment, population, Attacks(), Interception(), Targets(), Growth(), Threat()));
        }
        public DiagnosticResult Result() => new(options.Variant, snapshots.ToArray(), rng.ToArray(), Attacks(), Interception(), Targets(), Growth(), Threat());
    }
}
