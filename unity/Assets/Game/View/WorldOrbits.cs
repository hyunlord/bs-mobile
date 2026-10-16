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
        public const float FanDegrees = 60, PuffFrom = .5f, PuffTo = 1.1f, PuffSwayDegrees = 20, MinPuffHeroHeights = .5f, MaxPuffHeroHeights = .6f;
        public const float GrainHeroHeights = .11f;   // the grain fills about half its canvas: 5-8px on screen
        // StreamSegments slices per stream: fine enough that the end taper (alpha and width) and the bend change in small steps
        // (at 18 the painted stream's square-cut slices showed as steps at both ends and joints at the bends).
        public const int VortexArms = 3, StreamSegments = 60, StreamGrains = 8, EvolvedPuffs = 2;
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
        // Guard ring (docs/design/m1-weapon-effect-sprites.md): an upright heater shield picture per drawn orbiter, front face
        // always shown (it only wobbles on the orbit), a gold trail picture bent along the orbit behind it; evolution throws
        // the nearest shield out to the struck enemy and back without turning it, trailing gold, and a gold shockwave spreads
        // ShockFrom -> ShockTo of ShockOfShieldHeight where it lands.
        public const int ThrowFrames = 10, ShockwaveFrames = 10, MaxThrows = 16, MaxShocks = 4, TrailSegments = 8;
        public const float ThrownShieldDegrees = 0;
        public const float ShockFrom = .3f, ShockTo = 1.2f, ShockOfShieldHeight = 2.4f, TrailShieldWidths = 2.5f, TrailOfShieldHeight = .6f;
        // A thrown shield's place along its flight (0 at the orbit, 1 at the struck enemy) at share t of the throw: out and back.
        public static float ThrowReach(float t) { var r = t < .5f ? t * 2 : (1 - t) * 2; r = Mathf.Clamp01(r); return r * r * (3 - 2 * r); }
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
        ArtVisual wardShield, wardTrail, wardShock;
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
                // Spore fans are recorded at the activation's first orbiter; guard rings at the first shield actually drawn
                // (with more than MaxShields orbiters some are not drawn, and the first may be one of them).
                if (shape == "tornado" && age >= OrbitSpec.Seconds(OrbitSpec.SpreadFrames) && orbitRecordedActivations.Add(attack.ActivationId)) RecordOrbitActivation(attack, shape, evolved, center, orbitRadius, gear);
            }
            var shown = OrbitSpec.Shown(presence);
            if (shape == "tornado")
            {
                orbitSeen.TryGetValue(attack.ActivationId, out var orbiter); orbitSeen[attack.ActivationId] = orbiter + 1;
                DrawSporeFan(center, lordPoint, attack.Radius / (float)settings.WorldUnitsPerUnityUnit, shown, attack.ActivationId * 31 + orbiter, attack.Source, evolved);
                return;
            }
            orbitSeen.TryGetValue(attack.ActivationId, out var index); orbitSeen[attack.ActivationId] = index + 1;
            if (!OrbitSpec.ShieldDrawn(index, gear.Count)) return;
            DrawShield(center, lordPoint, ShieldRadius(attack.Source) * shown, shown, age); ShieldsDrawn++;
            if (age >= OrbitSpec.Seconds(OrbitSpec.SpreadFrames) && orbitRecordedActivations.Add(attack.ActivationId)) RecordOrbitActivation(attack, shape, evolved, center, orbitRadius, gear);
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
                var colour = Color.white; colour.a = .95f * OrbitSpec.PuffAlpha(life) * shown;
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
                    var colour = Color.white; colour.a = .95f * shown * taper;
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
                var alpha = Mathf.Clamp01((density + lump) * 1.5f) * .9f;
                if (alpha < .02f) return new Color(0, 0, 0, 0);
                var c = SporeShade((x - (height - y)) / (float)width + .5f, density * .2f);
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

        // The shield picture upright at `at` (front face), `radius` half its height; it wobbles on the orbit only. The gold
        // trail bends back along the orbit behind it.
        void DrawShield(Vector2 at, Vector2 hero, float radius, float presence, float age)
        {
            if (radius <= 0) return;
            EnsureWardArt();
            var offset = at - hero; var orbit = offset.magnitude;
            if (orbit > .0001f)
            {
                // The orbit turns counter-clockwise, so the trail lies at smaller angles behind the shield.
                var angle = Angle(offset);
                var sweep = ShieldWidth(radius) * OrbitSpec.TrailShieldWidths / orbit * Mathf.Rad2Deg;
                DrawWardTrail(t => hero + M1Direction(angle - t * sweep) * orbit, radius, presence);
            }
            var wobble = Mathf.Sin(age * 9) * OrbitSpec.ShieldWobble;
            DrawShieldPicture(at, radius, presence, wobble);
        }

        float ShieldWidth(float radius) => radius * 2 * wardShield.WorldSize.x / wardShield.WorldSize.y;

        void DrawShieldPicture(Vector2 at, float radius, float presence, float degrees)
        {
            var colour = Color.white; colour.a = presence;
            Draw(wardShield, ProjectileLayer, at, 0, new Vector2(ShieldWidth(radius), radius * 2), degrees, 1, colour, applyTween: false);
        }

        // Gold trail picture laid along `path` (0 = at the shield, 1 = the trail's far end) in TrailSegments slices, its bright
        // right end at the shield. Under the shield (layer 71); a wide element, capped over enemies.
        void DrawWardTrail(System.Func<float, Vector2> path, float radius, float presence)
        {
            var uv = wardTrail.UvRects[0];
            var width = radius * 2 * OrbitSpec.TrailOfShieldHeight;
            var near = path(0);
            for (var k = 0; k < OrbitSpec.TrailSegments; k++)
            {
                var t0 = k / (float)OrbitSpec.TrailSegments; var t1 = (k + 1) / (float)OrbitSpec.TrailSegments;
                var far = path(t1); var span = near - far;
                if (span.sqrMagnitude > 1e-10f)
                {
                    var slice = new Rect(uv.x + uv.width * (1 - t1), uv.y, uv.width * (t1 - t0), uv.height);
                    var colour = Color.white; colour.a = .9f * presence;
                    Draw(wardTrail, GameVisualTokens.WaveTransientCueLayer, near, 0, new Vector2(span.magnitude * 1.06f, width), Angle(span), 1, colour, uv: slice, applyTween: false);
                }
                near = far;
            }
        }

        // Evolution element for the guard ring: thrown shields fly out to the struck enemy and back, front face kept upright
        // (no turn, no wobble), trailing gold along the flight; a gold shockwave spreads where they hit (at most MaxShocks).
        void DrawShieldThrows()
        {
            if (shieldThrows.Count == 0) return;
            EnsureLightningArt(); EnsureWardArt();
            var throwSeconds = OrbitSpec.Seconds(OrbitSpec.ThrowFrames);
            var shocks = 0;
            for (var i = shieldThrows.Count - 1; i >= 0; i--)
            {
                var item = shieldThrows[i];
                var age = visualTime - item.Started;
                if (age > Mathf.Max(throwSeconds, OrbitSpec.Seconds(OrbitSpec.ShockwaveFrames) + throwSeconds * .5f)) { shieldThrows.RemoveAt(i); continue; }
                if (age < throwSeconds)
                {
                    var t = age / throwSeconds;
                    var at = Vector2.Lerp(item.From, item.To, OrbitSpec.ThrowReach(t));
                    // Behind the shield along the flight: toward the orbit on the way out, toward the enemy on the way back.
                    var start = t < .5f ? item.From : item.To;
                    var behind = start - at; var travelled = behind.magnitude;
                    if (travelled > 1e-5f)
                    {
                        var length = Mathf.Min(travelled, ShieldWidth(item.Size) * OrbitSpec.TrailShieldWidths);
                        var direction = behind / travelled;
                        DrawWardTrail(d => at + direction * (length * d), item.Size, 1);
                    }
                    DrawShieldPicture(at, item.Size, 1, OrbitSpec.ThrownShieldDegrees);   // front face kept: never turns
                }
                var hit = age - throwSeconds * .5f;
                if (hit < 0 || shocks >= OrbitSpec.MaxShocks) continue;
                var s = hit / OrbitSpec.Seconds(OrbitSpec.ShockwaveFrames);
                if (s > 1) continue;
                shocks++;
                var size = item.Size * 2 * OrbitSpec.ShockOfShieldHeight * Mathf.Lerp(OrbitSpec.ShockFrom, OrbitSpec.ShockTo, Mathf.Sqrt(s));
                var colour = Color.white; colour.a = 1 - s * s;
                Draw(wardShock, ShieldEmblemLayer, item.To, 0, new Vector2(size, size * wardShock.WorldSize.y / wardShock.WorldSize.x), 0, 1, colour, applyTween: false);
            }
        }

        const string WardShieldRole = "fx.ward_orbit.shield", WardTrailRole = "fx.ward_orbit.trail", WardShockRole = "fx.ward_orbit.shock";

        void EnsureWardArt()
        {
            if (wardShield != null) return;
            wardShield = EffectArt(WardShieldRole, TemporaryWardShield);
            wardTrail = EffectArt(WardTrailRole, TemporaryWardTrail);
            wardShock = EffectArt(WardShockRole, TemporaryWardShock);
        }

        static readonly Color WardInk = new Color32(0x29, 0x28, 0x22, 255);
        static readonly Color[] WardGold = { new Color32(0xF6, 0xD7, 0x7A, 255), new Color32(0xE2, 0xB5, 0x4A, 255), new Color32(0xB4, 0x86, 0x2E, 255) };
        static readonly Color[] WardBlue = { new Color32(0x4C, 0x78, 0xD8, 255), new Color32(0x33, 0x59, 0xB8, 255), new Color32(0x24, 0x40, 0x8A, 255) };

        // Temporary heater shield (128x160, front face): flat top, sides curving down to a point, charcoal outline, a gold rim
        // in three tones, a blue field, a gold wheat-sheaf crest and an upper-left highlight. Lit from the upper left.
        ArtVisual TemporaryWardShield()
        {
            const int width = 128, height = 160;
            var outline = new List<Vector2> { new Vector2(10, 150), new Vector2(118, 150), new Vector2(118, 100) };
            for (var k = 1; k <= 16; k++)
            {
                var t = k / 16f;   // right side down to the point: a quadratic curve
                var a = new Vector2(118, 100); var c = new Vector2(116, 40); var b = new Vector2(64, 8);
                outline.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b);
            }
            for (var k = outline.Count - 2; k >= 2; k--) outline.Add(new Vector2(width - outline[k].x, outline[k].y));
            bool Inside(Vector2 q)
            {
                var inside = false;
                for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
                    if ((outline[i].y > q.y) != (outline[j].y > q.y) && q.x < (outline[j].x - outline[i].x) * (q.y - outline[i].y) / (outline[j].y - outline[i].y) + outline[i].x) inside = !inside;
                return inside;
            }
            float Edge(Vector2 q)
            {
                var best = float.MaxValue;
                for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
                {
                    var a = outline[j]; var ab = outline[i] - a;
                    var t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
                }
                return best;
            }
            bool Crest(Vector2 q)
            {
                // Three wheat stalks bound by a band: stems, grain ovals along their upper halves, the band across.
                for (var k = -1; k <= 1; k++)
                {
                    var top = new Vector2(64 + k * 14, 120); var foot = new Vector2(64 + k * 6, 44);
                    var ab = top - foot; var t = Mathf.Clamp01(Vector2.Dot(q - foot, ab) / ab.sqrMagnitude);
                    if ((q - (foot + ab * t)).magnitude < 2.4f) return true;
                    for (var g = 0; g < 5; g++)
                    {
                        var at = Vector2.Lerp(foot, top, .5f + g * .11f);
                        for (var side = -1; side <= 1; side += 2)
                        {
                            var grain = at + new Vector2(side * 4.5f, 1.5f);
                            var d = q - grain; var tilt = side * .6f;
                            var u = d.x * Mathf.Cos(tilt) + d.y * Mathf.Sin(tilt); var v = -d.x * Mathf.Sin(tilt) + d.y * Mathf.Cos(tilt);
                            if (u * u / 9f + v * v / 20f < 1) return true;
                        }
                    }
                }
                return Mathf.Abs(q.y - 72) < 4 && Mathf.Abs(q.x - 64) < 16;
            }
            var texture = PaintTexture("Temporary ward shield", width, height, (x, y) =>
            {
                var q = new Vector2(x + .5f, y + .5f);
                if (!Inside(q)) return new Color(0, 0, 0, 0);
                var edge = Edge(q);
                if (edge < 3) return WardInk;
                var light = Mathf.Clamp01(.5f + ((64 - q.x) / 128f + (q.y - 80) / 160f));   // upper left is lit
                var tone = light > .62f ? 0 : light > .38f ? 1 : 2;
                if (edge < 14)
                {
                    var rim = WardGold[tone];
                    if (edge < 6 && light > .6f) rim = Color.Lerp(rim, Color.white, .5f);
                    return rim;
                }
                if (edge < 15.5f) return WardInk;
                if (Crest(q)) return light > .5f ? WardGold[0] : WardGold[1];
                var field = WardBlue[tone];
                if (edge < 24 && q.x < 64 && q.y > 96) field = Color.Lerp(field, Color.white, .45f);
                return field;
            });
            return new ArtVisual(WardShieldRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .5f));
        }

        // Temporary gold trail (256x64): bright toward its right end (white core line), fading to the left and to the edges.
        ArtVisual TemporaryWardTrail()
        {
            const int width = 256, height = 64;
            var texture = PaintTexture("Temporary ward trail", width, height, (x, y) =>
            {
                var u = (x + .5f) / width; var across = Mathf.Abs(y + .5f - height * .5f) / (height * .5f - 6);
                if (across >= 1) return new Color(0, 0, 0, 0);
                var grain = .06f * Mathf.Sin(x * .08f + y * .9f);
                var alpha = Mathf.Clamp01(Mathf.Pow(u, 1.4f) * (1 - across * across) + grain * u);
                var c = Color.Lerp(WardGold[1], WardGold[0], u);
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(Mathf.Pow(u, 6) * (1 - across * 2.5f)));
                c.a = alpha * .95f; return c;
            });
            return new ArtVisual(WardTrailRole, texture, new Vector2(width, height) / 384f, new Vector2(1, .5f));
        }

        // Temporary gold shockwave (256x128): one flat gold ring seen from above at an angle, white along its inner edge.
        ArtVisual TemporaryWardShock()
        {
            const int width = 256, height = 128;
            var texture = PaintTexture("Temporary ward shock", width, height, (x, y) =>
            {
                var e = new Vector2((x + .5f - width * .5f) / (width * .5f - 8), (y + .5f - height * .5f) / (height * .5f - 8)).magnitude;
                var ring = 1 - Mathf.Abs(e - .82f) / .13f;
                var bloom = Mathf.Clamp01(1 - Mathf.Abs(e - .86f) / .2f) * .35f;
                var alpha = Mathf.Clamp01(Mathf.Max(ring, bloom));
                if (alpha <= 0) return new Color(0, 0, 0, 0);
                var c = Color.Lerp(WardGold[1], WardGold[0], Mathf.Clamp01(ring));
                if (e < .82f && ring > .4f) c = Color.Lerp(c, Color.white, .75f);
                c.a = alpha; return c;
            });
            return new ArtVisual(WardShockRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .5f));
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
