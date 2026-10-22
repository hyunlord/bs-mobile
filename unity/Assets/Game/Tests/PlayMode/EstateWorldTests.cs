#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    // M2 dark estate View first slice (#347) on a real session with the estate light: shader constants, buildings, ring and the
    // T1 mark appear; a default run draws none of it and leaves the shader's estate block off.
    public sealed class EstateWorldTests
    {
        const float Frame = 1f / 60;
        static readonly int EstateActive = Shader.PropertyToID("_EstateActive"), EstateCount = Shader.PropertyToID("_EstateCircleCount");

        const string Tower = "core:estate_watchtower", Well = "core:estate_well_shrine", Granary = "core:estate_granary";
        // The opt-in light's shape (data/opt-in/estate-light/dark-estate-m2.json) with its real building IDs.
        static ContentCatalog EstateCatalog()
        {
            var c = CanonicalContent.CreateCatalog();
            var light = new WaveEstateLightDefinition(WaveEstateLight.Version, "core:estate_home", 2500, 2600, 250, 50, 150,
                new Dictionary<string, WaveEstateBuildingDefinition>
                {
                    [Tower] = new(Tower, "test", WaveEstateWake.Channel, 450, 2200, 150, WaveEstateResource.Food, 0, 0, new(WaveEstateEffectKind.Attack, 30, 45)),
                    [Well] = new(Well, "test", WaveEstateWake.Spend, 450, 2200, 0, WaveEstateResource.Food, 20, 15, new(WaveEstateEffectKind.Heal, 18, 30, 90)),
                    [Granary] = new(Granary, "test", WaveEstateWake.Spend, 450, 2200, 0, WaveEstateResource.Food, 15, 15, new(WaveEstateEffectKind.Magnet, 60, 0, 0, 1))
                },
                new Dictionary<string, WaveEstateSlot[]> { ["L1"] = new[] { new WaveEstateSlot(Tower, 8000, 11000), new WaveEstateSlot(Well, 3000, 5000), new WaveEstateSlot(Granary, 4000, 6000) } });
            return c with { WaveRuntime = c.WaveRuntime with { EstateLight = light } };
        }

        static WorldRenderer CreateWorld(GameObject owner, Camera camera, ContentCatalog catalog, RunFrame frame)
        {
            var world = owner.AddComponent<WorldRenderer>(); var config = CanonicalContent.Presentation.Camera;
            world.SetFallowChapter(catalog.WaveRuntime.ChapterId == "meta:chapter_1");
            world.Initialize(camera, new WorldCameraSettings(config.WorldUnitsPerUnityUnit, config.MinHalfHeight, config.MaxHalfHeight, config.EstatePadding,
                config.FollowMilliseconds, config.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
            return world;
        }

        static InteractiveSession Session(ContentCatalog catalog) => new InteractiveSession(catalog, new InteractiveOptions(
            new RunOptions(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash,
            MeleeAim: CatalogRunOptions.MeleeAim(catalog), Estate: CatalogRunOptions.Estate(catalog)));

        // Advances with the first offered card so level-ups never stall.
        static void Advance(InteractiveSession session, int ticks)
        {
            for (var i = 0; i < ticks;)
            {
                var tick = session.View.CaptureFrame().Tick;
                if (session.View.Status == RunStatus.AwaitingCard) { session.Apply(new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: session.View.CaptureCards().Cards[0])); continue; }
                session.Apply(new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance)); i++;
            }
        }

        [UnityTest]
        public IEnumerator EstateRunDrawsDarknessBuildingsRingAndDarkMarks([Values] EstateDarkMarker marker)
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = EstateCatalog();
            var session = Session(catalog);
            session.Apply(new ReplayCommand(session.NextSequence, 0, ReplayCommandKind.SetInvulnerable, Value: 1));
            Advance(session, 600);
            var owner = new GameObject("Estate QA"); var cameraOwner = new GameObject("Estate camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var frame = session.View.CaptureFrame();
                var wave = ((IWaveRunView)session.View).CaptureWaveRuntime();
                // One building awake for the test: the view reads phases from the frame only.
                var awake = wave.Estate.Buildings.Select((b, i) => i == 1 ? b with { Phase = WaveEstatePhase.Active } : b).ToArray();
                wave = wave with { Estate = wave.Estate with { Buildings = awake } };
                var world = CreateWorld(owner, camera, catalog, frame);
                world.DarkMarker = marker;
                for (var i = 0; i < 3; i++)
                {
                    world.AcceptWave(catalog, wave);
                    world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), wave), 1, Frame, new Rect(0, 0, 720, 1280));
                    yield return null;
                }
                Assert.That(Shader.GetGlobalFloat(EstateActive), Is.EqualTo(1));
                Assert.That(world.EstateCircleCount, Is.EqualTo(2), "home and the awake building");
                Assert.That(Shader.GetGlobalFloat(EstateCount), Is.EqualTo(2));
                Assert.That(world.EstateBuildingDraws, Is.EqualTo(3), "every L1 building, asleep or awake");
                var dark = frame.Enemies.Count(e => e.Health > 0 && !WorldRenderer.EstateLit(wave.Estate, e.Position));
                Assert.That(world.EstateDarkMarks, Is.EqualTo(dark), "a mark on exactly the enemies outside every lit circle");
                Assert.That(dark, Is.GreaterThan(0), "the scene has enemies in the dark");
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
            yield return null;
            Assert.That(Shader.GetGlobalFloat(EstateActive), Is.EqualTo(0), "disposing the world switches the estate block off");
        }

        [UnityTest]
        public IEnumerator DefaultRunDrawsNoEstateAndKeepsTheShaderBlockOff()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = Session(catalog);
            Advance(session, 120);
            var owner = new GameObject("Default QA"); var cameraOwner = new GameObject("Default camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var frame = session.View.CaptureFrame();
                var wave = ((IWaveRunView)session.View).CaptureWaveRuntime();
                Assert.That(wave.Estate, Is.Null);
                var world = CreateWorld(owner, camera, catalog, frame);
                for (var i = 0; i < 3; i++)
                {
                    world.AcceptWave(catalog, wave);
                    world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), wave), 1, Frame, new Rect(0, 0, 720, 1280));
                    yield return null;
                }
                Assert.That(Shader.GetGlobalFloat(EstateActive), Is.EqualTo(0));
                Assert.That((world.EstateCircleCount, world.EstateBuildingDraws, world.EstateRingDraws, world.EstateDarkMarks), Is.EqualTo((0, 0, 0, 0)));
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }
    }
}
#endif
