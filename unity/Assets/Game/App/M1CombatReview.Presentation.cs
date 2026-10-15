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
            public string interpretation = "View-only feel counters from the rendered run. Giant-blade kill points are on-screen kill positions (Core and last rendered) tested against the drawn envelope. Muster path RuntimeEvolution requires an active runtime evolution; GearRatio is the runtime gear count/range over the canonical maximum level, capped at 5 soldiers and 1.3x. Evolution-only elements (storm sky, flag bearer, giant sword, arrow rain) follow the View-only display stage table (WeaponDisplayStages.json). Blade hits are presented when the sweep reaches them after a 4-frame wind-up; arrows land on the Core impact tick. Lightning hits are presented when their strike becomes visible (3-frame telegraph). Minion stationary frames exclude spawn, attack and despawn animation.";
            public int bladeEvolvedStrikes, bladeDeferredHits, bladePresentedHits, arrowVolleys, arrowRainVolleys, arrowImpacts, arrowLandedOnTime;
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
        readonly List<string> bladeRows = new List<string>(64);
        readonly List<string> arrowRows = new List<string>(64);
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
                $"{frameCount},{r.VisibleFrame - r.AcceptedFrame},{r.Tick},{r.Links},{r.SkyBolts},{r.CoveredSkyBolts},{r.OnScreenTargets},{r.Storm},{r.Strength:R},{r.CorePixels:R},{r.GlowPixels:R},{r.FirstSkyTargetScreen.x:R},{r.FirstSkyTargetScreen.y:R},{r.FirstSkyTopScreen.x:R},{r.FirstSkyTopScreen.y:R},{r.ScreenFlash:R}")); };
            world.BladeStrikeSample = r => { if (bladeRows.Count < 1024) bladeRows.Add(FormattableString.Invariant(
                $"{frameCount},{r.VisibleFrame - r.AcceptedFrame},{r.Tick},{r.Evolved},{r.Radius:R},{r.HalfAngle:R},{r.Aim:R},{r.OriginScreen.x:R},{r.OriginScreen.y:R},{r.RadiusPixels.x:R},{r.RadiusPixels.y:R}")); };
            world.ArrowVolleySample = r => { if (arrowRows.Count < 1024) arrowRows.Add(FormattableString.Invariant(
                $"{frameCount},{r.Tick},{r.Impacts},{r.OnScreenImpacts},{r.Evolved},{r.FirstShownFrame - r.AcceptedFrame},{r.DueFrame - r.FirstShownFrame},{r.FirstImpactScreen.x:R},{r.FirstImpactScreen.y:R},{r.FirstImpactRadiusPixels:R},{r.LaunchScreen.x:R},{r.LaunchScreen.y:R}")); };
            world.MinionThrustSample = (group, soldiers) => { if (thrustRows.Count < 8192) thrustRows.Add(FormattableString.Invariant($"{frameCount},{group},thrust,{soldiers}")); };
            // Hits are accepted before the frame that presents them, i.e. the frame Record() will number next.
            world.MinionHitSample = group => { if (thrustRows.Count < 8192) thrustRows.Add(FormattableString.Invariant($"{frameCount},{group},hit,0")); };
        }

        void SaveFeelLogs(string directory)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-lightning-strikes.csv"),
                new[] { "frameIndex,telegraphFrames,tick,links,skyBolts,coveredSkyBolts,onScreenTargets,storm,strength,corePixels,glowPixels,firstSkyScreenX,firstSkyScreenY,firstSkyTopScreenX,firstSkyTopScreenY,screenFlash" }.Concat(lightningRows));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-minion-thrusts.csv"), new[] { "frameIndex,group,kind,soldiers" }.Concat(thrustRows));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-blade-strikes.csv"),
                new[] { "frameIndex,windupFrames,tick,evolved,radius,halfAngle,aim,originScreenX,originScreenY,radiusPixelsX,radiusPixelsY" }.Concat(bladeRows));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "m1-arrow-volleys.csv"),
                new[] { "frameIndex,tick,impacts,onScreenImpacts,evolved,tellToShownFrames,shownToDueFrames,firstImpactScreenX,firstImpactScreenY,firstImpactRadiusPixels,launchScreenX,launchScreenY" }.Concat(arrowRows));
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
                bladeEvolvedStrikes = world.M1BladeEvolvedStrikes, bladeDeferredHits = world.BladeDeferredHits, bladePresentedHits = world.BladePresentedHits,
                arrowVolleys = world.ArrowVolleys, arrowRainVolleys = world.ArrowRainVolleys, arrowImpacts = world.ArrowImpacts, arrowLandedOnTime = world.ArrowLandedOnTime,
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
