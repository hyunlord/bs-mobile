using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    public sealed record WorldStandingSlotView(string TypeId, int Ordinal, WorldPoint Position, int Health, int MaximumHealth, bool Disabled);
    public sealed record WorldMarkerView(string Id, WorldPoint Position, int Health);
    public sealed record WorldCombatFrame(int Tick, WorldTerminalReason? Terminal, IReadOnlyList<WorldObjectiveResult> Objectives,
        IReadOnlyList<WorldStandingSlotView> Slots, IReadOnlyList<WorldMarkerView> Markers);

    internal sealed class WorldCombatState
    {
        internal sealed class Slot
        {
            internal readonly int Type, Ordinal, MaximumHealth;
            internal Position Position;
            internal int Health, ReadyTick;
            internal bool Disabled;
            internal Slot(int type, WorldTroopSlot slot, int health) { Type = type; Ordinal = slot.Ordinal; Position = new(slot.X, slot.Y); Health = MaximumHealth = health; }
        }
        internal sealed class Marker
        {
            internal readonly WorldMarker Definition;
            internal int Health;
            internal Marker(WorldMarker definition) { Definition = definition; Health = definition.Health; }
        }
        internal WorldCombatOptions Options { get; }
        internal WorldCombatSnapshot Snapshot => Options.Snapshot;
        internal WorldEffectState Effects { get; }
        internal readonly Slot[] Slots;
        internal readonly Marker[] Markers;
        private readonly int[] progress;
        private readonly int[] objectiveMarkers;
        private readonly bool[] spawned;
        private readonly SortedDictionary<int, int> cohorts = new();
        private readonly SortedSet<int> defeated = new();
        private bool heroDefeated;
        internal WorldTerminalReason? Terminal { get; private set; }
        internal int TerminalTick { get; private set; }
        internal long EventSequence { get; private set; }
        internal int LimitTicks { get; }
        internal WorldCombatState(WorldCombatOptions options)
        {
            Options = WorldCombatCodec.Freeze(options); Effects = new(Snapshot);
            LimitTicks = Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            progress = new int[Snapshot.Objectives.Count]; objectiveMarkers = new int[Snapshot.Objectives.Count]; spawned = new bool[Snapshot.Cohorts.Count];
            var slots = new List<Slot>();
            for (var i = 0; i < Snapshot.Troops.Count; i++)
            {
                var troop = Snapshot.Troops[i];
                foreach (var slot in troop.Slots) { slots.Add(new(i, slot, Effects.Scale(troop.Stats.Health, MetaAbility.Allies, WorldEffectTarget.Standing))); }
            }
            Slots = slots.ToArray(); Markers = Snapshot.Markers.Select(m => new Marker(m)).ToArray();
            for (var i = 0; i < Snapshot.Objectives.Count; i++)
            {
                objectiveMarkers[i] = -1;
                for (var j = 0; j < Markers.Length; j++) { if (Markers[j].Definition.Id == Snapshot.Objectives[i].TargetId) { objectiveMarkers[i] = j; break; } }
            }
        }
        internal void ValidateCatalog(ContentCatalog catalog, InteractiveOptions options)
        {
            if (Snapshot.Mode != WorldCombatMode.DirectPve || catalog.WaveRuntime is null) { throw new ArgumentException("Direct world combat requires a wave catalog."); }
            if (Snapshot.Provenance.TickRate != catalog.Tuning.TickRate || Snapshot.Provenance.Seed != options.Run.Seed || Snapshot.Hero.Id != options.Run.HeroId)
            { throw new ArgumentException("World catalog cadence or hero binding mismatch."); }
            if (Snapshot.Provenance.ContentHash != options.DataHash) { throw new ArgumentException("World consumed content hash mismatch."); }
            if (Snapshot.Provenance.ProfileHash != PortableStateCodec.HashCatalog(catalog)) { throw new ArgumentException("World profile hash mismatch."); }
            var center = new Position(catalog.Tuning.World.Map.Width >> 1, catalog.Tuning.World.Map.Height >> 1);
            if (Clamp(center) != center) { throw new ArgumentException("Battlefield excludes the catalog hero start."); }
            foreach (var troop in Snapshot.Troops) { if (troop.Stats.CooldownTicks > int.MaxValue - LimitTicks) { throw new ArgumentException("Standing cooldown cannot be represented at the battle frontier."); } }
            foreach (var cohort in Snapshot.Cohorts)
            {
                if (cohort.Stats.CooldownTicks > int.MaxValue - LimitTicks) { throw new ArgumentException("Enemy cooldown cannot be represented at the battle frontier."); }
                if (!catalog.Enemies.ContainsKey(cohort.EnemyId) || !catalog.WaveRuntime.Enemies.ContainsKey(cohort.EnemyId)) { throw new ArgumentException("Unknown encounter enemy."); }
            }
        }
        internal Position Clamp(Position p) => new(Math.Clamp(p.X, Snapshot.Battlefield.MinX, Snapshot.Battlefield.MaxX), Math.Clamp(p.Y, Snapshot.Battlefield.MinY, Snapshot.Battlefield.MaxY));
        internal void TickEnemies(WorldState world, WaveEnemySystem enemies)
        {
            if (Terminal is not null) { throw new InvalidOperationException("World battle already ended."); }
            for (var i = 0; i < Snapshot.Cohorts.Count; i++)
            {
                var cohort = Snapshot.Cohorts[i]; if (spawned[i] || cohort.SpawnTick != world.Tick) { continue; }
                spawned[i] = true;
                for (var ordinal = 0; ordinal < cohort.Count; ordinal++)
                {
                    var enemy = new EnemyState { Id = world.AllocateId(), Definition = cohort.EnemyId, Health = cohort.Stats.Health, Position = new(cohort.X, cohort.Y) };
                    world.Enemies.Add(enemy); cohorts.Add(enemy.Id, i); world.SpawnedEnemies++; EventSequence++;
                    world.WaveRuntime!.EnemyActions.Add(enemy.Id, new WaveEnemyAction { Phase = "approach", Origin = enemy.Position, Target = world.Lord, TargetId = -1 });
                }
            }
            foreach (var enemy in world.Enemies)
            {
                if (enemy.Health <= 0 || !cohorts.TryGetValue(enemy.Id, out var cohortIndex)) { continue; }
                var stats = Snapshot.Cohorts[cohortIndex].Stats;
                var target = world.Lord; var distance = world.LordHealth > 0 ? enemy.Position.DistanceSquared(target) : long.MaxValue; var slotIndex = -1; var markerIndex = -1;
                // Stable target priority on equal distance: hero, then (type, ordinal), then marker ID.
                for (var i = 0; i < Slots.Length; i++)
                {
                    var slot = Slots[i]; if (slot.Disabled) { continue; }
                    var candidate = enemy.Position.DistanceSquared(slot.Position);
                    if (candidate < distance) { distance = candidate; target = slot.Position; slotIndex = i; markerIndex = -1; }
                }
                for (var i = 0; i < Markers.Length; i++)
                {
                    var marker = Markers[i]; if (marker.Health <= 0) { continue; }
                    var position = new Position(marker.Definition.X, marker.Definition.Y); var candidate = enemy.Position.DistanceSquared(position);
                    if (candidate < distance) { distance = candidate; target = position; slotIndex = -1; markerIndex = i; }
                }
                if (distance == long.MaxValue) { continue; }
                enemy.Position = Clamp(enemy.Position.MoveToward(target, stats.Speed));
                if (enemy.Position.DistanceSquared(target) > (long)stats.Range * stats.Range || enemy.AttackTick > world.Tick) { continue; }
                enemy.AttackTick = checked(world.Tick + stats.CooldownTicks);
                if (slotIndex >= 0) { DamageSlot(slotIndex, Math.Max(0, stats.Attack - Snapshot.Troops[Slots[slotIndex].Type].Stats.Defense)); }
                else if (markerIndex >= 0)
                {
                    var marker = Markers[markerIndex]; marker.Health -= Math.Min(marker.Health, stats.Attack); EventSequence++;
                }
                else if (enemies.DamageWorldHero(enemy, stats.Attack)) { heroDefeated |= world.LordHealth <= 0; EventSequence++; }
            }
        }
        internal void TickStandingArmy(WorldState world)
        {
            foreach (var slot in Slots)
            {
                if (slot.Disabled) { continue; }
                EnemyState? target = null; long distance = long.MaxValue;
                foreach (var enemy in world.Enemies)
                {
                    if (enemy.Health <= 0) { continue; }
                    var candidate = slot.Position.DistanceSquared(enemy.Position);
                    if (candidate < distance || candidate == distance && (target is null || enemy.Id < target.Id)) { target = enemy; distance = candidate; }
                }
                if (target is null) { continue; }
                var stats = Snapshot.Troops[slot.Type].Stats;
                slot.Position = Clamp(slot.Position.MoveToward(target.Position, Effects.Scale(stats.Speed, MetaAbility.Movement, WorldEffectTarget.Standing)));
                if (slot.ReadyTick > world.Tick || slot.Position.DistanceSquared(target.Position) > (long)stats.Range * stats.Range) { continue; }
                slot.ReadyTick = checked(world.Tick + stats.CooldownTicks);
                var defense = cohorts.TryGetValue(target.Id, out var cohortIndex) ? Snapshot.Cohorts[cohortIndex].Stats.Defense : 0;
                var amount = Math.Max(0, Effects.Scale(stats.Attack, MetaAbility.Allies, WorldEffectTarget.Standing) - defense);
                var dealt = Math.Min(target.Health, amount); target.Health -= dealt; world.AllyDamage += dealt; EventSequence++;
            }
        }
        internal int EnemyMaximumHealth(int id, int fallback) => cohorts.TryGetValue(id, out var cohort) ? Snapshot.Cohorts[cohort].Stats.Health : fallback;
        internal int AdjustDamage(int enemyId, int damage) => Math.Max(0, damage - (cohorts.TryGetValue(enemyId, out var cohort) ? Snapshot.Cohorts[cohort].Stats.Defense : 0));
        internal void DamageSlot(int index, int damage)
        {
            if (index < 0 || index >= Slots.Length || damage < 0) { throw new ArgumentOutOfRangeException(nameof(index)); }
            var slot = Slots[index]; if (slot.Disabled) { return; }
            slot.Health -= Math.Min(slot.Health, damage); if (slot.Health == 0) { slot.Disabled = true; } EventSequence++;
        }
        internal void OnEnemyDefeated(int id)
        {
            if (!cohorts.TryGetValue(id, out var cohort) || !defeated.Add(id)) { return; }
            for (var i = 0; i < Snapshot.Objectives.Count; i++)
            {
                var objective = Snapshot.Objectives[i];
                if (objective.Kind == WorldObjectiveKind.DefeatDesignated && objective.TargetId == Snapshot.Cohorts[cohort].Id)
                { progress[i] = Math.Min(objective.RequiredCount, checked(progress[i] + 1)); }
            }
            EventSequence++;
        }
        internal void CompleteTick(WorldState world)
        {
            if (Terminal is not null) { return; }
            var success = true;
            for (var i = 0; i < Snapshot.Objectives.Count; i++)
            {
                var objective = Snapshot.Objectives[i];
                if (objective.Kind == WorldObjectiveKind.TimeLimit) { progress[i] = world.Tick; continue; }
                if (objective.Kind == WorldObjectiveKind.HoldMarker)
                {
                    var marker = Markers[objectiveMarkers[i]]; var position = new Position(marker.Definition.X, marker.Definition.Y);
                    var radiusSquared = (long)marker.Definition.Radius * marker.Definition.Radius;
                    var valid = marker.Health > 0 && world.LordHealth > 0 && world.Lord.DistanceSquared(position) <= radiusSquared;
                    foreach (var enemy in world.Enemies) { if (enemy.Health > 0 && enemy.Position.DistanceSquared(position) <= radiusSquared) { valid = false; break; } }
                    progress[i] = valid ? Math.Min(objective.HoldTicks, checked(progress[i] + 1)) : 0;
                }
                success &= progress[i] == Required(objective);
            }
            var requiredDestroyed = false;
            foreach (var marker in Markers) { if (marker.Definition.Required && marker.Health <= 0) { requiredDestroyed = true; break; } }
            if (heroDefeated || world.LordHealth <= 0) { SetTerminal(WorldTerminalReason.HeroDefeated, world.Tick); }
            else if (requiredDestroyed) { SetTerminal(WorldTerminalReason.RequiredMarkerDestroyed, world.Tick); }
            else if (success) { SetTerminal(WorldTerminalReason.ObjectivesCompleted, world.Tick); }
            else if (world.Tick >= LimitTicks) { SetTerminal(WorldTerminalReason.Timeout, world.Tick); }
            EventSequence++;
        }
        internal void Quit(int tick)
        {
            if (Terminal is null) { SetTerminal(WorldTerminalReason.Quit, tick); }
        }
        private void SetTerminal(WorldTerminalReason reason, int tick) { Terminal = reason; TerminalTick = tick; EventSequence++; }
        private static int Required(WorldObjective objective) => objective.Kind == WorldObjectiveKind.DefeatDesignated ? objective.RequiredCount : objective.Kind == WorldObjectiveKind.HoldMarker ? objective.HoldTicks : objective.LimitTicks;
        internal WorldObjectiveResult[] ObjectiveResults() => Snapshot.Objectives.Select((o, i) => new WorldObjectiveResult(o.Id, progress[i], progress[i] == Required(o))).ToArray();
        internal WorldCombatResult Result(string hash)
        {
            if (Terminal is null) { throw new InvalidOperationException("World battle is not terminal."); }
            var troops = Snapshot.Troops.Select((t, i) => new WorldTroopResult(t.Id, t.N, Slots.Count(s => s.Type == i && s.Disabled))).ToArray();
            return new(Snapshot.ContractHash, Snapshot.Mode, Snapshot.Provenance.RulesHash, Snapshot.Provenance.ContentHash, Terminal.Value, TerminalTick,
                Array.AsReadOnly(ObjectiveResults()), Array.AsReadOnly(troops), hash);
        }
        internal WorldCombatFrame Frame(int tick) => new(tick, Terminal, Array.AsReadOnly(ObjectiveResults()), Array.AsReadOnly(Slots.Select(s =>
            new WorldStandingSlotView(Snapshot.Troops[s.Type].Id, s.Ordinal, new(s.Position.X, s.Position.Y), s.Health, s.MaximumHealth, s.Disabled)).ToArray()),
            Array.AsReadOnly(Markers.Select(m => new WorldMarkerView(m.Definition.Id, new(m.Definition.X, m.Definition.Y), m.Health)).ToArray()));
        internal byte[] Encode()
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, true);
            w.Write("world-runtime-v1"); w.Write(1); w.Write(WorldCombatCodec.Hash(Options)); w.Write(EventSequence); w.Write(heroDefeated);
            w.Write(Terminal.HasValue); if (Terminal.HasValue) { w.Write((int)Terminal.Value); w.Write(TerminalTick); }
            w.Write(progress.Length); foreach (var value in progress) { w.Write(value); }
            w.Write(spawned.Length); foreach (var value in spawned) { w.Write(value); }
            w.Write(cohorts.Count); foreach (var pair in cohorts) { w.Write(pair.Key); w.Write(pair.Value); }
            w.Write(defeated.Count); foreach (var id in defeated) { w.Write(id); }
            w.Write(Slots.Length); foreach (var slot in Slots) { w.Write(slot.Type); w.Write(slot.Ordinal); w.Write(slot.Position.X); w.Write(slot.Position.Y); w.Write(slot.Health); w.Write(slot.MaximumHealth); w.Write(slot.ReadyTick); w.Write(slot.Disabled); }
            w.Write(Markers.Length); foreach (var marker in Markers) { w.Write(marker.Definition.Id); w.Write(marker.Health); }
            Effects.Write(w); w.Flush(); return stream.ToArray();
        }
    }
}
