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
        [Serializable] sealed class Gear { public string id, kind; public int damage, range, cooldownTicks, speed, count, lifetimeTicks, knockback, workTicks, workRadius, rewardExperience, capacity; public Level[] levels; }
        [Serializable] sealed class Case { public string id, stage; public Gear gear; }
        [Serializable] sealed class Cases { public Case[] cases; }

        static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        static WaveGearDefinition Definition(Gear g) => new WaveGearDefinition(g.id, "test", WaveAttackKind.Chain, g.damage, g.range, g.cooldownTicks, g.speed, g.count,
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

        [TestCase("core:storm_fork")]
        [TestCase("core:muster_horn")]
        public void EvolutionOnlyElementsAppearOnlyAtTheReviewEvolutionAndNeverForCanonicalLevels(string id)
        {
            var canonical = Canonical(id);
            var review = Review(id);
            Assert.That(review.Keys, Is.EquivalentTo(new[] { "lv1", "max", "evolved" }));
            foreach (var stage in new[] { "lv1", "max" })
            {
                var ratio = WeaponEvolutionGrade.RangeRatio(review[stage], 1, canonical);
                Assert.That(WeaponEvolutionGrade.Active(ratio, null), Is.False, $"{id} {stage}: range ratio {ratio:0.00} must stay below {WeaponEvolutionGrade.EvolutionRangeRatio}");
            }
            var evolved = WeaponEvolutionGrade.RangeRatio(review["evolved"], 1, canonical);
            Assert.That(WeaponEvolutionGrade.Active(evolved, null), Is.True, $"{id} evolved: range ratio {evolved:0.00}");
            for (var level = 1; level <= canonical.Levels.Length; level++)
            {
                var ratio = WeaponEvolutionGrade.RangeRatio(canonical, level, canonical);
                Assert.That(ratio, Is.EqualTo(1), $"canonical level {level}");
                Assert.That(WeaponEvolutionGrade.Active(ratio, null), Is.False);
            }
            Assert.That(WeaponEvolutionGrade.Active(1, "core:some_runtime_evolution"), Is.True, "An active runtime evolution always enables it.");
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
