using System;
using System.IO;
using System.Linq;
using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    // #375: the C# port of Astra's assembly.js placement reproduces her B composition exactly (same rules, same seed).
    public sealed class AstraDecorTests
    {
        [Serializable] sealed class Fixture { public long seed; public string styleVersion; public Item[] objects; public Skip[] skipped; }
        [Serializable] sealed class Item { public string key, id, kind; public double x, y, w; }
        [Serializable] sealed class Skip { public string key, reason; }

        static Fixture Expected() => JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Tests/EditMode/Fixtures/astra-assembly-r1.json")));

        // Astra's fixed B composition (assembly.js): buildings, hand-drawn routes, the church and market reservations, screen bounds.
        static AstraScene SceneB(bool reversed = false)
        {
            var scene = new AstraScene { LandOriginX = 15, LandOriginY = 20, LandColumns = 8, LandRows = 15 };
            var buildings = new[]
            {
                ("home-01", "house", 1, 42, 76, 70), ("home-02", "house", 2, 112, 147, 70), ("home-03", "house", 2, 149, 113, 70), ("home-04", "house", 2, 283, 154, 70),
                ("home-05", "house", 1, 337, 197, 70), ("home-06", "house", 2, 290, 235, 70), ("home-07", "house", 3, 80, 330, 78), ("home-08", "house", 2, 177, 371, 74),
                ("well-01", "well", 2, 74, 219, 58), ("granary-01", "granary", 2, 369, 352, 105),
            }.Select(b => new AstraBuilding(b.Item1, b.Item2, b.Item3, b.Item4, b.Item5, b.Item6));
            scene.Buildings.AddRange(reversed ? buildings.Reverse() : buildings);
            double[][] R(params int[] xy) => Enumerable.Range(0, xy.Length / 2).Select(i => new double[] { xy[2 * i], xy[2 * i + 1] }).ToArray();
            scene.Routes.Add(R(48, 70, 112, 144, 78, 207, 107, 284, 177, 364, 256, 426, 348, 466));
            scene.Routes.Add(R(338, 198, 274, 232, 199, 270, 107, 284, 43, 325));
            scene.Routes.Add(R(177, 364, 112, 433, 31, 477, 82, 559, 210, 602, 342, 668));
            scene.Routes.Add(R(78, 207, 145, 178, 199, 191, 221, 255, 199, 270));
            scene.Obstacles.Add((199, 155, .7, 45)); scene.Obstacles.Add((217, 277, 1, 28));
            scene.RoadsideAllowed = (x, y) => !(x < 8 || x > 324);
            scene.LandAllowed = (x, y) => !(x > 319 || x < 10 || y < 10 || y > 818 || y > 425 && y < 566 && x < 150);
            scene.LandState = (x, y) => y < 410 ? "restored" : y < 630 ? "sage" : "fallow";
            return scene;
        }

        [Test] public void TheHashMatchesAssemblyJs()
        {
            Assert.That(AstraDecor.Hash(""), Is.EqualTo(2166136261u));
            Assert.That(AstraDecor.Hash("a"), Is.EqualTo(0xe40c292cu), "FNV-1a 32-bit over UTF-16 code units, as Math.imul in the JS.");
        }

        [Test] public void TheGameRulesReproduceAstrasBCompositionObjectForObject()
        {
            var rules = EstateDecorRules.Load().astra; var expected = Expected();
            Assert.That(rules.seed, Is.EqualTo(expected.seed)); Assert.That(rules.styleVersion, Is.EqualTo(expected.styleVersion));
            var (objects, skipped) = AstraDecor.Layout(rules, SceneB());
            Assert.That(objects.Count, Is.EqualTo(expected.objects.Length));
            var byKey = objects.ToDictionary(o => o.Key);
            foreach (var e in expected.objects)
            {
                Assert.That(byKey.TryGetValue(e.key, out var o), Is.True, "missing " + e.key);
                Assert.That(o.Id, Is.EqualTo(e.id), e.key); Assert.That(o.Kind, Is.EqualTo(e.kind), e.key);
                Assert.That(o.X, Is.EqualTo(e.x).Within(1e-9), e.key); Assert.That(o.Y, Is.EqualTo(e.y).Within(1e-9), e.key); Assert.That(o.W, Is.EqualTo(e.w).Within(1e-9), e.key);
            }
            Assert.That(skipped.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(expected.skipped.Select(s => s.key).OrderBy(k => k, StringComparer.Ordinal)));
            Assert.That(objects.Count(o => o.Kind == "yard"), Is.EqualTo(16)); Assert.That(objects.Count(o => o.Kind == "roadside"), Is.EqualTo(30)); Assert.That(objects.Count(o => o.Kind == "empty-land"), Is.EqualTo(13));
        }

        [Test] public void BuildingOrderSeedAndMovesBehaveAsAstrasChecks()
        {
            var rules = EstateDecorRules.Load().astra;
            string Key(AstraObject o) => o.ToString();
            var same = AstraDecor.Layout(rules, SceneB()).Objects.Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.That(AstraDecor.Layout(rules, SceneB(true)).Objects.Select(Key).OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(same), "building input order does not matter");
            var other = JsonUtility.FromJson<AstraDecorRules>(JsonUtility.ToJson(rules)); other.seed = rules.seed + 1;
            Assert.That(AstraDecor.Layout(other, SceneB()).Objects.Select(Key).OrderBy(k => k, StringComparer.Ordinal), Is.Not.EqualTo(same), "a different seed gives a different layout");
            var home = SceneB().Buildings[0]; var moved = new AstraBuilding(home.Key, home.Kind, home.Stage, home.X + 50, home.Y + 30, home.W);
            var before = AstraDecor.YardItems(rules, home); var after = AstraDecor.YardItems(rules, moved);
            Assert.That(after.Select(o => o.Id), Is.EqualTo(before.Select(o => o.Id)), "a moved building keeps its yard kit");
            for (var i = 0; i < before.Count; i++) { Assert.That(after[i].X - before[i].X, Is.EqualTo(50).Within(1e-8)); Assert.That(after[i].Y - before[i].Y, Is.EqualTo(30).Within(1e-8)); }
        }
    }
}
