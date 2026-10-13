using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        struct KnightRenderedFrame { public int index, tick; public float alpha; public Vector2 hero; public string method, action; public int poseFrame; public long textureBytes; }
        List<KnightRenderedFrame> knightRenderedFrames;
        void RecordKnightRenderedFrame(int index, float alpha)
        {
            if (knightReview == null) return;
            knightRenderedFrames ??= new List<KnightRenderedFrame>(2048);
            if (knightRenderedFrames.Count == knightRenderedFrames.Capacity) throw new InvalidOperationException("Knight review frame budget exceeded.");
            knightRenderedFrames.Add(new KnightRenderedFrame { index = index, tick = CurrentFrame.Tick, alpha = alpha, hero = world.RenderedHeroAnchorScreenPosition,
                method = heroProductionReview == null ? "a-knight-live" : world.HeroProductionMethod,
                action = heroProductionReview == null ? "live-rig" : world.HeroProductionAction,
                poseFrame = heroProductionReview == null ? -1 : world.HeroProductionFrame, textureBytes = world.HeroProductionTextureBytes });
        }
        void SaveKnightReview(bool verified)
        {
            if (knightReview == null || world == null || cameraScaleComparison == null) return;
            File.WriteAllText(Path.Combine(benchmarkOutput, "knight-review.json"), JsonUtility.ToJson(new KnightManifest {
                hero = heroProductionReview == null ? "a-knight-live" : world.HeroProductionMethod,
                productionManifestSha256 = heroProductionReview?.ManifestSha256, productionDirection = heroProductionReview?.Direction,
                productionTextureBytes = world.HeroProductionTextureBytes,
                impactFeedback = knightReview.ImpactFeedback, cameraScale = cameraScaleComparison.Magnification,
                edgeWarnings = knightReview.EdgeWarnings, replayVerified = verified, replaySha256 = benchmarkReplaySha,
                fixture = knightReview.DamageReplay ? "distinct-dense-approach-candidate2" : "original-frozen-c05",
                startTick = cameraScaleComparison.StartTick, endTick = cameraScaleComparison.EndTick,
                offlineAudio = offlineAudioRequested
            }, true));
            using var writer = new StreamWriter(Path.Combine(benchmarkOutput, "knight-rendered.csv"));
            writer.WriteLine("frameIndex,tick,alpha,heroX,heroY,method,action,poseFrame,textureBytes");
            if (knightRenderedFrames == null) return;
            foreach (var frame in knightRenderedFrames)
                writer.WriteLine(FormattableString.Invariant($"{frame.index},{frame.tick},{frame.alpha:R},{frame.hero.x:R},{frame.hero.y:R},{frame.method},{frame.action},{frame.poseFrame},{frame.textureBytes}"));
        }
        [Serializable] sealed class KnightManifest
        {
            public string contract = "knight-impact-v1", hero = "a-knight-live";
            public string heroCoordinates = "Actual rendered hero ground/root anchor after camera follow and shake, bottom-left native screen pixels. Not a deformed pose centroid or pixel segmentation.";
            public string fixture, replaySha256, productionManifestSha256, productionDirection;
            public long productionTextureBytes;
            public bool impactFeedback, edgeWarnings, replayVerified, offlineAudio;
            public float cameraScale;
            public int startTick, endTick;
        }
    }
}
