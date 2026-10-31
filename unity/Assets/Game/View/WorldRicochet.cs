using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Canal bow ricochet (ADR0062): a straight water arrow from the hero to an enemy that then turns to the next enemy. Core
    // announces every flight (review-impact-tell from where it leaves to its target) and lands it (review-impact, at the
    // target it followed); bounces are announced on the tick the previous flight lands, from its impact point.
    public static class RicochetSpec
    {
        // The flight path lingers TraceFrames after landing so a bounce reads as one bent path; each landing (and turn) splashes.
        public const int TraceFrames = 14, MidFlightFramesBeforeDue = 5;
        // Evolved water-channel arrow: the channel is cut into the ground behind the arrow as it flies (ChannelHeroHeights wide,
        // under the enemies) and stays ChannelFrames after landing, fading over its last ChannelFadeFrames. Turns splash
        // TurnSplashScale larger.
        public const int ChannelFrames = 40, ChannelFadeFrames = 16;
        public const float ChannelHeroHeights = .2f, TurnSplashScale = 1.5f, TracePixels = 3;
        public const int MaxChannelPieces = 96;
        public static float Seconds(int frames) => frames / 60f;
    }

    public sealed partial class WorldRenderer
    {
        const int RicochetHopCapacity = 96;
        sealed class RicochetHop
        {
            public int Id = -1, Chain, Hop, Tick, AcceptedFrame, MidFrame = -1;
            public string Source; public bool Evolved, First, Flash, Recorded, Sampled, Turns; public float Accepted, Due, Landed = -1, Stage;
            public Vector2 From, To, MidScreen;
        }
        readonly RicochetHop[] ricochetHops = CreateRicochetHops();
        int ricochetNext, ricochetChains, ricochetLastCastTick = -1;
        (int Tick, string Source, Vector2 Point, int Chain, int Hop, int Index) ricochetLastImpact = (-1, null, Vector2.zero, 0, 0, -1);
        public int RicochetHops { get; private set; }          // flights announced (first flights and bounces)
        public int RicochetBounces { get; private set; }
        public int RicochetLanded { get; private set; }        // counted as they land (flights due after the capture ends never land)
        public int RicochetLandedOnTime { get; private set; }
        public int RicochetChannelDraws { get; private set; }  // evolved channel pieces drawn (review counter)
        public System.Action<RicochetHopRecord> RicochetHopSample;
        ArtVisual bowChannel;
        const string ChannelRole = "fx.canal_bow.channel";

        static RicochetHop[] CreateRicochetHops()
        {
            var hops = new RicochetHop[RicochetHopCapacity];
            for (var i = 0; i < hops.Length; i++) hops[i] = new RicochetHop();
            return hops;
        }

        void ResetRicochet()
        {
            foreach (var hop in ricochetHops) { hop.Id = -1; hop.Landed = -1; }
            ricochetNext = ricochetChains = 0; ricochetLastCastTick = -1; ricochetLastImpact = (-1, null, Vector2.zero, 0, 0, -1);
            RicochetHops = RicochetBounces = RicochetLanded = RicochetLandedOnTime = RicochetChannelDraws = 0;
        }

        bool IsRicochetSource(string source) => source != null && waveDefinition != null && waveDefinition.Gear.TryGetValue(source, out var gear) && gear.Composition?.Placement == "ricochet";

        void AcceptRicochetEvent(WaveEvent value)
        {
            var composition = waveDefinition.Gear[value.Source].Composition;
            if (value.Kind == "review-impact-tell")
            {
                var hop = ricochetHops[ricochetNext++ % ricochetHops.Length];
                var from = Point(value.Position);
                var last = ricochetLastImpact;
                var bounce = last.Tick == value.Tick && last.Source == value.Source && (last.Point - from).sqrMagnitude < 1e-6f;
                hop.Id = value.SubjectId; hop.Source = value.Source; hop.Tick = value.Tick; hop.To = Point(value.Target);
                hop.Accepted = visualTime; hop.AcceptedFrame = PresentedFrames + 1; hop.Landed = -1; hop.Recorded = hop.Sampled = hop.Turns = false; hop.MidFrame = -1;
                hop.Evolved = WeaponEvolved(value.Source);
                hop.Stage = hop.Evolved ? 1 : ArrowSpec.NormalStageShare * GearTier(value.Source);
                hop.First = !bounce; hop.Flash = false;
                if (bounce)
                {
                    // A bounce whose earlier flight was cast before the View started still bounces (a chain of its own).
                    if (last.Index < 0) { hop.Chain = ricochetChains++; hop.Hop = 1; } else { hop.Chain = last.Chain; hop.Hop = last.Hop + 1; }
                    hop.From = from;
                    if (last.Index >= 0 && ricochetHops[last.Index].Id >= 0) ricochetHops[last.Index].Turns = true;
                    RicochetBounces++;
                }
                else
                {
                    // First flights leave from the hero (the bow hand stands in for the launch); one bowstring flash per cast.
                    var launch = PredictedLordPosition ?? RenderedLordPosition;
                    var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
                    hop.Chain = ricochetChains++; hop.Hop = 0;
                    hop.From = launch + (hop.To - launch).normalized * (.35f * w) + Vector2.up * (.45f * w);
                    hop.Flash = ricochetLastCastTick != value.Tick; ricochetLastCastTick = value.Tick;
                }
                var ticks = bounce ? composition.StepTicks : composition.DelayTicks;
                hop.Due = visualTime + ticks / (float)Mathf.Max(1, waveTickRate);
                RicochetHops++; ArrowImpacts++;
            }
            else if (value.Kind == "review-impact")
            {
                ricochetLastImpact = (value.Tick, value.Source, Point(value.Position), 0, 0, -1);
                for (var i = 0; i < ricochetHops.Length; i++)
                {
                    var hop = ricochetHops[i];
                    if (hop.Id != value.SubjectId || hop.Source != value.Source || hop.Landed >= 0) continue;
                    hop.Landed = visualTime; hop.To = Point(value.Position);
                    RicochetLanded++;
                    if (Mathf.Abs(visualTime - hop.Due) <= ArrowSpec.Seconds(2)) { RicochetLandedOnTime++; ArrowLandedOnTime++; }
                    impactParticles.Hit(value.Id, hop.To);
                    ricochetLastImpact = (value.Tick, value.Source, Point(value.Position), hop.Chain, hop.Hop, i);
                    break;
                }
            }
        }

        // Core moves a pending arrow's impact with the enemy it follows (its attack view carries the impact id in ActivationId);
        // the arrow homes on it until it lands.
        void TrackRicochet(WaveAttackView attack)
        {
            foreach (var hop in ricochetHops)
                if (hop.Id == attack.ActivationId && hop.Landed < 0) { hop.To = Point(attack.Position); return; }
        }

        // Ricochet hits push along the flight of the arrow that struck them.
        bool RicochetHitDirection(WaveEvent value, Vector2 origin, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!IsRicochetSource(value.Source)) return false;
            var best = float.PositiveInfinity;
            foreach (var hop in ricochetHops)
            {
                if (hop.Id < 0 || hop.Landed < 0 || visualTime - hop.Landed > ArrowSpec.Seconds(4)) continue;
                var distance = (origin - hop.To).sqrMagnitude;
                if (distance < best) { best = distance; direction = hop.To - hop.From; }
            }
            if (direction.sqrMagnitude < .000001f) direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
            if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
            return true;
        }

        void DrawRicochetHops()
        {
            var any = false;
            foreach (var hop in ricochetHops) if (hop.Id >= 0 && !Expired(hop)) { any = true; break; }
            if (!any) return;
            EnsureLightningArt(); EnsureBowArt();
            if (bowChannel == null) bowChannel = EffectArt(ChannelRole, TemporaryBowChannel);
            var pixel = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var w = heroBodyWidth > 0 ? heroBodyWidth : .6f;
            var view = CameraWorldViewport;
            var channelBudget = RicochetSpec.MaxChannelPieces;
            foreach (var hop in ricochetHops)
            {
                if (hop.Id < 0 || Expired(hop)) continue;
                var landed = hop.Landed >= 0 ? hop.Landed : hop.Due;
                var flight = Mathf.Max(.0001f, hop.Due - hop.Accepted);
                var t = Mathf.Clamp01((visualTime - hop.Accepted) / flight);
                var path = hop.To - hop.From; var direction = path.sqrMagnitude > 1e-8f ? path.normalized : Vector2.right;
                var at = hop.From + path * t;
                var length = HeroHeight * ArrowSpec.ArrowHeroHeights(hop.Stage);
                if (hop.Evolved) DrawBowChannel(hop, t, landed, ref channelBudget);
                if (hop.Flash && visualTime - hop.Accepted < ArrowSpec.Seconds(ArrowSpec.BowstringFlashFrames))
                {
                    BowstringFlashDraws++;
                    bowstringRequests.Add((hop.From, Mathf.Clamp01((ArrowSpec.Seconds(ArrowSpec.BowstringFlashFrames) - (visualTime - hop.Accepted)) / ArrowSpec.Seconds(1))));
                }
                DrawRicochetTrace(hop.From, at, visualTime - landed, pixel);
                if (hop.Landed < 0 || visualTime < landed)
                {
                    // Straight flight; the water tail runs back along the path behind the fletching.
                    var tail = at - direction * (length * .85f);
                    var trailStart = hop.From + path * Mathf.Max(0, t - ArrowSpec.TrailOfDistance);
                    var trail = tail - trailStart;
                    if (Vector2.Dot(trail, direction) > .0001f)
                        Draw(bowStreak, ProjectileLayer, tail, 0, new Vector2(trail.magnitude, HeroHeight * ArrowSpec.TailHeroHeights(hop.Stage)), Angle(trail), 1, Color.white, applyTween: false);
                    if (hop.First && visualTime - hop.Accepted < ArrowSpec.Seconds(ArrowSpec.LaunchBloomFrames))
                    {
                        var bloom = ArrowSpec.Silver; bloom.a = 1;
                        Draw(lightningFlash, LightningLayer, at, 0, Vector2.one * w * .75f, 0, 1, bloom, applyTween: false);
                    }
                    DrawArrowPicture(at, direction, length, 1, hop.First ? OverHeroThinLayer : ThinEffectLayer);
                    // The arrow's point MidFlightFramesBeforeDue frames before it lands, sampled on the frame it is drawn there.
                    if (hop.MidFrame < 0 && hop.Due - visualTime <= ArrowSpec.Seconds(RicochetSpec.MidFlightFramesBeforeDue))
                    { hop.MidFrame = PresentedFrames; hop.MidScreen = renderCamera.WorldToScreenPoint(at); }
                }
                else
                {
                    // Landed: a turning arrow leaves only its splash (the next flight carries on); the last one stays briefly.
                    if (!hop.Turns) DrawPlantedArrow(hop.To, direction, visualTime - landed, length, ArrowSpec.RainPlantedFrames);
                    DrawArrowLanding(hop.To, Mathf.Max(w * .5f, 90 / (float)settings.WorldUnitsPerUnityUnit), landed, w, hop.Evolved && hop.Turns ? RicochetSpec.TurnSplashScale : 1);
                    if (!hop.Sampled) { hop.Sampled = true; RecordRicochetHop(hop, view); }
                }
            }
        }

        bool Expired(RicochetHop hop)
        {
            var end = (hop.Landed >= 0 ? hop.Landed : hop.Due) + RicochetSpec.Seconds(hop.Evolved ? RicochetSpec.ChannelFrames : RicochetSpec.TraceFrames);
            return visualTime > end + .25f;
        }

        // Thin water trace of the flown part of the path, fading over TraceFrames once the arrow lands.
        void DrawRicochetTrace(Vector2 from, Vector2 to, float sinceLanding, float pixel)
        {
            var fade = sinceLanding <= 0 ? 1 : 1 - sinceLanding / RicochetSpec.Seconds(RicochetSpec.TraceFrames);
            var span = to - from;
            if (fade <= 0 || span.sqrMagnitude < 1e-8f) return;
            var line = ArrowSpec.Trace; line.a = .9f * fade;
            Draw(lightningCore, ProjectileLayer, (from + to) * .5f, 0, new Vector2(span.magnitude, RicochetSpec.TracePixels * pixel * 1.6f), Angle(span), 1, line, applyTween: false);
            var bloom = ArrowSpec.Water; bloom.a = .3f * fade;
            Draw(lightningGlow, LightningLayer, (from + to) * .5f, 0, new Vector2(span.magnitude, RicochetSpec.TracePixels * pixel * 4), Angle(span), 1, bloom, applyTween: false);
        }

        // The evolved channel: cut behind the arrow up to where it has flown, tiled along the path under the enemies, fading
        // over its last ChannelFadeFrames.
        void DrawBowChannel(RicochetHop hop, float t, float landed, ref int budget)
        {
            var since = visualTime - landed;
            var fade = since <= 0 ? 1 : Mathf.Clamp01((RicochetSpec.Seconds(RicochetSpec.ChannelFrames) - since) / RicochetSpec.Seconds(RicochetSpec.ChannelFadeFrames));
            var path = hop.To - hop.From; var length = path.magnitude * t;
            if (fade <= 0 || length <= 1e-4f) return;
            var direction = path.normalized; var width = HeroHeight * RicochetSpec.ChannelHeroHeights;
            var tile = width * bowChannel.WorldSize.x / bowChannel.WorldSize.y;
            var uv = bowChannel.UvRects[0]; var colour = Color.white; colour.a = fade;
            for (var start = 0f; start < length && budget > 0; start += tile, budget--)
            {
                var piece = Mathf.Min(tile, length - start);
                var slice = new Rect(uv.x, uv.y, uv.width * piece / tile, uv.height);
                Draw(bowChannel, GameVisualTokens.AttackLayer, hop.From + direction * start, 0, new Vector2(piece, width), Angle(direction), 1, colour, uv: slice, applyTween: false);
                RicochetChannelDraws++;
            }
        }

        // Temporary channel picture until Astra paints fx.canal_bow.channel: wet dark banks either side of clear water with a
        // light ripple line; ends left open so tiles join.
        ArtVisual TemporaryBowChannel()
        {
            const int width = 256, height = 64;
            var texture = PaintTexture("Temporary canal bow channel", width, height, (x, y) =>
            {
                var v = (y + .5f - height * .5f) / (height * .5f - 4);   // -1 .. 1 across
                var a = Mathf.Abs(v); if (a >= 1) return new Color(0, 0, 0, 0);
                var wobble = .05f * Mathf.Sin(x * .07f) + .03f * Mathf.Sin(x * .19f + .7f);
                var bank = new Color(.30f, .25f, .19f); var water = new Color(.30f, .62f, .92f); var ripple = new Color(.78f, .93f, 1f);
                Color c;
                if (a > .66f + wobble) { c = bank; c.a = Mathf.Clamp01((1 - a) * 5) * .7f; return c; }
                c = water;
                var r = Mathf.Abs(v - .25f - .08f * Mathf.Sin(x * .11f));
                if (r < .1f) c = Color.Lerp(ripple, water, r / .1f);
                c.a = .85f; return c;
            });
            return new ArtVisual(ChannelRole, texture, new Vector2(width, height) / 384f, new Vector2(0, .5f));
        }

        void RecordRicochetHop(RicochetHop hop, Rect view)
        {
            if (RicochetHopSample == null) return;
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            RicochetHopSample(new RicochetHopRecord
            {
                Tick = hop.Tick, Chain = hop.Chain, Hop = hop.Hop, Evolved = hop.Evolved, Stage = hop.Stage, Turns = hop.Turns,
                AcceptedOffset = hop.AcceptedFrame - PresentedFrames, MidOffset = hop.MidFrame >= 0 ? hop.MidFrame - PresentedFrames : 1,
                FromScreen = renderCamera.WorldToScreenPoint(hop.From), ToScreen = renderCamera.WorldToScreenPoint(hop.To), MidScreen = hop.MidFrame >= 0 ? hop.MidScreen : new Vector2(-1, -1),
                OnScreen = view.Contains(hop.To), ArrowLengthPixels = HeroHeight * ArrowSpec.ArrowHeroHeights(hop.Stage) / pixelsToWorld,
                HeroHeightPixels = HeroHeight / pixelsToWorld, ChannelWidthPixels = hop.Evolved ? HeroHeight * RicochetSpec.ChannelHeroHeights / pixelsToWorld : 0
            });
        }
    }

    // One ricochet flight, sampled on the first frame drawn after it lands; offsets are frames relative to that frame.
    public struct RicochetHopRecord
    {
        public int Tick, Chain, Hop, AcceptedOffset, MidOffset;
        public bool Evolved, Turns, OnScreen;
        public float Stage, ArrowLengthPixels, HeroHeightPixels, ChannelWidthPixels;
        public Vector2 FromScreen, ToScreen, MidScreen;
    }
}
