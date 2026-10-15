using System;
using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed partial class M1CombatReview
    {
        [Serializable] public sealed class MusterRecord { public int frame; public string source, path, evolutionId; public float ratio, scale; public int soldiers; }
        [Serializable] public sealed class PresentationSummary
        {
            public string interpretation = "View-only feel counters from the rendered run. Giant-blade kill points are on-screen kill positions (Core and last rendered) tested against the drawn envelope. Muster path RuntimeEvolution requires an active runtime evolution; GearRatio is the runtime gear count/range over the canonical maximum level, capped at 5 soldiers and 1.3x. Evolution-only elements (storm sky, flag bearer) need a runtime evolution or a range ratio >= 4.0. Lightning hits are presented when their strike becomes visible (3-frame telegraph). Minion stationary frames exclude spawn, attack and despawn animation.";
            public int lightningStrikes, lightningStormStrikes, lightningDeferredHits, lightningPresentedHits, minionGroupHits, minionThrustsOnHitFrame, minionBearerFrames, minionChargeShoves;
            public long minionAgentFrames, minionStationaryFrames;
            public float minionStationaryRatio;
            public int giantBladeStrikes, giantBladeStrikesFullyVisible, giantBladeKillsInside, giantBladeKillsOutside, enemyDeathPeakFull, enemyDeathOverflowed, enemyDeathDropped, enemyDeathFullCapacity, heroHitFlashes, musterLunges;
            public float giantBladeMaximumRadius;
        }
        const int MusterRecordCapacity = 256;
        readonly List<MusterRecord> musterRecords = new List<MusterRecord>(16);
        readonly List<string> lightningRows = new List<string>(64);
        readonly List<string> thrustRows = new List<string>(256);
        PresentationSummary presentation;

        public void Observe(WorldRenderer world)
        {
            ObserveFeel(world);
            world.MusterPresentationChanged = (source, formation) =>
            {
                UnityEngine.Debug.Log($"[muster-presentation] frame={frameCount} source={source} path={formation.Path} evolution={formation.EvolutionId ?? "none"} ratio={formation.Ratio:0.###} soldiers={formation.Soldiers} scale={formation.Scale:0.###}");
                if (musterRecords.Count < MusterRecordCapacity)
                    musterRecords.Add(new MusterRecord { frame = frameCount, source = source, path = formation.Path.ToString(), evolutionId = formation.EvolutionId ?? "",
                        ratio = formation.Ratio, scale = formation.Scale, soldiers = formation.Soldiers });
            };
        }

        void ObserveFeel(WorldRenderer world)
        {
            world.LightningStrikeSample = r => { if (lightningRows.Count < 1024) lightningRows.Add(FormattableString.Invariant(
                $"{frameCount},{r.Tick},{r.Links},{r.SkyBolts},{r.OnScreenTargets},{r.Storm},{r.Strength:R},{r.CorePixels:R},{r.GlowPixels:R},{r.FirstSkyTargetScreen.x:R},{r.FirstSkyTargetScreen.y:R}")); };
            world.MinionThrustSample = (group, soldiers) => { if (thrustRows.Count < 8192) thrustRows.Add(FormattableString.Invariant($"{frameCount},{group},{soldiers}")); };
        }

        void SaveFeelLogs(string directory)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-lightning-strikes.csv"),
                new[] { "frameIndex,tick,links,skyBolts,onScreenTargets,storm,strength,corePixels,glowPixels,firstSkyScreenX,firstSkyScreenY" }.Concat(lightningRows));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-minion-thrusts.csv"), new[] { "frameIndex,group,soldiers" }.Concat(thrustRows));
        }

        public void CapturePresentation(WorldRenderer world)
        {
            if (world == null) return;
            presentation = new PresentationSummary
            {
                giantBladeStrikes = world.M1BladeStrikes, giantBladeStrikesFullyVisible = world.M1BladeStrikesFullyVisible, giantBladeKillsInside = world.M1BladeKillsInside, giantBladeKillsOutside = world.M1BladeKillsOutside,
                giantBladeMaximumRadius = world.M1BladeMaximumRadius, enemyDeathPeakFull = world.PeakFullEnemyDeaths, enemyDeathOverflowed = world.OverflowedEnemyDeaths,
                enemyDeathDropped = world.DroppedEnemyDeaths, enemyDeathFullCapacity = EnemyDeathPresentationPool.FullCapacity,
                heroHitFlashes = world.HeroHitFlashes, musterLunges = world.MusterLunges,
                lightningStrikes = world.LightningStrikes, lightningStormStrikes = world.LightningStormStrikes,
                lightningDeferredHits = world.LightningDeferredHits, lightningPresentedHits = world.LightningPresentedHits,
                minionGroupHits = world.MinionGroupHits, minionThrustsOnHitFrame = world.MinionThrustsOnHitFrame,
                minionBearerFrames = world.MinionBearerFrames, minionChargeShoves = world.MinionChargeShoves,
                minionAgentFrames = world.MinionAgentFrames, minionStationaryFrames = world.MinionStationaryFrames,
                minionStationaryRatio = world.MinionAgentFrames > 0 ? (float)world.MinionStationaryFrames / world.MinionAgentFrames : 0
            };
        }
    }
}
