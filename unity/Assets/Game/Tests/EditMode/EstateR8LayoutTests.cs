using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateR8LayoutTests
    {
        [Test]
        public void R8RoadsAndZoneLabelsUseTheApprovedLayoutRatherThanHistoricalCoordinates()
        {
            var config = CanonicalContent.CreateAccountEstateConfig();
            var catalog = CanonicalContent.CreateMetaCatalog();
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var plan = EstatePlanner.Plan(rules, art, config, state.Estate);
            var again = EstatePlanner.Plan(rules, art, config, state.Estate);
            string Item(EstateDrawItem i) => i.Key + i.Role + i.Grid.ToString("F4");
            Assert.That(again.Items.Select(Item), Is.EqualTo(plan.Items.Select(Item)));
            var footprints = state.Estate.Buildings.SelectMany(b =>
            {
                var kind = config.Buildings.Single(k => k.Kind == b.Kind);
                return Enumerable.Range(b.X, kind.Width).SelectMany(x => Enumerable.Range(b.Y, kind.Height).Select(y => new Vector2Int(x, y)));
            }).ToHashSet();
            Assert.That(plan.Road.Overlaps(footprints), Is.False);
            foreach (var item in plan.Items.Where(i => i.Source == "yard" || i.Source == "roadside" || i.Source == "empty-land"))
            {
                var cell = Vector2Int.FloorToInt(item.Grid);
                Assert.That(config.Zones.Any(z => state.Estate.OpenZones.Contains(z.Id) && cell.x >= z.X && cell.y >= z.Y && cell.x < z.X + z.Width && cell.y < z.Y + z.Height), Is.True, item.Key);
                Assert.That(footprints.Contains(cell) && item.Source != "yard", Is.False, item.Key);
            }
            foreach (var item in plan.In(EstateDrawLayer.Veiled))
                Assert.That(plan.StateOf(Vector2Int.FloorToInt(item.Grid)), Is.EqualTo("closed").Or.Null, item.Key);
            var authored = EstateAuthoredLayout.Load(config);
            Assert.That(plan.Count("road"), Is.Zero, "r8 drawing uses approved path instances, not generated road ribbons.");
            Assert.That(authored.instances.Count(i => i.category == "path"), Is.EqualTo(24));
            foreach (var zone in config.Zones)
            {
                var ground = authored.zones.Single(z => z.id == zone.Id);
                Assert.That(ground.rect, Is.EqualTo(new float[] { zone.X, zone.Y, zone.Width, zone.Height }));
                Assert.That(authored.assets.Any(a => a.id == ground.groundAssetId && a.size.SequenceEqual(new float[] { 512,512 })), Is.True);
                for (var x = zone.X; x < zone.X + zone.Width; x++)
                    for (var y = zone.Y; y < zone.Y + zone.Height; y++)
                        Assert.That(x + .5f >= ground.rect[0] && x + .5f < ground.rect[0] + ground.rect[2] &&
                            y + .5f >= ground.rect[1] && y + .5f < ground.rect[1] + ground.rect[3], Is.True, zone.Id);
            }
            var screen = new EstateMapScreen(catalog, config, state);
            foreach (var zone in config.Zones.Where(z => !z.OpenAtStart))
            {
                Assert.That(screen.ZoneAt(new Vector2Int(zone.X, zone.Y)).Id, Is.EqualTo(zone.Id));
                Assert.That(screen.ZoneLabel(zone), Is.EqualTo("닫힌 구역"), "All r8 production requirements are reachable: " + zone.Id);
            }
            Assert.That(screen.OpenZone("zone:outskirts-east"), Is.False);
            Assert.That(screen.Notice, Is.EqualTo("생산 터를 더 완성해야 합니다"));
        }

        [Test]
        public void RiverArtFollowsTheProjectedTangentAndOnlyTurnsUseBendArt()
        {
            var layout = EstateR8Layout.Load();
            var river = layout.placed.Where(p => p.id.StartsWith("river")).ToArray();
            Assert.That(river.Count(p => p.id == "river-bend-patch"), Is.EqualTo(2));
            foreach (var piece in river.Where(p => p.id == "riverbank-patch"))
            {
                var expected = piece.u > 27 && piece.u < 45 ? 63.43495f : 0;
                Assert.That(piece.rotation, Is.EqualTo(expected).Within(.001f));
                // The straight art's NE slope becomes vertical on the central diagonal grid path.
                var artTangent = EstateR8Layout.RotateGridOffset(new Vector2(0, -1), piece.rotation);
                var screenTangent = EstateMapModel.Project(artTangent).normalized;
                if (expected != 0) Assert.That(Mathf.Abs(screenTangent.x), Is.LessThan(.001f));
            }
            Assert.That(EstateDecorRules.Load().placed.All(p => p.rotation == 0), Is.True, "Historical art direction is untouched.");
            var config = CanonicalContent.CreateAccountEstateConfig();
            var state = AccountEstate.NewPrototype(CanonicalContent.CreateMetaCatalog(), config, 0, 0);
            var plan = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            foreach (var silhouette in plan.Items.Where(i => i.Source == "landmark" && i.Role.StartsWith("river")))
                Assert.That(silhouette.Rotation, Is.EqualTo(plan.Items.Single(i => i.Source == "placed" && i.Role == silhouette.Role && i.Grid == silhouette.Grid).Rotation));
        }

        [Test]
        public void DefaultIsR8AndSavedVersionsKeepTheirOwnGeometry()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            Assert.That(CanonicalContent.CreateAccountEstateConfig().Version, Is.EqualTo(EstateR8Layout.Version));
            foreach (var revision in new[] { "r3", "r4", "r5", "r6", "r7" })
            {
                var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-" + revision);
                var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
                var bytes = MetaSaveCodec.Encode(state);
                var decoded = MetaSaveCodec.Decode(bytes).State;
                var historical = CanonicalContent.CreateAccountEstateConfig(decoded.Estate.ConfigVersion);
                var screen = new EstateMapScreen(catalog, historical, decoded);
                Assert.That(screen.FromSave, Is.True);
                Assert.That(screen.Config.Version, Is.EqualTo(config.Version));
                Assert.That(MetaSaveCodec.Encode(screen.State), Is.EqualTo(bytes));
            }
        }

        [Test]
        public void R8RiverAndBridgesAvoidTheUnchangedStartAndLeaveHistoricalPiecesUntouched()
        {
            var config = CanonicalContent.CreateAccountEstateConfig();
            var layout = EstateR8Layout.Load();
            var start = config.Zones.Single(z => z.OpenAtStart);
            Assert.That(layout.version, Is.EqualTo(config.Version));
            Assert.That(layout.bridges, Has.Length.EqualTo(3));
            Assert.That(layout.riverCells.All(c => !(c.x >= start.X && c.x < start.X + start.Width && c.y >= start.Y && c.y < start.Y + start.Height)), Is.True);
            foreach (var bridge in layout.bridges)
            {
                Assert.That(layout.riverCells.Any(c => c.y == bridge.y && c.x >= bridge.x && c.x < bridge.x + bridge.width), Is.True, bridge.id);
                Assert.That(layout.riverCells.Any(c => c.y == bridge.y && c.x == bridge.x), Is.False, bridge.id);
                Assert.That(layout.riverCells.Any(c => c.y == bridge.y && c.x == bridge.x + bridge.width - 1), Is.False, bridge.id);
            }
            var rules = EstateDecorRules.Load(); var original = JsonUtility.ToJson(rules);
            var state = AccountEstate.NewPrototype(CanonicalContent.CreateMetaCatalog(), config, 0, 0);
            var plan = EstatePlanner.Plan(rules, EstateArt.Load(), config, state.Estate);
            var allOpen = state.Estate with { OpenZones = config.Zones.Select(z => z.Id).ToArray() };
            var full = EstatePlanner.Plan(rules, EstateArt.Load(), config, allOpen);
            var river = layout.riverCells.Select(c => new Vector2Int(c.x, c.y)).ToHashSet();
            foreach (var cell in river)
            {
                var point = EstateMapModel.Project(cell + new Vector2(.5f, .5f));
                Assert.That(full.Scene.LandAllowed(point.x, point.y), Is.False, "No empty-land decoration on water.");
                Assert.That(full.Scene.RoadsideAllowed(point.x, point.y), Is.False, "No roadside decoration on water.");
            }
            foreach (var item in full.Items.Where(i => i.Source == "soil" || i.Source == "empty-land" || i.Source == "roadside"))
                Assert.That(river.Contains(Vector2Int.FloorToInt(item.Grid)), Is.False, item.Key);
            Assert.That(JsonUtility.ToJson(rules), Is.EqualTo(original));
            var r7 = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            Assert.That(EstateR8Layout.Pieces(rules, r7), Is.SameAs(rules.placed));
            var authored = EstateAuthoredLayout.Load(config);
            Assert.That(authored.instances.Single(i => i.category == "river").assetId, Is.EqualTo("river-r2-reconstructed-native"));
            var bridges = authored.instances.Where(i => i.category == "bridge").OrderBy(i => i.drawOrder).ToArray();
            Assert.That(bridges.Select(i => i.assetId), Is.EqualTo(new[] {
                "terrain-r8-bridge-north-wood-bright-r1-1x", "terrain-r8-bridge-east-fallow-bright-r1-1x", "terrain-r8-bridge-south-riverside-bright-r1-1x" }));
            Assert.That(bridges.SelectMany(i => i.grid), Is.EqualTo(new float[] { 31.5f,16.5f,45,33.5f,45,44.5f }));
            Assert.That(plan.Items.Any(i => i.Role == "bridge-nw-se-base" && i.Source == "placed"), Is.False, "No duplicate legacy bridge below the approved bridge.");
            Assert.That(plan.Road, Does.Contain(new Vector2Int(45, 33)), "The trunk reaches the east bridge without changing Core occupancy.");
        }
    }
}
