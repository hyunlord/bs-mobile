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

        static AstraScene SceneB(bool reversed = false) => AstraReference.SceneB(reversed);

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
