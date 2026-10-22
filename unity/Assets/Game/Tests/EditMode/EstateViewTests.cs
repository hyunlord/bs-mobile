using System;
using System.Collections.Generic;
using System.IO;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // M2 dark estate View first slice (#347): run options, Core-derived ring and dark rules, review flags and building art.
    public sealed class EstateViewTests
    {
        internal const string Tower = "core:estate_watchtower", Well = "core:estate_well_shrine", Granary = "core:estate_granary";

        // The opt-in light's shape (data/opt-in/estate-light/dark-estate-m2.json) with its real building IDs.
        internal static WaveEstateLightDefinition Light() => new(WaveEstateLight.Version, "core:estate_home", 2500, 2600, 250, 50, 150,
            new Dictionary<string, WaveEstateBuildingDefinition>
            {
                [Tower] = new(Tower, "test", WaveEstateWake.Channel, 450, 2200, 150, WaveEstateResource.Food, 0, 0, new(WaveEstateEffectKind.Attack, 30, 45)),
                [Well] = new(Well, "test", WaveEstateWake.Spend, 450, 2200, 0, WaveEstateResource.Food, 20, 15, new(WaveEstateEffectKind.Heal, 18, 30, 90)),
                [Granary] = new(Granary, "test", WaveEstateWake.Spend, 450, 2200, 0, WaveEstateResource.Food, 15, 15, new(WaveEstateEffectKind.Magnet, 60, 0, 0, 1))
            },
            new Dictionary<string, WaveEstateSlot[]>
            {
                ["L1"] = new[] { new WaveEstateSlot(Tower, 8000, 11000), new WaveEstateSlot(Well, 3000, 5000), new WaveEstateSlot(Granary, 4000, 6000) }
            });

        internal static ContentCatalog EstateCatalog()
        {
            var c = CanonicalContent.CreateCatalog();
            return c with { WaveRuntime = c.WaveRuntime with { EstateLight = Light() } };
        }

        [Test]
        public void OnlyACatalogWithAnEstateLightGetsEstateOptionsAndRecordsFormatSeven()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var plain = CanonicalContent.CreateCatalog();
            Assert.That(CatalogRunOptions.Estate(plain), Is.Null, "default profiles keep their run options");
            var c = EstateCatalog();
            var estate = CatalogRunOptions.Estate(c);
            Assert.That((estate.Version, estate.LoadoutId, estate.LoadoutCatalogHash), Is.EqualTo((WaveEstateLight.Version, "L1", ReviewShowcaseCatalog.Hash(c))));
            var options = new InteractiveOptions(new RunOptions(30001, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement,
                CanonicalContent.DataHash, MeleeAim: CatalogRunOptions.MeleeAim(c), Estate: estate);
            // wave-1a gear declares no melee aim (the "aim" values in its data are attack-shape program parameters), so the
            // estate run records format 7 schema 1; a catalog with melee aim would record schema 2 (#349).
            Assert.That(options.MeleeAim, Is.Null, "wave-1a gear has no melee aim");
            var session = new InteractiveSession(c, options);
            Assert.That(((IWaveRunView)session.View).CaptureWaveRuntime().Estate.Buildings.Count, Is.EqualTo(4), "home and three L1 buildings");
            var header = ReplayCodec.Header(options);
            Assert.That(header.FormatVersion, Is.EqualTo(ReplayCodec.EstateVersion));
            using var bytes = new MemoryStream();
            ReplayCodec.WriteHeader(bytes, header);
            ReplayCodec.WriteEnd(bytes, new(0, 0, ReplayEndKind.Quit, session.ComputeStateHash()));
            bytes.Position = 0;
            Assert.That(ReplayCodec.Read(bytes).Header.Options.MeleeAim, Is.EqualTo(options.MeleeAim), "format 7 keeps the run's melee aim either way");
        }

        static WaveEstateBuildingView Building(WaveEstatePhase phase, int progress = 0, int required = 0, int dwell = 0, int confirm = 0, bool paid = false, int x = 0) =>
            new(1, Tower, new WorldPoint(x, 0), phase, progress, required, dwell, confirm, paid, WaveEstateResource.Food, 10, 450, 2200);

        [Test]
        public void RingFillsFromCoreProgressOnly()
        {
            Assert.That(WorldRenderer.EstateWakeFraction(Building(WaveEstatePhase.Asleep, required: 150)), Is.EqualTo(0));
            Assert.That(WorldRenderer.EstateWakeFraction(Building(WaveEstatePhase.Waking, 75, 150)), Is.EqualTo(.5f));
            Assert.That(WorldRenderer.EstateWakeFraction(Building(WaveEstatePhase.Asleep, dwell: 5, confirm: 15)), Is.EqualTo(1f / 3).Within(1e-6f), "paying: the confirming stay");
            Assert.That(WorldRenderer.EstateWakeFraction(Building(WaveEstatePhase.Waking, 30, 60, 15, 15, true)), Is.EqualTo(.5f), "both: paid, then the channel");
            Assert.That(WorldRenderer.EstateWakeFraction(Building(WaveEstatePhase.Active)), Is.EqualTo(1));
            Assert.That(WorldRenderer.EstateWakeStalled(Building(WaveEstatePhase.Asleep, dwell: 15, confirm: 15)), Is.True, "stay complete but unpaid");
            Assert.That(WorldRenderer.EstateWakeStalled(Building(WaveEstatePhase.Asleep, dwell: 14, confirm: 15)), Is.False);
        }

        [Test]
        public void DarkMeansOutsideEveryActiveLitCircleAsInCore()
        {
            var home = new WaveEstateBuildingView(0, "core:estate_home", new WorldPoint(0, 0), WaveEstatePhase.Active, 0, 0, 0, 0, false, WaveEstateResource.Food, 0, 0, 2500);
            var asleep = Building(WaveEstatePhase.Asleep, x: 9000);
            var frame = new WaveEstateFrame(1, "L1", 2600, new[] { home, asleep }, 1, 0);
            Assert.That(WorldRenderer.EstateLit(frame, new WorldPoint(2500, 0)), Is.True, "edge of the home circle is lit");
            Assert.That(WorldRenderer.EstateLit(frame, new WorldPoint(2501, 0)), Is.False);
            Assert.That(WorldRenderer.EstateLit(frame, new WorldPoint(9000, 0)), Is.False, "a sleeping building lights nothing");
            var awake = new WaveEstateFrame(1, "L1", 2600, new[] { home, Building(WaveEstatePhase.Active, x: 9000) }, 0, 0);
            Assert.That(WorldRenderer.EstateLit(awake, new WorldPoint(9000 + 2200, 0)), Is.True);
            // The hero's sight never lights: Core's ground for the same point is fallow grey, not restored.
            Assert.That(WaveEstateLight.Ground(frame, new WorldPoint(5000, 0), new WorldPoint(5100, 0)), Is.EqualTo(WaveEstateGround.Fallow));
            Assert.That(WorldRenderer.EstateLit(frame, new WorldPoint(5100, 0)), Is.False);
        }

        [Test]
        public void DarkMarkerFlagAcceptsEyesAndOutlineOnly()
        {
            Assert.That(WorldRenderer.ParseDarkMarker(new[] { "app" }), Is.EqualTo(EstateDarkMarker.Eyes));
            Assert.That(WorldRenderer.ParseDarkMarker(new[] { "--estate-dark-marker", "outline" }), Is.EqualTo(EstateDarkMarker.Outline));
            Assert.That(WorldRenderer.ParseDarkMarker(new[] { "--estate-dark-marker", "eyes" }), Is.EqualTo(EstateDarkMarker.Eyes));
            Assert.Throws<ArgumentException>(() => WorldRenderer.ParseDarkMarker(new[] { "--estate-dark-marker", "glow" }));
            Assert.Throws<ArgumentException>(() => WorldRenderer.ParseDarkMarker(new[] { "--estate-dark-marker" }));
        }

        [Test]
        public void EstateReviewMovementWalksToTheNearestSleepingBuildingAndSkipsAStalledOne()
        {
            var lord = new LordView(new WorldPoint(0, 0), new WorldPoint(1, 0), 100, 100);
            RunFrame Frame() => new RunFrame(0, RunStatus.Running, 0, 0, 0, 60, 1, 1, lord, default, 0, 1, 1, 1, new EntityCounts(0, 0, 0, 0, 0), Array.Empty<EnemyView>(), Array.Empty<FarmView>(),
                Array.Empty<BuildingView>(), Array.Empty<PersonView>(), Array.Empty<GroundLootView>(), Array.Empty<RemainView>(), Array.Empty<EquipmentView>(), Array.Empty<PresentationEvent>());
            WaveRuntimeFrame Wave(params WaveEstateBuildingView[] buildings) => new WaveRuntimeFrame("r", "c", 0, 0, 0, false, Array.Empty<WaveWorkView>(), Array.Empty<WaveRewardView>(),
                Array.Empty<WaveProjectileView>(), Array.Empty<WaveEnemyView>(), Array.Empty<WaveGroupView>(), Array.Empty<WorldPoint>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<WaveEvent>(), new Dictionary<string, long>(), Array.Empty<WaveAttackView>(), 0, Array.Empty<WaveDetourView>(), default, 0,
                new WaveEstateFrame(1, "L1", 2600, buildings, buildings.Length, 0));
            var near = Building(WaveEstatePhase.Asleep, x: 3000); var far = Building(WaveEstatePhase.Asleep, x: 9000);
            Assert.That(WaveCaptureInput.EstateTarget(Frame(), Wave(far, near)), Is.EqualTo(near.Position));
            var stalled = Building(WaveEstatePhase.Asleep, dwell: 15, confirm: 15, x: 3000);
            Assert.That(WaveCaptureInput.EstateTarget(Frame(), Wave(far, stalled)), Is.EqualTo(far.Position), "an unaffordable stay waits while another is left");
            Assert.That(WaveCaptureInput.EstateTarget(Frame(), Wave(Building(WaveEstatePhase.Active, x: 3000))), Is.Null, "all awake: the ordinary target");
        }

        // Astra's packed sheets: both states of a building share one cell and pivot; the visible heights at a 115 px hero are the
        // approved 210/130/170 px, so the world height is that many pixels at the knight's own pixel scale.
        [TestCase(Tower, 227, 210)]
        [TestCase(Well, 148, 130)]
        [TestCase(Granary, 179, 170)]
        public void BuildingSheetsBindBothLoadoutsAndKeepTheApprovedSize(string id, int cellHeight, int visibleHeight)
        {
            var m = JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(Application.dataPath, "Art", "wave1a-manifest.json")));
            ArtRoleDefinition Bound(string content, string state)
            {
                var binding = Array.Find(m.bindings, b => b.kind == "estate" && b.contentId == content && b.state == state);
                Assert.That(binding, Is.Not.Null, content + " " + state);
                return Array.Find(m.roles, r => r.id == binding.roleId);
            }
            var heroPixel = 1.081423f / 240;
            foreach (var content in new[] { id, id + "_l3" })
            {
                var asleep = Bound(content, "asleep"); var awake = Bound(content, "active");
                Assert.That(asleep.atlas, Is.EqualTo("estate-buildings"));
                Assert.That((asleep.pivot.x, asleep.pivot.y, asleep.worldSize.x, asleep.worldSize.y), Is.EqualTo((awake.pivot.x, awake.pivot.y, awake.worldSize.x, awake.worldSize.y)));
                Assert.That(asleep.frames[0].height, Is.EqualTo(cellHeight));
                Assert.That(asleep.worldSize.y, Is.EqualTo(cellHeight * heroPixel).Within(1e-5f));
                Assert.That(visibleHeight, Is.LessThanOrEqualTo(cellHeight));
            }
        }
    }
}
