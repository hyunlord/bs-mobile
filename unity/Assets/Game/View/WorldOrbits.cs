using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Orbit spec ("무기 연출 레퍼런스·이론", area/orbit row): spore fan clouds and guard ring shields. Frames are 60Hz.
    public static class OrbitSpec
    {
        public const int SpreadFrames = 6;           // ground marker spreads from the hero to the orbit radius
        public const int ShrinkFrames = 6;           // orbiters shrink inward as their activation ends
        public const int ContactFlashFrames = 3;     // hits flash briefly; no freeze (orbits hit many enemies)
        // Spore fan (option A, docs/design/m1-weapon-effect-sprites.md): each orbiter puffs a small fan of spore clouds
        // outward (away from the hero) within FanDegrees, PuffsLv1..PuffsMax puffs on a staggered PuffFrames cycle, each
        // pushed out by the Core orbiter radius while it grows PuffFrom -> PuffTo and fades, with grains blown out faster.
        // Puffs are PuffHeroHeights tall by gear strength. Evolution: three continuous spore streams spiralling
        // StreamTurns from near the hero out past the orbit, grains flowing inward along them, and a pair of puffs at each
        // orbiter; no ground ring and no stacked turns (the old 1.4-turn segmented spiral read as dotted concentric rings).
        public const int PuffsLv1 = 4, PuffsMax = 5, PuffFrames = 30, PuffInFrames = 6, PuffOutFrames = 15, GrainsLv1 = 6, GrainsMax = 8, GrainFrames = 24;
        public const float FanDegrees = 60, PuffFrom = .5f, PuffTo = 1.1f, PuffSwayDegrees = 20, MinPuffHeroHeights = .35f, MaxPuffHeroHeights = .45f;
        public const float GrainHeroHeights = .06f;
        public const int VortexArms = 3, StreamSegments = 18, StreamGrains = 8, EvolvedPuffs = 2;
        public const float StreamTurns = .6f, VortexSpin = 140, VortexReach = 1.18f, StreamInner = .15f, StreamInnerHeroHeights = .12f, StreamOuterHeroHeights = .3f;
        // Per-frame caps (docs/design/m1-weapon-effect-sprites.md).
        public const int MaxPuffs = 24, MaxGrains = 60;
        public static readonly Color Spore = new Color(.35f, 1f, .85f);       // bright teal, distinct from the grass
        public static readonly Color SporeLeaf = new Color(.85f, .93f, .48f), SporeTeal = new Color(.56f, .94f, .86f);   // yellow-green, teal
        // A puff's shown size and alpha over its life (0..1).
        public static float PuffScale(float life) => Mathf.Lerp(PuffFrom, PuffTo, Mathf.Sqrt(Mathf.Clamp01(life)));
        public static float PuffAlpha(float life)
            => Mathf.Clamp01(life * PuffFrames / PuffInFrames) * Mathf.Clamp01((1 - life) * PuffFrames / PuffOutFrames);
        public static int Puffs(float tier) => Mathf.RoundToInt(Mathf.Lerp(PuffsLv1, PuffsMax, Mathf.Clamp01(tier)));
        public static float PuffHeroHeights(float tier) => Mathf.Lerp(MinPuffHeroHeights, MaxPuffHeroHeights, Mathf.Clamp01(tier));
        // Spiral point of a stream (0 = inner end, 1 = outer end) as a reach share and an angle offset in degrees.
        public static float StreamReach(float t) => Mathf.Lerp(StreamInner, 1, t);
        public static float StreamAngle(float t) => t * StreamTurns * 360;
        // Guard ring: gold-rimmed shields with a blue crest and a gold motion trail; evolution throws a shield out to the
        // struck enemy and back with a gold shockwave on contact.
        public const int ThrowFrames = 10, ShockwaveFrames = 10, MaxThrows = 16;
        // Shields are drawn 0.35–0.45 hero heights tall by gear strength, whatever the Core collision radius; at most
        // MaxShields per activation are drawn (evenly picked), hits by the others still flash.
        public const float MinShieldHeroHeights = .35f, MaxShieldHeroHeights = .45f, ShieldWobble = 12;
        public const int MaxShields = 6;
        // Which of `count` orbiters (0-based index) are drawn: all up to MaxShields, else MaxShields evenly spaced.
        public static bool ShieldDrawn(int index, int count)
            => count <= MaxShields || (index + 1) * MaxShields / count > index * MaxShields / count;
        public static readonly Color Gold = new Color(.97f, .8f, .32f), Crest = new Color(.2f, .38f, .82f);
        public static float Seconds(int frames) => frames / 60f;
        // 1 while the activation runs, ramping in over the spread and out over the shrink.
        public static float Presence(float age, float remaining)
            => Mathf.Clamp01(age / Seconds(SpreadFrames)) * Mathf.Clamp01(remaining / Seconds(ShrinkFrames));
        // Core starts the next activation as one ends (every cooldown), so orbiters shrink and regrow only part way instead
        // of vanishing, which would blink once a second.
        public const float PresenceFloor = .45f;
        public static float Shown(float presence) => Mathf.Lerp(PresenceFloor, 1, presence);
    }

    public sealed partial class WorldRenderer
    {
        struct ShieldThrow { public Vector2 From, To; public float Started; public float Size; }
        readonly List<ShieldThrow> shieldThrows = new List<ShieldThrow>(OrbitSpec.MaxThrows);
        readonly HashSet<int> orbitMarkedActivations = new HashSet<int>();
        readonly HashSet<int> orbitRecordedActivations = new HashSet<int>();
        readonly Dictionary<int, int> orbitSeen = new Dictionary<int, int>();   // orbiters met per activation this frame
        ShapeBatch orbitShapes; ShapeMeshes orbitShapeMeshes;
        const int ShieldEmblemLayer = ProjectileLayer + 2;   // above the shield discs, below the hero; normal blend
        public int OrbitActivations { get; private set; }
        public int OrbitContactFlashes { get; private set; }
        public int VortexFrames { get; private set; }
        public int SporePuffsDrawn { get; private set; }   // puff draws summed over frames (at most MaxPuffs per frame)
        int puffBudget = OrbitSpec.MaxPuffs, grainBudget = OrbitSpec.MaxGrains;
        ArtVisual[] sporePuffs, sporeGrains; ArtVisual sporeStream;
        public int ShieldThrows { get; private set; }
        public int ShieldsDrawn { get; private set; }   // shield draws summed over frames (at most MaxShields per activation per frame)
        public System.Action<OrbitActivationRecord> OrbitActivationSample;

        void ResetOrbits()
        {
            shieldThrows.Clear(); orbitMarkedActivations.Clear(); orbitRecordedActivations.Clear(); orbitSeen.Clear();
            OrbitActivations = OrbitContactFlashes = VortexFrames = ShieldThrows = ShieldsDrawn = SporePuffsDrawn = 0;
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

        // Drawn shield radius (half its height). Not the Core collision radius (gear speed), which at review max nearly fills
        // the orbit: contact flashes mark hits beyond the drawn rim. Grows with the gear over its canonical Lv1 (the
        // canonical-top ratio read every gear up to the top level as 0, so review max stayed at the Lv1 size); evolved is
        // the largest.
        float ShieldRadius(string source)
            => HeroHeight * Mathf.Lerp(OrbitSpec.MinShieldHeroHeights, OrbitSpec.MaxShieldHeroHeights, WeaponEvolved(source) ? 1 : GearTier(source) * .8f) * .5f;

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
            if (shape == "tornado") EnsureSporeArt();
            if (orbitMarkedActivations.Add(attack.ActivationId))
            {
                // Once per activation and frame: the ground marker spreading out to the orbit (not under the evolved spore
                // vortex), and the evolved vortex.
                if (age < OrbitSpec.Seconds(OrbitSpec.SpreadFrames + 4) && !(shape == "tornado" && evolved))
                {
                    var spread = Mathf.Clamp01(age / OrbitSpec.Seconds(OrbitSpec.SpreadFrames));
                    var marker = color; marker.a = .8f * (1 - Mathf.Clamp01((age - OrbitSpec.Seconds(OrbitSpec.SpreadFrames)) / OrbitSpec.Seconds(4)));
                    DrawMusterRing(lordPoint, Mathf.Max(.01f, orbitRadius * spread), marker, 1);
                }
                if (shape == "tornado" && evolved) { DrawSporeVortex(lordPoint, orbitRadius, OrbitSpec.Shown(presence)); VortexFrames++; }
                if (age >= OrbitSpec.Seconds(OrbitSpec.SpreadFrames) && orbitRecordedActivations.Add(attack.ActivationId)) RecordOrbitActivation(attack, shape, evolved, center, orbitRadius, gear);
            }
            var shown = OrbitSpec.Shown(presence);
            if (shape == "tornado")
            {
                orbitSeen.TryGetValue(attack.ActivationId, out var orbiter); orbitSeen[attack.ActivationId] = orbiter + 1;
                DrawSporeFan(center, lordPoint, attack.Radius / (float)settings.WorldUnitsPerUnityUnit, shown, attack.ActivationId * 31 + orbiter, attack.Source, evolved);
                return;
            }
            orbitSeen.TryGetValue(attack.ActivationId, out var index); orbitSeen[attack.ActivationId] = index + 1;
            if (OrbitSpec.ShieldDrawn(index, gear.Count)) { DrawShield(center, lordPoint, ShieldRadius(attack.Source) * shown, shown, age); ShieldsDrawn++; }
        }

        float PuffSize(string source) => HeroHeight * OrbitSpec.PuffHeroHeights(WeaponEvolved(source) ? 1 : GearTier(source));

        static float SporeHash(int a, int b) { unchecked { var h = (uint)(a * 73856093 ^ b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; } }
        static Vector2 Rotate(Vector2 v, float degrees) { var r = degrees * Mathf.Deg2Rad; var c = Mathf.Cos(r); var s = Mathf.Sin(r); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }

        // Spore fan cloud at one orbiter: puffs fanning out away from the hero (the evolved pair swirls with the vortex) and
        // grains blown out faster. Puffs are wide (capped over enemies), grains thin.
        void DrawSporeFan(Vector2 orbiter, Vector2 hero, float radius, float shown, int seed, string source, bool evolved)
        {
            if (shown <= 0) return;
            var outward = orbiter - hero; outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : Vector2.up;
            if (evolved) outward = Rotate(outward, 90);   // along the vortex's turn (it spins clockwise, struck enemies are pulled in)
            var tier = evolved ? 1 : GearTier(source);
            var size = PuffSize(source) * shown;
            var puffs = evolved ? OrbitSpec.EvolvedPuffs : OrbitSpec.Puffs(tier);
            var cycle = OrbitSpec.Seconds(OrbitSpec.PuffFrames);
            for (var k = 0; k < puffs && puffBudget > 0; k++)
            {
                var clock = visualTime / cycle + k / (float)puffs + SporeHash(seed, 7) ;
                var born = Mathf.FloorToInt(clock); var life = clock - born;
                var spread = (SporeHash(seed * 17 + k, born) - .5f) * OrbitSpec.FanDegrees;
                var push = radius * (evolved ? .5f : 1) * Mathf.Lerp(-.1f, 1, 1 - (1 - life) * (1 - life));
                var at = orbiter + Rotate(outward, spread) * push;
                var colour = Color.white; colour.a = .85f * OrbitSpec.PuffAlpha(life) * shown;
                var picture = sporePuffs[(k + born) % sporePuffs.Length];
                var scale = size * OrbitSpec.PuffScale(life);
                var sway = (SporeHash(seed + 3, born * 5 + k) - .5f) * 2 * OrbitSpec.PuffSwayDegrees + life * OrbitSpec.PuffSwayDegrees;
                Draw(picture, ProjectileLayer, at, 0, new Vector2(scale, scale * picture.WorldSize.y / picture.WorldSize.x), sway, 1, colour, applyTween: false);
                puffBudget--; SporePuffsDrawn++;
            }
            if (evolved) return;
            var grains = Mathf.RoundToInt(Mathf.Lerp(OrbitSpec.GrainsLv1, OrbitSpec.GrainsMax, tier));
            var grainCycle = OrbitSpec.Seconds(OrbitSpec.GrainFrames);
            for (var g = 0; g < grains && grainBudget > 0; g++)
            {
                var clock = visualTime / grainCycle + g / (float)grains + SporeHash(seed, 11);
                var born = Mathf.FloorToInt(clock); var life = clock - born;
                var spread = (SporeHash(seed * 13 + g, born) - .5f) * OrbitSpec.FanDegrees * 1.2f;
                var at = orbiter + Rotate(outward, spread) * (radius * Mathf.Lerp(.1f, 1.4f, life));
                DrawSporeGrain(at, g + born, shown * Mathf.Sin(life * Mathf.PI));
            }
        }

        void DrawSporeGrain(Vector2 at, int variant, float alpha)
        {
            if (alpha <= 0 || grainBudget <= 0) return;
            var colour = Color.white; colour.a = alpha;
            Draw(sporeGrains[((variant % sporeGrains.Length) + sporeGrains.Length) % sporeGrains.Length], ThinEffectLayer, at, 0, Vector2.one * HeroHeight * OrbitSpec.GrainHeroHeights, 0, 1, colour, applyTween: false);
            grainBudget--;
        }

        // Evolution element: three continuous spore streams spiralling StreamTurns around the hero from near it out past the
        // orbit, each the stream picture stretched once along it (drawn as StreamSegments slices of the picture), tapered at
        // both ends, the whole vortex turning slowly; grains flow inward along the streams.
        void DrawSporeVortex(Vector2 centre, float orbitRadius, float shown)
        {
            if (shown <= 0 || orbitRadius <= 0) return;
            var reach = orbitRadius * OrbitSpec.VortexReach * shown;
            var spin = -visualTime * OrbitSpec.VortexSpin;
            var uv = sporeStream.UvRects[0];
            for (var arm = 0; arm < OrbitSpec.VortexArms; arm++)
            {
                var start = spin + arm * 360f / OrbitSpec.VortexArms;
                Vector2 Along(float t) => centre + M1Direction(start + OrbitSpec.StreamAngle(t)) * (reach * OrbitSpec.StreamReach(t));
                var previous = Along(0);
                for (var k = 0; k < OrbitSpec.StreamSegments; k++)
                {
                    var t0 = k / (float)OrbitSpec.StreamSegments; var t1 = (k + 1) / (float)OrbitSpec.StreamSegments;
                    var next = Along(t1); var span = next - previous; var mid = (t0 + t1) * .5f;
                    var taper = Mathf.SmoothStep(0, 1, mid / .15f) * Mathf.SmoothStep(0, 1, (1 - mid) / .2f);
                    var colour = Color.white; colour.a = .85f * shown * taper;
                    var width = HeroHeight * Mathf.Lerp(OrbitSpec.StreamInnerHeroHeights, OrbitSpec.StreamOuterHeroHeights, mid) * Mathf.Lerp(.6f, 1, taper);
                    var slice = new Rect(uv.x + uv.width * t0, uv.y, uv.width / OrbitSpec.StreamSegments, uv.height);
                    Draw(sporeStream, ProjectileLayer, previous, 0, new Vector2(span.magnitude * 1.06f, width), Angle(span), 1, colour, uv: slice, applyTween: false);
                    previous = next;
                }
                for (var g = 0; g < OrbitSpec.StreamGrains; g++)
                {
                    var t = Mathf.Repeat(-visualTime * .9f + g / (float)OrbitSpec.StreamGrains + arm * .33f, 1);
                    DrawSporeGrain(Along(t) + M1Direction(start + OrbitSpec.StreamAngle(t) + 90) * (HeroHeight * .06f * Mathf.Sin(g * 2.3f)), arm * 5 + g, shown * Mathf.SmoothStep(0, 1, t / .1f) * Mathf.SmoothStep(0, 1, (1 - t) / .1f));
                }
            }
        }

        const string SporePuffRole = "fx.spore_fan.puff", SporeGrainRole = "fx.spore_fan.grain", SporeStreamRole = "fx.spore_fan.stream";

        void EnsureSporeArt()
        {
            if (sporePuffs != null) return;
            sporePuffs = new ArtVisual[3]; sporeGrains = new ArtVisual[4];
            for (var i = 0; i < sporePuffs.Length; i++) { var v = i; sporePuffs[i] = EffectArt($"{SporePuffRole}.{i + 1}", () => TemporarySporePuff(v)); }
            for (var i = 0; i < sporeGrains.Length; i++) { var v = i; sporeGrains[i] = EffectArt($"{SporeGrainRole}.{i + 1}", () => TemporarySporeGrain(v)); }
            sporeStream = EffectArt(SporeStreamRole, TemporarySporeStream);
        }

        // Temporary pictures (docs/design/m1-weapon-effect-sprites.md, spore fan): yellow-green and teal only, no outline and
        // no pixel darker than the grass (Rec.709 luma >= 140 wherever alpha >= .1).
        static Color SporeShade(float leaf, float light)
        {
            var c = Color.Lerp(OrbitSpec.SporeTeal, OrbitSpec.SporeLeaf, Mathf.Clamp01(leaf));
            return Color.Lerp(c, Color.white, Mathf.Clamp01(light));
        }

        ArtVisual TemporarySporePuff(int variant)
        {
            const int width = 192, height = 160;
            var lobes = variant == 0 ? new[] { new Vector3(96, 80, 58), new Vector3(70, 92, 34), new Vector3(122, 70, 36) }
                : variant == 1 ? new[] { new Vector3(70, 82, 40), new Vector3(112, 78, 44), new Vector3(146, 84, 30), new Vector3(92, 100, 30) }
                : new[] { new Vector3(72, 70, 34), new Vector3(118, 92, 38), new Vector3(96, 58, 26) };
            var texture = PaintTexture($"Temporary spore puff {variant + 1}", width, height, (x, y) =>
            {
                var density = 0f;
                foreach (var lobe in lobes)
                {
                    var d = new Vector2(x - lobe.x, y - lobe.y).magnitude / lobe.z;
                    density = Mathf.Max(density, 1 - d * d);
                }
                var lump = .08f * Mathf.Sin(x * .21f + variant) * Mathf.Sin(y * .17f - variant * 2);
                var alpha = Mathf.Clamp01((density + lump) * 1.4f) * .8f;
                if (alpha < .02f) return new Color(0, 0, 0, 0);
                var c = SporeShade((x - (height - y)) / (float)width + .5f, density * .35f);
                var speck = Mathf.Sin(x * 1.31f + y * .73f + variant * 5) * Mathf.Sin(x * .57f - y * 1.13f);
                if (speck > .93f && density > .3f) c = Color.Lerp(c, Color.white, .7f);
                c.a = alpha; return c;
            });
            return new ArtVisual($"{SporePuffRole}.{variant + 1}", texture, new Vector2(width, height) / 384f, new Vector2(.5f, .5f));
        }

        ArtVisual TemporarySporeGrain(int variant)
        {
            const int size = 32; const float c0 = 15.5f;
            var texture = PaintTexture($"Temporary spore grain {variant + 1}", size, size, (x, y) =>
            {
                var p = new Vector2(x - c0, y - c0);
                float d;
                switch (variant)
                {
                    case 0: d = p.magnitude / 6; break;
                    case 1: d = new Vector2(p.x / 8, p.y / 5).magnitude; break;
                    case 2: d = Mathf.Min((p - new Vector2(-3.5f, 1)).magnitude / 4.5f, (p - new Vector2(4, -2)).magnitude / 3.5f); break;
                    default: d = Mathf.Min(new Vector2(p.x / 8, p.y / 1.6f).magnitude, new Vector2(p.x / 1.6f, p.y / 8).magnitude); break;
                }
                if (d >= 1) return new Color(0, 0, 0, 0);
                var c = SporeShade(variant % 2 == 0 ? 1 : 0, (1 - d) * .8f);
                c.a = Mathf.Clamp01((1 - d) * 3); return c;
            });
            return new ArtVisual($"{SporeGrainRole}.{variant + 1}", texture, new Vector2(size, size) / 384f, new Vector2(.5f, .5f));
        }

        ArtVisual TemporarySporeStream()
        {
            const int width = 512, height = 64;
            var texture = PaintTexture("Temporary spore stream", width, height, (x, y) =>
            {
                var across = Mathf.Abs(y - 31.5f) / 26f;   // 0 at the core, 1 at the band's edge (8px margin)
                if (across >= 1) return new Color(0, 0, 0, 0);
                var grain = .1f * Mathf.Sin(x * .09f + y * .4f) + .06f * Mathf.Sin(x * .31f - y * .9f);
                var alpha = Mathf.Clamp01((1 - across * across) * 1.1f + grain) * .85f;
                var c = SporeShade(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .8f, across)), Mathf.Clamp01(1 - across * 3) * .7f);
                var speck = Mathf.Sin(x * 1.7f + y * 2.3f) * Mathf.Sin(x * .53f - y * 1.9f);
                if (speck > .95f) c = Color.Lerp(c, Color.white, .6f);
                c.a = alpha; return c;
            });
            return new ArtVisual(SporeStreamRole, texture, new Vector2(width, height) / 384f, new Vector2(0, .5f));
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
            var size = shape == "tornado" ? PuffSize(attack.Source) : ShieldRadius(attack.Source) * 2;
            OrbitActivationSample(new OrbitActivationRecord { Activation = attack.ActivationId, Evolved = evolved, Count = gear.Count, Shape = shape,
                OrbitRadiusPixels = orbitRadius / pixel, OrbiterSizePixels = size / pixel, HeroHeightPixels = HeroHeight / pixel,
                FirstOrbiterScreen = renderCamera.WorldToScreenPoint(center), FrameOffsetFromStart = Mathf.RoundToInt(OrbitSpec.SpreadFrames),
                Drawn = shape == "shield" ? Mathf.Min(gear.Count, OrbitSpec.MaxShields) : gear.Count });
        }
    }

    public struct OrbitActivationRecord
    {
        public int Activation, Count, FrameOffsetFromStart, Drawn;
        public bool Evolved;
        public string Shape;
        public float OrbitRadiusPixels, OrbiterSizePixels, HeroHeightPixels;
        public Vector2 FirstOrbiterScreen;
    }
}
