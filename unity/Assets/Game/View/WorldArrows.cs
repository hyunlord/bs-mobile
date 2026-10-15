using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Projectile spec ("무기 연출 레퍼런스·이론", projectile row) for the canal bow. Frames are 60Hz display frames.
    public static class ArrowSpec
    {
        public const int FlightFrames = 10;          // launched so it lands exactly on the Core impact tick
        public const int MuzzleFlashFrames = 1;      // small flash when shot
        public const int LaunchBloomFrames = 1;      // the arrow's first frame is a round bloom
        public const int LandFrames = 8, PlantedFrames = 14;
        public const int HitHoldFrames = 2;          // projectile hits freeze briefly (1–2) and push away from the impact
        public const float ArcOfDistance = .12f, ArrowBodyWidths = 1.1f, TrailOfDistance = .3f;
        public const float ShaftPixels = 3, OutlinePixels = 1.5f, HeadPixels = 10;
        // Evolution element: warning circles on the ground at every impact, then arrows drive down from the sky onto each one
        // as Core lands it (a cast's impacts land one step apart, so the rain rolls across the circles).
        public const int WarningGrowFrames = 4, FallFrames = 8, MinRainArrows = 4, MaxRainArrows = 9, MaxRainArrowsTotal = 180;
        public const float RainDropHeight = .55f, RainSlant = .18f;
        public static readonly Color Silver = new Color(.9f, .93f, .97f);
        public static readonly Color Water = new Color(.5f, .82f, 1f);   // silver-white + water sky blue
        public static float Seconds(int frames) => frames / 60f;
        public static int RainArrows(float radius, float bodyWidth) => Mathf.Clamp(Mathf.RoundToInt(radius / Mathf.Max(.0001f, bodyWidth) * 1.2f), MinRainArrows, MaxRainArrows);
        // Parabolic flight from launch to landing; 0..1.
        public static Vector2 Flight(Vector2 from, Vector2 to, float t)
            => Vector2.Lerp(from, to, t) + Vector2.up * ((to - from).magnitude * ArcOfDistance * 4 * t * (1 - t));
    }

    public sealed partial class WorldRenderer
    {
        const int ArrowVolleyCapacity = 8, ArrowImpactCapacity = 32;
        sealed class ArrowVolley
        {
            public int Tick = -1, Count; public string Source; public float Accepted, Due, Step; public bool Evolved, Recorded; public int AcceptedFrame;
            // Core lands a cast's impacts StepTicks apart (review compositions use 1 tick), so each has its own due time.
            public float DueOf(int index) => Due + index * Step;
            public readonly int[] Ids = new int[ArrowImpactCapacity];
            public readonly Vector2[] Points = new Vector2[ArrowImpactCapacity];
            public readonly float[] Radii = new float[ArrowImpactCapacity], Landed = new float[ArrowImpactCapacity];
            public Vector2 Launch;
        }
        readonly ArrowVolley[] arrowVolleys = CreateVolleys();
        int arrowNext, waveTickRate = 30;
        public int ArrowVolleys { get; private set; }
        public int ArrowRainVolleys { get; private set; }
        public int ArrowImpacts { get; private set; }
        public int ArrowLandedOnTime { get; private set; }
        public System.Action<ArrowVolleyRecord> ArrowVolleySample;

        static ArrowVolley[] CreateVolleys()
        {
            var volleys = new ArrowVolley[ArrowVolleyCapacity];
            for (var i = 0; i < volleys.Length; i++) volleys[i] = new ArrowVolley();
            return volleys;
        }

        void ResetArrows()
        {
            foreach (var volley in arrowVolleys) { volley.Tick = -1; volley.Count = 0; }
            arrowNext = 0; ArrowVolleys = ArrowRainVolleys = ArrowImpacts = ArrowLandedOnTime = 0;
        }

        bool IsArrowSource(string source) => source != null && m1CombatShapes.TryGetValue(source, out var shape) && shape == "arrowrain";

        ArrowVolley VolleyFor(int tick, string source)
        {
            for (var i = 1; i <= arrowVolleys.Length; i++)
            {
                var volley = arrowVolleys[(arrowNext - i + arrowVolleys.Length * 4) % arrowVolleys.Length];
                if (volley.Tick == tick && volley.Source == source) return volley;
            }
            return null;
        }

        // Core announces each delayed impact (review-impact-tell) at the cast and resolves it DelayTicks later
        // (review-impact). Arrows fly or fall so they land on that tick; the targets and damage stay Core's.
        void AcceptArrowEvent(WaveEvent value)
        {
            if (!IsArrowSource(value.Source)) return;
            if (value.Kind == "review-impact-tell")
            {
                var volley = VolleyFor(value.Tick, value.Source);
                if (volley == null)
                {
                    volley = arrowVolleys[arrowNext++ % arrowVolleys.Length];
                    var composition = waveDefinition.Gear[value.Source].Composition;
                    volley.Tick = value.Tick; volley.Source = value.Source; volley.Count = 0; volley.Recorded = false;
                    volley.Accepted = visualTime; volley.AcceptedFrame = PresentedFrames + 1;
                    volley.Due = visualTime + (composition?.DelayTicks ?? 12) / (float)Mathf.Max(1, waveTickRate);
                    volley.Step = (composition?.StepTicks ?? 0) / (float)Mathf.Max(1, waveTickRate);
                    volley.Evolved = WeaponEvolved(value.Source);
                    volley.Launch = PredictedLordPosition ?? RenderedLordPosition;
                    ArrowVolleys++; if (volley.Evolved) ArrowRainVolleys++;
                }
                if (volley.Count == ArrowImpactCapacity) return;
                var i = volley.Count++;
                volley.Ids[i] = value.SubjectId; volley.Points[i] = Point(value.Position);
                volley.Radii[i] = value.Amount / (float)settings.WorldUnitsPerUnityUnit; volley.Landed[i] = -1;
                ArrowImpacts++;
            }
            else if (value.Kind == "review-impact")
            {
                foreach (var volley in arrowVolleys)
                    for (var i = 0; i < volley.Count; i++)
                    {
                        if (volley.Ids[i] != value.SubjectId || volley.Landed[i] >= 0) continue;
                        volley.Landed[i] = visualTime;
                        if (Mathf.Abs(visualTime - volley.DueOf(i)) <= ArrowSpec.Seconds(2)) ArrowLandedOnTime++;
                        impactParticles.Hit(value.Id, volley.Points[i]);
                    }
            }
        }

        // Projectile hits: a brief freeze and a push away from the impact they belong to.
        bool ArrowHitDirection(WaveEvent value, Vector2 origin, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!IsArrowSource(value.Source)) return false;
            var best = float.PositiveInfinity;
            foreach (var volley in arrowVolleys)
                for (var i = 0; i < volley.Count; i++)
                {
                    var away = origin - volley.Points[i]; var distance = away.sqrMagnitude;
                    if (distance < best) { best = distance; direction = away; }
                }
            if (direction.sqrMagnitude < .000001f) direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
            if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
            return true;
        }

        void DrawArrowVolleys()
        {
            if (waveDefinition == null) return;
            var any = false;
            foreach (var volley in arrowVolleys)
                if (volley.Tick >= 0 && visualTime - volley.DueOf(volley.Count) < ArrowSpec.Seconds(ArrowSpec.PlantedFrames) + .2f) { any = true; break; }
            if (!any) return;
            EnsureLightningArt();
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
            var view = CameraWorldViewport;
            var rainBudget = ArrowSpec.MaxRainArrowsTotal;
            foreach (var volley in arrowVolleys)
            {
                if (volley.Tick < 0 || volley.Count == 0) continue;
                var now = visualTime;
                if (now - volley.DueOf(volley.Count) > ArrowSpec.Seconds(ArrowSpec.PlantedFrames) + .2f) continue;
                if (!volley.Recorded && now >= volley.Due - ArrowSpec.Seconds(volley.Evolved ? ArrowSpec.FallFrames : ArrowSpec.FlightFrames)) RecordArrowVolley(volley, view);
                for (var i = 0; i < volley.Count; i++)
                {
                    var landed = volley.Landed[i] >= 0 ? volley.Landed[i] : volley.DueOf(i);
                    var point = volley.Points[i]; var radius = volley.Radii[i];
                    if (volley.Evolved) DrawArrowRain(volley, i, point, radius, landed, w, pixel, view, ref rainBudget);
                    else DrawArrowShot(volley, i, point, landed, w, pixel);
                    DrawArrowLanding(point, radius, landed, w, volley.Evolved);
                }
            }
        }

        void DrawArrowShot(ArrowVolley volley, int index, Vector2 point, float landed, float w, float pixel)
        {
            var flight = ArrowSpec.Seconds(ArrowSpec.FlightFrames);
            var launchAt = landed - flight;
            var age = visualTime - launchAt;
            var from = volley.Launch + (point - volley.Launch).normalized * (.35f * w) + Vector2.up * (.45f * w);
            if (age < 0) return;
            if (age < ArrowSpec.Seconds(ArrowSpec.MuzzleFlashFrames) && index == 0)
            {
                var flash = ArrowSpec.Water; flash.a = .95f;
                Draw(lightningFlash, LightningLayer, from, 0, Vector2.one * w * .9f, 0, 1, flash, applyTween: false);
            }
            if (age >= flight)
            {
                DrawPlantedArrow(point, (point - from).normalized, visualTime - landed, w, pixel);
                return;
            }
            var t = age / flight;
            var at = ArrowSpec.Flight(from, point, t);
            var ahead = ArrowSpec.Flight(from, point, Mathf.Min(1, t + .05f));
            var direction = (ahead - at).sqrMagnitude > .0000001f ? (ahead - at).normalized : (point - from).normalized;
            // Tail of water-blue light along the path behind the arrow.
            var trailStart = ArrowSpec.Flight(from, point, Mathf.Max(0, t - ArrowSpec.TrailOfDistance));
            var trail = at - trailStart;
            if (trail.magnitude > .0001f)
            {
                var glow = ArrowSpec.Water; glow.a = .85f;
                Draw(lightningGlow, LightningLayer, (at + trailStart) * .5f, 0, new Vector2(trail.magnitude, w * .28f), Angle(trail), 1, glow, applyTween: false);
            }
            if (age < ArrowSpec.Seconds(ArrowSpec.LaunchBloomFrames))
            {
                var bloom = ArrowSpec.Silver; bloom.a = 1;
                Draw(lightningFlash, LightningLayer, at, 0, Vector2.one * w * .75f, 0, 1, bloom, applyTween: false);
            }
            DrawArrowBody(at, direction, w, pixel, 1);
        }

        void DrawArrowRain(ArrowVolley volley, int index, Vector2 point, float radius, float landed, float w, float pixel, Rect view, ref int budget)
        {
            // Warning circle from the cast until the arrows land.
            var sinceTell = visualTime - volley.Accepted;
            if (visualTime < landed + ArrowSpec.Seconds(2))
            {
                var grow = Mathf.Clamp01(sinceTell / ArrowSpec.Seconds(ArrowSpec.WarningGrowFrames));
                var pulse = .6f + .4f * Mathf.Sin(sinceTell * 30);
                DrawGlowRing(point, radius * Mathf.Lerp(.35f, 1, grow), Mathf.Max(w * .22f, radius * .1f), ArrowSpec.Water, .95f * pulse);
                var fill = ArrowSpec.Water; fill.a = .18f * grow;
                Draw(lightningFlash, LightningLayer, point, 0, Vector2.one * radius * 2.1f, 0, 1, fill, applyTween: false);
            }
            var fall = ArrowSpec.Seconds(ArrowSpec.FallFrames);
            var age = visualTime - (landed - fall);
            if (age < 0) return;
            var count = ArrowSpec.RainArrows(radius, w);
            for (var k = 0; k < count && budget > 0; k++, budget--)
            {
                var seed = LightningBolt.Hash(volley.Ids[index], 50 + k);
                var angle = (seed & 1023) / 1023f * 360; var distance = Mathf.Sqrt(((seed >> 10) & 1023) / 1023f) * radius * .85f;
                var landing = point + M1Direction(angle) * distance;
                var down = new Vector2(ArrowSpec.RainSlant, -1).normalized;
                if (age >= fall) { DrawPlantedArrow(landing, down, visualTime - landed, w, pixel); continue; }
                var t = age / fall;
                var height = view.height * ArrowSpec.RainDropHeight * (.85f + .3f * (((seed >> 20) & 255) / 255f));
                var at = landing - down * (height * (1 - t));
                var glow = ArrowSpec.Water; glow.a = .7f;
                Draw(lightningGlow, LightningLayer, at - down * (w * .8f), 0, new Vector2(w * 1.6f, w * .22f), Angle(down), 1, glow, applyTween: false);
                DrawArrowBody(at, down, w, pixel, 1);
            }
        }

        // A ring of additive glow segments with a thin bright core: reads on grass and over dark enemies alike.
        void DrawGlowRing(Vector2 centre, float radius, float width, Color color, float alpha)
        {
            if (radius <= 0 || alpha <= 0) return;
            var segments = Mathf.Clamp(Mathf.CeilToInt(radius / Mathf.Max(.0001f, width) * 3), 16, 48);
            for (var pass = 0; pass < 2; pass++)
            {
                var visual = pass == 0 ? lightningGlow : lightningCore;
                var tint = pass == 0 ? color : Color.white; tint.a = alpha * (pass == 0 ? 1 : .7f);
                for (var s = 0; s < segments; s++)
                {
                    var middle = (s + .5f) * 360f / segments;
                    Draw(visual, LightningLayer, centre + M1Direction(middle) * radius, 0, new Vector2(radius * Mathf.PI * 2 / segments * 1.2f, pass == 0 ? width : width * .3f), middle + 90, 1, tint, applyTween: false);
                }
            }
        }

        void DrawArrowLanding(Vector2 point, float radius, float landed, float w, bool rain)
        {
            var age = visualTime - landed;
            if (age < 0 || age > ArrowSpec.Seconds(ArrowSpec.LandFrames)) return;
            var t = age / ArrowSpec.Seconds(ArrowSpec.LandFrames);
            // Debris and dust at the impact: a bright splash that spreads to the Core impact radius and fades.
            var ring = ArrowSpec.Silver; ring.a = (1 - t) * .9f;
            DrawMusterRing(point, radius * Mathf.Lerp(.4f, 1, Mathf.Sqrt(t)), ring, 1);
            var dust = new Color(.86f, .84f, .78f, (1 - t) * .55f);
            Draw(impactDust, GameVisualTokens.WaveTransientCueLayer, point, 0, impactDust.WorldSize * (radius * (rain ? 1.6f : 1.1f) * (.7f + .5f * t) / Mathf.Max(impactDust.WorldSize.x, impactDust.WorldSize.y)), 0, 1, dust, applyTween: false);
            var flash = ArrowSpec.Silver; flash.a = (1 - t);
            Draw(lightningFlash, LightningLayer, point, 0, Vector2.one * Mathf.Max(w * .8f, radius * .7f) * (1 - .4f * t), 0, 1, flash, applyTween: false);
        }

        void DrawPlantedArrow(Vector2 tip, Vector2 direction, float age, float w, float pixel)
        {
            if (age < 0 || age > ArrowSpec.Seconds(ArrowSpec.PlantedFrames)) return;
            var opacity = 1 - Mathf.Clamp01(age / ArrowSpec.Seconds(ArrowSpec.PlantedFrames));
            DrawArrowBody(tip - direction * (w * .25f), direction, w, pixel, opacity);
        }

        // Outlined silver shaft with a bright head, the tip at `tip`.
        void DrawArrowBody(Vector2 tip, Vector2 direction, float w, float pixel, float opacity)
        {
            var length = w * ArrowSpec.ArrowBodyWidths; var angle = Angle(direction);
            var shaft = Mathf.Max(ArrowSpec.ShaftPixels * pixel, .04f * w); var outline = ArrowSpec.OutlinePixels * pixel;
            var head = Mathf.Max(ArrowSpec.HeadPixels * pixel, .2f * w);
            var middle = tip - direction * (length * .5f);
            var ink = GameVisualTokens.Ink; ink.a = opacity;
            var silver = ArrowSpec.Silver; silver.a = opacity;
            var bright = Color.white; bright.a = opacity;
            Draw(lightningSolid, ProjectileLayer, middle, 0, new Vector2(length + outline * 2, shaft + outline * 2), angle, 1, ink, applyTween: false);
            Draw(lightningSolid, ProjectileLayer, middle, 0, new Vector2(length, shaft), angle, 1, silver, applyTween: false);
            var headAt = tip - direction * (head * .5f);
            Draw(lightningSolid, ProjectileLayer, headAt, 0, new Vector2(head + outline * 2, shaft * 2.2f + outline * 2), angle, 1, ink, applyTween: false);
            Draw(lightningSolid, ProjectileLayer, headAt, 0, new Vector2(head, shaft * 2.2f), angle, 1, bright, applyTween: false);
            // Fletching at the back, water blue.
            var fletch = ArrowSpec.Water; fletch.a = opacity;
            Draw(lightningSolid, ProjectileLayer, tip - direction * (length - head * .4f), 0, new Vector2(head * .8f, shaft * 2.4f), angle, 1, fletch, applyTween: false);
        }

        void RecordArrowVolley(ArrowVolley volley, Rect view)
        {
            volley.Recorded = true;
            if (ArrowVolleySample == null) return;
            var record = new ArrowVolleyRecord { Tick = volley.Tick, Impacts = volley.Count, Evolved = volley.Evolved, AcceptedFrame = volley.AcceptedFrame, FirstShownFrame = PresentedFrames,
                DueFrame = PresentedFrames + Mathf.RoundToInt((volley.Due - visualTime) * 60), FirstImpactScreen = new Vector2(-1, -1), LaunchScreen = renderCamera.WorldToScreenPoint(volley.Launch) };
            for (var i = 0; i < volley.Count; i++)
            {
                if (!view.Contains(volley.Points[i])) continue;
                record.OnScreenImpacts++;
                if (record.FirstImpactScreen.x < 0) { record.FirstImpactScreen = renderCamera.WorldToScreenPoint(volley.Points[i]); record.FirstImpactRadiusPixels = volley.Radii[i] / (renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight)); }
            }
            ArrowVolleySample(record);
        }
    }

    public struct ArrowVolleyRecord
    {
        public int Tick, Impacts, OnScreenImpacts, AcceptedFrame, FirstShownFrame, DueFrame;
        public bool Evolved;
        public float FirstImpactRadiusPixels;
        public Vector2 FirstImpactScreen, LaunchScreen;
    }
}
