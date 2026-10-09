using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    internal sealed class WaveWork
    {
        public int Id, Progress, Required, Health, ParentId = -1, Cycle, ReadyTick, DormantUntil, WetUntil;
        public string Source = "", Kind = "";
        public Position Position;
        public bool Complete, Protected, Irrigated, Dry, ShipmentActive;
    }
    internal sealed class WaveDetour { public Position Position; public int Radius, UntilTick; public string Source = ""; }
    internal sealed class WaveReward
    {
        public int Id, Experience;
        public string Source = "", CompletionKey = "";
        public Position Position;
    }
    internal sealed class WaveProjectile
    {
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
        public SortedDictionary<string, long> Counters = new(StringComparer.Ordinal);
        public void Count(string key) => Counters[key] = Counters.TryGetValue(key, out var n) ? n + 1 : 1;
        public void Emit(int tick, string kind, string source, int subject, Position position, Position target, int amount = 0)
        {
            Count(kind + ":" + source);
            Events.Add(new(NextEvent++, tick, kind, source, subject, new(position.X, position.Y), new(target.X, target.Y), amount));
        }
        public WaveRuntimeFrame Capture(WaveRuntimeDefinition d, int tick) => new(d.Revision, d.ChapterId, Water, Timber, AvailableWorkers, BossDefeated,
            Array.AsReadOnly(Work.Select(w => new WaveWorkView(w.Id, w.Source, w.Kind, new(w.Position.X, w.Position.Y), w.Progress, w.Required, w.Complete, w.Health, w.ParentId, w.Protected, w.DormantUntil > tick, w.WetUntil > tick, w.ShipmentActive, w.Dry)).ToArray()),
            Array.AsReadOnly(Rewards.Select(r => new WaveRewardView(r.Id, r.Source, new(r.Position.X, r.Position.Y), r.Experience, r.CompletionKey)).ToArray()),
            Array.AsReadOnly(Projectiles.Select(p => new WaveProjectileView(p.Id, p.Source, new(p.Position.X, p.Position.Y), new(p.Previous.X, p.Previous.Y), p.TargetId, p.Hostile)).ToArray()),
            Array.AsReadOnly(EnemyActions.OrderBy(p => p.Key).Select(p => new WaveEnemyView(p.Key, p.Value.Phase, new(p.Value.Origin.X, p.Value.Origin.Y), new(p.Value.Target.X, p.Value.Target.Y), p.Value.UntilTick, p.Value.WetUntil > tick, p.Value.StopUntil > tick, p.Value.BossPhase)).ToArray()),
            Array.AsReadOnly(Groups.Select(g => new WaveGroupView(g.Id, g.Source, new(g.Position.X, g.Position.Y), g.Phase, g.Mission, g.Training, g.ReservedFood, g.FrontRank, g.Formation)).ToArray()),
            Array.AsReadOnly(Paths.Select(p => new WorldPoint(p.X, p.Y)).ToArray()), Array.AsReadOnly(Items.ToArray()), Array.AsReadOnly(Evolutions.ToArray()), Array.AsReadOnly(Events.ToArray()), new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(Counters, StringComparer.Ordinal)), Array.AsReadOnly(Attacks.ToArray()), CarriedWater, Array.AsReadOnly(Detours.Select(d => new WaveDetourView(new(d.Position.X, d.Position.Y), d.Radius, d.UntilTick, d.Source)).ToArray()),new(TimberOrigin.X,TimberOrigin.Y),ProcessedTimber);
    }
}
