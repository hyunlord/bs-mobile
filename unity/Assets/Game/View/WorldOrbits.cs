using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Orbit spec ("무기 연출 레퍼런스·이론", area/orbit row): spore fan tornadoes and guard ring shields. Frames are 60Hz.
    public static class OrbitSpec
    {
        public const int SpreadFrames = 6;           // ground marker spreads from the hero to the orbit radius
        public const int ShrinkFrames = 6;           // orbiters shrink inward as their activation ends
        public const int ContactFlashFrames = 3;     // hits flash briefly; no freeze (orbits hit many enemies)
        // Spore fan: a bright teal tornado cone of stacked spinning bands; evolution merges them into one giant vortex
        // around the hero whose arms run through the tornadoes and draw struck enemies inward.
        public const int TornadoBands = 6, TornadoSegments = 12, VortexArms = 3, VortexArmPoints = 44;
        public const float TornadoHeroHeights = 1.2f, TornadoSpin = 540, VortexSpin = 140, VortexTurns = 1.4f, VortexReach = 1.18f;
        public static readonly Color Spore = new Color(.35f, 1f, .85f);       // bright teal, distinct from the grass
        // Guard ring: gold-rimmed shields with a blue crest and a gold motion trail; evolution throws a shield out to the
        // struck enemy and back with a gold shockwave on contact.
        public const int ThrowFrames = 10, ShockwaveFrames = 10, MaxThrows = 16;
        public const float MinShieldHeroHeights = .45f, MaxShieldHeroHeights = .75f, ShieldOfCollision = .85f, ShieldWobble = 12;
        public static readonly Color Gold = new Color(.97f, .8f, .32f), Crest = new Color(.2f, .38f, .82f);
        public static float Seconds(int frames) => frames / 60f;
        // 1 while the activation runs, ramping in over the spread and out over the shrink.
        public static float Presence(float age, float remaining)
            => Mathf.Clamp01(age / Seconds(SpreadFrames)) * Mathf.Clamp01(remaining / Seconds(ShrinkFrames));
    }

    public sealed partial class WorldRenderer
    {
        struct ShieldThrow { public Vector2 From, To; public float Started; public float Size; }
        readonly List<ShieldThrow> shieldThrows = new List<ShieldThrow>(OrbitSpec.MaxThrows);
        readonly HashSet<int> orbitMarkedActivations = new HashSet<int>();
        readonly HashSet<int> orbitRecordedActivations = new HashSet<int>();
        ShapeBatch orbitShapes; ShapeMeshes orbitShapeMeshes;
        const int ShieldEmblemLayer = ProjectileLayer + 2;   // above the shield discs, below the hero; normal blend
        public int OrbitActivations { get; private set; }
        public int OrbitContactFlashes { get; private set; }
        public int VortexFrames { get; private set; }
        public int ShieldThrows { get; private set; }
        public System.Action<OrbitActivationRecord> OrbitActivationSample;

        void ResetOrbits()
        {
            shieldThrows.Clear(); orbitMarkedActivations.Clear(); orbitRecordedActivations.Clear();
            OrbitActivations = OrbitContactFlashes = VortexFrames = ShieldThrows = 0;
        }

        bool IsOrbitShape(string source, out string shape)
        {
            shape = null;
            return source != null && m1CombatShapes.TryGetValue(source, out shape) && (shape == "tornado" || shape == "shield");
        }

        // Orbit hits: a brief flash without a freeze; spore hits in the evolved vortex are pulled toward the hero.
        bool OrbitHitReaction(WaveEvent value, Vector2 origin)
        {
            if (!IsOrbitShape(value.Source, out var shape)) return false;
            var hero = PredictedLordPosition ?? RenderedLordPosition;
            var outward = origin - hero; if (outward.sqrMagnitude < .000001f) outward = Vector2.up;
            var evolved = WeaponEvolved(value.Source);
            var direction = shape == "tornado" && evolved ? -outward : outward;
            enemyImpactPoses.Hit(value.SubjectId, visualTime, direction.normalized, 0, 0, true);
            OrbitContactFlashes++;
            if (shape == "shield" && evolved && shieldThrows.Count < OrbitSpec.MaxThrows)
            {
                // The shield nearest the struck enemy is thrown out to it and comes back.
                var orbit = (origin - hero).normalized * NearestOrbitRadius(value.Source, hero, origin);
                shieldThrows.Add(new ShieldThrow { From = hero + orbit, To = origin, Started = visualTime, Size = ShieldRadius(value.Source) });
                ShieldThrows++;
            }
            return true;
        }

        float NearestOrbitRadius(string source, Vector2 hero, Vector2 point)
        {
            var range = waveDefinition.Gear[source].Range / (float)settings.WorldUnitsPerUnityUnit;
            return Mathf.Min(range, (point - hero).magnitude);
        }

        float ShieldRadius(string source)
            // Core collision radius is the gear speed; at review max it nearly fills the orbit, so the drawn shield is capped and
            // contact flashes mark the hits beyond its rim.
            => Mathf.Clamp(waveDefinition.Gear[source].Speed / (float)settings.WorldUnitsPerUnityUnit * OrbitSpec.ShieldOfCollision, HeroHeight * OrbitSpec.MinShieldHeroHeights, HeroHeight * OrbitSpec.MaxShieldHeroHeights);

        // Called for each Core orbit attack (one per orbiter) from DrawM1CombatAttack.
        void DrawOrbitAttack(WaveAttackView attack, Vector2 center, int tick, float alpha, Vector2 lordPoint, string shape)
        {
            EnsureLightningArt();
            var gear = waveDefinition.Gear[attack.Source];
            var rate = Mathf.Max(1, waveTickRate);
            var remaining = Mathf.Max(0, attack.ExpireTick - tick - alpha) / rate;
            var age = Mathf.Max(0, gear.CooldownTicks / (float)rate - remaining);
            var presence = OrbitSpec.Presence(age, remaining);
            var evolved = WeaponEvolved(attack.Source);
            var orbitRadius = (center - lordPoint).magnitude;
            var color = shape == "tornado" ? OrbitSpec.Spore : OrbitSpec.Gold;
            if (orbitMarkedActivations.Add(attack.ActivationId))
            {
                // Once per activation and frame: the ground marker spreading out to the orbit, and the evolved vortex.
                if (age < OrbitSpec.Seconds(OrbitSpec.SpreadFrames + 4))
                {
                    var spread = Mathf.Clamp01(age / OrbitSpec.Seconds(OrbitSpec.SpreadFrames));
                    var marker = color; marker.a = .8f * (1 - Mathf.Clamp01((age - OrbitSpec.Seconds(OrbitSpec.SpreadFrames)) / OrbitSpec.Seconds(4)));
                    DrawMusterRing(lordPoint, Mathf.Max(.01f, orbitRadius * spread), marker, 1);
                }
                if (shape == "tornado" && evolved) { DrawVortex(lordPoint, orbitRadius, presence, attack.ActivationId); VortexFrames++; }
                if (age >= OrbitSpec.Seconds(OrbitSpec.SpreadFrames) && orbitRecordedActivations.Add(attack.ActivationId)) RecordOrbitActivation(attack, shape, evolved, center, orbitRadius, gear);
            }
            if (presence <= 0) return;
            if (shape == "tornado") DrawTornado(center, presence, attack.ActivationId, gear);
            else DrawShield(center, lordPoint, ShieldRadius(attack.Source) * Mathf.Lerp(.2f, 1, presence), presence, age);
        }

        float TornadoHeight(WaveGearDefinition gear)
        {
            WaveGearDefinition reference = null; GearReference?.Gear.TryGetValue(gear.Id, out reference);
            var strength = LightningSpec.Strength(MusterFormationRule.Ratio(gear, EquipmentLevel(gear.Id), reference));
            return HeroHeight * OrbitSpec.TornadoHeroHeights * (.85f + .35f * strength);
        }

        // Stacked spinning bands narrowing to the ground: translucent teal bloom with bright streaks, debris specks around.
        void DrawTornado(Vector2 foot, float presence, int seed, WaveGearDefinition gear)
        {
            var height = TornadoHeight(gear) * presence;
            if (height <= 0) return;
            var spin = visualTime * OrbitSpec.TornadoSpin;
            for (var band = 0; band < OrbitSpec.TornadoBands; band++)
            {
                var t = band / (float)(OrbitSpec.TornadoBands - 1);
                var y = foot.y + height * (.08f + t * .92f);
                var half = height * Mathf.Lerp(.08f, .42f, t * t);
                var depth = half * .32f;
                for (var s = 0; s < OrbitSpec.TornadoSegments; s++)
                {
                    var a0 = (s / (float)OrbitSpec.TornadoSegments * 360 + spin + band * 37) * Mathf.Deg2Rad;
                    var a1 = ((s + 1) / (float)OrbitSpec.TornadoSegments * 360 + spin + band * 37) * Mathf.Deg2Rad;
                    var p0 = new Vector2(foot.x + Mathf.Cos(a0) * half, y + Mathf.Sin(a0) * depth);
                    var p1 = new Vector2(foot.x + Mathf.Cos(a1) * half, y + Mathf.Sin(a1) * depth);
                    var span = p1 - p0; var front = Mathf.Sin((a0 + a1) * .5f) < 0;   // the near half of each band reads brighter
                    var glow = OrbitSpec.Spore; glow.a = (front ? .75f : .35f) * presence;
                    Draw(lightningGlow, LightningLayer, (p0 + p1) * .5f, 0, new Vector2(span.magnitude * 1.3f, height * .1f), Angle(span), 1, glow, applyTween: false);
                    if (front && (s + band) % 2 == 0)
                    {
                        var streak = Color.white; streak.a = .85f * presence;
                        Draw(lightningCore, LightningLayer, (p0 + p1) * .5f, 0, new Vector2(span.magnitude * 1.2f, Mathf.Max(height * .04f, .012f)), Angle(span), 1, streak, applyTween: false);
                    }
                }
            }
            for (var k = 0; k < 4; k++)
            {
                var a = (spin * 1.6f + k * 90 + seed * 13) * Mathf.Deg2Rad;
                var speck = OrbitSpec.Spore; speck.a = .9f * presence;
                Draw(lightningFlash, LightningLayer, foot + new Vector2(Mathf.Cos(a) * height * .5f, height * (.3f + .15f * k) + Mathf.Sin(a) * height * .12f), 0, Vector2.one * height * .09f, 0, 1, speck, applyTween: false);
            }
        }

        // Evolution element: the tornadoes merge into one vortex around the hero, its spiral arms running out through the
        // orbit to just past it.
        void DrawVortex(Vector2 centre, float orbitRadius, float presence, int seed)
        {
            if (presence <= 0 || orbitRadius <= 0) return;
            var reach = orbitRadius * OrbitSpec.VortexReach;
            var spin = -visualTime * OrbitSpec.VortexSpin;
            for (var arm = 0; arm < OrbitSpec.VortexArms; arm++)
            {
                var previous = Vector2.zero;
                for (var k = 0; k <= OrbitSpec.VortexArmPoints; k++)
                {
                    var t = k / (float)OrbitSpec.VortexArmPoints;
                    var angle = spin + arm * 360f / OrbitSpec.VortexArms + t * OrbitSpec.VortexTurns * 360;
                    var point = centre + M1Direction(angle) * (reach * Mathf.Lerp(.15f, 1, t)) * presence;
                    if (k > 0)
                    {
                        var span = point - previous;
                        var glow = OrbitSpec.Spore; glow.a = .6f * presence * Mathf.Lerp(.4f, 1, t);
                        Draw(lightningGlow, LightningLayer, (point + previous) * .5f, 0, new Vector2(span.magnitude * 1.25f, reach * Mathf.Lerp(.05f, .14f, t)), Angle(span), 1, glow, applyTween: false);
                        if (k % 2 == 0)
                        {
                            var core = Color.white; core.a = .7f * presence * t;
                            Draw(lightningCore, LightningLayer, (point + previous) * .5f, 0, new Vector2(span.magnitude * 1.2f, reach * .018f), Angle(span), 1, core, applyTween: false);
                        }
                    }
                    previous = point;
                }
            }
            var eye = OrbitSpec.Spore; eye.a = .35f * presence;
            Draw(lightningFlash, LightningLayer, centre, 0, Vector2.one * reach * .7f, 0, 1, eye, applyTween: false);
        }

        void EnsureOrbitShapes()
        {
            if (orbitShapes != null) return;
            orbitShapeMeshes = new ShapeMeshes();
            orbitShapes = new ShapeBatch(orbitShapeMeshes[WorldShape.Circle], Resources.Load<Shader>("WorldShape"), renderCamera, ProjectileLayer);
            orbitShapes.BeginFrame();
        }

        // Gold rim, blue crest disc and a gold cross that spins, outlined in ink, with a gold trail along the orbit.
        void DrawShield(Vector2 at, Vector2 hero, float radius, float presence, float age)
        {
            if (radius <= 0) return;
            EnsureOrbitShapes();
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            // Trail: three fading glow arcs behind the shield along its orbit (the orbit turns counter-clockwise).
            var offset = at - hero; var orbit = offset.magnitude;
            if (orbit > .0001f)
            {
                var angle = Angle(offset);
                for (var k = 1; k <= 3; k++)
                {
                    var back = angle - k * radius / orbit * Mathf.Rad2Deg * .9f;
                    var trail = OrbitSpec.Gold; trail.a = .5f * presence * (1 - k / 4f);
                    Draw(lightningGlow, LightningLayer, hero + M1Direction(back) * orbit, 0, new Vector2(radius * 1.1f, radius * (1.2f - k * .2f)), back + 90, 1, trail, applyTween: false);
                }
            }
            var ink = GameVisualTokens.Ink; ink.a = presence;
            var gold = OrbitSpec.Gold; gold.a = presence;
            var crest = OrbitSpec.Crest; crest.a = presence;
            orbitShapes.Add(at, Vector2.one * (radius + 2.5f * pixel), 0, ink, 0);
            orbitShapes.Add(at, Vector2.one * radius, 0, gold, 0);
            orbitShapes.Add(at, Vector2.one * (radius * .76f), 0, crest, 0);
            // Upright gold cross crest (a slight wobble, not a spin, so it never reads as an X) with an ink edge.
            var wobble = Mathf.Sin(age * 9) * OrbitSpec.ShieldWobble;
            var edge = 1.8f * pixel;
            Draw(lightningSolid, ShieldEmblemLayer, at, 0, new Vector2(radius * 1.08f + edge * 2, radius * .24f + edge * 2), wobble + 90, 1, ink, applyTween: false);
            Draw(lightningSolid, ShieldEmblemLayer, at + M1Direction(wobble + 90) * (radius * .12f), 0, new Vector2(radius * .78f + edge * 2, radius * .24f + edge * 2), wobble, 1, ink, applyTween: false);
            Draw(lightningSolid, ShieldEmblemLayer, at, 0, new Vector2(radius * 1.08f, radius * .24f), wobble + 90, 1, gold, applyTween: false);
            Draw(lightningSolid, ShieldEmblemLayer, at + M1Direction(wobble + 90) * (radius * .12f), 0, new Vector2(radius * .78f, radius * .24f), wobble, 1, gold, applyTween: false);
            // Near-white highlight along the upper-left rim: the bright core of the shape.
            for (var k = 0; k < 5; k++)
            {
                var a = 105 + k * 16; var highlight = Color.white; highlight.a = .95f * presence;
                Draw(lightningSolid, ShieldEmblemLayer, at + M1Direction(a) * (radius * .88f), 0, new Vector2(radius * .3f, Mathf.Max(3 * pixel, radius * .09f)), a + 90, 1, highlight, applyTween: false);
            }
        }

        // Evolution element for the guard ring: thrown shields fly out to the struck enemy and back; a gold shockwave rings
        // out where they hit.
        void DrawShieldThrows()
        {
            if (shieldThrows.Count == 0) return;
            EnsureLightningArt();
            var throwSeconds = OrbitSpec.Seconds(OrbitSpec.ThrowFrames);
            for (var i = shieldThrows.Count - 1; i >= 0; i--)
            {
                var item = shieldThrows[i];
                var age = visualTime - item.Started;
                if (age > Mathf.Max(throwSeconds, OrbitSpec.Seconds(OrbitSpec.ShockwaveFrames) + throwSeconds * .5f)) { shieldThrows.RemoveAt(i); continue; }
                if (age < throwSeconds)
                {
                    var t = age / throwSeconds; var reach = t < .5f ? t * 2 : (1 - t) * 2;
                    DrawShield(Vector2.Lerp(item.From, item.To, reach * reach * (3 - 2 * reach)), item.From, item.Size, 1, age * 3);
                }
                var hit = age - throwSeconds * .5f;
                if (hit < 0) continue;
                var s = hit / OrbitSpec.Seconds(OrbitSpec.ShockwaveFrames);
                if (s > 1) continue;
                var ring = OrbitSpec.Gold; ring.a = 1 - s;
                DrawMusterRing(item.To, item.Size * (1 + 2.2f * Mathf.Sqrt(s)), ring, 1);
                var flash = OrbitSpec.Gold; flash.a = (1 - s) * .9f;
                Draw(lightningFlash, LightningLayer, item.To, 0, Vector2.one * item.Size * 2.4f * (1 - s * .4f), 0, 1, flash, applyTween: false);
            }
        }

        void FlushOrbitShapes()
        {
            if (orbitShapes == null) return;
            orbitShapes.Flush(); SubmittedInstances += orbitShapes.SubmittedInstances; DrawCalls += orbitShapes.DrawCalls;
        }

        void RecordOrbitActivation(WaveAttackView attack, string shape, bool evolved, Vector2 center, float orbitRadius, WaveGearDefinition gear)
        {
            OrbitActivations++;
            if (OrbitActivationSample == null) return;
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var size = shape == "tornado" ? TornadoHeight(gear) : ShieldRadius(attack.Source) * 2;
            OrbitActivationSample(new OrbitActivationRecord { Activation = attack.ActivationId, Evolved = evolved, Count = gear.Count, Shape = shape,
                OrbitRadiusPixels = orbitRadius / pixel, OrbiterSizePixels = size / pixel, HeroHeightPixels = HeroHeight / pixel,
                FirstOrbiterScreen = renderCamera.WorldToScreenPoint(center), FrameOffsetFromStart = Mathf.RoundToInt(OrbitSpec.SpreadFrames) });
        }
    }

    public struct OrbitActivationRecord
    {
        public int Activation, Count, FrameOffsetFromStart;
        public bool Evolved;
        public string Shape;
        public float OrbitRadiusPixels, OrbiterSizePixels, HeroHeightPixels;
        public Vector2 FirstOrbiterScreen;
    }
}
