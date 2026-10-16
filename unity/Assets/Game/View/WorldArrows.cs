using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Projectile spec ("무기 연출 레퍼런스·이론", projectile row) for the canal bow. Frames are 60Hz display frames.
    public static class ArrowSpec
    {
        public const int FlightFrames = 10;          // launched so it lands exactly on the Core impact tick
        // Short shots (Lv1 and max targets often stand beside the hero, 20-30px away) still read as shots: a bowstring flash at
        // the hero's hand for BowstringFlashFrames, at most BowstringHeroHeights across and drawn above the hero's figure, and
        // a thin water trace of the flight path from the hero to the impact that lingers TraceFrames after it lands.
        // Normal-shot splashes are NormalSplashScale larger, and a volley's overlapping splashes merge into one.
        public const int BowstringFlashFrames = 3, TraceFrames = 5;
        public const float NormalSplashScale = 1.5f, TracePixels = 3, BowstringHeroHeights = .25f;
        public static readonly Color Trace = new Color(.78f, .93f, 1f);
        public const int LaunchBloomFrames = 1;      // the arrow's first frame is a round bloom
        public const int LandFrames = 8, PlantedFrames = 14;
        // Rain arrows land 7-11 at a time one step apart; planted they stay only briefly so they do not pile up over the crowd.
        public const int RainPlantedFrames = 6;
        public const int HitHoldFrames = 2;          // projectile hits freeze briefly (1–2) and push away from the impact
        public const float ArcOfDistance = .12f, TrailOfDistance = .3f;
        // Evolution element: warning circles on the ground at every impact, then arrows drive down from the sky onto each one
        // as Core lands it (a cast's impacts land one step apart, so the rain rolls across the circles).
        public const int WarningGrowFrames = 4, FallFrames = 8, MinRainArrows = 6, MaxRainArrows = 12;
        // Each drawn element is one picture (painted role when the manifest has it, else a temporary generated texture):
        // the arrow (head, shaft, fletching), a water tail behind it in flight, a water splash where it lands, and in the
        // evolution short slanted rain streaks behind the falling arrows. Arrow length and tail width grow with the stage
        // (Lv1 < max < evolved). Warning circles are code: merged where they overlap, thin outlines.
        public const float MinArrowHeroHeights = .55f, MaxArrowHeroHeights = .7f, MinTailHeroHeights = .18f, MaxTailHeroHeights = .26f;
        // The painted rain streak fills only its middle quarter across (rows 9-14 of 24), so it is drawn .24 hero heights
        // wide for a ~.06 hero-height line on screen.
        public const float RainStreakHeroHeights = .8f, RainStreakWidthHeroHeights = .24f, WarningLinePixels = 2.5f;
        public const int SplashFrames = 12;
        public const int MidFlightFramesBeforeDue = 5;
        // Per-frame caps (docs/design/m1-weapon-effect-sprites.md): evolved rain streaks and splashes on screen at once.
        public const int MaxRainStreaks = 48, MaxSplashes = 16;
        public const float SplashOfImpactDiameter = .6f;
        public const float SplashFrom = .4f, SplashTo = 1.1f;
        public const float NormalStageShare = .75f;
        public static float ArrowHeroHeights(float stage) => Mathf.Lerp(MinArrowHeroHeights, MaxArrowHeroHeights, stage);
        public static float TailHeroHeights(float stage) => Mathf.Lerp(MinTailHeroHeights, MaxTailHeroHeights, stage);
        public const float RainDropHeight = .55f, RainSlant = .18f;
        public static readonly Color Silver = new Color(.9f, .93f, .97f);
        public static readonly Color Water = new Color(.5f, .82f, 1f);   // silver-white + water sky blue
        public static float Seconds(int frames) => frames / 60f;
        public static int RainArrows(float radius, float bodyWidth) => Mathf.Clamp(Mathf.RoundToInt(radius / Mathf.Max(.0001f, bodyWidth) * 1.2f), MinRainArrows, MaxRainArrows);
        // Parabolic flight from launch to landing; 0..1. Core's nearest targets often stand beside the hero, so the arc has a
        // floor (in body widths) that lofts a short volley up and down where it can be seen.
        public const float MinimumArcBodyWidths = 1.5f;
        public static Vector2 Flight(Vector2 from, Vector2 to, float t, float minimumArc = 0)
            => Vector2.Lerp(from, to, t) + Vector2.up * (Mathf.Max((to - from).magnitude * ArcOfDistance, minimumArc) * 4 * t * (1 - t));
    }

    public sealed partial class WorldRenderer
    {
        const int ArrowVolleyCapacity = 8, ArrowImpactCapacity = 32;
        sealed class ArrowVolley
        {
            public int Tick = -1, Count; public string Source; public float Accepted, Due, Step, Stage; public bool Evolved, Recorded; public int AcceptedFrame;
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
        public int BowstringFlashDraws { get; private set; }   // frames a bowstring flash was drawn (review counter)
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
            arrowNext = 0; ArrowVolleys = ArrowRainVolleys = ArrowImpacts = ArrowLandedOnTime = BowstringFlashDraws = 0;
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
                    volley.Stage = volley.Evolved ? 1 : ArrowSpec.NormalStageShare * GearTier(value.Source);
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
            bowstringRequests.Clear();
            if (waveDefinition == null) return;
            var any = false;
            foreach (var volley in arrowVolleys)
                if (volley.Tick >= 0 && visualTime - volley.DueOf(volley.Count) < ArrowSpec.Seconds(ArrowSpec.PlantedFrames) + .2f) { any = true; break; }
            if (!any) return;
            EnsureLightningArt(); EnsureBowArt();
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
            var view = CameraWorldViewport;
            var rainBudget = ArrowSpec.MaxRainStreaks; splashBudget = ArrowSpec.MaxSplashes;
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
                    if (volley.Evolved) { DrawArrowRain(volley, i, point, radius, landed, w, pixel, view, ref rainBudget); DrawArrowLanding(point, radius, landed, w, 1); continue; }
                    DrawArrowShot(volley, i, point, landed, w, pixel);
                    // One splash per group of overlapping impacts, drawn by the group's first impact at the group's centre.
                    var reach = NormalSplashWidth(radius, w) * .5f;
                    var leader = true;
                    for (var j = 0; j < i && leader; j++) if ((volley.Points[j] - point).sqrMagnitude < reach * reach) leader = false;
                    if (!leader) continue;
                    var centre = Vector2.zero; var members = 0; var spread = 0f;
                    for (var k = 0; k < volley.Count; k++)
                        if ((volley.Points[k] - point).sqrMagnitude < reach * reach) { centre += volley.Points[k]; members++; }
                    centre /= members;
                    for (var k = 0; k < volley.Count; k++)
                        if ((volley.Points[k] - point).sqrMagnitude < reach * reach) spread = Mathf.Max(spread, (volley.Points[k] - centre).magnitude);
                    DrawArrowLanding(centre, radius, landed, w, ArrowSpec.NormalSplashScale, spread);
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
            var length = ArrowLength(volley);
            if (age < ArrowSpec.Seconds(ArrowSpec.BowstringFlashFrames) && index == 0)
            {
                // Bowstring flash at the hero's hand, full strength for all but the last frame, then gone. It is placed after
                // the hero is drawn this frame (PlaceBowstringFlashes); `from` stands in when the hero has no live rig.
                BowstringFlashDraws++;
                bowstringRequests.Add((from, Mathf.Clamp01((ArrowSpec.Seconds(ArrowSpec.BowstringFlashFrames) - age) / ArrowSpec.Seconds(1))));
            }
            DrawArrowTrace(from, point, Mathf.Min(1, age / flight), visualTime - landed, w, pixel);
            if (age >= flight)
            {
                DrawPlantedArrow(point, (point - from).normalized, visualTime - landed, length);
                return;
            }
            var t = age / flight;
            var arc = ArrowSpec.MinimumArcBodyWidths * w;
            var at = ArrowSpec.Flight(from, point, t, arc);
            var ahead = ArrowSpec.Flight(from, point, Mathf.Min(1, t + .05f), arc);
            var direction = (ahead - at).sqrMagnitude > .0000001f ? (ahead - at).normalized : (point - from).normalized;
            // Water tail along the path behind the arrow's fletching.
            var tail = at - direction * (length * .85f);
            var trailStart = ArrowSpec.Flight(from, point, Mathf.Max(0, t - ArrowSpec.TrailOfDistance), arc);
            var trail = tail - trailStart;
            if (Vector2.Dot(trail, direction) > .0001f)
                Draw(bowStreak, ProjectileLayer, tail, 0, new Vector2(trail.magnitude, HeroHeight * ArrowSpec.TailHeroHeights(volley.Stage)), Angle(trail), 1, Color.white, applyTween: false);
            if (age < ArrowSpec.Seconds(ArrowSpec.LaunchBloomFrames))
            {
                var bloom = ArrowSpec.Silver; bloom.a = 1;
                Draw(lightningFlash, LightningLayer, at, 0, Vector2.one * w * .75f, 0, 1, bloom, applyTween: false);
            }
            DrawArrowPicture(at, direction, length, 1, OverHeroThinLayer);
        }

        void DrawArrowRain(ArrowVolley volley, int index, Vector2 point, float radius, float landed, float w, float pixel, Rect view, ref int budget)
        {
            // Warning circle from the cast until the arrows land: thin outline only, merged with the volley's other circles.
            var sinceTell = visualTime - volley.Accepted;
            if (visualTime < landed + ArrowSpec.Seconds(2))
            {
                var grow = Mathf.Lerp(.35f, 1, Mathf.Clamp01(sinceTell / ArrowSpec.Seconds(ArrowSpec.WarningGrowFrames)));
                DrawMergedWarning(volley, index, point, radius * grow, grow, pixel);
            }
            var fall = ArrowSpec.Seconds(ArrowSpec.FallFrames);
            var age = visualTime - (landed - fall);
            if (age < 0) return;
            var count = ArrowSpec.RainArrows(radius, w);
            var height = HeroHeight;
            for (var k = 0; k < count && budget > 0; k++, budget--)
            {
                var seed = LightningBolt.Hash(volley.Ids[index], 50 + k);
                var angle = (seed & 1023) / 1023f * 360; var distance = Mathf.Sqrt(((seed >> 10) & 1023) / 1023f) * radius * .85f;
                var landing = point + M1Direction(angle) * distance;
                var down = new Vector2(ArrowSpec.RainSlant, -1).normalized;
                if (age >= fall) { DrawPlantedArrow(landing, down, visualTime - landed, ArrowLength(volley), ArrowSpec.RainPlantedFrames); continue; }
                var t = age / fall;
                var drop = view.height * ArrowSpec.RainDropHeight * (.85f + .3f * (((seed >> 20) & 255) / 255f));
                var at = landing - down * (drop * (1 - t));
                // Short slanted rain streak behind the falling arrow (capped over enemies like every wide effect).
                var length = ArrowLength(volley);
                var streakAt = at - down * (length * .85f);
                Draw(bowRainStreak, ProjectileLayer, streakAt, 0, new Vector2(height * ArrowSpec.RainStreakHeroHeights, height * ArrowSpec.RainStreakWidthHeroHeights), Angle(down), 1, Color.white, applyTween: false);
                DrawArrowPicture(at, down, length, 1);
            }
        }

        // The outline of the union of a volley's warning circles: each circle's arc is drawn only where no other circle of
        // the same volley covers it, so overlapping circles read as one region.
        void DrawMergedWarning(ArrowVolley volley, int index, Vector2 centre, float radius, float grow, float pixel)
        {
            const int segments = 40;
            var line = ArrowSpec.Water; line.a = .9f;
            for (var s = 0; s < segments; s++)
            {
                var middle = (s + .5f) * 360f / segments;
                var at = centre + M1Direction(middle) * radius;
                var covered = false;
                for (var o = 0; o < volley.Count && !covered; o++)
                    if (o != index && (at - volley.Points[o]).sqrMagnitude < (volley.Radii[o] * grow) * (volley.Radii[o] * grow) * .995f) covered = true;
                if (covered) continue;
                Draw(lightningSolid, ProjectileLayer, at, 0, new Vector2(radius * Mathf.PI * 2 / segments * 1.08f, ArrowSpec.WarningLinePixels * pixel), middle + 90, 1, line, applyTween: false);
            }
        }

        // Water splash where an arrow lands: the splash picture grows from SplashFrom to SplashTo of the Core impact
        // diameter over SplashFrames and fades.
        float NormalSplashWidth(float radius, float w) => Mathf.Max(w * 1.2f, radius * 2 * ArrowSpec.SplashOfImpactDiameter) * ArrowSpec.SplashTo * ArrowSpec.NormalSplashScale;

        // scale: NormalSplashScale for normal shots; spread: how far a merged group's impacts lie from its centre.
        void DrawArrowLanding(Vector2 point, float radius, float landed, float w, float scale, float spread = 0)
        {
            var age = visualTime - landed;
            if (age < 0 || age > ArrowSpec.Seconds(ArrowSpec.SplashFrames) || splashBudget <= 0) return;
            splashBudget--;
            var t = age / ArrowSpec.Seconds(ArrowSpec.SplashFrames);
            var width = (Mathf.Max(w * 1.2f, radius * 2 * ArrowSpec.SplashOfImpactDiameter) * scale + spread * 2) * Mathf.Lerp(ArrowSpec.SplashFrom, ArrowSpec.SplashTo, Mathf.Sqrt(t));
            var color = Color.white; color.a = 1 - t * t;
            Draw(bowSplash, ProjectileLayer, point, 0, new Vector2(width, width * bowSplash.WorldSize.y / bowSplash.WorldSize.x), 0, 1, color, applyTween: false);
        }

        // The hero's hand (the live rig's sword-grip joint), or `fallback` when the hero is not drawn by the live rig.
        Vector2 HeroHand(Vector2 fallback) => heroLiveRig != null && heroLiveRig.TryHand(out var hand) ? hand : fallback;

        // The hero's live rig draws sprite renderers sorted over every instanced effect layer, so the bowstring flash is drawn
        // by sprite renderers sorted above it: a water-blue bloom (BowstringHeroHeights across) with a white core.
        const int BowstringSortingOrder = 1000;
        readonly System.Collections.Generic.List<(Vector2 fallback, float fade)> bowstringRequests = new System.Collections.Generic.List<(Vector2, float)>();
        readonly System.Collections.Generic.List<SpriteRenderer> bowstringRenderers = new System.Collections.Generic.List<SpriteRenderer>();
        GameObject bowstringRoot; Sprite bowstringSprite; Material bowstringMaterial;
        static readonly float[] BowstringPieces = { 1, .45f, .3f };

        void PlaceBowstringFlashes()
        {
            var used = 0;
            if (bowstringRequests.Count > 0 && EnsureBowstringFlash())
                foreach (var (fallback, fade) in bowstringRequests)
                {
                    var hand = HeroHand(fallback);
                    for (var p = 0; p < BowstringPieces.Length; p++)
                    {
                        if (used == bowstringRenderers.Count)
                        {
                            var piece = new GameObject("Bowstring flash");
                            piece.transform.SetParent(bowstringRoot.transform, false);
                            var created = piece.AddComponent<SpriteRenderer>();
                            created.sprite = bowstringSprite; created.sharedMaterial = bowstringMaterial; created.sortingOrder = BowstringSortingOrder + p;
                            bowstringRenderers.Add(created);
                        }
                        var renderer = bowstringRenderers[used++];
                        var colour = p == 0 ? ArrowSpec.Water : Color.white; colour.a = fade;
                        renderer.color = colour; renderer.sortingOrder = BowstringSortingOrder + p;
                        renderer.transform.position = new Vector3(hand.x, hand.y, 0);
                        renderer.transform.localScale = Vector3.one * HeroHeight * ArrowSpec.BowstringHeroHeights * BowstringPieces[p];
                        renderer.enabled = true;
                    }
                }
            for (var i = used; i < bowstringRenderers.Count; i++) bowstringRenderers[i].enabled = false;
            bowstringRequests.Clear();
        }

        bool EnsureBowstringFlash()
        {
            if (bowstringRoot != null) return true;
            var template = Resources.Load<Material>("HeroMotion/live-material");
            if (template == null || lightningFlash == null) return false;
            var texture = lightningFlash.Texture; var uv = lightningFlash.UvRects[0];
            var rect = new Rect(uv.x * texture.width, uv.y * texture.height, uv.width * texture.width, uv.height * texture.height);
            bowstringSprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), rect.width);   // one world unit across
            bowstringMaterial = new Material(template) { name = "Bowstring flash", renderQueue = 3000 + GameVisualTokens.LordLayer + 3, hideFlags = HideFlags.DontSave };
            bowstringRoot = new GameObject("Bowstring flashes");
            return true;
        }

        void DisposeBowstringFlashes()
        {
            if (bowstringRoot != null) Destroy(bowstringRoot);
            if (bowstringSprite != null) Destroy(bowstringSprite);
            if (bowstringMaterial != null) Destroy(bowstringMaterial);
            bowstringRenderers.Clear(); bowstringRequests.Clear(); bowstringRoot = null;
        }

        // Thin water line along the flight path from the hand to where the arrow is (shown = 0..1 of the path), lingering
        // TraceFrames after landing. The line is a thin element (no P5 cap); a faint wide bloom under it is capped.
        void DrawArrowTrace(Vector2 from, Vector2 point, float shown, float sinceLanding, float w, float pixel)
        {
            var fade = sinceLanding <= 0 ? 1 : 1 - sinceLanding / ArrowSpec.Seconds(ArrowSpec.TraceFrames);
            if (fade <= 0 || shown <= 0) return;
            const int segments = 10;
            var arc = ArrowSpec.MinimumArcBodyWidths * w;
            var previous = ArrowSpec.Flight(from, point, 0, arc);
            for (var s = 1; s <= segments; s++)
            {
                var t = shown * s / segments;
                var next = ArrowSpec.Flight(from, point, t, arc);
                var span = next - previous;
                if (span.sqrMagnitude > 1e-8f)
                {
                    var line = ArrowSpec.Trace; line.a = .9f * fade;
                    Draw(lightningCore, ProjectileLayer, (previous + next) * .5f, 0, new Vector2(span.magnitude * 1.15f, ArrowSpec.TracePixels * pixel * 1.6f), Angle(span), 1, line, applyTween: false);
                    var bloom = ArrowSpec.Water; bloom.a = .3f * fade;
                    Draw(lightningGlow, LightningLayer, (previous + next) * .5f, 0, new Vector2(span.magnitude * 1.15f, ArrowSpec.TracePixels * pixel * 4), Angle(span), 1, bloom, applyTween: false);
                }
                previous = next;
            }
        }

        void DrawPlantedArrow(Vector2 tip, Vector2 direction, float age, float length, int frames = ArrowSpec.PlantedFrames)
        {
            if (age < 0 || age > ArrowSpec.Seconds(frames)) return;
            var opacity = 1 - Mathf.Clamp01(age / ArrowSpec.Seconds(frames));
            DrawArrowPicture(tip - direction * (length * .2f), direction, length, opacity);
        }

        float ArrowLength(ArrowVolley volley) => HeroHeight * ArrowSpec.ArrowHeroHeights(volley.Stage);
        int splashBudget;

        // The arrow picture, its point at `tip`: a thin element on its own layer, exempt from the P5 cap over enemies.
        void DrawArrowPicture(Vector2 tip, Vector2 direction, float length, float opacity, int layer = ThinEffectLayer)
        {
            var color = Color.white; color.a = opacity;
            Draw(bowArrow, layer, tip, 0, new Vector2(length, length * bowArrow.WorldSize.y / bowArrow.WorldSize.x), Angle(direction), 1, color, applyTween: false);
        }

        // Painted roles (docs/design/m1-weapon-effect-sprites.md, canal bow); temporary generated pictures until they are in.
        const string ArrowRole = "fx.canal_bow.arrow", StreakRole = "fx.canal_bow.streak", SplashRole = "fx.canal_bow.splash", RainStreakRole = "fx.canal_bow.rain_streak";
        ArtVisual bowArrow, bowStreak, bowSplash, bowRainStreak;

        void EnsureBowArt()
        {
            if (bowArrow != null) return;
            bowArrow = EffectArt(ArrowRole, () => TemporaryArrow());
            bowStreak = EffectArt(StreakRole, () => TemporaryStreak("Temporary bow streak", 256, 32, StreakRole));
            bowSplash = EffectArt(SplashRole, () => TemporarySplash());
            bowRainStreak = art != null && art.TryResolveRole(RainStreakRole, out var rain) ? rain : bowStreak;
        }

        // Arrow pointing right: silver head (point = pivot, right), light wood shaft, two water-blue vanes, 3px ink outline.
        ArtVisual TemporaryArrow()
        {
            const int width = 192, height = 40; const float cy = height * .5f;
            int Part(float x, float y)
            {
                var dy = Mathf.Abs(y - cy);
                if (x >= 144 && x <= 184 && dy <= (184 - x) * .4f) return 1;                         // head
                if (x >= 30 && x < 146 && dy <= 3.5f) return 2;                                          // shaft
                if (x >= 10 && x < 48 && dy >= 3 && dy <= Mathf.Lerp(5, 15, (48 - x) / 38f)) return 3;   // vanes
                return 0;
            }
            var parts = new int[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) parts[y * width + x] = Part(x + .5f, y + .5f);
            int At(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? 0 : parts[y * width + x];
            var ink = GameVisualTokens.Ink; ink.a = 1;
            var texture = PaintTexture("Temporary bow arrow", width, height, (x, y) =>
            {
                var part = At(x, y);
                if (part == 0)
                {
                    for (var dy = -3; dy <= 3; dy++) for (var dx = -3; dx <= 3; dx++)
                        if (dx * dx + dy * dy <= 9 && At(x + dx, y + dy) != 0) return ink;
                    return new Color(0, 0, 0, 0);
                }
                var upper = y + .5f > cy;   // lit from the upper left
                switch (part)
                {
                    case 1: return upper ? new Color(.95f, .96f, .98f) : new Color(.7f, .74f, .8f);
                    case 2: return upper ? new Color(.86f, .72f, .5f) : new Color(.66f, .52f, .33f);
                    default: return upper ? new Color(.62f, .86f, 1f) : new Color(.38f, .66f, .88f);
                }
            });
            return new ArtVisual(ArrowRole, texture, new Vector2(width, height) / 384f, new Vector2(.96f, .5f));
        }

        // Horizontal band: a white core at the right end fading left to water blue and clear; soft top and bottom edges.
        ArtVisual TemporaryStreak(string name, int width, int height, string role)
        {
            var texture = PaintTexture(name, width, height, (x, y) =>
            {
                var u = (x + .5f) / width; var v = Mathf.Abs((y + .5f) / height - .5f) * 2;
                var across = Mathf.Clamp01(1 - v * v);
                var color = Color.Lerp(ArrowSpec.Water, Color.white, Mathf.SmoothStep(.55f, 1, u) * (1 - v));
                color.a = Mathf.Clamp01(Mathf.Pow(u, .8f) * 1.15f) * across;
                return color;
            });
            return new ArtVisual(role, texture, new Vector2(width, height) / 384f, new Vector2(1, .5f));
        }

        // Water crown rising from the ground: droplets on outward arcs, water blue and white, thin ink rims.
        ArtVisual TemporarySplash()
        {
            const int width = 192, height = 144;
            var drops = new System.Collections.Generic.List<(Vector2 c, float r, bool white)>();
            for (var k = 0; k < 11; k++)
            {
                var a = Mathf.Lerp(20, 160, k / 10f) * Mathf.Deg2Rad; var seed = LightningBolt.Hash(266, 200 + k);
                var reach = Mathf.Lerp(.55f, 1, ((seed & 255) / 255f));
                drops.Add((new Vector2(96 + Mathf.Cos(a) * 80 * reach, 12 + Mathf.Sin(a) * 118 * reach), Mathf.Lerp(5, 9, ((seed >> 8) & 255) / 255f), (k & 1) == 0));
                drops.Add((new Vector2(96 + Mathf.Cos(a) * 46 * reach, 12 + Mathf.Sin(a) * 64 * reach), 4, (k & 1) == 1));
            }
            var ink = GameVisualTokens.Ink; ink.a = 1;
            var texture = PaintTexture("Temporary bow splash", width, height, (x, y) =>
            {
                var p = new Vector2(x + .5f, y + .5f);
                foreach (var (c, r, white) in drops)
                {
                    var d = (p - c).magnitude;
                    if (d <= r) return white ? new Color(.97f, .99f, 1f) : ArrowSpec.Water;
                    if (d <= r + 1.5f) return ink;
                }
                return new Color(0, 0, 0, 0);
            });
            return new ArtVisual(SplashRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .08f));
        }

        void RecordArrowVolley(ArrowVolley volley, Rect view)
        {
            volley.Recorded = true;
            if (ArrowVolleySample == null) return;
            var record = new ArrowVolleyRecord { Tick = volley.Tick, Impacts = volley.Count, Evolved = volley.Evolved, AcceptedFrame = volley.AcceptedFrame, FirstShownFrame = PresentedFrames,
                DueFrame = PresentedFrames + Mathf.RoundToInt((volley.Due - visualTime) * 60), FirstImpactScreen = new Vector2(-1, -1), MidFlightScreen = new Vector2(-1, -1), HandScreen = new Vector2(-1, -1), LaunchScreen = renderCamera.WorldToScreenPoint(volley.Launch) };
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
            record.ArrowLengthPixels = ArrowLength(volley) / pixelsToWorld; record.HeroHeightPixels = HeroHeight / pixelsToWorld; record.Stage = volley.Stage;
            for (var i = 0; i < volley.Count; i++)
            {
                if (!view.Contains(volley.Points[i])) continue;
                record.OnScreenImpacts++;
                if (record.FirstImpactScreen.x >= 0) continue;
                record.FirstImpactScreen = renderCamera.WorldToScreenPoint(volley.Points[i]); record.FirstImpactRadiusPixels = volley.Radii[i] / (renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight));
                if (volley.Evolved) continue;
                // Where this arrow's point is MidFlightFramesBeforeDue frames before it lands (for the arrow contrast check).
                var from = volley.Launch + (volley.Points[i] - volley.Launch).normalized * (.35f * w) + Vector2.up * (.45f * w);
                record.HandScreen = renderCamera.WorldToScreenPoint(HeroHand(from));   // the bowstring flash
                var t = 1 - ArrowSpec.MidFlightFramesBeforeDue / (float)ArrowSpec.FlightFrames;
                record.MidFlightScreen = renderCamera.WorldToScreenPoint(ArrowSpec.Flight(from, volley.Points[i], t, ArrowSpec.MinimumArcBodyWidths * w));
                record.MidFlightImpactOffset = i * Mathf.RoundToInt(volley.Step * 60);   // frames after the volley's first due
            }
            ArrowVolleySample(record);
        }
    }

    public struct ArrowVolleyRecord
    {
        public int Tick, Impacts, OnScreenImpacts, AcceptedFrame, FirstShownFrame, DueFrame;
        public bool Evolved;
        public float FirstImpactRadiusPixels, ArrowLengthPixels, HeroHeightPixels, Stage;
        public int MidFlightImpactOffset;
        public Vector2 FirstImpactScreen, LaunchScreen, MidFlightScreen, HandScreen;
    }
}
