using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    internal sealed class WaveWork
    {
        public int Id, Progress, Required, Health, ParentId = -1, Cycle, ReadyTick, DormantUntil, WetUntil;
        public string Source = "", Kind = "";
        public Position Position;
        public bool Complete, Protected, Irrigated, Dry, ShipmentActive;
        private WaveWorkView? cachedView;
        internal WaveWorkView Capture(int tick)
        {
            var dormant = DormantUntil > tick; var wet = WetUntil > tick;
            var view = cachedView;
            if (view is not null && view.Id == Id && view.Source == Source && view.Kind == Kind &&
                view.Position.X == Position.X && view.Position.Y == Position.Y && view.Progress == Progress &&
                view.Required == Required && view.Complete == Complete && view.Health == Health &&
                view.ParentId == ParentId && view.Protected == Protected && view.Dormant == dormant &&
                view.Wet == wet && view.ShipmentActive == ShipmentActive && view.Dry == Dry) { return view; }
            return cachedView = new(Id, Source, Kind, new(Position.X, Position.Y), Progress, Required, Complete,
                Health, ParentId, Protected, dormant, wet, ShipmentActive, Dry);
        }
    }
    internal sealed class WaveDetour { public Position Position; public int Radius, UntilTick; public string Source = ""; }
    // A landed lure sack (ADR0063): normal enemies within Radius walk to Position while UntilTick > tick.
    internal sealed class WaveLure { public int Id, Radius, UntilTick; public Position Position; public string Source = ""; }
    internal sealed class WaveReward
    {
        public int Id, Experience;
        public string Source = "", CompletionKey = "";
        public Position Position;
    }
    internal sealed class WaveProjectile
    {
        // Observational ownership is deliberately absent from the historical codec.
        // A projectile created without this metadata (including restoration) stays unknown.
        public int DiagnosticAttackerId = -1;
        public int Id, TargetId, Damage, Speed, ExpireTick, ActivationId = -1;
        public string Source = "";
        public Position Position, Previous, Direction;
        public bool Hostile;
    }
    internal sealed class WaveEnemyAction
    {
        public string Phase = "approach";
        public Position Origin, Target;
        public int UntilTick, WetUntil, StopUntil, BossPhase, TargetId;
        public bool Hit;
    }
    internal sealed class WaveGroup
    {
        public int Id, Mission, Training, ReservedFood, ReadyTick, Health, FrontRank;
        public string Source = "", Phase = "idle", Formation = "advance";
        public Position Position, Destination;
        public bool Engaged;
        public HashSet<int> Participants = new();
    }
    internal sealed partial class WaveRuntimeState
    {
        public Position TimberOrigin;
        public int ProcessedTimber;
        public int Water, Timber, AvailableWorkers, CarriedWater, NextMission, NextCycle;
        public long NextEvent;
        public bool BossSpawned, BossDefeated, BladePlotKill, RepairCompleted, HarvestNearBuilding;
        public Position Facing = new(1, 0);
        public List<WaveWork> Work = new();
        public List<WaveDetour> Detours = new();
        public List<WaveReward> Rewards = new();
        public List<WaveProjectile> Projectiles = new();
        public Dictionary<int, WaveEnemyAction> EnemyActions = new();
        public List<WaveGroup> Groups = new();
        public List<Position> Paths = new();
        public SortedSet<string> Items = new(StringComparer.Ordinal), Evolutions = new(StringComparer.Ordinal), Completed = new(StringComparer.Ordinal);
        public List<WaveEvent> Events = new();
        public List<WaveAttackView> Attacks = new();
        public List<WavePendingImpact> PendingImpacts = new();
        public List<WaveLure> Lures = new();
        public SortedDictionary<string, long> Counters = new(StringComparer.Ordinal);
        public void Count(string key) => Counters[key] = Counters.TryGetValue(key, out var n) ? n + 1 : 1;
        public void Emit(int tick, string kind, string source, int subject, Position position, Position target, int amount = 0)
        {
            Count(kind + ":" + source);
            Events.Add(new(NextEvent++, tick, kind, source, subject, new(position.X, position.Y), new(target.X, target.Y), amount));
        }
        private static readonly Comparison<WaveEnemyView> EnemyViewOrder = (a, b) => a.Id.CompareTo(b.Id);
        private static readonly IComparer<WaveEnemyView> EnemyViewComparer = Comparer<WaveEnemyView>.Create(EnemyViewOrder);
        private WaveAttackView[] CaptureAttacks()
        {
            if (PendingImpacts.Count == 0) { return Attacks.ToArray(); }
            var result = new WaveAttackView[Attacks.Count + PendingImpacts.Count];
            Attacks.CopyTo(result);
            for (var i = 0; i < PendingImpacts.Count; i++)
            {
                var p = PendingImpacts[i]; var point = new WorldPoint(p.Position.X, p.Position.Y);
                result[Attacks.Count + i] = new(p.Source, "review-ground-impact", point, point, p.Radius, p.Tick, p.Id);
            }
            return result;
        }
        public WaveRuntimeFrame Capture(WaveRuntimeDefinition d, int tick, WaveSnapshotBuffer? buffer = null)
        {
            buffer?.Enemies.Begin(EnemyActions.Count);
            buffer?.Counters.Capture(Counters);
            var work = new WaveWorkView[Work.Count];
            for (var i = 0; i < work.Length; i++)
            {
                var w = Work[i];
                work[i] = w.Capture(tick);
            }
            var rewards = new WaveRewardView[Rewards.Count];
            for (var i = 0; i < rewards.Length; i++) { var r = Rewards[i]; rewards[i] = new(r.Id, r.Source, new(r.Position.X, r.Position.Y), r.Experience, r.CompletionKey); }
            var projectiles = new WaveProjectileView[Projectiles.Count];
            for (var i = 0; i < projectiles.Length; i++) { var p = Projectiles[i]; projectiles[i] = new(p.Id, p.Source, new(p.Position.X, p.Position.Y), new(p.Previous.X, p.Previous.Y), p.TargetId, p.Hostile); }
            var enemies = buffer?.Enemies.Storage ?? new WaveEnemyView[EnemyActions.Count];
            var index = 0;
            foreach (var pair in EnemyActions)
            {
                var e = pair.Value;
                enemies[index++] = new(pair.Key, e.Phase, new(e.Origin.X, e.Origin.Y), new(e.Target.X, e.Target.Y), e.UntilTick, e.WetUntil > tick, e.StopUntil > tick, e.BossPhase);
            }
            Array.Sort(enemies, 0, EnemyActions.Count, EnemyViewComparer);
            var groups = new WaveGroupView[Groups.Count];
            for (var i = 0; i < groups.Length; i++) { var g = Groups[i]; groups[i] = new(g.Id, g.Source, new(g.Position.X, g.Position.Y), g.Phase, g.Mission, g.Training, g.ReservedFood, g.FrontRank, g.Formation, g.Health); }
            var paths = new WorldPoint[Paths.Count];
            for (var i = 0; i < paths.Length; i++) { var p = Paths[i]; paths[i] = new(p.X, p.Y); }
            var detours = new WaveDetourView[Detours.Count];
            for (var i = 0; i < detours.Length; i++) { var v = Detours[i]; detours[i] = new(new(v.Position.X, v.Position.Y), v.Radius, v.UntilTick, v.Source); }
            var items = new string[Items.Count]; Items.CopyTo(items);
            var evolutions = new string[Evolutions.Count]; Evolutions.CopyTo(evolutions);
            return new(d.Revision, d.ChapterId, Water, Timber, AvailableWorkers, BossDefeated,
                Array.AsReadOnly(work), Array.AsReadOnly(rewards), Array.AsReadOnly(projectiles), buffer is null ? Array.AsReadOnly(enemies) : buffer.Enemies, Array.AsReadOnly(groups), Array.AsReadOnly(paths), Array.AsReadOnly(items), Array.AsReadOnly(evolutions), Array.AsReadOnly(Events.ToArray()), buffer is null ? new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(Counters, StringComparer.Ordinal)) : buffer.Counters, Array.AsReadOnly(CaptureAttacks()), CarriedWater, Array.AsReadOnly(detours), new(TimberOrigin.X, TimberOrigin.Y), ProcessedTimber);
        }
    }
}
