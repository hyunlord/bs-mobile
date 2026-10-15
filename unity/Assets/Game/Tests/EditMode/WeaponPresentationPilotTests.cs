using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // "무기 연출 2차" pilot: storm_fork lightning and muster_horn soldiers.
    public sealed class WeaponPresentationPilotTests
    {
        const float Frame = 1f / 60;

        [Serializable] sealed class Level { public int damage, range, cooldownTicks, speed, count, lifetimeTicks, knockback; }
        [Serializable] sealed class Gear { public string id, designRef, kind; public int damage, range, cooldownTicks, speed, count, lifetimeTicks, knockback, workTicks, workRadius, rewardExperience, capacity; public Level[] levels; }
        [Serializable] sealed class Case { public string id, stage; public Gear gear; }
        [Serializable] sealed class Cases { public Case[] cases; }

        static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        static WaveGearDefinition Definition(Gear g) => new WaveGearDefinition(g.id, g.designRef, WaveAttackKind.Chain, g.damage, g.range, g.cooldownTicks, g.speed, g.count,
            g.lifetimeTicks, g.knockback, g.workTicks, g.workRadius, g.rewardExperience, g.capacity,
            g.levels == null || g.levels.Length == 0 ? null : g.levels.Select(l => new WaveGearLevel(l.damage, l.range, l.cooldownTicks, l.speed, l.count, l.lifetimeTicks, l.knockback)).ToArray());

        // Reads the canonical gear from data/runtime/wave-1a.json without a JSON dictionary parser: the gear object is located by
        // its id key and parsed with JsonUtility.
        static WaveGearDefinition Canonical(string id)
        {
            var text = File.ReadAllText(Path.Combine(Repository, "data/runtime/wave-1a.json"));
            var key = text.IndexOf("\"" + id + "\"", text.IndexOf("\"gear\"", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.That(key, Is.GreaterThan(0), id);
            var start = text.IndexOf('{', key);
            var depth = 0; var end = start;
            for (; end < text.Length; end++) { if (text[end] == '{') depth++; else if (text[end] == '}' && --depth == 0) break; }
            return Definition(JsonUtility.FromJson<Gear>(text.Substring(start, end - start + 1)));
        }

        static Dictionary<string, WaveGearDefinition> Review(string id) =>
            JsonUtility.FromJson<Cases>(File.ReadAllText(Path.Combine(Repository, "data/review/m1-weapon-combat-cases.json")))
                .cases.Where(c => c.id == id).ToDictionary(c => c.stage, c => Definition(c.gear));

        static WeaponDisplayStages Table() => WeaponDisplayStages.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Game/View/Resources", WeaponDisplayStages.ResourceName + ".json")));

        static readonly string[] ReviewWeapons = { "core:iron_blade", "core:storm_fork", "core:spore_fan", "core:ward_orbit", "core:canal_bow", "core:ditch_pike", "core:granary_sling", "core:muster_horn" };

        // The display stage table is checked against the real data files, so a new review stage, canonical gear or runtime
        // evolution that is missing from it, or listed at the wrong stage, fails here.
        [Test]
        public void DisplayStageTableMatchesTheReviewStagesCanonicalGearAndRuntimeEvolutions()
        {
            var table = Table();
            var cases = JsonUtility.FromJson<Cases>(File.ReadAllText(Path.Combine(Repository, "data/review/m1-weapon-combat-cases.json"))).cases;
            Assert.That(cases.Select(c => c.id).Distinct(), Is.EquivalentTo(ReviewWeapons));
            foreach (var c in cases)
            {
                Assert.That(table.TryGetGear(c.gear.designRef, out var stage), Is.True, c.gear.designRef);
                Assert.That(stage, Is.EqualTo(c.stage == "evolved" ? WeaponDisplayStage.Evolved : WeaponDisplayStage.Normal), c.gear.designRef);
            }
            var text = File.ReadAllText(Path.Combine(Repository, "data/runtime/wave-1a.json"));
            var gearStart = text.IndexOf('{', text.IndexOf("\"gear\"", StringComparison.Ordinal));
            var depth = 0; var gearEnd = gearStart;
            for (; gearEnd < text.Length; gearEnd++) { if (text[gearEnd] == '{') depth++; else if (text[gearEnd] == '}' && --depth == 0) break; }
            var canonicalRefs = System.Text.RegularExpressions.Regex.Matches(text.Substring(gearStart, gearEnd - gearStart), "\"designRef\"\\s*:\\s*\"([^\"]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.That(canonicalRefs.Length, Is.GreaterThanOrEqualTo(9));
            foreach (var designRef in canonicalRefs)
            {
                Assert.That(table.TryGetGear(designRef, out var stage), Is.True, designRef);
                Assert.That(stage, Is.EqualTo(WeaponDisplayStage.Normal), designRef);
            }
            foreach (var file in Directory.GetFiles(Path.Combine(Repository, "data/evolutions"), "*.json"))
            {
                var evolution = "core:" + Path.GetFileNameWithoutExtension(file);
                Assert.That(table.TryGetEvolution(evolution, out var stage), Is.True, evolution);
                Assert.That(stage, Is.EqualTo(WeaponDisplayStage.Evolved), evolution);
            }
        }

        [TestCase("core:storm_fork")]
        [TestCase("core:muster_horn")]
        [TestCase("core:iron_blade")]
        [TestCase("core:ward_orbit")]
        public void EvolutionOnlyElementsAppearOnlyAtTheReviewEvolutionAndNeverForCanonicalLevels(string id)
        {
            var table = Table();
            var canonical = Canonical(id);
            var review = Review(id);
            Assert.That(review.Keys, Is.EquivalentTo(new[] { "lv1", "max", "evolved" }));
            Assert.That(table.Evolved(review["lv1"], null), Is.False, id + " lv1");
            Assert.That(table.Evolved(review["max"], null), Is.False, id + " max");
            Assert.That(table.Evolved(review["evolved"], null), Is.True, id + " evolved");
            // Canonical levels share the canonical definition: off at every level, Lv12 included.
            Assert.That(canonical.Levels.Length, Is.GreaterThanOrEqualTo(12));
            Assert.That(table.Evolved(canonical, null), Is.False, id + " canonical");
            Assert.That(table.Evolved(canonical, "core:rally_kitchen"), Is.True, "An active runtime evolution in the table enables it.");
        }

        [Test]
        public void UnlistedGearIsDrawnNormalAndLoggedOnce()
        {
            var table = Table();
            var unknown = new WaveGearDefinition("core:unknown", "data/unknown.json#core:unknown", WaveAttackKind.Arc, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("weapon-display-stage.*core:unknown"));
            Assert.That(table.Of(unknown, null), Is.EqualTo(WeaponDisplayStage.Normal));
            Assert.That(table.Of(unknown, null), Is.EqualTo(WeaponDisplayStage.Normal));
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LightningTimingAndSizeStayInsideTheReferenceRanges()
        {
            Assert.That(LightningSpec.TelegraphFrames, Is.InRange(2, 4));
            Assert.That(LightningSpec.HitHoldFrames, Is.InRange(4, 6));
            Assert.That(LightningSpec.StrikeOpacity(0), Is.EqualTo(1));
            Assert.That(LightningSpec.StrikeOpacity(LightningSpec.Seconds(LightningSpec.StrikeFrames) - .001f), Is.EqualTo(1));
            var gone = LightningSpec.StrikeFrames + LightningSpec.FadeFrames;
            Assert.That(gone, Is.InRange(8, 10), "Fades out 8–10 frames after the strike.");
            Assert.That(LightningSpec.StrikeOpacity(LightningSpec.Seconds(gone)), Is.Zero);
            Assert.That(LightningSpec.StrikeOpacity(LightningSpec.Seconds(gone - 1)), Is.GreaterThan(0));
            foreach (var strength in new[] { 0f, .5f, 1f })
            {
                // At 390 pt portrait width a point is a pixel; the review captures are 500 px wide, so pixels only grow.
                Assert.That(LightningSpec.CorePoints(strength), Is.InRange(4f, 6f));
                Assert.That(LightningSpec.GlowPoints(strength), Is.InRange(16f, 24f));
                Assert.That(LightningSpec.Branches(strength), Is.InRange(3, 6));
            }
            Assert.That(LightningSpec.ScreenBrighten(false), Is.Zero, "Ordinary strikes flash around their targets only.");
            Assert.That(LightningSpec.ScreenBrighten(true), Is.EqualTo(.13f / 2).Within(.0001f), "Storm screen flash is half the first pilot's .13.");
            Assert.That(LightningSpec.Strength(1), Is.Zero);
            Assert.That(LightningSpec.Strength(1.56f), Is.LessThan(LightningSpec.Strength(3.12f)));
            Assert.That(LightningSpec.Strength(5.85f), Is.LessThan(LightningSpec.Strength(1e6f)).Or.EqualTo(1));
        }

        [Test]
        public void BladeAndArrowTimingStayInsideTheReferenceRanges()
        {
            Assert.That(BladeSpec.WindupFrames, Is.InRange(3, 4), "Hero pulls the sword back 3–4 frames.");
            Assert.That(BladeSpec.HitHoldFrames, Is.InRange(3, 5));
            Assert.That(GiantBladeEnvelope.FadeSeconds * 60, Is.InRange(6f, 8f), "The trail fades over 6–8 frames.");
            Assert.That(BladeSpec.SwordHeroHeights, Is.GreaterThanOrEqualTo(3f), "The evolved sword is at least three hero heights.");
            Assert.That(BladeSpec.MinBandHeroHeights, Is.GreaterThanOrEqualTo(.4f), "The Lv1 arc band is not a hairline.");
            Assert.That(BladeSpec.AfterimageOpacity, Is.LessThan(.5f), "One faint afterimage.");
            Assert.That(BladeSpec.SwordFallFrames, Is.InRange(8, 10), "The giant sword falls over 8–10 frames.");
            var fall = BladeSpec.Seconds(BladeSpec.SwordFallFrames);
            Assert.That(BladeSpec.SwordDrop(0, 1), Is.EqualTo(1)); Assert.That(BladeSpec.SwordDrop(fall, 1), Is.Zero);
            Assert.That(BladeSpec.SwordDrop(fall * 2 / 3, 1) - BladeSpec.SwordDrop(fall, 1), Is.GreaterThan(BladeSpec.SwordDrop(0, 1) - BladeSpec.SwordDrop(fall / 3, 1)),
                "The fall speeds up: the last third covers more than the first.");
            Assert.That(BladeSpec.SwordSquashFrames, Is.InRange(1, 2));
            Assert.That(BladeSpec.SwordScale(0).y, Is.LessThan(1)); Assert.That(BladeSpec.SwordScale(0).x, Is.GreaterThan(1), "Squashes as it plants.");
            Assert.That(BladeSpec.SwordScale(BladeSpec.Seconds(BladeSpec.SwordSquashFrames)), Is.EqualTo(Vector2.one));
            Assert.That(BladeSpec.SwordStaySeconds, Is.EqualTo(.5f).Within(1e-5f), "Stays planted half a second before it splits away.");
            Assert.That(BladeSpec.ArcScreenFill, Is.EqualTo(.85f).Within(1e-5f), "Arc radius: min(Core reach, short half-width x .85).");
            Assert.That(BladeSpec.BandHeroHeights(0), Is.LessThan(BladeSpec.BandHeroHeights(BladeSpec.NormalStageShare)), "Band widens Lv1 < max.");
            Assert.That(BladeSpec.BandHeroHeights(BladeSpec.NormalStageShare), Is.LessThan(BladeSpec.BandHeroHeights(1)), "Band widens max < evolved.");
            Assert.That(BladeSpec.HalfAngle(0), Is.EqualTo(GiantBladeEnvelope.CoreHalfAngle)); Assert.That(BladeSpec.HalfAngle(1), Is.GreaterThan(BladeSpec.HalfAngle(.5f)));
            var room = .8f; var drop = BladeSpec.SwordDropFor(room, 10);
            for (var k = BladeSpec.SwordFallFrames - BladeSpec.SwordShownFallFrames + 1; k <= BladeSpec.SwordFallFrames; k++)
                Assert.That(BladeSpec.SwordDrop(BladeSpec.Seconds(k), drop), Is.LessThanOrEqualTo(room + 1e-4f), $"Blade in view on fall frame {k}.");
            Assert.That(BladeSpec.SwordShownFallFrames, Is.GreaterThanOrEqualTo(5));
            Assert.That(ArrowSpec.ArrowHeroHeights, Is.GreaterThanOrEqualTo(.5f), "Arrows are at least half a hero tall.");
            Assert.That(OrbitSpec.Presence(0, 1), Is.Zero, "Orbit marker spreads in."); Assert.That(OrbitSpec.Presence(1, 1), Is.EqualTo(1));
            Assert.That(OrbitSpec.Presence(1, 0), Is.Zero, "Orbiters shrink away as the activation ends.");
            Assert.That(OrbitSpec.Shown(0), Is.InRange(.3f, .6f), "Back-to-back activations shrink part way, not to nothing (no blinking).");
            Assert.That(OrbitSpec.TornadoHeroHeights, Is.GreaterThanOrEqualTo(1f));
            Assert.That(OrbitSpec.MinShieldHeroHeights, Is.EqualTo(.35f).Within(1e-5f)); Assert.That(OrbitSpec.MaxShieldHeroHeights, Is.EqualTo(.45f).Within(1e-5f));
            Assert.That(OrbitSpec.MaxShields, Is.EqualTo(6));
            Assert.That(Enumerable.Range(0, 12).Count(i => OrbitSpec.ShieldDrawn(i, 12)), Is.EqualTo(6), "Twelve orbiters draw six shields.");
            Assert.That(Enumerable.Range(0, 12).Where(i => OrbitSpec.ShieldDrawn(i, 12)), Is.EqualTo(new[] { 1, 3, 5, 7, 9, 11 }), "Evenly spaced.");
            Assert.That(Enumerable.Range(0, 4).All(i => OrbitSpec.ShieldDrawn(i, 4)), Is.True);
            Assert.That(Enumerable.Range(0, 7).Count(i => OrbitSpec.ShieldDrawn(i, 7)), Is.EqualTo(6));
            Assert.That(ArrowSpec.MuzzleFlashFrames, Is.EqualTo(1));
            Assert.That(ArrowSpec.HitHoldFrames, Is.InRange(1, 2), "Projectile hits freeze briefly.");
            Assert.That(ArrowSpec.Flight(Vector2.zero, new Vector2(10, 0), 0), Is.EqualTo(Vector2.zero));
            Assert.That(ArrowSpec.Flight(Vector2.zero, new Vector2(10, 0), 1), Is.EqualTo(new Vector2(10, 0)));
            Assert.That(ArrowSpec.Flight(Vector2.zero, new Vector2(10, 0), .5f).y, Is.GreaterThan(0), "Arcs above the straight line.");
            Assert.That(ArrowSpec.RainArrows(.1f, 1), Is.EqualTo(ArrowSpec.MinRainArrows)); Assert.That(ArrowSpec.RainArrows(100, 1), Is.EqualTo(ArrowSpec.MaxRainArrows));
        }

        [Test]
        public void AlphaCapCoversOwnEffectsOverEnemiesButNotFullScreenTints()
        {
            Assert.That(EffectAlphaCap.OverEnemies, Is.EqualTo(.5f));
            Assert.That(EffectAlphaCap.IsEnemyLayer(GameVisualTokens.EnemyLayer), Is.True); Assert.That(EffectAlphaCap.IsEnemyLayer(GameVisualTokens.EnemyLayer + 19), Is.True);
            Assert.That(EffectAlphaCap.IsEnemyLayer(GameVisualTokens.WaveAttackEdgeLayer), Is.False);
            foreach (var layer in new[] { 70, 71, WorldRenderer.ProjectileLayer, WorldRenderer.LightningLayer, WorldRenderer.ProjectileLayer + 2 })
                Assert.That(EffectAlphaCap.IsEffectLayer(layer), Is.True, layer.ToString());
            foreach (var layer in new[] { GameVisualTokens.WaveDangerLayer, WorldRenderer.StormSkyLayer, WorldRenderer.ScreenFlashLayer, GameVisualTokens.LordLayer, GameVisualTokens.AttackLayer })
                Assert.That(EffectAlphaCap.IsEffectLayer(layer), Is.False, layer.ToString());
        }

        [Test]
        public void BoltsAreDeterministicZigzagsBetweenTheirEndpoints()
        {
            var a = Vector2.zero; var b = new Vector2(0, 10);
            var first = new Vector2[LightningBolt.PointCount(LightningBolt.Generations)];
            var second = new Vector2[first.Length];
            LightningBolt.Build(a, b, LightningBolt.Hash(42, 1), .18f, LightningBolt.Generations, first, 0);
            LightningBolt.Build(a, b, LightningBolt.Hash(42, 1), .18f, LightningBolt.Generations, second, 0);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first[0], Is.EqualTo(a)); Assert.That(first[first.Length - 1], Is.EqualTo(b));
            var deviation = first.Max(p => Mathf.Abs(p.x));
            Assert.That(deviation, Is.GreaterThan(.2f), "Not a straight line.");
            Assert.That(deviation, Is.LessThan(5f), "Still reads as one bolt.");
            LightningBolt.Branch(first, 0, first.Length, LightningBolt.Hash(42, 10), out var from, out var to);
            Assert.That(first.Contains(from), Is.True);
            Assert.That((to - from).magnitude, Is.InRange(1.7f, 3.5f));
        }

        [Test]
        public void SoldiersAreOpaqueOutlinedAndThrustTheirSpearFarForward()
        {
            Assert.That(WorldRenderer.MinionTint.a, Is.EqualTo(1), "Opaque: lower saturation is not transparency.");
            Assert.That(Mathf.Min(WorldRenderer.MinionTint.r, WorldRenderer.MinionTint.g, WorldRenderer.MinionTint.b), Is.GreaterThanOrEqualTo(.85f), "Keeps the armour's mid value.");
            Assert.That(WorldRenderer.MinionOutlineTexels, Is.InRange(1f, 2f));
            Assert.That(WorldRenderer.SpearThrustReach, Is.GreaterThanOrEqualTo(WorldRenderer.SpearRestReach * 1.8f), "The thrust visibly drives the spear forward.");
        }

        static MinionSquad.GroupInput Group(int id, MinionMode mode, Vector2 target, float nextHit = -1, int count = 3) => new MinionSquad.GroupInput
        { Id = id, Count = count, Mode = mode, Anchor = Vector2.zero, Target = target, Hero = new Vector2(-3, 0), HasTarget = mode == MinionMode.Seek, NextHitIn = nextHit, Scale = 1 };

        [Test]
        public void SoldiersSpawnSeekThrustOnTheCoreHitFrameReturnAndDisperse()
        {
            var squad = new MinionSquad(); const float w = 1;
            var now = 0f;
            void Frame60(MinionSquad.GroupInput g) { squad.BeginSync(); squad.SyncGroup(g, now); squad.EndSync(now); squad.Step(now, Frame, w); now += Frame; }
            var seek = Group(1, MinionMode.Seek, new Vector2(2, 0));
            Frame60(seek);
            Assert.That(squad.Count, Is.EqualTo(3));
            Assert.That(squad[0].State, Is.EqualTo(MinionState.Spawn));
            for (var i = 0; i < MinionSpec.SpawnFrames; i++) Frame60(seek);
            Assert.That(Enumerable.Range(0, squad.Count).All(i => squad[i].State == MinionState.Seek), Is.True);
            for (var i = 0; i < 30; i++) Frame60(seek);
            var started = squad.GroupHit(1, new Vector2(2, 0), now);
            Assert.That(started, Is.InRange(1, MinionSpec.MaxThrustersPerHit));
            var thrusting = Enumerable.Range(0, squad.Count).Count(i => squad[i].AttackPhase(now) == 1);
            Assert.That(thrusting, Is.EqualTo(started), "Thrust starts on the very frame of the Core hit.");
            for (var i = 0; i * Frame < MinionSpec.AttackSeconds + Frame; i++) Frame60(seek);
            Assert.That(Enumerable.Range(0, squad.Count).All(i => squad[i].State == MinionState.Seek), Is.True);
            var back = Group(1, MinionMode.Return, Vector2.zero);
            Frame60(back);
            Assert.That(Enumerable.Range(0, squad.Count).All(i => squad[i].State == MinionState.Return), Is.True);
            squad.BeginSync(); squad.EndSync(now);
            Assert.That(Enumerable.Range(0, squad.Count).All(i => squad[i].State == MinionState.Despawn), Is.True, "Group ended in Core: soldiers disperse.");
            for (var i = 0; i * Frame < MinionSpec.DespawnSeconds + Frame; i++) { squad.Step(now, Frame, w); now += Frame; }
            Assert.That(squad.Count, Is.Zero);
            Assert.That(squad.ThrustsOnHitFrame, Is.EqualTo(squad.GroupHits));
        }

        [Test]
        public void PredictedWindupLeadsIntoTheThrustAndSoldiersKeepMovingAndApart()
        {
            var squad = new MinionSquad(); const float w = 1; var now = 0f;
            var target = new Vector2(2, 0);
            for (var i = 0; i < 120; i++)
            {
                // A target drifting around like an enemy under attack; Core hits every 16 frames.
                target = new Vector2(2 + Mathf.Sin(i * .05f) * .8f, Mathf.Cos(i * .04f) * .8f);
                var next = 16 - i % 16;
                squad.BeginSync(); squad.SyncGroup(Group(1, MinionMode.Seek, target, next * Frame, 5), now); squad.EndSync(now);
                if (i % 16 == 0 && i > 0) squad.GroupHit(1, target, now);
                squad.Step(now, Frame, w);
                if (i == 47) Assert.That(Enumerable.Range(0, squad.Count).Any(k => squad[k].AttackPhase(now) == 0), Is.True, "Wind-up shows before the predicted hit.");
                now += Frame;
            }
            var ratio = (float)squad.StationaryFrames / squad.AgentFrames;
            Assert.That(ratio, Is.LessThan(.2f), "Soldiers are not standing stickers.");
            for (var i = 0; i < squad.Count; i++)
                for (var j = i + 1; j < squad.Count; j++)
                    Assert.That((squad[i].Position - squad[j].Position).magnitude, Is.GreaterThan(MinionSpec.Separation * w * .4f), "Separation keeps bodies from stacking.");
        }
    }
}
