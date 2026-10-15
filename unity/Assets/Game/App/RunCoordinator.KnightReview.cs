using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        struct KnightRenderedFrame { public int index, tick; public float alpha; public Vector2 hero; public string method, action; public int poseFrame; public long textureBytes, nominalRgbaBytes; public bool textureBytesAvailable; public int clipImpactFrame; public int targetId, attackTick; public bool terminalTail; public Vector2 targetDirection; public string slashDirection, drawnDirection; public bool bodyHeightMeasured; public Vector2 bodyWorldY, bodyScreenY; }
        List<KnightRenderedFrame> knightRenderedFrames;
        void RecordKnightRenderedFrame(int index, float alpha, bool terminalPresentation=false)
        {
            if (knightReview == null && heroProductionReview == null && forgeWalkReview?.CutoutBaseline != true) return;
            knightRenderedFrames ??= new List<KnightRenderedFrame>(2048);
            if (knightRenderedFrames.Count == knightRenderedFrames.Capacity) throw new InvalidOperationException("Knight review frame budget exceeded.");
            knightRenderedFrames.Add(new KnightRenderedFrame { index = index, tick = CurrentFrame.Tick, alpha = alpha, hero = world.RenderedHeroAnchorScreenPosition,
                method = forgeWalkReview?.CutoutBaseline == true ? "a-knight-cutout-baseline" : heroProductionReview == null ? "a-knight-live" : world.HeroProductionMethod,
                action = heroProductionReview == null ? "live-rig" : world.HeroProductionAction,
                poseFrame = heroProductionReview == null ? -1 : world.HeroProductionFrame, textureBytes = world.HeroProductionTextureBytes, nominalRgbaBytes = world.HeroProductionNominalRgbaBytes, textureBytesAvailable = world.HeroProductionTextureBytesAvailable, clipImpactFrame = world.HeroProductionClipImpactFrame, targetId=world.HeroProductionTargetId, attackTick=world.HeroProductionAttackTick, targetDirection=world.HeroProductionTargetDirection, slashDirection=world.HeroProductionSlashDirection, drawnDirection=world.HeroProductionDirection,terminalTail=terminalPresentation,
                bodyHeightMeasured = world.HeroHeadFootMeasured, bodyWorldY = world.HeroHeadFootWorldY, bodyScreenY = world.HeroHeadFootScreenY });
        }
        void SaveKnightReview(bool verified)
        {
            if ((knightReview == null && heroProductionReview == null && forgeWalkReview?.CutoutBaseline != true) || world == null || cameraScaleComparison == null) return;
            var bodySamples = 0;
            if (knightRenderedFrames != null) foreach (var frame in knightRenderedFrames) if (frame.bodyHeightMeasured) bodySamples++;
            var heightsWorld = new float[bodySamples]; var heightsScreen = new float[bodySamples]; var heightIndex = 0;
            if (knightRenderedFrames != null) foreach (var frame in knightRenderedFrames)
                if (frame.bodyHeightMeasured) { heightsWorld[heightIndex] = frame.bodyWorldY.y - frame.bodyWorldY.x; heightsScreen[heightIndex++] = frame.bodyScreenY.y - frame.bodyScreenY.x; }
            Array.Sort(heightsWorld); Array.Sort(heightsScreen);
            File.WriteAllText(Path.Combine(benchmarkOutput, "knight-review.json"), JsonUtility.ToJson(new KnightManifest {
                contract = forgeWalkReview?.CutoutBaseline == true ? "forge-cutout-height-v1" : "knight-impact-v1",
                hero = forgeWalkReview?.CutoutBaseline == true ? "a-knight-cutout-baseline" : heroProductionReview == null ? "a-knight-live" : world.HeroProductionMethod,
                productionManifestSha256 = heroProductionReview?.ManifestSha256, productionDirection = heroProductionReview?.Direction,
                productionAtlasSha256 = world.HeroProductionAtlasSha256, heightReferenceSha256 = forgeWalkReview?.HeightReferenceSha256,
                bodyHeightSamples = bodySamples, medianBodyHeightWorld = MedianHeight(heightsWorld), medianBodyHeightPixels = MedianHeight(heightsScreen),
                productionTextureBytes = world.HeroProductionTextureBytes, productionTextureBytesAvailable = world.HeroProductionTextureBytesAvailable, productionNominalRgbaBytes = world.HeroProductionNominalRgbaBytes,
                impactFeedback = knightReview?.ImpactFeedback ?? true, cameraScale = cameraScaleComparison.Magnification,
                edgeWarnings = knightReview?.EdgeWarnings ?? true, replayVerified = verified, replaySha256 = benchmarkReplaySha,
                fixture = forgeWalkReview!=null ? "forge-walk-review-v1" : combatReview!=null ? "actual-combat-case" : knightReview?.DamageReplay == true ? "distinct-dense-approach-candidate2" : "original-frozen-c05",
                startTick = cameraScaleComparison.StartTick, endTick = cameraScaleComparison.EndTick,
                terminalPresentationFrames=heroTerminalTailFrames, offlineAudio = offlineAudioRequested
            }, true));
            using var writer = new StreamWriter(Path.Combine(benchmarkOutput, "knight-rendered.csv"));
            writer.WriteLine("frameIndex,tick,alpha,heroX,heroY,method,action,poseFrame,textureBytes,attackTick,targetId,targetDx,targetDy,slashDirection,drawnDirection,terminalPresentation,textureBytesAvailable,nominalRgbaBytes,clipImpactFrame,bodyHeightMeasured,bodyBottomWorldY,bodyTopWorldY,bodyBottomScreenY,bodyTopScreenY");
            if (knightRenderedFrames == null) return;
            foreach (var frame in knightRenderedFrames)
                writer.WriteLine(FormattableString.Invariant($"{frame.index},{frame.tick},{frame.alpha:R},{frame.hero.x:R},{frame.hero.y:R},{frame.method},{frame.action},{frame.poseFrame},{frame.textureBytes},{frame.attackTick},{frame.targetId},{frame.targetDirection.x:R},{frame.targetDirection.y:R},{frame.slashDirection},{frame.drawnDirection},{frame.terminalTail},{frame.textureBytesAvailable},{frame.nominalRgbaBytes},{frame.clipImpactFrame},{frame.bodyHeightMeasured},{frame.bodyWorldY.x:R},{frame.bodyWorldY.y:R},{frame.bodyScreenY.x:R},{frame.bodyScreenY.y:R}"));
        }
        static float MedianHeight(float[] values) => values.Length == 0 ? 0 : (values[(values.Length - 1) / 2] + values[values.Length / 2]) * .5f;
        [Serializable] sealed class KnightManifest
        {
            public string contract = "knight-impact-v1", hero = "a-knight-live";
            public string heroCoordinates = "Actual rendered hero ground/root anchor after camera follow and shake, bottom-left native screen pixels. Not a deformed pose centroid or pixel segmentation.";
            public int terminalPresentationFrames;
            public string fixture, replaySha256, productionManifestSha256, productionDirection;
            public string productionAtlasSha256, heightReferenceSha256;
            public string cutoutSource = "Resources/HeroKnight/motion-runtime.json and parts.png; existing SpriteSkin bone poses. Source commit/hash are in benchmark.json.";
            public string bodyHeightMeaning = "Cutout: alpha>=128 head/boot source-pixel extents transformed by actual current bone poses. Forge: per-frame authored head/boot landmarks. Transparent padding, cape, sword excluded. Median scale stays fixed; not normalized per frame.";
            public int bodyHeightSamples;
            public float medianBodyHeightWorld, medianBodyHeightPixels;
            public long productionTextureBytes, productionNominalRgbaBytes;
            public bool productionTextureBytesAvailable;
            public string textureMemoryMeaning = "Measured bytes are -1 when Profiler memory is unavailable. Nominal RGBA bytes are width*height*4 for unique loaded atlases, excluding overhead and not a resident-memory measurement.";
            public bool impactFeedback, edgeWarnings, replayVerified, offlineAudio;
            public float cameraScale;
            public int startTick, endTick;
        }
    }
}
