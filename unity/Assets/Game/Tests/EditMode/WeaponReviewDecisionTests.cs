using System;
using System.IO;
using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    // User decisions 2026-10-20 on the #335 weapon review videos (View only; Core radii and timings unchanged).
    public sealed class WeaponReviewDecisionTests
    {
        [Serializable] sealed class Box { public int left, top, right, bottom; }
        [Serializable] sealed class Frame { public int width, height; }
        [Serializable] sealed class Replaced { public string id; public Frame frame; public Box visibleBox; }
        [Serializable] sealed class Record { public Replaced[] replaced; }

        // The aroma replaces the dashed lure ring: its visible outer edge must sit within 5% of the Core radius.
        [Test] public void AromaRingOuterEdgeIsWithinFivePercentOfTheCoreRadius()
        {
            var record = JsonUtility.FromJson<Record>(File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/weapon-fx/replaced-cells.json")));
            var aroma = record.replaced.Single(r => r.id == "fx.granary_sling.aroma");
            var topMargin = aroma.visibleBox.top / (float)aroma.frame.height;   // v 1 (the strip's top) is the ring's outer edge
            var outer = SlingSpec.AromaVisibleOuter(topMargin);
            Assert.That(outer, Is.InRange(.95f, 1.05f), $"visible outer edge {outer:0.000} x the Core radius");
            Assert.That(SlingSpec.AromaBand, Is.InRange(.2f, .6f));
        }

        // The flying sack is 1.5x and its shadow shrinks and fades with height, matching the sack when it lands.
        [Test] public void FlyingSackShadowShrinksAndFadesWithHeightAndIsFullOnLanding()
        {
            Assert.That(SlingSpec.FlightScale, Is.EqualTo(1.5f));
            var ground = SlingSpec.Shadow(0); var apex = SlingSpec.Shadow(1);
            Assert.That(ground.Width, Is.EqualTo(1));
            Assert.That(ground.Alpha, Is.EqualTo(SlingSpec.ShadowAlpha));
            Assert.That(apex.Width, Is.LessThan(ground.Width)); Assert.That(apex.Alpha, Is.LessThan(ground.Alpha));
            for (var h = 0f; h < 1; h += .1f)
            {
                Assert.That(SlingSpec.Shadow(h + .1f).Width, Is.LessThanOrEqualTo(SlingSpec.Shadow(h).Width));
                Assert.That(SlingSpec.Shadow(h + .1f).Alpha, Is.LessThanOrEqualTo(SlingSpec.Shadow(h).Alpha));
            }
        }

        // The spore patch is drawn at .55, superseded patches fade within .3 s, and stacked patches never cover more than 1.3x one.
        [Test] public void SporePatchStackNeverCoversMoreThanOnePointThreePatches()
        {
            Assert.That(SporePatchSpec.PatchAlpha, Is.EqualTo(.55f));
            Assert.That(SporePatchSpec.PatchOfRadius, Is.EqualTo(2.1f), "the visible stain keeps matching the damage area");
            var cap = SporePatchSpec.PatchAlpha * SporePatchSpec.OverlapCap;
            var random = new System.Random(7);
            for (var trial = 0; trial < 200; trial++)
            {
                var transmittance = 1f;
                var layers = 1 + random.Next(8);
                for (var k = 0; k < layers; k++)
                    SporePatchSpec.CappedAlpha((float)random.NextDouble() * SporePatchSpec.PatchAlpha * (1 + SporePatchSpec.PulseBrighten), ref transmittance);
                Assert.That(1 - transmittance, Is.LessThanOrEqualTo(cap + 1e-5f), $"trial {trial}: {layers} stacked patches");
            }
            var one = 1f;
            Assert.That(SporePatchSpec.CappedAlpha(SporePatchSpec.PatchAlpha, ref one), Is.EqualTo(SporePatchSpec.PatchAlpha), "a lone patch keeps its alpha");
            Assert.That(SporePatchSpec.Superseded(0), Is.EqualTo(1));
            Assert.That(SporePatchSpec.Superseded(SporePatchSpec.SupersededFadeSeconds), Is.EqualTo(0), "gone .3 s after a newer patch is laid");
            Assert.That(GameVisualTokens.AttackLayer, Is.LessThan(GameVisualTokens.EnemyLayer));
            Assert.That(GameVisualTokens.AttackLayer, Is.LessThan(GameVisualTokens.LordLayer), "the decal is drawn under the enemies and the hero");
        }

        // Crowd flash (2026-10-20): of the enemies whose flash starts on one frame, the 8 nearest the hero flash fully, the rest at .25.
        [Test] public void OnlyTheEightNearestOfOneFramesHitsFlashFully()
        {
            var crowd = new CrowdFlash();
            for (var id = 0; id < 24; id++) crowd.Note(100 + id, 5, 1, (24 - id) * (24 - id));   // id 123 is the nearest
            for (var id = 0; id < 3; id++) crowd.Note(500 + id, 6, 1.02f, 900);                   // another frame: its own ranking
            var full = Enumerable.Range(100, 24).Where(id => crowd.Strength(id) == 1).ToArray();
            Assert.That(full, Is.EqualTo(Enumerable.Range(116, 8).ToArray()), "the 8 nearest");
            Assert.That(Enumerable.Range(100, 16).All(id => crowd.Strength(id) == HitReaction.CrowdDimFlash), Is.True);
            Assert.That(HitReaction.CrowdDimFlash, Is.EqualTo(.25f));
            Assert.That(Enumerable.Range(500, 3).All(id => crowd.Strength(id) == 1), Is.True, "three hits on a frame all flash fully");
            Assert.That(crowd.Strength(42), Is.EqualTo(1), "an unranked flash is full");
            crowd.Forget(1.01f);
            Assert.That(crowd.Strength(100), Is.EqualTo(1), "forgotten after the flash");
            Assert.That(crowd.Strength(500), Is.EqualTo(1));
        }

        // Damage numbers keep off the hero (2026-10-20): within .6 x the knight's height of its centre a number moves above the head;
        // with no free slot there it stays, at .35 alpha. No full-alpha number overlaps the keep-out circle.
        [Test] public void DamageNumbersNearTheHeroMoveAboveItsHeadOrFade()
        {
            Assert.That(WorldDamageNumbers.HeroKeepOutOfFigureHeight * .6f, Is.EqualTo(.36f).Within(1e-6f), "radius .36 for the .60 knight");
            var owner = new GameObject("Damage numbers near the hero");
            var centre = new Vector2(0, .3f); const float radius = .36f;
            using (var numbers = new WorldDamageNumbers(owner.transform, visibleLimit: WorldDamageNumbers.Capacity))
            {
                numbers.SetHero(centre, radius);
                numbers.Add(new Vector2(.05f, .2f), 24);
                Assert.That(numbers.MovedAboveHero, Is.EqualTo(1));
                for (var i = 0; i < 60; i++) numbers.Add(new Vector2(.05f, .2f), 25 + i);
                Assert.That(numbers.FadedOverHero, Is.GreaterThan(0), "once the slots above are taken, numbers fade instead");
                var labels = owner.GetComponentsInChildren<TextMesh>().Where(t => t.transform.parent == owner.transform).ToArray();
                Assert.That(labels.Length, Is.GreaterThan(1));
                foreach (var label in labels)
                {
                    var bounds = label.GetComponent<MeshRenderer>().bounds;
                    var closest = new Vector2(Mathf.Clamp(centre.x, bounds.min.x, bounds.max.x), Mathf.Clamp(centre.y, bounds.min.y, bounds.max.y));
                    if ((closest - centre).magnitude < radius) Assert.That(label.color.a, Is.LessThanOrEqualTo(WorldDamageNumbers.HeroFadedAlpha + 1e-4f), label.text);
                }
                foreach (var label in labels)
                {
                    var bounds = label.GetComponent<MeshRenderer>().bounds;
                    var risen = new Vector2(Mathf.Clamp(centre.x, bounds.min.x, bounds.max.x), Mathf.Clamp(centre.y, bounds.min.y, bounds.max.y + WorldDamageNumbers.RiseDistance));
                    if ((risen - centre).magnitude < radius) Assert.That(label.color.a, Is.LessThanOrEqualTo(WorldDamageNumbers.HeroFadedAlpha + 1e-4f), "no full number rises into the hero: " + label.text);
                }
                Assert.That(numbers.NearHero(new Vector2(0, -.2f)), Is.True, "born below the feet, it would rise into the body");
                Assert.That(numbers.NearHero(new Vector2(0, -.4f)), Is.False);
                numbers.SetHero(centre, 0);
                numbers.Add(new Vector2(3, 3), 7);
                Assert.That(numbers.MovedAboveHero + numbers.FadedOverHero, Is.EqualTo(61), "away from the hero nothing changes");
            }
            UnityEngine.Object.DestroyImmediate(owner);
        }

        // Cast lightning over the hero (2026-10-20): inside the hero box bolt, glow and target flash are drawn at .35.
        [Test] public void LightningInsideTheHeroBoxIsDrawnAtAThirdAndOutsideUnchanged()
        {
            Assert.That(LightningSpec.OverHeroAlpha, Is.EqualTo(.35f));
            var box = new Rect(-.395f, -.16f, .79f, .92f);
            Assert.That(LightningSpec.OverHero(box, new Vector2(0, .3f)), Is.True);
            Assert.That(LightningSpec.OverHero(box, new Vector2(.5f, .3f)), Is.False);
        }

        [Serializable] sealed class HornRole { public string id; public float outerRadiusPx; public Box2 crop; }
        [Serializable] sealed class Box2 { public int left, top, width, height; }
        [Serializable] sealed class HornRecord { public HornRole[] roles; }
        [Serializable] sealed class ArtRole { public string id; public Vec2 worldSize; }
        [Serializable] sealed class Vec2 { public float x, y; }
        [Serializable] sealed class ArtManifestRoles { public ArtRole[] roles; }

        // Muster horn sound wave (2026-10-20): the painted outer edge lands on the Core radius, the wave grows 0.3 -> 1.0 like the
        // outline ring it replaces, and the comparison layer sits above the enemies, under the hero, at half opacity.
        [Test] public void HornWaveOuterEdgeLandsOnTheCoreRadiusAndGrowsFromThreeTenths()
        {
            var packed = JsonUtility.FromJson<HornRecord>(File.ReadAllText(Path.Combine(Application.dataPath, "../../tools/art-authoring/horn-wave/packed.json")));
            var manifest = JsonUtility.FromJson<ArtManifestRoles>(File.ReadAllText(Path.Combine(Application.dataPath, "Art/wave1a-manifest.json")));
            foreach (var id in new[] { HornWaveSpec.Level1Role, HornWaveSpec.EvolvedRole })
            {
                var record = packed.roles.Single(r => r.id == id);
                var role = manifest.roles.Single(r => r.id == id);
                var edgeInRadii = record.outerRadiusPx * role.worldSize.x / record.crop.width;
                Assert.That(edgeInRadii, Is.EqualTo(1).Within(.01f), id + ": painted outer edge at the radius");
                Assert.That(role.worldSize.y / role.worldSize.x, Is.EqualTo(record.crop.height / (float)record.crop.width).Within(.001f), id + ": not stretched");
            }
            Assert.That(HornWaveSpec.Share(0), Is.EqualTo(.3f)); Assert.That(HornWaveSpec.Share(1), Is.EqualTo(1));
            Assert.That(HornWaveSpec.AboveEnemiesOpacity, Is.EqualTo(.5f));
            Assert.That(GameVisualTokens.AttackLayer, Is.LessThan(GameVisualTokens.EnemyLayer), "Astra's layer: under the enemies and the hero");
            Assert.That(GameVisualTokens.WaveTransientCueLayer, Is.GreaterThan(GameVisualTokens.EnemyLayer + 19).And.LessThan(GameVisualTokens.LordLayer), "comparison layer: above every enemy row, under the hero");
        }
    }
}
