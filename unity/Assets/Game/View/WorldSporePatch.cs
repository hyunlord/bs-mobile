using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Spore fan as designed (system-design-v1 ground-zone, data/weapons/spore_fan.json "고정된 포자 장판"): every cast leaves a
    // spore patch on the ground where the hero stands; Core pulses it (review-impact) StepTicks apart and each pulse hits the
    // enemies on it. The patch stays where it was laid while the hero walks on. Frames are 60Hz display frames.
    public static class SporePatchSpec
    {
        // The patch spreads in over SpreadFrames from its cast, brightens for PulseFrames at every Core pulse and fades over
        // FadeFrames after its last pulse. Each pulse bursts PulsePuffs puffs up off the patch and scatters PulseGrains grains.
        public const int SpreadFrames = 6, PulseFrames = 4, FadeFrames = 24, PulsePuffs = 3, PulseGrains = 6, BurstFrames = 30;
        // The decal is drawn PatchOfRadius of the Core patch radius across (its painted stain fills ~85% of the canvas).
        public const float PatchOfRadius = 2.1f, PatchAlpha = .6f, PulseBrighten = .35f, BurstHeroHeights = .55f;
        // Evolution element: the patch turns into the spore vortex (the approved three streams), reaching the patch's edge.
        public const float VortexOfRadius = .85f;
        public static float Seconds(int frames) => frames / 60f;
        public static float Spread(float age) => Mathf.Clamp01(age / Seconds(SpreadFrames));
        public static float Fade(float sinceLastPulse) => sinceLastPulse <= 0 ? 1 : Mathf.Clamp01(1 - sinceLastPulse / Seconds(FadeFrames));
        public static float Pulse(float sincePulse) => sincePulse < 0 || sincePulse > Seconds(PulseFrames) ? 0 : 1 - sincePulse / Seconds(PulseFrames);
    }

    public sealed partial class WorldRenderer
    {
        const int SporePatchCapacity = 8, SporePulseCapacity = 16;
        sealed class SporePatch
        {
            public int Tick = -1, Count; public string Source; public float Accepted, Radius; public bool Evolved, Recorded;
            public Vector2 Centre;
            public readonly int[] Ids = new int[SporePulseCapacity];
            public readonly float[] Due = new float[SporePulseCapacity], Landed = new float[SporePulseCapacity];
            public float LastPulse => Count == 0 ? Accepted : Mathf.Max(Landed[Count - 1] >= 0 ? Landed[Count - 1] : Due[Count - 1], Accepted);
        }
        readonly SporePatch[] sporePatches = CreateSporePatches();
        int sporePatchNext;
        ArtVisual sporePatchPicture;
        public int SporePatches { get; private set; }
        public int SporePulses { get; private set; }
        public int SporePulsesOnTime { get; private set; }
        public System.Action<SporePatchRecord> SporePatchSample;

        static SporePatch[] CreateSporePatches()
        {
            var patches = new SporePatch[SporePatchCapacity];
            for (var i = 0; i < patches.Length; i++) patches[i] = new SporePatch();
            return patches;
        }

        void ResetSporePatches()
        {
            foreach (var patch in sporePatches) { patch.Tick = -1; patch.Count = 0; }
            sporePatchNext = 0; SporePatches = SporePulses = SporePulsesOnTime = 0;
        }

        bool IsSporePatchSource(string source) => source != null && m1CombatShapes.TryGetValue(source, out var shape) && shape == "tornado"
            && waveDefinition != null && waveDefinition.Gear.TryGetValue(source, out var gear) && gear.Composition != null;

        void AcceptSporePatchEvent(WaveEvent value)
        {
            if (!IsSporePatchSource(value.Source)) return;
            if (value.Kind == "review-impact-tell")
            {
                SporePatch patch = null;
                foreach (var candidate in sporePatches) if (candidate.Tick == value.Tick && candidate.Source == value.Source) patch = candidate;
                if (patch == null)
                {
                    patch = sporePatches[sporePatchNext++ % sporePatches.Length];
                    patch.Tick = value.Tick; patch.Source = value.Source; patch.Count = 0; patch.Recorded = false;
                    patch.Accepted = visualTime; patch.Centre = Point(value.Position);
                    patch.Radius = value.Amount / (float)settings.WorldUnitsPerUnityUnit;
                    patch.Evolved = WeaponEvolved(value.Source);
                    SporePatches++;
                }
                if (patch.Count == SporePulseCapacity) return;
                var composition = waveDefinition.Gear[value.Source].Composition;
                var rate = (float)Mathf.Max(1, waveTickRate);
                var i = patch.Count++;
                patch.Ids[i] = value.SubjectId; patch.Landed[i] = -1;
                patch.Due[i] = visualTime + ((composition?.DelayTicks ?? 1) + i * (composition?.StepTicks ?? 0)) / rate;
            }
            else if (value.Kind == "review-impact")
            {
                foreach (var patch in sporePatches)
                    for (var i = 0; i < patch.Count; i++)
                    {
                        if (patch.Ids[i] != value.SubjectId || patch.Landed[i] >= 0) continue;
                        patch.Landed[i] = visualTime; SporePulses++;
                        if (Mathf.Abs(visualTime - patch.Due[i]) <= SporePatchSpec.Seconds(2)) SporePulsesOnTime++;
                    }
            }
        }

        // Patch hits: struck enemies hop away from the patch's middle, or are drawn into it once it is the evolved vortex.
        bool SporePatchHitDirection(WaveEvent value, Vector2 origin, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!IsSporePatchSource(value.Source)) return false;
            var best = float.PositiveInfinity; SporePatch nearest = null;
            foreach (var patch in sporePatches)
            {
                if (patch.Tick < 0) continue;
                var d = (origin - patch.Centre).sqrMagnitude;
                if (d < best) { best = d; nearest = patch; }
            }
            if (nearest == null) return false;
            direction = origin - nearest.Centre;
            if (direction.sqrMagnitude < 1e-8f) direction = Vector2.up;
            direction.Normalize();
            if (nearest.Evolved) direction = -direction;   // pulled inward (View only: Core has no pull)
            return true;
        }

        void DrawSporePatches()
        {
            if (waveDefinition == null) return;
            var any = false;
            foreach (var patch in sporePatches) if (patch.Tick >= 0 && patch.Count > 0) { any = true; break; }
            if (!any) return;
            EnsureSporeArt(); EnsureSporePatchArt();
            var view = CameraWorldViewport;
            // Only the newest evolved patch carries the vortex (four overlapping patches turning four vortices read as clutter).
            SporePatch newest = null;
            foreach (var patch in sporePatches)
                if (patch.Tick >= 0 && patch.Count > 0 && patch.Evolved && SporePatchSpec.Fade(visualTime - patch.LastPulse) > 0 && (newest == null || patch.Tick > newest.Tick)) newest = patch;
            foreach (var patch in sporePatches)
            {
                if (patch.Tick < 0 || patch.Count == 0) continue;
                var now = visualTime;
                var fade = SporePatchSpec.Fade(now - patch.LastPulse);
                if (fade <= 0) continue;
                if (!patch.Recorded && now >= patch.Due[0]) RecordSporePatch(patch, view);
                var spread = SporePatchSpec.Spread(now - patch.Accepted);
                var pulse = 0f;
                for (var i = 0; i < patch.Count; i++) pulse = Mathf.Max(pulse, SporePatchSpec.Pulse(now - (patch.Landed[i] >= 0 ? patch.Landed[i] : patch.Due[i])));
                // The decal: ground layer under the enemies, brightening at each pulse.
                var width = patch.Radius * SporePatchSpec.PatchOfRadius * Mathf.Lerp(.6f, 1, spread);
                var colour = Color.white; colour.a = SporePatchSpec.PatchAlpha * spread * fade * (1 + SporePatchSpec.PulseBrighten * pulse);
                Draw(sporePatchPicture, GameVisualTokens.AttackLayer, patch.Centre, 0, new Vector2(width, width * sporePatchPicture.WorldSize.y / sporePatchPicture.WorldSize.x), 0, 1, colour, applyTween: false);
                if (patch == newest) { DrawSporeVortex(patch.Centre, patch.Radius * SporePatchSpec.VortexOfRadius / OrbitSpec.VortexReach, spread * fade); VortexFrames++; }
                // Each pulse bursts puffs up off the patch and scatters grains outward (inward toward the vortex when evolved).
                for (var i = 0; i < patch.Count; i++)
                {
                    var age = now - (patch.Landed[i] >= 0 ? patch.Landed[i] : patch.Due[i]);
                    if (age < 0 || age > SporePatchSpec.Seconds(SporePatchSpec.BurstFrames)) continue;
                    var life = age / SporePatchSpec.Seconds(SporePatchSpec.BurstFrames);
                    var seed = patch.Tick * 31 + i;
                    for (var k = 0; k < SporePatchSpec.PulsePuffs && puffBudget > 0; k++)
                    {
                        var at = patch.Centre + M1Direction(SporeHash(seed, k) * 360) * (patch.Radius * .55f * Mathf.Sqrt(SporeHash(seed + 5, k)))
                            + Vector2.up * (HeroHeight * .25f * life);
                        var size = HeroHeight * SporePatchSpec.BurstHeroHeights * OrbitSpec.PuffScale(life);
                        var picture = sporePuffs[(k + i) % sporePuffs.Length];
                        var puff = Color.white; puff.a = .9f * OrbitSpec.PuffAlpha(life) * fade;
                        Draw(picture, ProjectileLayer, at, 0, new Vector2(size, size * picture.WorldSize.y / picture.WorldSize.x), (SporeHash(seed + 9, k) - .5f) * 40, 1, puff, applyTween: false);
                        puffBudget--; SporePuffsDrawn++;
                    }
                    for (var g = 0; g < SporePatchSpec.PulseGrains; g++)
                    {
                        var direction = M1Direction(SporeHash(seed + 13, g) * 360);
                        var from = patch.Radius * (patch.Evolved ? 1 - .7f * life : .3f + .7f * life);
                        DrawSporeGrain(patch.Centre + direction * from, g + i, Mathf.Sin(life * Mathf.PI) * fade);
                    }
                }
            }
        }

        const string SporePatchRole = "fx.spore_fan.patch";

        void EnsureSporePatchArt()
        {
            if (sporePatchPicture != null) return;
            sporePatchPicture = EffectArt(SporePatchRole, TemporarySporePatch);
        }

        // Temporary patch (384x256, 3/4 view): a flat oval spore stain, palest in the middle, a lumpy colony rim most dense,
        // teal flecks over a pale yellow-green ground; yellow-green and teal only, nothing darker than the grass.
        ArtVisual TemporarySporePatch()
        {
            const int width = 384, height = 256;
            var texture = PaintTexture("Temporary spore patch", width, height, (x, y) =>
            {
                var u = (x + .5f - width * .5f) / (width * .5f - 8); var v = (y + .5f - height * .5f) / (height * .5f - 8);
                var r = Mathf.Sqrt(u * u + v * v);
                var angle = Mathf.Atan2(v, u);
                var rim = .86f + .05f * Mathf.Sin(angle * 9) + .03f * Mathf.Sin(angle * 23 + 1.7f);
                if (r >= rim + .1f) return new Color(0, 0, 0, 0);
                var colony = Mathf.Clamp01(1 - Mathf.Abs(r - rim) / .12f);
                var inner = Mathf.Clamp01(1 - r / rim) * .25f;
                var alpha = Mathf.Clamp01(Mathf.Max(colony * .85f, r < rim ? .3f + inner : 0));
                var fleck = Mathf.Sin(x * .41f + y * .73f) * Mathf.Sin(x * .29f - y * .37f);
                var c = SporeShade(colony > .5f ? .3f : .9f, .25f + inner);
                if (fleck > .8f && r < rim) { c = SporeShade(0, .1f); alpha = Mathf.Max(alpha, .55f); }
                c.a = alpha; return c;
            });
            return new ArtVisual(SporePatchRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .5f));
        }

        void RecordSporePatch(SporePatch patch, Rect view)
        {
            patch.Recorded = true;
            if (SporePatchSample == null) return;
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            SporePatchSample(new SporePatchRecord { Tick = patch.Tick, Pulses = patch.Count, Evolved = patch.Evolved, CentreScreen = renderCamera.WorldToScreenPoint(patch.Centre),
                RadiusPixels = patch.Radius / pixel, HeroHeightPixels = HeroHeight / pixel, OnScreen = view.Contains(patch.Centre),
                StepFrames = patch.Count > 1 ? Mathf.RoundToInt((patch.Due[1] - patch.Due[0]) * 60) : 0 });
        }
    }

    public struct SporePatchRecord
    {
        public int Tick, Pulses, StepFrames;
        public bool Evolved, OnScreen;
        public float RadiusPixels, HeroHeightPixels;
        public Vector2 CentreScreen;
    }
}
