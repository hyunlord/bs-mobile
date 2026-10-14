using System;
using System.Collections.Generic;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed partial class M1CombatReview
    {
        [Serializable] public sealed class MusterRecord { public int frame; public string source, path, evolutionId; public float ratio, scale; public int soldiers; }
        [Serializable] public sealed class PresentationSummary
        {
            public string interpretation = "View-only feel counters from the rendered run. Giant-blade kill points are on-screen kill positions (Core and last rendered) tested against the drawn envelope. Muster path RuntimeEvolution requires an active runtime evolution; GearRatio is the runtime gear count/range over the canonical maximum level, capped at 7 soldiers and 1.5x.";
            public int giantBladeStrikes, giantBladeKillsInside, giantBladeKillsOutside, enemyDeathPeakFull, enemyDeathOverflowed, enemyDeathDropped, enemyDeathFullCapacity, heroHitFlashes, musterLunges;
            public float giantBladeMaximumRadius;
        }
        const int MusterRecordCapacity = 256;
        readonly List<MusterRecord> musterRecords = new List<MusterRecord>(16);
        PresentationSummary presentation;

        public void Observe(WorldRenderer world)
        {
            world.MusterPresentationChanged = (source, formation) =>
            {
                UnityEngine.Debug.Log($"[muster-presentation] frame={frameCount} source={source} path={formation.Path} evolution={formation.EvolutionId ?? "none"} ratio={formation.Ratio:0.###} soldiers={formation.Soldiers} scale={formation.Scale:0.###}");
                if (musterRecords.Count < MusterRecordCapacity)
                    musterRecords.Add(new MusterRecord { frame = frameCount, source = source, path = formation.Path.ToString(), evolutionId = formation.EvolutionId ?? "",
                        ratio = formation.Ratio, scale = formation.Scale, soldiers = formation.Soldiers });
            };
        }

        public void CapturePresentation(WorldRenderer world)
        {
            if (world == null) return;
            presentation = new PresentationSummary
            {
                giantBladeStrikes = world.M1BladeStrikes, giantBladeKillsInside = world.M1BladeKillsInside, giantBladeKillsOutside = world.M1BladeKillsOutside,
                giantBladeMaximumRadius = world.M1BladeMaximumRadius, enemyDeathPeakFull = world.PeakFullEnemyDeaths, enemyDeathOverflowed = world.OverflowedEnemyDeaths,
                enemyDeathDropped = world.DroppedEnemyDeaths, enemyDeathFullCapacity = EnemyDeathPresentationPool.FullCapacity,
                heroHitFlashes = world.HeroHitFlashes, musterLunges = world.MusterLunges
            };
        }
    }
}
