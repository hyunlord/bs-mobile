using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Furrow pike spec (docs/design/m1-weapon-effect-sprites.md, ditch pike). Frames are 60Hz display frames. Core announces a
    // cast's impacts along a forward line at the cast (review-impact-tell) and lands them DelayTicks later, StepTicks apart
    // outward (review-impact); targets, damage and timing stay Core's.
    public static class PikeSpec
    {
        // The furrow opens from the hero along the line over FurrowOpenFrames at the cast and fades FurrowFadeFrames after the
        // thrust retracts. The pike's point leaves the hero ThrustLeadFrames before the first impact lands, reaches each
        // impact on its Core tick, holds HoldFrames at the last and retracts over RetractFrames; the thrust streak runs from
        // the hero to the point and fades with the retract. Each landed impact flashes at the point for TipFlashFrames.
        public const int FurrowOpenFrames = 6, FurrowFadeFrames = 36, ThrustLeadFrames = 4, HoldFrames = 3, RetractFrames = 6, TipFlashFrames = 4;
        // Evolution element: pikes erupt from the furrow at every impact as it lands (a palisade along the line), fanned
        // outward, rising EruptRiseFrames, held EruptHoldFrames and sinking EruptSinkFrames.
        public const int EruptRiseFrames = 4, EruptHoldFrames = 10, EruptSinkFrames = 6, EruptPikesPerImpact = 3, MaxEruptPikes = 30;
        public const float EruptFanDegrees = 25, EruptLengthOfPike = .6f;
        public const float PikeHeroHeights = 1.3f, TipFlashHeroHeights = .5f, StreakHeroHeights = .2f, EvolvedStreakHeroHeights = .3f;
        public const float MinFurrowHeroHeights = .35f, MaxFurrowHeroHeights = .5f, EvolvedFurrowHeroHeights = .7f, NormalStageShare = .75f;
        public const int HitHoldFrames = 2;   // struck enemies freeze briefly and are pushed along the thrust line
        public const float FurrowTileU0 = 39f / 512, FurrowTileU1 = 473f / 512;
        public static float Seconds(int frames) => frames / 60f;
        public static float FurrowHeroHeights(float stage, bool evolved)
            => evolved ? EvolvedFurrowHeroHeights : Mathf.Lerp(MinFurrowHeroHeights, MaxFurrowHeroHeights, Mathf.Clamp01(stage / NormalStageShare));
        // Where the pike's point is, as an impact index: -1 at the hero, i on impact i's Core tick, eased between.
        public static float Reach(float time, float firstDue, float step, int count)
        {
            var lead = Seconds(ThrustLeadFrames);
            if (time <= firstDue - lead) return -1;
            if (time < firstDue) { var t = (time - (firstDue - lead)) / lead; return -1 + t * (2 - t); }
            if (count <= 1 || step <= 0) return 0;
            return Mathf.Min(count - 1, (time - firstDue) / step);
        }
        // The point's share of its reach while it holds at the last impact (1) and retracts (to 0).
        public static float Extension(float time, float lastDue)
        {
            var hold = lastDue + Seconds(HoldFrames);
            if (time <= hold) return 1;
            return Mathf.Clamp01(1 - (time - hold) / Seconds(RetractFrames));
        }
        // Height share of an erupted pike over its life since its impact landed.
        public static float Erupted(float age)
        {
            if (age < 0) return 0;
            if (age < Seconds(EruptRiseFrames)) return age / Seconds(EruptRiseFrames);
            age -= Seconds(EruptRiseFrames);
            if (age < Seconds(EruptHoldFrames)) return 1;
            age -= Seconds(EruptHoldFrames);
            return Mathf.Clamp01(1 - age / Seconds(EruptSinkFrames));
        }
    }

    public sealed partial class WorldRenderer
    {
        const int PikeCastCapacity = 4, PikeImpactCapacity = 16;
        sealed class PikeCast
        {
            public int Tick = -1, Count, AcceptedFrame; public string Source; public float Accepted, Due, Step, Stage; public bool Evolved, Recorded;
            public float DueOf(int index) => Due + index * Step;
            public float LastDue => DueOf(Mathf.Max(0, Count - 1));
            public readonly int[] Ids = new int[PikeImpactCapacity];
            public readonly Vector2[] Points = new Vector2[PikeImpactCapacity];
            public readonly float[] Landed = new float[PikeImpactCapacity];
            public Vector2 Launch;
        }
        readonly PikeCast[] pikeCasts = { new PikeCast(), new PikeCast(), new PikeCast(), new PikeCast() };
        int pikeNext;
        ArtVisual pikePicture, pikeThrust, pikeTipFlash, pikeFurrow;
        public int PikeCasts { get; private set; }
        public int PikeImpacts { get; private set; }
        public int PikeLandedOnTime { get; private set; }
        public int PikeEruptions { get; private set; }   // erupted pike draws summed over frames (evolution element)
        public System.Action<PikeCastRecord> PikeCastSample;

        void ResetPikes()
        {
            foreach (var cast in pikeCasts) { cast.Tick = -1; cast.Count = 0; }
            pikeNext = 0; PikeCasts = PikeImpacts = PikeLandedOnTime = PikeEruptions = 0;
        }

        bool IsPikeSource(string source) => source != null && m1CombatShapes.TryGetValue(source, out var shape) && shape == "spears";

        void AcceptPikeEvent(WaveEvent value)
        {
            if (!IsPikeSource(value.Source)) return;
            if (value.Kind == "review-impact-tell")
            {
                PikeCast cast = null;
                foreach (var candidate in pikeCasts) if (candidate.Tick == value.Tick && candidate.Source == value.Source) cast = candidate;
                if (cast == null)
                {
                    cast = pikeCasts[pikeNext++ % pikeCasts.Length];
                    var composition = waveDefinition.Gear[value.Source].Composition;
                    cast.Tick = value.Tick; cast.Source = value.Source; cast.Count = 0; cast.Recorded = false;
                    cast.Accepted = visualTime; cast.AcceptedFrame = PresentedFrames + 1;
                    cast.Due = visualTime + (composition?.DelayTicks ?? 15) / (float)Mathf.Max(1, waveTickRate);
                    cast.Step = (composition?.StepTicks ?? 0) / (float)Mathf.Max(1, waveTickRate);
                    cast.Evolved = WeaponEvolved(value.Source);
                    cast.Stage = cast.Evolved ? 1 : PikeSpec.NormalStageShare * GearTier(value.Source);
                    cast.Launch = PredictedLordPosition ?? RenderedLordPosition;
                    PikeCasts++;
                }
                if (cast.Count == PikeImpactCapacity) return;
                var i = cast.Count++;
                cast.Ids[i] = value.SubjectId; cast.Points[i] = Point(value.Position); cast.Landed[i] = -1;
                PikeImpacts++;
            }
            else if (value.Kind == "review-impact")
            {
                foreach (var cast in pikeCasts)
                    for (var i = 0; i < cast.Count; i++)
                    {
                        if (cast.Ids[i] != value.SubjectId || cast.Landed[i] >= 0) continue;
                        cast.Landed[i] = visualTime;
                        if (Mathf.Abs(visualTime - cast.DueOf(i)) <= PikeSpec.Seconds(2)) PikeLandedOnTime++;
                    }
            }
        }

        // Pike hits: a brief freeze and a push along the thrust line of the cast whose line passes nearest.
        bool PikeHitDirection(WaveEvent value, Vector2 origin, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!IsPikeSource(value.Source)) return false;
            var best = float.PositiveInfinity;
            foreach (var cast in pikeCasts)
            {
                if (cast.Tick < 0 || cast.Count == 0) continue;
                var line = cast.Points[cast.Count - 1] - cast.Launch;
                if (line.sqrMagnitude < 1e-8f) continue;
                var t = Mathf.Clamp01(Vector2.Dot(origin - cast.Launch, line) / line.sqrMagnitude);
                var distance = (origin - (cast.Launch + line * t)).sqrMagnitude;
                if (distance < best) { best = distance; direction = line; }
            }
            if (direction.sqrMagnitude < 1e-8f) direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
            if (direction.sqrMagnitude < 1e-8f) direction = Vector2.up;
            direction.Normalize();
            return true;
        }

        Vector2 PikeLine(PikeCast cast, float reach)
        {
            if (reach < 0) return Vector2.Lerp(cast.Launch, cast.Points[0], reach + 1);
            var i = Mathf.Min(cast.Count - 1, Mathf.FloorToInt(reach));
            return i >= cast.Count - 1 ? cast.Points[cast.Count - 1] : Vector2.Lerp(cast.Points[i], cast.Points[i + 1], reach - i);
        }

        void DrawPikeCasts()
        {
            if (waveDefinition == null) return;
            EnsurePikeArt();
            var view = CameraWorldViewport;
            var eruptBudget = PikeSpec.MaxEruptPikes;
            foreach (var cast in pikeCasts)
            {
                if (cast.Tick < 0 || cast.Count == 0) continue;
                var now = visualTime;
                var retracted = cast.LastDue + PikeSpec.Seconds(PikeSpec.HoldFrames + PikeSpec.RetractFrames);
                var eruptEnd = cast.LastDue + PikeSpec.Seconds(PikeSpec.EruptRiseFrames + PikeSpec.EruptHoldFrames + PikeSpec.EruptSinkFrames);
                var end = Mathf.Max(retracted, cast.Evolved ? eruptEnd : 0) + PikeSpec.Seconds(PikeSpec.FurrowFadeFrames);
                if (now > end) continue;
                // Recorded as its last impact lands, at the last impact then on screen (the camera follows the hero meanwhile).
                if (!cast.Recorded && now >= cast.LastDue) RecordPikeCast(cast, view);
                var far = cast.Points[cast.Count - 1];
                var line = far - cast.Launch; var length = line.magnitude;
                if (length < 1e-5f) continue;
                var direction = line / length; var angle = Angle(line);
                // Furrow: opens from the hero outward, fades once the thrust (and the evolved palisade) is over.
                var open = Mathf.Clamp01((now - cast.Accepted) / PikeSpec.Seconds(PikeSpec.FurrowOpenFrames));
                var fade = 1 - Mathf.Clamp01((now - Mathf.Max(retracted, cast.Evolved ? eruptEnd : 0)) / PikeSpec.Seconds(PikeSpec.FurrowFadeFrames));
                DrawPikeFurrow(cast.Launch, direction, length * open, HeroHeight * PikeSpec.FurrowHeroHeights(cast.Stage, cast.Evolved), fade);
                // Thrust: the point travels impact to impact on their Core ticks; the streak runs from the hero to it.
                var reach = PikeSpec.Reach(now, cast.Due, cast.Step, cast.Count);
                var extension = PikeSpec.Extension(now, cast.LastDue);
                if (reach > -1 && extension > 0)
                {
                    var tip = cast.Launch + (PikeLine(cast, reach) - cast.Launch) * extension;
                    var span = (tip - cast.Launch).magnitude;
                    if (span > 1e-4f)
                    {
                        var streak = Color.white; streak.a = .9f * extension;
                        var width = HeroHeight * (cast.Evolved ? PikeSpec.EvolvedStreakHeroHeights : PikeSpec.StreakHeroHeights);
                        Draw(pikeThrust, ProjectileLayer, tip, 0, new Vector2(span, width), angle, 1, streak, applyTween: false);
                    }
                    var pikeLength = HeroHeight * PikeSpec.PikeHeroHeights;
                    Draw(pikePicture, ThinEffectLayer, tip, 0, new Vector2(pikeLength, pikeLength * pikePicture.WorldSize.y / pikePicture.WorldSize.x), angle, 1, Color.white, applyTween: false);
                }
                for (var i = 0; i < cast.Count; i++)
                {
                    var landed = cast.Landed[i] >= 0 ? cast.Landed[i] : cast.DueOf(i);
                    var age = now - landed;
                    if (age >= 0 && age < PikeSpec.Seconds(PikeSpec.TipFlashFrames))
                    {
                        var t = age / PikeSpec.Seconds(PikeSpec.TipFlashFrames);
                        var flash = Color.white; flash.a = 1 - t * t;
                        Draw(pikeTipFlash, ProjectileLayer, cast.Points[i], 0, Vector2.one * HeroHeight * PikeSpec.TipFlashHeroHeights * Mathf.Lerp(.6f, 1.1f, t), 45 * t, 1, flash, applyTween: false);
                    }
                    if (!cast.Evolved) continue;
                    var risen = PikeSpec.Erupted(age);
                    if (risen <= 0) continue;
                    var eruptLength = HeroHeight * PikeSpec.PikeHeroHeights * PikeSpec.EruptLengthOfPike;
                    for (var k = 0; k < PikeSpec.EruptPikesPerImpact && eruptBudget > 0; k++)
                    {
                        // Fanned up out of the furrow: straight up, then leaning to each side of the line.
                        var lean = (k - (PikeSpec.EruptPikesPerImpact - 1) * .5f) * PikeSpec.EruptFanDegrees;
                        var up = 90 + lean;
                        var shown = eruptLength * risen;
                        var tipAt = cast.Points[i] + M1Direction(up) * shown;
                        Draw(pikePicture, ThinEffectLayer, tipAt, 0, new Vector2(eruptLength, eruptLength * pikePicture.WorldSize.y / pikePicture.WorldSize.x), up, 1, Color.white, applyTween: false);
                        eruptBudget--; PikeEruptions++;
                    }
                }
            }
        }

        // The furrow picture tiled along the line from `from`, the last tile cut. Tiles use the picture's content columns
        // (FurrowTileU0..U1: Astra's contentRect, its transparent side margins left out) so they join without gaps.
        void DrawPikeFurrow(Vector2 from, Vector2 direction, float length, float width, float alpha)
        {
            if (length <= 0 || alpha <= 0) return;
            var uv = pikeFurrow.UvRects[0];
            var share = PikeSpec.FurrowTileU1 - PikeSpec.FurrowTileU0;
            var tile = width * pikeFurrow.WorldSize.x / pikeFurrow.WorldSize.y * share;
            var angle = Angle(direction);
            var colour = Color.white; colour.a = alpha;
            for (var start = 0f; start < length; start += tile)
            {
                var piece = Mathf.Min(tile, length - start);
                var slice = new Rect(uv.x + uv.width * PikeSpec.FurrowTileU0, uv.y, uv.width * share * piece / tile, uv.height);
                Draw(pikeFurrow, GameVisualTokens.AttackLayer, from + direction * start, 0, new Vector2(piece, width), angle, 1, colour, uv: slice, applyTween: false);
            }
        }

        const string PikeRole = "fx.ditch_pike.pike", PikeThrustRole = "fx.ditch_pike.thrust", PikeTipFlashRole = "fx.ditch_pike.tip_flash", PikeFurrowRole = "fx.ditch_pike.furrow";

        void EnsurePikeArt()
        {
            if (pikePicture != null) return;
            pikePicture = EffectArt(PikeRole, TemporaryPike);
            pikeThrust = EffectArt(PikeThrustRole, TemporaryPikeThrust);
            pikeTipFlash = EffectArt(PikeTipFlashRole, TemporaryPikeTipFlash);
            pikeFurrow = EffectArt(PikeFurrowRole, TemporaryPikeFurrow);
        }

        static readonly Color PikeInk = new Color32(0x29, 0x28, 0x22, 255);

        // Temporary pike (384x48, pointing right, point = pivot): an iron leaf head, a binding at the socket, an ash shaft.
        ArtVisual TemporaryPike()
        {
            const int width = 384, height = 48; const float cy = height * .5f;
            var texture = PaintTexture("Temporary pike", width, height, (x, y) =>
            {
                var px = x + .5f; var py = y + .5f - cy;
                // Head: a leaf from x 290 to the point at 372, widest (11px half) at 318.
                if (px >= 290 && px <= 372)
                {
                    var t = px < 318 ? (px - 290) / 28f : 1 - (px - 318) / 54f;
                    var half = 11 * Mathf.Sqrt(Mathf.Clamp01(t));
                    if (Mathf.Abs(py) <= half)
                    {
                        if (Mathf.Abs(py) > half - 2.5f) return PikeInk;
                        var lit = py > 0 ? new Color(.86f, .89f, .93f) : new Color(.62f, .66f, .72f);
                        return Mathf.Abs(py) < 1.2f ? new Color(.95f, .97f, 1f) : lit;
                    }
                }
                // Binding at the socket.
                if (px >= 276 && px < 292 && Mathf.Abs(py) <= 6) return Mathf.Abs(py) > 4.5f || px < 277.5f ? PikeInk : new Color(.55f, .38f, .22f);
                // Shaft.
                if (px >= 12 && px < 276 && Mathf.Abs(py) <= 4.5f)
                {
                    if (Mathf.Abs(py) > 3 || px < 13.5f) return PikeInk;
                    return py > 0 ? new Color(.82f, .66f, .44f) : new Color(.66f, .5f, .32f);
                }
                return new Color(0, 0, 0, 0);
            });
            return new ArtVisual(PikeRole, texture, new Vector2(width, height) / 384f, new Vector2(.97f, .5f));
        }

        // Temporary thrust streak (256x48): a pale steel-white band brightest at its right end, fading left and to its edges.
        ArtVisual TemporaryPikeThrust()
        {
            const int width = 256, height = 48;
            var texture = PaintTexture("Temporary pike thrust", width, height, (x, y) =>
            {
                var u = (x + .5f) / width; var across = Mathf.Abs(y + .5f - height * .5f) / (height * .5f - 8);
                if (across >= 1) return new Color(0, 0, 0, 0);
                var alpha = Mathf.Pow(u, 1.6f) * (1 - across * across) * .85f;
                var c = Color.Lerp(new Color(.74f, .78f, .84f), Color.white, Mathf.Clamp01(Mathf.Pow(u, 5) * (1 - across * 2)));
                c.a = alpha; return c;
            });
            return new ArtVisual(PikeThrustRole, texture, new Vector2(width, height) / 384f, new Vector2(1, .5f));
        }

        // Temporary point flash (96x96): a four-pointed white star with a pale steel glow.
        ArtVisual TemporaryPikeTipFlash()
        {
            const int size = 96; const float c0 = size * .5f;
            var texture = PaintTexture("Temporary pike tip flash", size, size, (x, y) =>
            {
                var p = new Vector2(x + .5f - c0, y + .5f - c0) / (c0 - 8);
                var r = p.magnitude; if (r >= 1) return new Color(0, 0, 0, 0);
                var star = Mathf.Max(Mathf.Clamp01(1 - Mathf.Abs(p.x) * 7 - Mathf.Abs(p.y)), Mathf.Clamp01(1 - Mathf.Abs(p.y) * 7 - Mathf.Abs(p.x)));
                var glow = Mathf.Clamp01(1 - r) * .45f;
                var alpha = Mathf.Clamp01(Mathf.Max(star, glow));
                var c = Color.Lerp(new Color(.78f, .84f, .92f), Color.white, star); c.a = alpha; return c;
            });
            return new ArtVisual(PikeTipFlashRole, texture, new Vector2(size, size) / 384f, new Vector2(.5f, .5f));
        }

        // Temporary furrow (512x96, tiles along the line): a dark soil groove between two turned-earth ridges, lit from above.
        ArtVisual TemporaryPikeFurrow()
        {
            const int width = 512, height = 96;
            var texture = PaintTexture("Temporary pike furrow", width, height, (x, y) =>
            {
                var v = (y + .5f - height * .5f) / (height * .5f - 8);   // -1 bottom .. 1 top
                var a = Mathf.Abs(v); if (a >= 1) return new Color(0, 0, 0, 0);
                var wobble = .06f * Mathf.Sin(x * .05f) + .04f * Mathf.Sin(x * .17f + 1.3f);
                var groove = new Color(.33f, .25f, .17f); var ridge = new Color(.58f, .45f, .30f); var lit = new Color(.70f, .57f, .39f);
                Color c;
                if (a < .28f + wobble) c = groove;
                else if (v > 0) c = Color.Lerp(lit, ridge, (a - .28f) / .72f);
                else c = Color.Lerp(ridge, groove, Mathf.Clamp01((a - .6f) / .4f) * .5f);
                c.a = Mathf.Clamp01((1 - a) * 4) * .85f; return c;
            });
            return new ArtVisual(PikeFurrowRole, texture, new Vector2(width, height) / 384f, new Vector2(0, .5f));
        }

        void RecordPikeCast(PikeCast cast, Rect view)
        {
            cast.Recorded = true;
            if (PikeCastSample == null) return;
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var last = -1;
            for (var i = 0; i < cast.Count; i++) if (view.Contains(cast.Points[i])) last = i;
            var record = new PikeCastRecord { Tick = cast.Tick, Impacts = cast.Count, Evolved = cast.Evolved, Stage = cast.Stage, ShownFrame = PresentedFrames,
                FirstDueFrame = PresentedFrames + Mathf.RoundToInt((cast.Due - visualTime) * 60), StepFrames = Mathf.RoundToInt(cast.Step * 60),
                LaunchScreen = renderCamera.WorldToScreenPoint(cast.Launch), LastImpactScreen = renderCamera.WorldToScreenPoint(cast.Points[Mathf.Max(0, last)]),
                LastImpactFrame = PresentedFrames + Mathf.RoundToInt((cast.DueOf(Mathf.Max(0, last)) - visualTime) * 60),
                PikeLengthPixels = HeroHeight * PikeSpec.PikeHeroHeights / pixelsToWorld, FurrowWidthPixels = HeroHeight * PikeSpec.FurrowHeroHeights(cast.Stage, cast.Evolved) / pixelsToWorld,
                HeroHeightPixels = HeroHeight / pixelsToWorld, LastImpactOnScreen = last >= 0 };
            for (var i = 0; i < cast.Count; i++) if (view.Contains(cast.Points[i])) record.OnScreenImpacts++;
            PikeCastSample(record);
        }
    }

    public struct PikeCastRecord
    {
        public int Tick, Impacts, OnScreenImpacts, ShownFrame, FirstDueFrame, StepFrames, LastImpactFrame;
        public bool Evolved, LastImpactOnScreen;
        public float Stage, PikeLengthPixels, FurrowWidthPixels, HeroHeightPixels;
        public Vector2 LaunchScreen, LastImpactScreen;
    }
}
