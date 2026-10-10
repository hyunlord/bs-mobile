using System.Linq;
namespace SowSiege.Core
{
    internal sealed partial class PortableStateCodec
    {
        private void WriteWave(WaveRuntimeDefinition d)
        {
            if (!WavePrimitiveModules.Equivalent(d))
            {
                Write("wave-primitive-program-v1");
                WriteMap(d.Programs!, program => WriteMap(program.Params, parameters => WriteMap(parameters, Write)));
            }
            Write("wave-runtime-definition-v1");
            Write(d.Revision);
            Write(d.ChapterId);
            Write(d.ChapterDesignRef);
            Write(d.BossId);
            Write(d.BossSpawnTick);
            Write(d.GroupCap);
            Write(d.InitialWorkers);
            Write(d.InitialTimber);
            Write(d.WaterCapacity);
            Write(d.WaterRefillTicks);
            Write(d.PickupRadius);
            Write(d.WetTicks);
            Write(d.StopTicks);
            Write(d.PathSpacing);
            Write(d.PathCapacity);
            Write(d.Behavior.WetSpeedDivisor); Write(d.Behavior.DryDamageDivisor); Write(d.Behavior.RangedRangeMultiplier); Write(d.Behavior.PathConnectionMultiplier); Write(d.DryAfterTicks);
            WriteMap(d.Gear, g =>
            {
                Write(g.Id); Write(g.DesignRef); Write((int)g.Kind); Write(g.Damage); Write(g.Range); Write(g.CooldownTicks); Write(g.Speed); Write(g.Count); Write(g.LifetimeTicks); Write(g.Knockback); Write(g.WorkTicks); Write(g.WorkRadius); Write(g.RewardExperience); Write(g.Capacity); Write(g.Levels is not null); if (g.Levels is not null)
                {
                    WriteList(g.Levels, l =>
                {
                    Write(l.Damage); Write(l.Range); Write(l.CooldownTicks); Write(l.Speed); Write(l.Count); Write(l.LifetimeTicks); Write(l.Knockback);
                }
                );
                }
            }
);
            WriteMap(d.Items, i =>
            {
                Write(i.Id); Write(i.DesignRef); Write((int)i.Kind); WriteList(i.EquipmentIds, Write); Write(i.Amount);
            }
);
            WriteMap(d.Evolutions, e =>
            {
                Write(e.Id); Write(e.DesignRef); Write((int)e.Kind); WriteList(e.InputIds, Write);
            }
);
            WriteMap(d.Enemies, e =>
            {
                Write(e.Id); Write(e.DesignRef); Write((int)e.Kind); Write(e.TellTicks); Write(e.ActiveTicks); Write(e.RecoveryTicks); Write(e.ProjectileSpeed); Write(e.FirstSpawnTick);
            }
);
            Write(d.MaterialTargets is not null);
            if (d.MaterialTargets is not null)
            {
                WriteMap(d.MaterialTargets, Write);
            }
        }
        private void WriteWave(WaveRuntimeState s)
        {
            Write("wave-runtime-state-v1");
            Write(s.Water);
            Write(s.Timber); Write(s.TimberOrigin); Write(s.ProcessedTimber);
            Write(s.AvailableWorkers);
            Write(s.CarriedWater);
            Write(s.NextMission);
            Write(s.NextCycle);
            Write(s.NextEvent);
            Write(s.NextActivation);
            WriteMap(s.Activations, a => { Write(a.Id); Write(a.Source); Write(a.PendingChildren); Write(a.ExpiresTick); Write(a.RequestedDamage); Write(a.HitDamage); WriteList(a.HitTargets.OrderBy(id => id), Write); });
            Write(s.BossSpawned);
            Write(s.BossDefeated);
            Write(s.BladePlotKill);
            Write(s.RepairCompleted);
            Write(s.HarvestNearBuilding);
            Write(s.Facing);
            WriteList(s.Work, w =>
            {
                Write(w.Id); Write(w.Source); Write(w.Kind); Write(w.Position); Write(w.Progress); Write(w.Required); Write(w.Health); Write(w.ParentId); Write(w.Cycle); Write(w.ReadyTick); Write(w.DormantUntil); Write(w.Complete); Write(w.Protected); Write(w.Irrigated); Write(w.WetUntil); Write(w.ShipmentActive); Write(w.Dry);
            }
);
            WriteList(s.Rewards, r =>
            {
                Write(r.Id); Write(r.Source); Write(r.Position); Write(r.Experience); Write(r.CompletionKey);
            }
);
            WriteList(s.Projectiles, p =>
            {
                Write(p.Id); Write(p.Source); Write(p.Position); Write(p.Previous); Write(p.Direction); Write(p.TargetId); Write(p.Damage); Write(p.Speed); Write(p.ExpireTick); Write(p.Hostile); Write(p.ActivationId);
            }
);
            WriteMap(s.EnemyActions, a =>
            {
                Write(a.Phase); Write(a.Origin); Write(a.Target); Write(a.UntilTick); Write(a.WetUntil); Write(a.StopUntil); Write(a.BossPhase); Write(a.TargetId); Write(a.Hit);
            }
);
            WriteList(s.Groups, g =>
            {
                Write(g.Id); Write(g.Source); Write(g.Phase); Write(g.Position); Write(g.Destination); Write(g.Mission); Write(g.Training); Write(g.ReservedFood); Write(g.ReadyTick); Write(g.Health); Write(g.Engaged); Write(g.FrontRank); Write(g.Formation); WriteList(g.Participants.OrderBy(id => id), Write);
            }
);
            WriteList(s.Paths, Write);
            WriteList(s.Items, Write);
            WriteList(s.Evolutions, Write);
            WriteList(s.Completed, Write);
            WriteMap(s.Counters, Write);
            WriteList(s.Detours, d => { Write(d.Position); Write(d.Radius); Write(d.UntilTick); Write(d.Source); });
            WriteList(s.Attacks, a =>
            {
                Write(a.Source); Write(a.Kind); Write(a.Origin); Write(a.Position); Write(a.Radius); Write(a.ExpireTick); Write(a.ActivationId);
            }
);
            WriteList(s.Events, e =>
            {
                Write(e.Id); Write(e.Tick); Write(e.Kind); Write(e.Source); Write(e.SubjectId); Write(e.Position); Write(e.Target); Write(e.Amount);
            }
);
        }
    }
}
