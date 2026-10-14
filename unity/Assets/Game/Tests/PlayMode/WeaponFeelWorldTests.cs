#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class WeaponFeelWorldTests
    {
        const float Frame = 1f / 60;

        static WorldRenderer CreateWorld(GameObject owner, Camera camera, ContentCatalog catalog, RunFrame frame)
        {
            var world = owner.AddComponent<WorldRenderer>(); var config = CanonicalContent.Presentation.Camera;
            world.Initialize(camera, new WorldCameraSettings(config.WorldUnitsPerUnityUnit, config.MinHalfHeight, config.MaxHalfHeight, config.EstatePadding,
                config.FollowMilliseconds, config.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
            return world;
        }

        [UnityTest]
        public IEnumerator KilledEnemyStaysDrawnAcrossTheEventDeliveryGapThenFlashesAndFalls()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var empty = session.View.CaptureFrame() with { Enemies = System.Array.Empty<EnemyView>() };
            var wave = ((IWaveRunView)session.View).CaptureWaveRuntime() with { Enemies = System.Array.Empty<WaveEnemyView>(), Events = System.Array.Empty<WaveEvent>() };
            var enemyId = catalog.WaveRuntime.Enemies.Keys.First(id => id != catalog.WaveRuntime.BossId);
            var at = new WorldPoint(empty.Lord.Position.X + 600, empty.Lord.Position.Y);
            var occupied = empty with { Enemies = new[] { new EnemyView(700, enemyId, at, 100, 100) } };
            var alive = wave with { Enemies = new[] { new WaveEnemyView(700, "approach", at, at, 100, false, false, 0) } };
            var killed = new WaveEvent(900001, empty.Tick + 1, "enemy-killed", enemyId, 700, at, at, 5);
            var removed = wave with { Events = new[] { killed } };
            var owner = new GameObject("Kill continuity QA"); var cameraOwner = new GameObject("Kill continuity camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var world = CreateWorld(owner, camera, catalog, empty);
                void Present(RunFrame frame, WaveRuntimeFrame state, bool events, float delta)
                {
                    world.AcceptWave(catalog, state, events);
                    world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), state), 1, delta, new Rect(0, 0, 720, 1280));
                }
                Present(empty, wave, true, 0); yield return null; var baseline = world.SubmittedInstances;
                Present(occupied, alive, true, Frame); yield return null; var drawn = world.SubmittedInstances;
                Assert.That(drawn, Is.GreaterThan(baseline));
                var gone = empty with { Tick = empty.Tick + 1 };
                // Boss-timeline delivery: Core has removed the actor but its kill has not reached the screen yet.
                Present(gone, removed, false, Frame); yield return null;
                Assert.That(world.EnemyDeathPresented(700), Is.True, "Actor killed in the current snapshot is held, not dropped.");
                Assert.That(world.SubmittedInstances, Is.GreaterThan(baseline), "No blank frame before the kill is delivered.");
                Present(gone, removed, false, Frame); yield return null;
                Assert.That(world.SubmittedInstances, Is.GreaterThan(baseline));
                Present(gone, removed, true, Frame); yield return null;
                Assert.That(world.FullEnemyDeaths, Is.EqualTo(1));
                Assert.That(world.ActiveImpactParticles, Is.GreaterThan(0), "Death burst starts with the flash.");
                for (var t = 0f; t < EnemyDeathPresentationPool.FallStartSeconds + EnemyDeathPresentationPool.FallSeconds; t += Frame)
                {
                    Present(gone, removed, true, Frame); yield return null;
                    Assert.That(world.EnemyDeathPresented(700), Is.True);
                    Assert.That(world.SubmittedInstances, Is.GreaterThan(baseline), "The falling body is on screen through the whole sequence.");
                }
                for (var t = 0f; t < EnemyDeathPresentationPool.FullSeconds; t += Frame) { Present(gone, removed, true, Frame); yield return null; }
                Assert.That(world.EnemyDeathPresented(700), Is.False);
                var relocated = empty with { Tick = empty.Tick + 2 };
                Present(occupied with { Tick = empty.Tick + 2 }, alive, true, Frame); yield return null;
                Present(relocated with { Tick = empty.Tick + 3 }, wave, true, Frame); yield return null;
                Assert.That(world.EnemyDeathPresented(700), Is.False, "Removal without a kill in the snapshot is not held.");
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }

        [UnityTest]
        public IEnumerator GiantBladeTrajectoryEnclosesEveryOnScreenKillOfItsStrike()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame() with { Enemies = System.Array.Empty<EnemyView>() };
            var wave = ((IWaveRunView)session.View).CaptureWaveRuntime() with { Enemies = System.Array.Empty<WaveEnemyView>(), Events = System.Array.Empty<WaveEvent>() };
            var enemyId = catalog.WaveRuntime.Enemies.Keys.First(id => id != catalog.WaveRuntime.BossId);
            var lord = frame.Lord.Position; var tick = frame.Tick + 1;
            // Mixed bearings and distances below the hero, including one knocked beyond the Core 90° fan.
            var offsets = new[] { (0, -2100), (900, -1700), (-1100, -1500), (300, -400), (1500, -300), (-1300, 250) };
            var events = new System.Collections.Generic.List<WaveEvent>();
            long id = 910000;
            for (var i = 0; i < offsets.Length; i++)
                events.Add(new WaveEvent(id++, tick, "hit", "core:iron_blade", 800 + i, new WorldPoint(lord.X + offsets[i].Item1, lord.Y + offsets[i].Item2), new WorldPoint(lord.X, lord.Y), 140));
            var attackId = id++;
            events.Add(new WaveEvent(attackId, tick, "attack", "core:iron_blade", -1, lord, new WorldPoint(lord.X, lord.Y - 1), 6000));
            for (var i = 0; i < offsets.Length; i++)
                events.Add(new WaveEvent(id++, tick, "enemy-killed", enemyId, 800 + i, new WorldPoint(lord.X + offsets[i].Item1, lord.Y + offsets[i].Item2), new WorldPoint(lord.X + offsets[i].Item1, lord.Y + offsets[i].Item2), 5));
            var owner = new GameObject("Giant blade QA"); var cameraOwner = new GameObject("Giant blade camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var world = CreateWorld(owner, camera, catalog, frame);
                world.ConfigureM1Combat("core:iron_blade", "giantblade", "test");
                // Victims are rendered alive first; the strike snapshot no longer contains them, as in Core.
                var victims = offsets.Select((o, i) => new EnemyView(800 + i, enemyId, new WorldPoint(lord.X + o.Item1, lord.Y + o.Item2), 100, 100)).ToArray();
                var crowd = frame with { Enemies = victims };
                var crowdWave = wave with { Enemies = victims.Select(v => new WaveEnemyView(v.Id, "approach", v.Position, v.Position, 100, false, false, 0)).ToArray() };
                world.AcceptWave(catalog, crowdWave);
                world.Present(crowd, crowd, WavePresentation.Envelope(catalog, crowd, session.View.CaptureCards(), crowdWave), 1, 0, new Rect(0, 0, 720, 1280));
                yield return null;
                frame = frame with { Tick = tick };
                var strike = wave with { Events = events.ToArray() };
                world.AcceptWave(catalog, strike);
                world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), strike), 1, Frame, new Rect(0, 0, 720, 1280));
                yield return null;
                Assert.That(world.EditorM1BladeEnvelope(attackId, out var origin, out var aim, out var radius, out var half, out var kills), Is.True);
                Assert.That(kills.Length, Is.GreaterThan(0), "Test kills are on screen.");
                foreach (var kill in kills)
                    Assert.That(GiantBladeEnvelope.Contains(origin, aim, radius, half, kill), Is.True, $"kill {kill} outside r={radius} half={half}");
                Assert.That(world.M1BladeKillsOutside, Is.Zero);
                Assert.That(world.CameraWorldViewport.Contains(origin), Is.True);
                for (var t = Frame; t < GiantBladeEnvelope.HoldSeconds; t += Frame)
                {
                    world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), strike), 1, Frame, new Rect(0, 0, 720, 1280));
                    yield return null;
                }
                Assert.That(world.FullEnemyDeaths, Is.GreaterThan(0), "Victims are still falling along the trajectory at its hold time.");
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }

        // P5: enemies > own attacks > allies. Muster soldiers, including a group standing on the hero and a lunge toward the hero
        // side, must never overlap the hero's drawn box.
        [UnityTest]
        public IEnumerator MusterFormationNeverOverlapsTheHero()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame() with { Enemies = System.Array.Empty<EnemyView>() };
            var lord = frame.Lord.Position;
            var groups = new[] { (0, 0), (200, 100), (-250, -150), (600, 0), (0, -700) }
                .Select((o, i) => new WaveGroupView(9000 + i, "core:muster_horn", new WorldPoint(lord.X + o.Item1, lord.Y + o.Item2), "engaging", 1, i % 2, 0, 0, "advance")).ToArray();
            var strike = new WaveEvent(950000, frame.Tick, "group-hit", "core:muster_horn", 1, new WorldPoint(lord.X - 300, lord.Y), new WorldPoint(lord.X - 300, lord.Y), 10);
            var wave = ((IWaveRunView)session.View).CaptureWaveRuntime() with { Enemies = System.Array.Empty<WaveEnemyView>(), Groups = groups, Events = new[] { strike } };
            // A weaker canonical reference makes the actual runtime horn a strong formation (GearRatio path).
            var reference = catalog.WaveRuntime with { Gear = catalog.WaveRuntime.Gear.ToDictionary(p => p.Key, p => p.Key == "core:muster_horn" ? p.Value with { Count = 1, Range = 100, Levels = null } : p.Value) };
            var owner = new GameObject("Muster overlap QA"); var cameraOwner = new GameObject("Muster overlap camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var world = CreateWorld(owner, camera, catalog, frame);
                world.GearReference = reference;
                var soldiers = new System.Collections.Generic.List<Rect>();
                world.RenderedMusterSoldierBounds = soldiers.Add;
                for (var i = 0; i < 12; i++)
                {
                    soldiers.Clear();
                    world.AcceptWave(catalog, wave);
                    world.Present(frame, frame, WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), wave), 1, Frame, new Rect(0, 0, 720, 1280));
                    yield return null;
                    if (i == 0) continue; // the hero box is learned from the first drawn hero
                    Assert.That(world.MusterPath, Is.EqualTo(MusterPresentationPath.GearRatio));
                    Assert.That(soldiers.Count, Is.GreaterThanOrEqualTo(groups.Length * 2));
                    foreach (var soldier in soldiers)
                        Assert.That(soldier.Overlaps(world.RenderedHeroSilhouette), Is.False, $"frame {i}: soldier {soldier} overlaps hero {world.RenderedHeroSilhouette}");
                }
                Assert.That(world.MusterLunges, Is.GreaterThan(0));
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }

        // Normal first-playable path: natural level-ups, real offered cards and no invulnerability or grants. Seed 30002 activates
        // rally_kitchen (muster horn + soup ladle at level 3) at tick 6630; the muster soldiers switch to the evolution look only then.
        [UnityTest]
        public IEnumerator NormalPlayRallyKitchenEvolutionIsTheOnlyPathToTheEvolutionLook()
        {
            if (CanonicalContent.ProfileName != "first-playable") Assert.Ignore("Requires first-playable export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30002, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var wanted = new[] { "core:muster_horn", "core:soup_ladle" };
            var tick = 0;
            bool Activated() => session.View.CaptureFirstPlayable().Evolutions.First(e => e.Id == "core:rally_kitchen").Activated;
            void Step()
            {
                if (session.View.Status == RunStatus.AwaitingCard)
                {
                    var offer = session.View.CaptureCards(); var details = session.View.CaptureFirstPlayable().Cards;
                    string pick = null;
                    foreach (var id in wanted) { var card = details.FirstOrDefault(c => c.Id == id); if (card != null && card.CurrentLevel < 3) { pick = id; break; } }
                    if (pick == null && offer.Rerolls > 0 && details.All(c => !wanted.Contains(c.Id)) && !Activated())
                    { session.Apply(new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.RerollCards)); return; }
                    pick = pick ?? offer.Cards.FirstOrDefault(c => catalog.Weapons.ContainsKey(c)) ?? offer.Cards[0];
                    session.Apply(new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: pick)); return;
                }
                var angle = (tick / 120) % 8 * Mathf.PI / 4;
                session.Apply(new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, new PlayerInput((short)(Mathf.Cos(angle) * 1000), (short)(Mathf.Sin(angle) * 1000))));
                tick++;
            }
            while (tick < 6540 && session.View.Status != RunStatus.Completed) Step();
            Assert.That(Activated(), Is.False);
            var owner = new GameObject("Normal muster evolution QA"); var cameraOwner = new GameObject("Normal muster evolution camera");
            var camera = cameraOwner.AddComponent<Camera>(); var texture = new RenderTexture(720, 1280, 24); camera.targetTexture = texture;
            try
            {
                var frame = session.View.CaptureFrame();
                Assert.That(frame.People.Any(p => p.Role == "militia" && p.SourceId == "core:muster_horn"), Is.True, "Muster soldiers are on the field before the evolution.");
                var world = CreateWorld(owner, camera, catalog, frame);
                var paths = new System.Collections.Generic.List<MusterPresentationPath>();
                world.MusterPresentationChanged = (source, formation) => paths.Add(formation.Path);
                world.AcceptWave(catalog, null);
                // Same per-frame order as RunCoordinator: first-playable passes a null wave frame before every presentation.
                void PresentNormal(RunFrame current)
                {
                    world.AcceptWave(catalog, null);
                    world.AcceptFrame(current, session.View.CaptureFirstPlayable());
                    world.Present(current, current, session.View.CaptureFirstPlayable(), 1, Frame, new Rect(0, 0, 720, 1280));
                }
                PresentNormal(frame);
                yield return null;
                Assert.That(world.MusterPath, Is.EqualTo(MusterPresentationPath.None), "Before the evolution the muster soldiers draw as before.");
                while (!Activated() && tick < 7200 && session.View.Status != RunStatus.Completed) Step();
                Assert.That(Activated(), Is.True, "Seed 30002 reaches rally_kitchen through normal play.");
                for (var i = 0; i < 90; i++) { Step(); PresentNormal(session.View.CaptureFrame()); if (i % 10 == 0) yield return null; }
                Assert.That(world.MusterPath, Is.EqualTo(MusterPresentationPath.RuntimeEvolution));
                Assert.That(world.MusterEvolutionId, Is.EqualTo("core:rally_kitchen"));
                Assert.That(world.MusterSoldiers, Is.EqualTo(MusterFormationRule.MaximumSoldiers));
                Assert.That(paths, Is.EqualTo(new[] { MusterPresentationPath.RuntimeEvolution }), "The review log records the path once, when the evolution activates.");
                Assert.That(world.MusterLunges, Is.GreaterThan(0), "Evolved soldiers attack with the real Core attack events.");
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }
    }
}
#endif
