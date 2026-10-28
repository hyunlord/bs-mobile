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
            Assert.That(plan.Items.Count(i => i.Role == "bridge-nw-se-base" && i.Source == "placed"), Is.EqualTo(3));
            Assert.That(plan.Road, Does.Contain(new Vector2Int(45, 33)), "The trunk reaches the east bridge without changing Core occupancy.");
        }
    }
}
