using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Melee swing spec ("무기 연출 레퍼런스·이론", melee row). Frames are 60Hz display frames.
    public static class BladeSpec
    {
        // The knight swing clip draws the sword fully back at 0.072s (frame 1 of 6 over 0.36s) and brings it through by
        // 0.216s; the crescent waits for the draw-back and sweeps over the downswing (GiantBladeEnvelope.SweepSeconds .15s).
        public const int WindupFrames = 4;           // hero pulls the sword back (3–4)
        public const int HitHoldFrames = 4;          // struck enemies freeze (3–5) and are pushed along the swing
        public const float PushTangent = .75f, PushOutward = .55f;
        // Crescent thickness at its leading edge, as a fraction of the drawn radius, with a floor in 390pt-screen points.
        public const float GlowFraction = .15f, CoreFraction = .38f, MinGlowPoints = 22, MaxGlowPoints = 40, TrailTaper = .12f;
        public const float FillOpacity = .16f;
        // Evolution element: a giant sword drives down from the sky into the fan's centre during the wind-up, then a 360°
        // shockwave rings out from where it lands, with a screen shake.
        public const float SwordDropHeight = .6f, SwordLengthOfRadius = .85f, ImpactAlong = .5f;
        public const int ShockwaveFrames = 12;
        public const float ShockwaveRadiusOfFan = .75f;
        public static readonly Color Core = new Color(1f, 1f, 1f);
        public static readonly Color Glow = new Color(.55f, .82f, 1f);   // steel white + sky blue
        public static float Seconds(int frames) => frames / 60f;
        public static float GlowPoints(float strength) => Mathf.Lerp(MinGlowPoints, MaxGlowPoints, strength);
        // Thickness along the drawn trail: thin at its oldest end, full at the leading edge (motion blur toward the start).
        public static float Taper(float along) => TrailTaper + (1 - TrailTaper) * Mathf.Pow(Mathf.Clamp01(along), 1.5f);
    }

    public sealed partial class WorldRenderer
    {
        public System.Action<BladeStrikeRecord> BladeStrikeSample;

        // Blade hits are held until the blade passes them: Core emits them before the strike's attack event in the same
        // tick, so they wait as pending and get their due time when the attack arrives.
        bool DeferBladeHit(WaveEvent value, Vector2 origin)
        {
            if (!IsM1GiantBlade(value.Source)) return false;
            deferredHits.Add(new DeferredHit { Subject = value.SubjectId, Tick = value.Tick, EventId = value.Id, Due = visualTime, Origin = origin, Amount = value.Amount,
                Hold = BladeSpec.Seconds(BladeSpec.HitHoldFrames), Shake = 0, Blade = true, Pending = true, Source = value.Source });
            BladeDeferredHits++;
            return true;
        }

        void ResolveBladeHits(M1BladeStrike strike, int tick)
        {
            for (var i = 0; i < deferredHits.Count; i++)
            {
                var hit = deferredHits[i];
                if (!hit.Blade || !hit.Pending || hit.Tick != tick) continue;
                hit.Pending = false;
                hit.Due = strike.Visible + GiantBladeEnvelope.ArrivalSeconds(strike.Aim, strike.Origin, hit.Origin);
                var radial = hit.Origin - strike.Origin;
                var outward = radial.sqrMagnitude > .000001f ? radial.normalized : M1Direction(strike.Aim);
                var tangent = new Vector2(-outward.y, outward.x);   // the sweep runs counter-clockwise from aim − half to aim + half
                hit.Direction = (tangent * BladeSpec.PushTangent + outward * BladeSpec.PushOutward).normalized;
                deferredHits[i] = hit;
            }
        }

        void DrawBladeStrikes()
        {
            if (waveDefinition == null) return;
            var any = false;
            foreach (var strike in m1BladeStrikes)
                if (strike.EventId >= 0 && visualTime - strike.Accepted < BladeSpec.Seconds(BladeSpec.WindupFrames) + GiantBladeEnvelope.LifetimeSeconds + BladeSpec.Seconds(BladeSpec.ShockwaveFrames)) { any = true; break; }
            if (!any) return;
            EnsureLightningArt();
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var pointsToWorld = renderCamera.pixelWidth / 390f * pixelsToWorld;
            foreach (var strike in m1BladeStrikes)
            {
                if (strike.EventId < 0) continue;
                var age = visualTime - strike.Visible;
                if (age > GiantBladeEnvelope.LifetimeSeconds + BladeSpec.Seconds(BladeSpec.ShockwaveFrames)) continue;
                FitM1Blade(strike);
                if (strike.Evolved) DrawGiantSwordDrop(strike, age);
                if (age < 0 || age > GiantBladeEnvelope.LifetimeSeconds) continue;
                DrawBladeCrescent(strike, age, pointsToWorld);
            }
        }

        void DrawBladeCrescent(M1BladeStrike strike, float age, float pointsToWorld)
        {
            var opacity = GiantBladeEnvelope.Opacity(age);
            if (opacity <= 0) return;
            var origin = strike.Origin; var radius = strike.Radius; var half = strike.HalfAngle; var aim = strike.Aim;
            var start = aim - half; var end = aim + half;
            var lead = GiantBladeEnvelope.SweepAngle(aim, half, age);
            // After the hold the trail disappears toward the blade tip: its oldest end runs up to the leading edge.
            var hold = Mathf.Max(GiantBladeEnvelope.SweepSeconds, GiantBladeEnvelope.HoldSeconds);
            var fade = Mathf.Clamp01((age - hold) / GiantBladeEnvelope.FadeSeconds);
            var tail = Mathf.Lerp(start, lead, fade * fade);
            if (lead - tail < .5f) return;
            if (!strike.Recorded) RecordBladeStrike(strike);
            // Faint swept area so the whole trajectory reads and encloses its victims.
            var arc = Resolve("attack", "core:iron_blade", "sector90");
            var extra = Mathf.Max(0, half - GiantBladeEnvelope.CoreHalfAngle);
            var stamps = extra <= 0 ? 1 : 2 + Mathf.CeilToInt(extra / GiantBladeEnvelope.CoreHalfAngle);
            for (var i = 0; i < stamps; i++)
            {
                var angle = stamps == 1 ? aim : Mathf.Lerp(aim - extra, aim + extra, i / (float)(stamps - 1));
                Draw(arc, GameVisualTokens.AttackLayer, origin, age, arc.WorldSize * radius, angle, opacity * BladeSpec.FillOpacity, BladeSpec.Glow, applyTween: false);
            }
            var thickness = Mathf.Max(BladeSpec.GlowPoints(strike.Strength) * pointsToWorld, radius * BladeSpec.GlowFraction);
            thickness = Mathf.Min(thickness, radius * .45f);
            var segments = Mathf.Clamp(Mathf.CeilToInt((lead - tail) / 4f), 2, 48);
            // The fitted envelope may reach the screen edge to enclose edge kills; the crescent itself stays on screen by
            // following the visible reach in each direction (the faint fill above still encloses every kill).
            var visible = M1BladeVisibleBounds();
            for (var pass = 0; pass < 2; pass++)
            {
                var visual = pass == 0 ? lightningGlow : lightningCore;
                for (var s = 0; s < segments; s++)
                {
                    var a = Mathf.Lerp(tail, lead, s / (float)segments); var b = Mathf.Lerp(tail, lead, (s + 1) / (float)segments);
                    var along = Mathf.InverseLerp(start, lead, (a + b) * .5f);
                    var middle = (a + b) * .5f;
                    var reach = Mathf.Min(radius, GiantBladeEnvelope.VisibleReach(origin, M1Direction(middle), visible) * .96f);
                    var band = Mathf.Min(thickness, reach * .45f) * BladeSpec.Taper(along);
                    var width = band * (pass == 0 ? 1 : BladeSpec.CoreFraction);
                    // The band hugs the outer edge of the fan (the blade tip's path).
                    var centre = reach - band * .5f;
                    var length = centre * (b - a) * Mathf.Deg2Rad * 1.25f;
                    var color = pass == 0 ? BladeSpec.Glow : BladeSpec.Core;
                    color.a = opacity * Mathf.Lerp(.35f, 1, along);
                    Draw(visual, LightningLayer, origin + M1Direction(middle) * centre, 0, new Vector2(length, width), middle + 90, 1, color, applyTween: false);
                }
            }
        }

        void DrawGiantSwordDrop(M1BladeStrike strike, float age)
        {
            var windup = BladeSpec.Seconds(BladeSpec.WindupFrames);
            var impact = strike.Origin + M1Direction(strike.Aim) * (strike.Radius * BladeSpec.ImpactAlong);
            var view = CameraWorldViewport;
            var length = strike.Radius * BladeSpec.SwordLengthOfRadius;
            var opacity = age < 0 ? 1 : GiantBladeEnvelope.Opacity(age);
            if (age > -windup && opacity > 0)
            {
                // Falls point first from above the screen and lands exactly when the sweep begins.
                var fall = Mathf.Clamp01((age + windup) / windup);
                var drop = view.height * BladeSpec.SwordDropHeight * (1 - fall * fall);
                var tip = impact + Vector2.up * drop;
                M1Draw("icon", "core:iron_blade", "default", tip + Vector2.up * (length * .5f), new Vector2(length * .2f, length), 180, opacity, Color.white);
                if (age < 0)
                {
                    var streak = BladeSpec.Glow; streak.a = .5f * fall;
                    Draw(lightningGlow, LightningLayer, tip + Vector2.up * (length * .9f), 0, new Vector2(length * 1.2f, length * .22f), 90, 1, streak, applyTween: false);
                }
            }
            if (age < 0) return;
            if (!strike.ShakeFired) { strike.ShakeFired = true; impactShakeUntil = visualTime + GameVisualTokens.ImpactShakeSeconds; }
            var t = age / BladeSpec.Seconds(BladeSpec.ShockwaveFrames);
            if (t > 1) return;
            // 360° shockwave from the landing point: a bright ring that expands and thins out.
            var ring = strike.Radius * BladeSpec.ShockwaveRadiusOfFan * Mathf.Sqrt(t);
            var width = Mathf.Lerp(strike.Radius * .12f, strike.Radius * .03f, t);
            var alpha = 1 - t;
            const int segments = 40;
            for (var pass = 0; pass < 2; pass++)
            {
                var visual = pass == 0 ? lightningGlow : lightningCore;
                var color = pass == 0 ? BladeSpec.Glow : BladeSpec.Core; color.a = alpha * (pass == 0 ? .9f : .8f);
                for (var s = 0; s < segments; s++)
                {
                    var middle = (s + .5f) * 360f / segments;
                    Draw(visual, LightningLayer, impact + M1Direction(middle) * ring, 0, new Vector2(ring * Mathf.PI * 2 / segments * 1.2f, pass == 0 ? width : width * .35f), middle + 90, 1, color, applyTween: false);
                }
            }
            var flash = BladeSpec.Core; flash.a = alpha;
            Draw(lightningFlash, LightningLayer, impact, 0, Vector2.one * strike.Radius * .5f * (1 - t * .5f), 0, 1, flash, applyTween: false);
        }

        void RecordBladeStrike(M1BladeStrike strike)
        {
            strike.Recorded = true;
            if (BladeStrikeSample == null) return;
            BladeStrikeSample(new BladeStrikeRecord { Tick = strike.Tick, AcceptedFrame = strike.AcceptedFrame, VisibleFrame = PresentedFrames, Evolved = strike.Evolved,
                Radius = strike.Radius, HalfAngle = strike.HalfAngle, OriginScreen = renderCamera.WorldToScreenPoint(strike.Origin), Aim = strike.Aim,
                RadiusPixels = (Vector2)renderCamera.WorldToScreenPoint(strike.Origin + M1Direction(strike.Aim) * strike.Radius) - (Vector2)renderCamera.WorldToScreenPoint(strike.Origin) });
        }
    }

    public struct BladeStrikeRecord
    {
        public int Tick, AcceptedFrame, VisibleFrame;
        public bool Evolved;
        public float Radius, HalfAngle, Aim;
        public Vector2 OriginScreen, RadiusPixels;
    }
}
