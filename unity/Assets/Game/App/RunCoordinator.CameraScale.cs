using System;
using System.Collections.Generic;
using System.IO;
using Game.App.Generated;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        CameraScaleComparison cameraScaleComparison;
        CameraScaleComparisonTelemetry cameraScaleTelemetry;
        ActualLordDamageTelemetry actualLordDamageTelemetry;
        readonly Dictionary<int, Rect> cameraScaleBounds = new Dictionary<int, Rect>(2048);
        int cameraScaleObservedTick = -1;
        long cameraScaleLastDamageEvent = -1;
        bool cameraScaleCollectBounds;
        int BenchmarkWidth => crowdBenchmark != null || cameraScaleComparison != null ? 1920 : 1600;
        int BenchmarkHeight => crowdBenchmark != null || cameraScaleComparison != null ? 1080 : 900;
        int BenchmarkDisplayEnd => cameraScaleComparison?.EndTick ?? CrowdEndTick;
        void ConfigureCameraScaleReview()
        {
            if (cameraScaleComparison == null) return;
            cameraScaleTelemetry = new CameraScaleComparisonTelemetry { ContactThresholdSource = "catalog.Enemies[definitionId].Range / WorldUnitsPerUnityUnit; lord radius=0. Same center-distance threshold as normal enemy strike range; not actual damaging hit attribution." };
            cameraScaleBounds.Clear(); cameraScaleObservedTick = -1;
            cameraScaleLastDamageEvent = CurrentWave.Events.Count == 0 ? -1 : CurrentWave.Events[CurrentWave.Events.Count - 1].Id;
            world.ReviewCameraMagnification = cameraScaleComparison.Magnification;
            world.RenderedEnemyBoundsSample = CaptureCameraScaleBounds;
        }
        void CaptureCameraScaleBounds(string definitionId, int id, Rect bounds)
        {
            if (cameraScaleCollectBounds) cameraScaleBounds[id] = bounds;
        }
        void BeginCameraScaleFrame(bool warming)
        {
            cameraScaleCollectBounds = cameraScaleTelemetry != null && !warming && cameraScaleObservedTick != CurrentFrame.Tick;
            if (cameraScaleCollectBounds) cameraScaleBounds.Clear();
        }
        void ObserveCameraScaleFrame()
        {
            if (!cameraScaleCollectBounds) return;
            cameraScaleCollectBounds = false;
            var units = (float)CanonicalContent.Presentation.Camera.WorldUnitsPerUnityUnit;
            var lord = CurrentFrame.Lord.Position;
            if (!cameraScaleTelemetry.BeginTick(CurrentFrame.Tick, world.CameraWorldViewport,
                new Vector2(lord.X / units, lord.Y / units), 0, cameraScaleComparison.InWindow(CurrentFrame.Tick),
                offlineReplayRequested || Application.isFocused && benchmarkForeground.Focused && !benchmarkForeground.Paused)) return;
            for (var i = 0; i < CurrentFrame.Enemies.Count; i++)
            {
                var enemy = CurrentFrame.Enemies[i];
                if (enemy.Health <= 0 || !cameraScaleBounds.TryGetValue(enemy.Id, out var bounds)) continue;
                cameraScaleTelemetry.ObserveEnemy(enemy.Id, bounds, new Vector2(enemy.Position.X / units, enemy.Position.Y / units), runCatalog.Enemies[enemy.DefinitionId].Range / units);
            }
            var damage = 0;
            for (var i = 0; i < CurrentWave.Events.Count; i++)
            {
                var e = CurrentWave.Events[i];
                if (e.Id <= cameraScaleLastDamageEvent) continue;
                if (e.Kind == "lord-hit" && e.Amount > 0) damage++;
                cameraScaleLastDamageEvent = e.Id;
            }
            cameraScaleTelemetry.EndTick(damage);
            actualLordDamageTelemetry?.CompleteTick(CurrentFrame.Tick, cameraScaleTelemetry, offlineReplayRequested || Application.isFocused && benchmarkForeground.Focused && !benchmarkForeground.Paused);
            cameraScaleObservedTick = CurrentFrame.Tick;
        }
        void SaveCameraScaleReview(bool verified)
        {
            if (cameraScaleTelemetry == null || benchmarkOutput == null) return;
            cameraScaleTelemetry.Save(benchmarkOutput);
            actualLordDamageTelemetry?.Save(benchmarkOutput);
            File.WriteAllText(Path.Combine(benchmarkOutput, "camera-scale.json"), JsonUtility.ToJson(new CameraScaleManifest {
                contract = knightReview == null ? "camera-scale-frozen-c05-v1" : "knight-camera-review-v1",
                hero = knightReview == null ? "current B parts, live Unity 2D Animation; historical scale review" : "a-knight-live",
                scale = cameraScaleComparison.Magnification, window = cameraScaleComparison.Window,
                startTick = cameraScaleComparison.StartTick, endTick = cameraScaleComparison.EndTick,
                preRollTick = cameraScaleComparison.PreRollTick, replayVerified = verified,
                replaySha256 = benchmarkReplaySha, offline = offlineReplayRequested,
                viewportPixels = Camera.main.pixelRect, worldViewport = world.CameraWorldViewport
            }, true));
        }
        [Serializable] sealed class CameraScaleManifest
        {
            public string contract = "camera-scale-frozen-c05-v1";
            public string contactProxy = "Enemy center enters catalog enemy Range of lord center; lord radius zero. This is the existing strike-range geometry, not body collision or actual damage attribution. Rendered bounds at the first presented frame per Core tick determine screen entry, while contact uses current Core centers; these are not exact same-time collision samples. No Core rules change.";
            public string timing = "Capture-off 60fps native Mac vsync only; camera experiment is not the historical C05 regression baseline. Offline rows are visibility evidence only.";
            public string hero = "current B parts, live Unity 2D Animation; selected A art awaits scale selection";
            public float scale;
            public string window, replaySha256;
            public int startTick, endTick, preRollTick;
            public bool replayVerified, offline;
            public Rect viewportPixels, worldViewport;
        }
    }
}
