using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Granary sling lure-bomb (ADR0063): a grain sack is slung from the hero's hand onto the nearest enemy, lands (Core
    // review-lure), draws the enemies within its radius in while a grain-scent ring closes on it, then bursts (Core
    // review-impact) over that radius. The evolved giant sack swells and swirls grain inward before it bursts. Frames are 60Hz.
    public static class SlingSpec
    {
        public const float SackHeroHeights = .38f, GiantSackHeroHeights = .75f, GiantSwell = .3f;   // giant sack swells 30% while luring
        public const float ArcHeroHeights = 1.4f, ArcOfDistance = .35f;      // the throw's apex above the straight line
        public const int LandSquashFrames = 6, BurstFrames = 16, FlashFrames = 4, DustPuffs = 3, MaxGrains = 64;
        public const int Grains = 6, GiantGrains = 14, VortexArms = 3;
        // Only the newest luring sack shows its dashed lure ring; earlier rings vanish at once (2026-10-17). The evolved giant
        // sack shows no lure ring but its blast range for the last RangeFrames before it bursts, and its burst is the weapon's
        // biggest moment: one short screen shake and GiantShards grains thrown outward (normal bursts throw Shards).
        public const int RangeFrames = 18, Shards = 6, GiantShards = 18, NormalBurstFrames = 12;
        public const float ScentRingFrom = 1, ScentRingTo = .3f, ScentLinePixels = 2.5f;   // shares of the Core radius
        public const float BurstFrom = .3f, BurstTo = 1.05f;
        public static readonly Color Grain = new Color(.96f, .80f, .36f), Scent = new Color(1f, .86f, .45f);
        public static float Seconds(int frames) => frames / 60f;
    }

    public sealed partial class WorldRenderer
    {
        const int SlingCastCapacity = 16;
        sealed class SlingCast
        {
            public int Id = -1, Tick, AcceptedFrame;
            public string Source; public bool Evolved, Recorded, Shaken;
            public float Accepted, LandDue, BurstDue, Landed = -1, Burst = -1, Radius;
            public Vector2 From, Point;
        }
        readonly SlingCast[] slingCasts = CreateSlingCasts();
        int slingNext;
        public int SlingCasts { get; private set; }
        public int SlingLanded { get; private set; }
        public int SlingLandedOnTime { get; private set; }
        public int SlingBursts { get; private set; }
        public int SlingBurstsOnTime { get; private set; }
        public int SlingVortexFrames { get; private set; }
        public int SlingRingFrames { get; private set; }     // frames with a lure or blast-range ring drawn (at most one per frame)
        public int SlingRangeFrames { get; private set; }    // frames the evolved blast range was shown
        public int SlingShakes { get; private set; }
        public System.Action<SlingCastRecord> SlingCastSample;
        ArtVisual slingSack, slingGrain, slingBurst;
        const string SackRole = "fx.granary_sling.sack", GiantSackRole = "fx.granary_sling.giant_sack", GrainRole = "fx.granary_sling.grain", BurstRole = "fx.granary_sling.burst";
        ArtVisual slingGiantSack;
        bool slingGrainPainted;   // Astra's painted grain carries its own gold; the temporary white grain is tinted gold
        Color SlingGrainColour(float alpha) { var c = slingGrainPainted ? Color.white : SlingSpec.Grain; c.a = alpha; return c; }

        static SlingCast[] CreateSlingCasts()
        {
            var casts = new SlingCast[SlingCastCapacity];
            for (var i = 0; i < casts.Length; i++) casts[i] = new SlingCast();
            return casts;
        }

        void ResetSling()
        {
            foreach (var cast in slingCasts) { cast.Id = -1; cast.Landed = cast.Burst = -1; }
            slingNext = 0; SlingCasts = SlingLanded = SlingLandedOnTime = SlingBursts = SlingBurstsOnTime = SlingVortexFrames = SlingRingFrames = SlingRangeFrames = SlingShakes = 0;
        }

        bool IsSlingLure(string source) => source != null && waveDefinition != null && waveDefinition.Gear.TryGetValue(source, out var gear) && gear.Composition?.Placement == "lure-bomb";

        SlingCast SlingCastFor(int id)
        {
            foreach (var cast in slingCasts) if (cast.Id == id) return cast;
            return null;
        }

        void AcceptSlingEvent(WaveEvent value)
        {
            if (!IsSlingLure(value.Source)) return;
            var composition = waveDefinition.Gear[value.Source].Composition;
            var rate = (float)Mathf.Max(1, waveTickRate);
            if (value.Kind == "review-impact-tell")
            {
                var cast = slingCasts[slingNext++ % slingCasts.Length];
                cast.Id = value.SubjectId; cast.Source = value.Source; cast.Tick = value.Tick; cast.Recorded = cast.Shaken = false;
                cast.Accepted = visualTime; cast.AcceptedFrame = PresentedFrames + 1; cast.Landed = cast.Burst = -1;
                cast.LandDue = visualTime + composition.DelayTicks / rate;
                cast.BurstDue = cast.LandDue + (composition.LureTicks ?? 0) / rate;
                cast.Point = Point(value.Position); cast.Radius = value.Amount / (float)settings.WorldUnitsPerUnityUnit;
                cast.Evolved = WeaponEvolved(value.Source);
                var launch = PredictedLordPosition ?? RenderedLordPosition;
                var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
                cast.From = HeroHand(launch + Vector2.up * (.45f * w));
                SlingCasts++;
            }
            else if (value.Kind == "review-lure")
            {
                var cast = SlingCastFor(value.SubjectId);
                if (cast == null || cast.Landed >= 0) return;
                cast.Landed = visualTime; cast.Point = Point(value.Position); SlingLanded++;
                if (Mathf.Abs(visualTime - cast.LandDue) <= SlingSpec.Seconds(2)) SlingLandedOnTime++;
                cast.BurstDue = visualTime + (composition.LureTicks ?? 0) / rate;
            }
            else if (value.Kind == "review-impact")
            {
                var cast = SlingCastFor(value.SubjectId);
                if (cast == null || cast.Burst >= 0) return;
                cast.Burst = visualTime; SlingBursts++;
                if (Mathf.Abs(visualTime - cast.BurstDue) <= SlingSpec.Seconds(2)) SlingBurstsOnTime++;
            }
        }

        // Burst hits push outward from the sack that burst.
        bool SlingHitDirection(WaveEvent value, Vector2 origin, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!IsSlingLure(value.Source)) return false;
            var best = float.PositiveInfinity;
            foreach (var cast in slingCasts)
            {
                if (cast.Id < 0 || cast.Burst < 0 || visualTime - cast.Burst > SlingSpec.Seconds(4)) continue;
                var away = origin - cast.Point; var distance = away.sqrMagnitude;
                if (distance < best) { best = distance; direction = away; }
            }
            if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
            return true;
        }

        void DrawSlingCasts()
        {
            var any = false;
            foreach (var cast in slingCasts) if (cast.Id >= 0 && (cast.Burst < 0 || visualTime - cast.Burst < SlingSpec.Seconds(SlingSpec.BurstFrames) + .2f)) { any = true; break; }
            if (!any) return;
            EnsureSlingArt(); EnsureLightningArt(); EnsureBladeArt();
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var grainBudget = SlingSpec.MaxGrains;
            SlingCast ringCast = null;   // the newest sack that has landed and not burst
            foreach (var cast in slingCasts)
                if (cast.Id >= 0 && cast.Burst < 0 && visualTime >= (cast.Landed >= 0 ? cast.Landed : cast.LandDue) && (ringCast == null || (cast.Landed >= 0 ? cast.Landed : cast.LandDue) > (ringCast.Landed >= 0 ? ringCast.Landed : ringCast.LandDue))) ringCast = cast;
            var ringDrawn = false;
            foreach (var cast in slingCasts)
            {
                if (cast.Id < 0) continue;
                if (cast.Burst >= 0 && visualTime - cast.Burst > SlingSpec.Seconds(SlingSpec.BurstFrames) + .2f) continue;
                var sack = HeroHeight * (cast.Evolved ? SlingSpec.GiantSackHeroHeights : SlingSpec.SackHeroHeights);
                var picture = cast.Evolved ? slingGiantSack : slingSack;
                var landed = cast.Landed >= 0 ? cast.Landed : cast.LandDue;
                if (visualTime < landed)
                {
                    // In flight: a high arc from the hand to the landing point, turning slowly; small, so drawn on the thin layer.
                    var t = Mathf.Clamp01((visualTime - cast.Accepted) / Mathf.Max(.0001f, landed - cast.Accepted));
                    var apex = Mathf.Max(HeroHeight * SlingSpec.ArcHeroHeights, (cast.Point - cast.From).magnitude * SlingSpec.ArcOfDistance);
                    var at = Vector2.Lerp(cast.From, cast.Point, t) + Vector2.up * (apex * 4 * t * (1 - t));
                    DrawSack(picture, ThinEffectLayer, at, sack * .8f, (t - .5f) * 140, 1);
                    continue;
                }
                var burst = cast.Burst;
                if (burst < 0 || visualTime < burst)
                {
                    // Landed and luring: the sack sits on the ground under the crowd (squashes as it lands; the giant sack swells),
                    // the grain-scent ring closes on it and grains drift (giant: swirl) inward.
                    var since = visualTime - landed;
                    var lure = Mathf.Clamp01(since / Mathf.Max(.0001f, cast.BurstDue - landed));
                    var squash = since < SlingSpec.Seconds(SlingSpec.LandSquashFrames) ? 1 - .25f * Mathf.Sin(since / SlingSpec.Seconds(SlingSpec.LandSquashFrames) * Mathf.PI) : 1;
                    var swell = cast.Evolved ? 1 + SlingSpec.GiantSwell * lure : 1;
                    var wobble = Mathf.Sin(since * 18) * 4 * (1 - lure * .5f);
                    DrawSack(picture, GameVisualTokens.AttackLayer, cast.Point, sack * swell, wobble, squash);
                    if (since < SlingSpec.Seconds(10)) DrawSlingDust(cast.Point, sack * .9f, since, .5f, 2, cast.Id);
                    if (cast == ringCast && !cast.Evolved) { DrawScentRing(cast, Mathf.Lerp(SlingSpec.ScentRingFrom, SlingSpec.ScentRingTo, lure * lure), lure, pixel, 1); ringDrawn = true; }
                    if (cast == ringCast && cast.Evolved && cast.BurstDue - visualTime <= SlingSpec.Seconds(SlingSpec.RangeFrames))
                    {
                        // Blast range just before the burst: the full Core radius, brightening and thickening toward it.
                        var near = 1 - Mathf.Clamp01((cast.BurstDue - visualTime) / SlingSpec.Seconds(SlingSpec.RangeFrames));
                        DrawScentRing(cast, 1, 1, pixel, 1 + near); SlingRangeFrames++; ringDrawn = true;
                    }
                    DrawGrains(cast, since, lure, ref grainBudget);
                    if (cast.Evolved) SlingVortexFrames++;
                    continue;
                }
                // Burst: a gold flash, a grain ring spreading to the Core radius and dust puffs thrown outward. Normal bursts
                // (up to four sacks overlap at max) are shorter and lighter than the evolved giant sack's.
                var age = visualTime - burst;
                var burstFrames = cast.Evolved ? SlingSpec.BurstFrames : SlingSpec.NormalBurstFrames;
                var k = Mathf.Clamp01(age / SlingSpec.Seconds(burstFrames));
                if (age < SlingSpec.Seconds(SlingSpec.FlashFrames))
                {
                    var flash = SlingSpec.Grain; flash.a = 1 - age / SlingSpec.Seconds(SlingSpec.FlashFrames);
                    Draw(lightningFlash, LightningLayer, cast.Point, 0, Vector2.one * sack * (cast.Evolved ? 3.2f : 2.4f), 0, 1, flash, applyTween: false);
                    // White-hot core so the burst reads against bright ground without widening the gold flash.
                    var core = Color.white; core.a = flash.a;
                    Draw(lightningFlash, LightningLayer, cast.Point, 0, Vector2.one * sack * 1.1f, 0, 1, core, applyTween: false);
                }
                if (cast.Evolved && !cast.Shaken) { cast.Shaken = true; SlingShakes++; impactShakeUntil = visualTime + GameVisualTokens.ImpactShakeSeconds * 2; }
                DrawShards(cast, age, cast.Evolved ? SlingSpec.GiantShards : SlingSpec.Shards);
                var width = cast.Radius * 2 * Mathf.Lerp(SlingSpec.BurstFrom, SlingSpec.BurstTo, Mathf.Sqrt(k));
                var ring = Color.white; ring.a = Mathf.Pow(1 - k, 1.5f) * (cast.Evolved ? 1 : .6f);
                Draw(slingBurst, ProjectileLayer, cast.Point, 0, Vector2.one * width, cast.Id * 37 % 360, 1, ring, applyTween: false);
                DrawSlingDust(cast.Point, cast.Radius * .55f, age, cast.Evolved ? .6f : .4f, cast.Evolved ? SlingSpec.DustPuffs : 2, cast.Id + 7);
                if (!cast.Recorded) { cast.Recorded = true; RecordSlingCast(cast); }
            }
            if (ringDrawn) SlingRingFrames++;
        }

        // Grain shards thrown outward from the burst to just past the Core radius, spinning and fading.
        void DrawShards(SlingCast cast, float age, int count)
        {
            var t = Mathf.Clamp01(age / SlingSpec.Seconds(cast.Evolved ? SlingSpec.BurstFrames : SlingSpec.NormalBurstFrames));
            if (t >= 1) return;
            var size = HeroHeight * (cast.Evolved ? .11f : .08f);
            for (var s = 0; s < count; s++)
            {
                var seed = LightningBolt.Hash(cast.Id, 700 + s);
                var angle = s * 360f / count + ((seed & 255) / 255f - .5f) * 20;
                var reach = cast.Radius * Mathf.Lerp(.1f, 1.05f, 1 - (1 - t) * (1 - t)) * (.8f + .2f * (((seed >> 8) & 255) / 255f));
                var colour = SlingGrainColour(1 - t * t);
                Draw(slingGrain, ProjectileLayer, cast.Point + M1Direction(angle) * reach + Vector2.up * (HeroHeight * .5f * 4 * t * (1 - t)), 0, new Vector2(size * .6f, size), angle + 90 + t * 360, 1, colour, applyTween: false);
            }
        }

        void DrawSack(ArtVisual picture, int layer, Vector2 bottom, float height, float degrees, float squash)
        {
            var size = new Vector2(height * picture.WorldSize.x / picture.WorldSize.y / Mathf.Max(.5f, squash), height * squash);
            Draw(picture, layer, bottom, 0, size, degrees, 1, Color.white, applyTween: false);
        }

        // The grain-scent ring: a thin gold outline at `share` of the Core radius, closing on the sack while it lures.
        void DrawScentRing(SlingCast cast, float share, float lure, float pixel, float thickness)
        {
            const int segments = 48;
            var radius = cast.Radius * share; var line = SlingSpec.Scent; line.a = .85f * (.6f + .4f * lure);
            for (var s = 0; s < segments; s++)
            {
                if (s % 4 == 3) continue;   // a dashed ring reads as scent rather than a wall
                var middle = (s + .5f) * 360f / segments;
                Draw(lightningSolid, ProjectileLayer, cast.Point + M1Direction(middle) * radius, 0, new Vector2(radius * Mathf.PI * 2 / segments * 1.05f, SlingSpec.ScentLinePixels * thickness * pixel), middle + 90, 1, line, applyTween: false);
            }
        }

        // Grains drift from the ring to the sack; the giant sack's grains swirl in along VortexArms spiral arms.
        void DrawGrains(SlingCast cast, float since, float lure, ref int budget)
        {
            var count = cast.Evolved ? SlingSpec.GiantGrains : SlingSpec.Grains;
            var size = HeroHeight * .07f;
            for (var g = 0; g < count && budget > 0; g++, budget--)
            {
                var seed = LightningBolt.Hash(cast.Id, 300 + g);
                var phase = (since * (cast.Evolved ? 1.6f : 1.1f) + (seed & 1023) / 1023f) % 1;   // 0 at the ring, 1 at the sack
                var reach = cast.Radius * Mathf.Lerp(SlingSpec.ScentRingFrom, .15f, phase) * Mathf.Lerp(1, .6f, lure);
                var angle = ((seed >> 10) & 1023) / 1023f * 360;
                if (cast.Evolved) angle = g % SlingSpec.VortexArms * 360f / SlingSpec.VortexArms + phase * 220 + since * 120;
                var colour = SlingGrainColour(Mathf.Sin(phase * Mathf.PI) * (cast.Evolved ? .95f : .8f));
                Draw(slingGrain, ProjectileLayer, cast.Point + M1Direction(angle) * reach, 0, Vector2.one * size, angle + 90, 1, colour, applyTween: false);
            }
        }

        void DrawSlingDust(Vector2 centre, float spread, float age, float life, int puffs, int seed)
        {
            if (age < 0 || age > life) return;
            var t = age / life;
            for (var p = 0; p < puffs; p++)
            {
                var hash = LightningBolt.Hash(seed, 500 + p);
                var angle = (hash & 1023) / 1023f * 360;
                var puff = bladeDust[p % bladeDust.Length];
                var colour = bladeDustPainted ? new Color(1, 1, 1, .85f * (1 - t)) : new Color(.84f, .8f, .72f, .7f * (1 - t));
                var size = HeroHeight * .32f * Mathf.Lerp(.6f, 1.1f, t);
                Draw(puff, ProjectileLayer, centre + M1Direction(angle) * spread * Mathf.Lerp(.3f, 1, Mathf.Sqrt(t)), 0, new Vector2(size, size * puff.WorldSize.y / puff.WorldSize.x), 0, 1, colour, applyTween: false);
            }
        }

        void EnsureSlingArt()
        {
            if (slingSack != null) return;
            slingSack = EffectArt(SackRole, () => TemporarySack("Temporary grain sack", false));
            slingGiantSack = EffectArt(GiantSackRole, () => TemporarySack("Temporary giant grain sack", true));
            slingGrain = EffectArt(GrainRole, TemporaryGrain);
            slingGrainPainted = art != null && art.TryResolveRole(GrainRole, out _);
            slingBurst = EffectArt(BurstRole, TemporaryBurst);
        }

        // Temporary pictures until Astra paints the fx.granary_sling roles: a tied burlap sack (bottom-centre pivot), a wheat
        // grain, and a ring of grain and chaff for the burst.
        ArtVisual TemporarySack(string name, bool giant)
        {
            const int width = 160, height = 192;
            var ink = GameVisualTokens.Ink; var burlap = new Color(.80f, .64f, .40f); var shade = new Color(.62f, .47f, .28f); var band = giant ? new Color(.93f, .76f, .28f) : new Color(.55f, .36f, .20f);
            var texture = PaintTexture(name, width, height, (x, y) =>
            {
                var u = (x + .5f) / width * 2 - 1; var v = (y + .5f) / height;
                var bodyHalf = v < .72f ? .78f * Mathf.Sqrt(Mathf.Clamp01(1 - Mathf.Pow((v - .36f) / .36f, 2))) + .06f : 0;
                var neck = v >= .68f && v < .8f ? .2f : 0; var tuft = v >= .8f && v < .94f ? .32f * (1 - Mathf.Abs(v - .87f) / .07f) + .1f : 0;
                var half = Mathf.Max(bodyHalf, Mathf.Max(neck, tuft));
                var edge = half - Mathf.Abs(u);
                if (edge < 0 || v < .02f) return new Color(0, 0, 0, 0);
                if (edge < .045f) return ink;
                Color c = u < -.15f && v > .25f ? Color.Lerp(burlap, Color.white, .15f) : u > .35f ? shade : burlap;
                if (Mathf.Abs(v - .72f) < .03f) c = band;
                if (giant && Mathf.Abs(v - .38f) < .025f) c = band;
                if (((x / 6) + (y / 6)) % 2 == 0) c = Color.Lerp(c, shade, .12f);   // weave
                c.a = 1; return c;
            });
            return new ArtVisual(giant ? GiantSackRole : SackRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .02f));
        }

        ArtVisual TemporaryGrain()
        {
            const int size = 32;
            var texture = PaintTexture("Temporary sling grain", size, size, (x, y) =>
            {
                var u = (x + .5f) / size * 2 - 1; var v = (y + .5f) / size * 2 - 1;
                var r = u * u / .16f + v * v / .7f;
                if (r > 1) return new Color(0, 0, 0, 0);
                var c = Color.Lerp(Color.white, new Color(1, .9f, .6f), r); c.a = Mathf.Clamp01((1 - r) * 4); return c;
            });
            return new ArtVisual(GrainRole, texture, Vector2.one * size / 384f, new Vector2(.5f, .5f));
        }

        ArtVisual TemporaryBurst()
        {
            const int size = 256;
            var texture = PaintTexture("Temporary sling burst", size, size, (x, y) =>
            {
                var u = (x + .5f) / size * 2 - 1; var v = (y + .5f) / size * 2 - 1;
                var r = Mathf.Sqrt(u * u + v * v); if (r > 1) return new Color(0, 0, 0, 0);
                var band = Mathf.Clamp01(1 - Mathf.Abs(r - .84f) / .09f);
                var speck = (LightningBolt.Hash(x * 7 + y, 9) & 31) == 0 && r < .9f ? .8f : 0;
                var a = Mathf.Max(band * .75f, speck * r);
                var c = Color.Lerp(new Color(1, .95f, .75f), new Color(.92f, .72f, .30f), r); c.a = a; return c;
            });
            return new ArtVisual(BurstRole, texture, Vector2.one * size / 384f, new Vector2(.5f, .5f));
        }

        void RecordSlingCast(SlingCast cast)
        {
            if (SlingCastSample == null) return;
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            SlingCastSample(new SlingCastRecord
            {
                Tick = cast.Tick, Evolved = cast.Evolved, AcceptedOffset = cast.AcceptedFrame - PresentedFrames,
                LureFrames = Mathf.RoundToInt((cast.Burst - cast.Landed) * 60), FlightFrames = Mathf.RoundToInt((cast.Landed - cast.Accepted) * 60),
                CentreScreen = renderCamera.WorldToScreenPoint(cast.Point), OnScreen = CameraWorldViewport.Contains(cast.Point),
                RadiusPixels = cast.Radius / pixelsToWorld, HeroHeightPixels = HeroHeight / pixelsToWorld,
                SackPixels = HeroHeight * (cast.Evolved ? SlingSpec.GiantSackHeroHeights : SlingSpec.SackHeroHeights) / pixelsToWorld
            });
        }
    }

    // One sling cast, sampled on the first frame drawn after its burst; AcceptedOffset is frames relative to that frame.
    public struct SlingCastRecord
    {
        public int Tick, AcceptedOffset, LureFrames, FlightFrames;
        public bool Evolved, OnScreen;
        public float RadiusPixels, HeroHeightPixels, SackPixels;
        public Vector2 CentreScreen;
    }
}
