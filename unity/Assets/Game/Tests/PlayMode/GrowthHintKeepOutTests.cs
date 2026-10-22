#if UNITY_EDITOR
using System.Collections;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    // #345: the collection hint keeps off the hero like the damage numbers (0.6 x the drawn knight's height round its centre).
    // Its usual spot (WaveCollectionLabelRise above the feet) is outside that circle; the screen clamp at the map's top edge
    // pushes it onto the hero, which this scene reproduces.
    public sealed class GrowthHintKeepOutTests
    {
        const float Frame = 1f / 60;

        [UnityTest]
        public IEnumerator CollectionHintMovesAboveTheHeroOrFades()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var start = session.View.CaptureFrame() with { Enemies = System.Array.Empty<EnemyView>() };
            // The hero at the map's top edge: the camera stops at the map, so the hint is clamped back onto the screen, onto the hero.
            var frame = start with { Lord = start.Lord with { Position = new WorldPoint(start.Lord.Position.X, start.MapHeight - 150) } };
            var runtime = ((IWaveRunView)session.View).CaptureWaveRuntime() with { Enemies = System.Array.Empty<WaveEnemyView>(), Events = System.Array.Empty<WaveEvent>() };
            var owner = new GameObject("Growth hint QA"); var cameraOwner = new GameObject("Growth hint camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var world = owner.AddComponent<WorldRenderer>(); var config = CanonicalContent.Presentation.Camera;
                world.Initialize(camera, new WorldCameraSettings(config.WorldUnitsPerUnityUnit, config.MinHalfHeight, config.MaxHalfHeight, config.EstatePadding,
                    config.FollowMilliseconds, config.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
                IEnumerator Present(int frames)
                {
                    for (var i = 0; i < frames; i++)
                    {
                        world.AcceptWave(catalog, runtime);
                        world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), runtime), 1, Frame, new Rect(0, 0, 720, 1280));
                        yield return null;
                    }
                }
                yield return Present(10);   // the hero silhouette is known after the first drawn frames
                world.GrowthHintKeepOutEnabled = false;
                world.EditorEnqueueGrowthHintForTesting(WorldHintKind.HarvestExperience, 15);
                yield return Present(2);
                var before = world.EditorGrowthHintForTesting();
                Assert.That(before.shown && before.radius > 0, Is.True);
                Assert.That(WorldGrowthLabels.TouchesCircle(before.bounds, before.centre, before.radius), Is.True, $"the usual spot overlaps the hero's keep-out circle: centre {before.centre} radius {before.radius} label {before.bounds.min}..{before.bounds.max}");
                yield return Present(90);   // let the first hint expire
                world.GrowthHintKeepOutEnabled = true;
                world.EditorEnqueueGrowthHintForTesting(WorldHintKind.HarvestExperience, 15);
                yield return Present(2);
                var after = world.EditorGrowthHintForTesting();
                Assert.That(after.shown, Is.True);
                Assert.That(world.GrowthHintsMovedAboveHero + world.GrowthHintsFadedOverHero, Is.GreaterThan(0));
                if (world.GrowthHintsFadedOverHero == 0)
                {
                    Assert.That(WorldGrowthLabels.TouchesCircle(after.bounds, after.centre, after.radius), Is.False, "moved clear of the circle");
                    Assert.That(after.bounds.min.y, Is.GreaterThanOrEqualTo(after.centre.y), "above the hero's centre");
                }
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }
    }
}
#endif
