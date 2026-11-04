using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    internal sealed class HeroZoneState
    {
        private sealed class Instance
        {
            internal bool Used;
            internal int Definition, Start, ActiveStart, End, Count;
            internal long Sequence;
            internal Position Center;
            internal readonly int[] Targets, ValidUntil;
            internal Instance(int capacity) { Targets = new int[capacity]; ValidUntil = new int[capacity]; }
        }
        private readonly struct Candidate
        {
            internal readonly int Id;
            internal readonly long Distance;
            internal Candidate(int id, long distance) { Id = id; Distance = distance; }
        }
        private sealed class CandidateOrder : IComparer<Candidate>
        {
            internal static readonly CandidateOrder Value = new();
            public int Compare(Candidate a, Candidate b)
            { var distance = a.Distance.CompareTo(b.Distance); return distance != 0 ? distance : a.Id.CompareTo(b.Id); }
        }
        private readonly HeroActiveOptions options;
        private readonly HeroActiveDefinition[] definitions;
        private readonly int[] ready;
        private readonly Instance[] instances;
        private readonly Candidate[] candidates;
        private readonly HashSet<string> bosses = new(StringComparer.Ordinal);
        private readonly int cap, minimumSpeed;
        private int count, tick;
        internal long RuntimeByteBound { get; }

        internal HeroZoneState(ContentCatalog catalog, InteractiveOptions run, HeroActiveOptions options)
        {
            if (catalog is null || run is null || options is null) { throw new ArgumentNullException(nameof(options)); }
            if (options.OwnerId != options.HeroId || options.Definitions is null || options.Definitions.Count > WorldCombatCodec.MaximumEntries)
            { throw new ArgumentException("Invalid zone owner or definition count."); }
            var battle = run.WorldCombat?.Snapshot ?? throw new ArgumentException("Zones require direct world combat.");
            WorldCombatCodec.Validate(run.WorldCombat!);
            if (battle.Mode != WorldCombatMode.DirectPve) { throw new ArgumentException("Zones require direct world combat."); }
            this.options = options;
            definitions = options.Definitions.Where(d => d.Kind == HeroActiveKind.ZoneSlow).ToArray();
            if (definitions.Length == 0) { throw new ArgumentException("No zone definitions."); }
            var limit = battle.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            var capacity = 0; var targetCapacity = 0; var sharedCap = -1; var sharedMinimum = 0; string? previous = null;
            long cooldownBytes = sizeof(int); long largestInstance = 0;
            foreach (var definition in definitions)
            {
                if (previous is not null && StringComparer.Ordinal.Compare(previous, definition.Id) >= 0)
                { throw new ArgumentException("Zone definitions must be canonical."); }
                previous = definition.Id;
                var p = definition.Zone ?? throw new ArgumentException("Missing zone parameters.");
                if (p.TargetRange <= 0 || p.TargetRange > HeroActiveCodec.MaximumCoordinate || p.Radius <= 0 || p.Radius > HeroActiveCodec.MaximumCoordinate ||
                    p.TellTicks < 0 || p.DurationTicks <= 0 || p.CooldownTicks <= 0 || (long)p.TellTicks + p.DurationTicks > int.MaxValue - (long)limit || p.CooldownTicks > int.MaxValue - limit ||
                    p.MaximumInstances <= 0 || p.MaximumInstances > HeroActiveCodec.MaximumActiveInstances || p.MaximumTargets <= 0 || p.MaximumTargets > WorldCombatCodec.MaximumEntries ||
                    p.SlowPermille < 0 || p.SlowPermille > PlayerInput.Scale || p.SlowCapPermille < 0 || p.SlowCapPermille > PlayerInput.Scale || p.MinimumPositiveSpeed <= 0 ||
                    p.EnemyPolicies is null || p.EnemyPolicies.Count > WorldCombatCodec.MaximumEntries)
                { throw new ArgumentException("Invalid zone parameters."); }
                if (capacity != 0 && capacity != p.MaximumInstances || sharedCap >= 0 && sharedCap != p.SlowCapPermille || sharedMinimum != 0 && sharedMinimum != p.MinimumPositiveSpeed)
                { throw new ArgumentException("Owner zone budgets or slow rules disagree."); }
                capacity = p.MaximumInstances; targetCapacity = Math.Max(targetCapacity, p.MaximumTargets); sharedCap = p.SlowCapPermille; sharedMinimum = p.MinimumPositiveSpeed;
                string? priorPolicy = null;
                foreach (var policy in p.EnemyPolicies)
                {
                    if (policy is null || priorPolicy is not null && StringComparer.Ordinal.Compare(priorPolicy, policy.EnemyId) >= 0 || !catalog.Enemies.ContainsKey(policy.EnemyId))
                    { throw new ArgumentException("Invalid zone enemy policy."); }
                    priorPolicy = policy.EnemyId;
                }
                foreach (var cohort in battle.Cohorts)
                {
                    var policy = p.EnemyPolicies.FirstOrDefault(e => e.EnemyId == cohort.EnemyId) ?? throw new ArgumentException("Missing zone enemy policy.");
                    if (policy.Eligible && cohort.Stats.Speed > 0 && p.MinimumPositiveSpeed > cohort.Stats.Speed)
                    { throw new ArgumentException("Zone minimum speed exceeds original speed."); }
                }
                cooldownBytes = checked(cooldownBytes + TextBytes(definition.Id) + sizeof(int));
                var instanceBytes = TextBytes(options.OwnerId) + TextBytes(definition.Id) + sizeof(long)
                    + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(bool) + sizeof(int);
                largestInstance = Math.Max(largestInstance, instanceBytes);
            }
            long totalInstances = capacity; var dashCount = 0; var tauntCapacity = 0; var tauntTargets = 0;
            foreach (var definition in options.Definitions)
            {
                if (definition.Kind == HeroActiveKind.Dash) { dashCount++; }
                if (definition.Taunt is not { } taunt) { continue; }
                if (taunt.MaximumInstances <= 0 || taunt.MaximumInstances > HeroActiveCodec.MaximumActiveInstances || taunt.MaximumTargets <= 0 || taunt.MaximumTargets > WorldCombatCodec.MaximumEntries)
                { throw new ArgumentException("Invalid shared taunt storage budget."); }
                if (tauntCapacity != 0 && tauntCapacity != taunt.MaximumInstances) { throw new ArgumentException("Owner taunt caps disagree."); }
                tauntCapacity = taunt.MaximumInstances; tauntTargets = Math.Max(tauntTargets, taunt.MaximumTargets);
            }
            totalInstances = checked(totalInstances + dashCount + tauntCapacity);
            if (dashCount > 1 || totalInstances > HeroActiveCodec.MaximumActiveInstances ||
                checked((long)tauntCapacity * tauntTargets) > HeroActiveCodec.MaximumActiveClaims ||
                checked((long)capacity * targetCapacity) > HeroActiveCodec.MaximumActiveSources)
            { throw new ArgumentException("Active aggregate storage budget exceeded."); }
            // Per target: target ID, raw magnitude and expiry. Text prefix is a conservative i32 bound.
            RuntimeByteBound = checked(cooldownBytes + sizeof(int) + (long)capacity * checked(largestInstance + (long)targetCapacity * (sizeof(int) + sizeof(int) + sizeof(int))));
            if (RuntimeByteBound > WorldCombatCodec.MaximumBytes || RuntimeByteBound > WorldRecoveryCodec.MaximumRuntimeBytes)
            { throw new ArgumentException("Zone runtime serialization budget exceeded."); }
            var enemyCapacity = 0;
            foreach (var cohort in battle.Cohorts) { enemyCapacity = checked(enemyCapacity + cohort.Count); }
            if (enemyCapacity > WorldCombatCodec.MaximumEntries) { throw new ArgumentException("Zone candidate budget exceeded."); }
            var wave = catalog.WaveRuntime ?? throw new ArgumentException("Zones require wave runtime.");
            var modules = new WavePrimitiveModules(wave);
            foreach (var definition in definitions)
            {
                foreach (var policy in definition.Zone!.EnemyPolicies)
                {
                    if (!wave.Enemies.ContainsKey(policy.EnemyId)) { throw new ArgumentException("Unknown zone enemy."); }
                    if (modules.Enemy(policy.EnemyId).Boss) { bosses.Add(policy.EnemyId); }
                }
            }
            cap = sharedCap; minimumSpeed = sharedMinimum;
            ready = new int[definitions.Length]; candidates = new Candidate[enemyCapacity]; instances = new Instance[capacity];
            for (var i = 0; i < instances.Length; i++) { instances[i] = new(targetCapacity); }
        }
        private static long TextBytes(string value) => checked((long)sizeof(int) + Encoding.UTF8.GetByteCount(value));
        internal void BeginTick(int currentTick)
        {
            tick = currentTick;
            foreach (var instance in instances)
            { if (instance.Used && tick >= instance.End) { instance.Used = false; instance.Count = 0; count--; } }
        }
        internal void Clear()
        {
            count = 0;
            foreach (var instance in instances) { instance.Used = false; instance.Count = 0; }
        }
        internal void AfterMovement(WorldState world, ref long ownerSequence)
        {
            tick = world.Tick;
            if (world.LordHealth <= 0) { Clear(); return; }
            for (var i = 0; i < definitions.Length; i++)
            {
                if (world.Tick < ready[i] || count >= instances.Length) { continue; }
                var p = definitions[i].Zone!; EnemyState? nearest = null; var best = long.MaxValue;
                foreach (var enemy in world.Enemies)
                {
                    if (enemy.Health <= 0) { continue; }
                    var distance = world.Lord.DistanceSquared(enemy.Position);
                    if (distance > (long)p.TargetRange * p.TargetRange || distance > best || distance == best && nearest is not null && enemy.Id > nearest.Id) { continue; }
                    nearest = enemy; best = distance;
                }
                if (nearest is null) { continue; }
                Instance? free = null;
                foreach (var instance in instances) { if (!instance.Used) { free = instance; break; } }
                if (free is null) { throw new InvalidOperationException("Zone instance accounting mismatch."); }
                free.Used = true; free.Definition = i; free.Start = world.Tick; free.ActiveStart = checked(world.Tick + p.TellTicks);
                free.End = checked(free.ActiveStart + p.DurationTicks); free.Sequence = ownerSequence = checked(ownerSequence + 1);
                free.Center = nearest.Position; free.Count = 0; count++; ready[i] = checked(world.Tick + p.CooldownTicks);
            }
        }
        private bool Eligible(EnemyState enemy, int definition)
        {
            if (enemy.Health <= 0 || bosses.Contains(enemy.Definition)) { return false; }
            foreach (var policy in definitions[definition].Zone!.EnemyPolicies)
            { if (policy.EnemyId == enemy.Definition) { return policy.Eligible; } }
            return false;
        }
        private bool Valid(Instance instance, EnemyState enemy)
        {
            var radius = definitions[instance.Definition].Zone!.Radius;
            return instance.Used && tick >= instance.ActiveStart && tick < instance.End && Eligible(enemy, instance.Definition)
                && enemy.Position.DistanceSquared(instance.Center) <= (long)radius * radius;
        }
        internal void RefreshMembership(WorldState world)
        {
            tick = world.Tick;
            if (world.LordHealth <= 0) { Clear(); return; }
            foreach (var instance in instances)
            {
                if (!instance.Used || tick < instance.ActiveStart || tick >= instance.End) { instance.Count = 0; continue; }
                var candidateCount = 0;
                foreach (var enemy in world.Enemies)
                {
                    if (!Valid(instance, enemy)) { continue; }
                    if (candidateCount >= candidates.Length) { throw new InvalidOperationException("Encounter exceeds validated zone candidate capacity."); }
                    candidates[candidateCount++] = new(enemy.Id, enemy.Position.DistanceSquared(instance.Center));
                }
                Array.Sort(candidates, 0, candidateCount, CandidateOrder.Value);
                var selected = Math.Min(candidateCount, definitions[instance.Definition].Zone!.MaximumTargets);
                instance.Count = selected;
                for (var i = 0; i < selected; i++) { instance.Targets[i] = candidates[i].Id; instance.ValidUntil[i] = instance.End; }
                // Membership is selected by distance, serialization and lookup retain stable target ID order.
                Array.Sort(instance.Targets, instance.ValidUntil, 0, selected);
            }
        }
        internal void NotifyEnemyChanged(EnemyState enemy)
        {
            foreach (var instance in instances)
            {
                if (!instance.Used || Valid(instance, enemy)) { continue; }
                for (var i = 0; i < instance.Count; i++)
                {
                    if (instance.Targets[i] != enemy.Id) { continue; }
                    for (var j = i + 1; j < instance.Count; j++)
                    { instance.Targets[j - 1] = instance.Targets[j]; instance.ValidUntil[j - 1] = instance.ValidUntil[j]; }
                    instance.Count--; break;
                }
            }
        }
        private bool Strongest(int enemyId, out int raw, out int expiry)
        {
            raw = 0; expiry = 0; var found = false;
            foreach (var instance in instances)
            {
                if (!instance.Used || tick < instance.ActiveStart || tick >= instance.End) { continue; }
                for (var i = 0; i < instance.Count; i++)
                {
                    if (instance.Targets[i] != enemyId || instance.ValidUntil[i] <= tick) { continue; }
                    var magnitude = definitions[instance.Definition].Zone!.SlowPermille;
                    if (!found || magnitude > raw) { raw = magnitude; expiry = instance.ValidUntil[i]; found = true; }
                    else if (magnitude == raw) { expiry = Math.Max(expiry, instance.ValidUntil[i]); }
                    break;
                }
            }
            return found;
        }
        internal HeroSlowFrame CaptureSlow(int enemyId)
        {
            Strongest(enemyId, out var raw, out var expiry);
            return new(raw, Math.Min(raw, cap), expiry);
        }
        internal int MovementSpeed(int enemyId, int originalSpeed, int wetSpeed)
        {
            if (originalSpeed < 0 || wetSpeed < 0 || wetSpeed > originalSpeed) { throw new ArgumentException("Invalid original or independently calculated wet speed."); }
            if (originalSpeed == 0) { return 0; }
            if (!Strongest(enemyId, out var raw, out _)) { return wetSpeed; }
            if (minimumSpeed > originalSpeed) { throw new InvalidOperationException("Original speed differs from validated cohort speed."); }
            var activeSpeed = Math.Max(minimumSpeed, (int)((long)originalSpeed * (PlayerInput.Scale - Math.Min(raw, cap)) / PlayerInput.Scale));
            return Math.Min(wetSpeed, activeSpeed);
        }
        internal HeroZoneFrame Capture()
        {
            var cooldowns = new HeroZoneCooldownFrame[definitions.Length];
            for (var i = 0; i < definitions.Length; i++) { cooldowns[i] = new(definitions[i].Id, ready[i]); }
            var zones = new List<HeroZoneInstanceFrame>(count); var sources = new List<HeroSlowSourceFrame>();
            foreach (var instance in instances)
            {
                if (!instance.Used) { continue; }
                zones.Add(new(options.OwnerId, definitions[instance.Definition].Id, instance.Sequence, instance.Start, instance.ActiveStart,
                    instance.End, new(instance.Center.X, instance.Center.Y), tick >= instance.ActiveStart && tick < instance.End));
                for (var i = 0; i < instance.Count; i++)
                { sources.Add(new(options.OwnerId, instance.Sequence, instance.Targets[i], definitions[instance.Definition].Zone!.SlowPermille, instance.ValidUntil[i])); }
            }
            zones.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            sources.Sort((a, b) => { var sequence = a.ZoneSequence.CompareTo(b.ZoneSequence); return sequence != 0 ? sequence : a.TargetId.CompareTo(b.TargetId); });
            return new(Array.AsReadOnly(cooldowns), zones.AsReadOnly(), sources.AsReadOnly());
        }
        internal void Write(BinaryWriter writer)
        {
            var frame = Capture();
            writer.Write(frame.Definitions.Count);
            foreach (var definition in frame.Definitions) { writer.Write(definition.DefinitionId); writer.Write(definition.CooldownReadyTick); }
            writer.Write(frame.Zones.Count);
            foreach (var zone in frame.Zones)
            {
                writer.Write(zone.OwnerId); writer.Write(zone.DefinitionId); writer.Write(zone.Sequence); writer.Write(zone.StartTick);
                writer.Write(zone.ActiveStartTick); writer.Write(zone.EndTick); writer.Write(zone.Center.X); writer.Write(zone.Center.Y); writer.Write(zone.Active);
                var sourceCount = 0;
                foreach (var source in frame.Sources) { if (source.ZoneSequence == zone.Sequence) { sourceCount++; } }
                writer.Write(sourceCount);
                foreach (var source in frame.Sources)
                {
                    if (source.ZoneSequence != zone.Sequence) { continue; }
                    writer.Write(source.TargetId); writer.Write(source.Magnitude); writer.Write(source.ValidUntilTick);
                }
            }
        }
    }
}
