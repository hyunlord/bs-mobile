using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public struct LightningStrikeRecord
    {
        public int Tick, Links, SkyBolts, CoveredSkyBolts, OnScreenTargets, AcceptedFrame, VisibleFrame;
        public bool Storm;
        public float Strength, CorePixels, GlowPixels, ScreenFlash;
        public Vector2 FirstSkyTargetScreen, FirstSkyTopScreen;
    }

    // Chain lightning drawn from Core chain-link events: telegraph → sky pillar(s) and target-to-target arcs with a bright
    // core, violet additive bloom and branches → fade, with scorch, target flash and per-enemy freeze+shake. Core is untouched;
    // hits, kills and numbers of a strike are presented together when the strike becomes visible after its telegraph.
    public sealed partial class WorldRenderer
    {
        public const int LightningLayer = 75, StormSkyLayer = 73, ScorchLayer = GameVisualTokens.GrowthLayer + 3;
        const int LightningLinkCapacity = 64, LightningStrikeCapacity = 12;
        sealed class LightningStrike
        {
            public long FirstEventId = -1; public int Tick, AcceptedFrame; public string Source; public float Accepted, Strength; public bool Storm, Recorded, SkyAssigned;
            public readonly Vector2[] From = new Vector2[LightningLinkCapacity], To = new Vector2[LightningLinkCapacity];
            public readonly long[] Ids = new long[LightningLinkCapacity];
            public readonly bool[] Sky = new bool[LightningLinkCapacity];
            public int Count, SkyCount;
            public float Visible => Accepted + LightningSpec.Seconds(LightningSpec.TelegraphFrames);
        }
        struct DeferredHit { public int Subject; public long EventId; public float Due; public Vector2 Origin, Direction; public long Amount; }
        readonly LightningStrike[] lightningStrikes = CreateStrikes();
        readonly List<DeferredHit> deferredHits = new List<DeferredHit>(256);
        readonly Dictionary<int, float> chainHitDue = new Dictionary<int, float>(256);
        readonly Vector2[] boltPoints = new Vector2[LightningBolt.PointCount(LightningBolt.Generations)];
        readonly Vector2[] branchPoints = new Vector2[LightningBolt.PointCount(3)];
        int lightningNext, chainHitTick = -1;
        ArtVisual lightningGlow, lightningCore, lightningFlash, lightningSolid, lightningScorch, stormSolid;
        public Action<LightningStrikeRecord> LightningStrikeSample;
        public int LightningStrikes { get; private set; }
        public int LightningStormStrikes { get; private set; }
        public int LightningDeferredHits { get; private set; }
        public int LightningPresentedHits { get; private set; }

        static LightningStrike[] CreateStrikes()
        {
            var strikes = new LightningStrike[LightningStrikeCapacity];
            for (var i = 0; i < strikes.Length; i++) strikes[i] = new LightningStrike();
            return strikes;
        }

        void ResetLightning()
        {
            foreach (var strike in lightningStrikes) { strike.FirstEventId = -1; strike.Count = 0; }
            deferredHits.Clear(); chainHitDue.Clear(); lightningNext = 0; chainHitTick = -1;
            LightningStrikes = LightningStormStrikes = LightningDeferredHits = LightningPresentedHits = 0;
        }

        bool IsChainSource(string source) => source != null && waveDefinition != null && waveDefinition.Gear.TryGetValue(source, out var gear) && gear.Kind == WaveAttackKind.Chain;

        LightningStrike CurrentStrike(int tick, string source)
        {
            for (var i = 1; i <= lightningStrikes.Length; i++)
            {
                var strike = lightningStrikes[(lightningNext - i + lightningStrikes.Length * 4) % lightningStrikes.Length];
                if (strike.FirstEventId >= 0 && strike.Tick == tick && strike.Source == source) return strike;
            }
            return null;
        }

        void AcceptLightningLink(WaveEvent value)
        {
            if (value.Kind != "chain-link" || !IsChainSource(value.Source)) return;
            var strike = CurrentStrike(value.Tick, value.Source);
            if (strike == null)
            {
                strike = lightningStrikes[lightningNext++ % lightningStrikes.Length];
                strike.FirstEventId = value.Id; strike.Tick = value.Tick; strike.Source = value.Source; strike.Accepted = visualTime; strike.AcceptedFrame = PresentedFrames + 1;
                strike.Count = strike.SkyCount = 0; strike.Recorded = strike.SkyAssigned = false;
                var runtime = waveDefinition.Gear[value.Source]; WaveGearDefinition reference = null;
                GearReference?.Gear.TryGetValue(value.Source, out reference);
                var level = EquipmentLevel(value.Source);
                strike.Strength = LightningSpec.Strength(MusterFormationRule.Ratio(runtime, level, reference));
                strike.Storm = WeaponEvolutionGrade.Active(WeaponEvolutionGrade.RangeRatio(runtime, level, reference),
                    MusterFormationRule.ActiveWaveEvolution(wave?.Evolutions, waveDefinition.Evolutions, value.Source));
                LightningStrikes++; if (strike.Storm) LightningStormStrikes++;
            }
            if (strike.Count == LightningLinkCapacity) return;
            var to = Point(value.Target);
            var i = strike.Count++;
            strike.Ids[i] = value.Id; strike.From[i] = Point(value.Position); strike.To[i] = to;
            strike.Sky[i] = false;
        }

        // Pillars are assigned once every link of the tick is known (first draw, before the telegraph). One pillar marks a target
        // whose column the hero does not cover — the nearest victim usually stands under the hero, and a pillar behind the
        // hero sprite reads as no lightning at all (P5 keeps the hero on top). The storm (evolution element) drops pillars on up to
        // eight on-screen victims, covered ones only when none is clear.
        void AssignSkyPillars(LightningStrike strike, Rect view, float glow)
        {
            strike.SkyAssigned = true; strike.SkyCount = 0;
            var limit = strike.Storm ? LightningSpec.MaxSkyBolts : 1;
            // Victims near the screen edge get a pillar cut by the edge, so inset ones come first.
            var inset = Mathf.Min(view.width * .12f, glow * 4);
            var inner = Rect.MinMaxRect(view.xMin + inset, view.yMin + inset, view.xMax - inset, view.yMax - inset);
            for (var pass = 0; pass < 3 && strike.SkyCount < limit; pass++)
            {
                if (pass == 2 && strike.SkyCount > 0) break;
                for (var i = 0; i < strike.Count && strike.SkyCount < limit; i++)
                {
                    if (strike.Sky[i] || !view.Contains(strike.To[i])) continue;
                    if (pass == 0 && !inner.Contains(strike.To[i])) continue;
                    if (pass < 2 && !PillarClearOfHero(strike.To[i], PillarHeight(strike.To[i], view), glow)) continue;
                    strike.Sky[i] = true; strike.SkyCount++;
                }
            }
            if (strike.SkyCount == 0 && strike.Count > 0) { strike.Sky[0] = true; strike.SkyCount = 1; }
        }

        Rect HeroBox => new Rect(heroSilhouetteLocal.position + RenderedLordPosition, heroSilhouetteLocal.size);

        bool PillarClearOfHero(Vector2 target, float height, float glow)
        {
            if (!heroSilhouetteKnown) return true;
            var column = Rect.MinMaxRect(target.x - glow * 2, target.y + glow, target.x + glow * 2, target.y + height);
            return !column.Overlaps(HeroBox);
        }

        // The pillar top leans away from the hero so the zigzag does not swing into the hero sprite. When the only victim stands
        // under the hero (a one-link chain), the pillar comes in on a slant from that side so it clears the hero box from 40% of
        // the hero's height up; at most 45 degrees.
        Vector2 PillarTop(Vector2 target, uint seed, float glow, Rect view)
        {
            var height = PillarHeight(target, view);
            var lean = (seed & 255) / 255f * glow * 3;
            if (!heroSilhouetteKnown) return target + new Vector2(lean * 2 - glow * 3, height);
            var hero = HeroBox; var side = target.x >= hero.center.x ? 1 : -1;
            if (!PillarClearOfHero(target, height, glow))
            {
                var edge = (side > 0 ? hero.xMax - target.x : target.x - hero.xMin) + glow * 2;
                var rise = Mathf.Max(glow, hero.yMax - target.y);
                lean = Mathf.Max(lean, Mathf.Min(height, edge * height / (.4f * rise)));
            }
            var x = Mathf.Clamp(target.x + side * lean, view.xMin + glow, view.xMax - glow);
            return new Vector2(x, target.y + height);
        }

        // Chain hits are held until their strike is visible so flash, freeze, number and death share the bolt's frame.
        bool DeferChainHit(WaveEvent value, Vector2 origin)
        {
            if (!IsChainSource(value.Source)) return false;
            var strike = CurrentStrike(value.Tick, value.Source);
            if (strike == null) return false;
            if (value.Tick != chainHitTick) { chainHitDue.Clear(); chainHitTick = value.Tick; }
            chainHitDue[value.SubjectId] = strike.Visible;
            var direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
            deferredHits.Add(new DeferredHit { Subject = value.SubjectId, EventId = value.Id, Due = strike.Visible, Origin = origin, Direction = direction, Amount = value.Amount });
            LightningDeferredHits++;
            return true;
        }

        bool LightningKillDelay(WaveEvent value, out float delay)
        {
            delay = 0;
            if (value.Tick != chainHitTick || !chainHitDue.TryGetValue(value.SubjectId, out var due)) return false;
            delay = Mathf.Max(0, due - visualTime);
            return true;
        }

        void AdvanceLightning()
        {
            for (var i = deferredHits.Count - 1; i >= 0; i--)
            {
                var hit = deferredHits[i];
                if (visualTime < hit.Due) continue;
                enemyImpactPoses.Hit(hit.Subject, visualTime, hit.Direction, LightningSpec.Seconds(LightningSpec.HitHoldFrames), LightningSpec.HitShakeBodyWidths, true);
                impactParticles.Hit(hit.EventId, hit.Origin);
                if (ShowDamageNumbers && showTransientText) numbers?.AddAggregated(hit.Subject, hit.Origin, hit.Amount);
                deferredHits[i] = deferredHits[deferredHits.Count - 1]; deferredHits.RemoveAt(deferredHits.Count - 1);
                LightningPresentedHits++;
            }
        }

        static Texture2D EffectTexture(string name, int width, int height, Func<float, float, float> alpha, Color rgb)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var c = rgb; c.a = Mathf.Clamp01(alpha((x + .5f) / width, (y + .5f) / height));
                    pixels[y * width + x] = c;
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        void EnsureLightningArt()
        {
            if (lightningGlow != null) return;
            float Across(float v, float sigma) { var d = (v - .5f) / sigma; return Mathf.Exp(-.5f * d * d); }
            lightningGlow = new ArtVisual("effect.lightning.glow", EffectTexture("Lightning glow", 8, 64, (u, v) => Across(v, .26f), Color.white), Vector2.one);
            lightningCore = new ArtVisual("effect.lightning.core", EffectTexture("Lightning core", 8, 32, (u, v) => Mathf.SmoothStep(0, 1, (.5f - Mathf.Abs(v - .5f)) / .08f), Color.white), Vector2.one);
            lightningFlash = new ArtVisual("effect.lightning.flash", EffectTexture("Lightning flash", 64, 64, (u, v) => { var r = new Vector2(u - .5f, v - .5f).magnitude * 2; return Mathf.Exp(-4 * r * r) * (1 - Mathf.SmoothStep(.85f, 1, r)); }, Color.white), Vector2.one);
            lightningSolid = new ArtVisual("effect.lightning.solid", EffectTexture("Lightning solid", 4, 4, (u, v) => 1, Color.white), Vector2.one);
            stormSolid = new ArtVisual("effect.storm.sky", EffectTexture("Storm sky", 4, 4, (u, v) => 1, Color.white), Vector2.one);
            lightningScorch = new ArtVisual("effect.lightning.scorch", EffectTexture("Lightning scorch", 64, 64, (u, v) =>
            {
                var p = new Vector2(u - .5f, v - .5f); var r = p.magnitude * 2;
                var ragged = 1 + .18f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 7) + .1f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 13);
                return Mathf.Pow(Mathf.Clamp01(1 - r * ragged), 1.4f) * .9f;
            }, LightningSpec.Scorch), Vector2.one);
            var additive = Resources.Load<Shader>("WorldSpriteAdditive");
            if (additive == null || !additive.isSupported) throw new InvalidOperationException("Additive world sprite shader is unavailable.");
            foreach (var visual in new[] { lightningGlow, lightningCore, lightningFlash, lightningSolid })
                batches.Add((LightningLayer, visual.Texture), new SpriteBatch(visual.Texture, additive, renderCamera, LightningLayer));
        }

        void DrawBolt(Vector2[] points, int count, float coreWidth, float glowWidth, float opacity)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var visual = pass == 0 ? lightningGlow : lightningCore;
                var width = pass == 0 ? glowWidth : coreWidth;
                var color = pass == 0 ? LightningSpec.Glow : LightningSpec.Core; color.a = opacity;
                for (var i = 0; i + 1 < count; i++)
                {
                    var a = points[i]; var b = points[i + 1]; var span = b - a; var length = span.magnitude;
                    if (length < .00001f) continue;
                    Draw(visual, LightningLayer, (a + b) * .5f, 0, new Vector2(length + coreWidth, width), Angle(span), 1, color, applyTween: false);
                }
            }
        }

        void DrawLightning()
        {
            if (wave == null) return;
            var any = false;
            foreach (var strike in lightningStrikes) if (strike.FirstEventId >= 0 && visualTime - strike.Visible < LightningSpec.ScorchSeconds) { any = true; break; }
            if (!any) return;
            EnsureLightningArt();
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            var pointsToWorld = renderCamera.pixelWidth / 390f * pixelsToWorld;
            var view = CameraWorldViewport;
            foreach (var strike in lightningStrikes)
            {
                if (strike.FirstEventId < 0 || strike.Count == 0) continue;
                var age = visualTime - strike.Visible;
                if (age > LightningSpec.ScorchSeconds) continue;
                var core = LightningSpec.CorePoints(strike.Strength) * pointsToWorld;
                var glow = LightningSpec.GlowPoints(strike.Strength) * pointsToWorld;
                if (!strike.SkyAssigned) AssignSkyPillars(strike, view, glow);
                if (strike.Storm)
                {
                    // Evolution element: the sky darkens through the telegraph and strike, then clears.
                    var envelope = age < 0 ? Mathf.Clamp01((age + LightningSpec.Seconds(LightningSpec.TelegraphFrames)) / LightningSpec.Seconds(LightningSpec.TelegraphFrames))
                        : 1 - Mathf.Clamp01((age - LightningSpec.Seconds(LightningSpec.StrikeFrames + LightningSpec.FadeFrames)) / .25f);
                    var sky = LightningSpec.StormSky; sky.a = LightningSpec.StormDarkness(strike.Strength) * envelope;
                    if (sky.a > 0) Draw(stormSolid, StormSkyLayer, view.center, 0, view.size * 1.1f, 0, 1, sky, applyTween: false);
                }
                if (age < 0)
                {
                    // Telegraph: flicker of the sky above each pillar target and a small ground marker on every target.
                    var flicker = ((Mathf.FloorToInt((age + 1) * 60) & 1) == 0 ? .55f : .3f);
                    for (var i = 0; i < strike.Count; i++)
                    {
                        if (!view.Contains(strike.To[i]) && i != 0) continue;
                        DrawMusterRing(strike.To[i], glow * .9f, LightningSpec.Glow, .8f);
                        if (!strike.Sky[i]) continue;
                        var height = PillarHeight(strike.To[i], view);
                        var column = strike.To[i] + Vector2.up * height * .5f;
                        var color = LightningSpec.Glow; color.a = flicker * .2f;
                        Draw(lightningGlow, LightningLayer, column, 0, new Vector2(height, glow * 1.4f), 90, 1, color, applyTween: false);
                    }
                    continue;
                }
                if (!strike.Recorded) RecordStrike(strike, core, glow, pixelsToWorld);
                for (var i = 0; i < strike.Count; i++)
                {
                    var fade = 1 - Mathf.Clamp01((age - .2f) / (LightningSpec.ScorchSeconds - .2f));
                    var scorch = LightningSpec.Scorch; scorch.a = fade * .75f;
                    Draw(lightningScorch, ScorchLayer, strike.To[i], 0, Vector2.one * glow * 2.4f, 0, 1, scorch, applyTween: false);
                }
                var opacity = LightningSpec.StrikeOpacity(age);
                if (opacity <= 0) continue;
                if (age < LightningSpec.Seconds(LightningSpec.ScreenBrightenFrames) && LightningSpec.ScreenBrighten(strike.Storm) > 0)
                {
                    var light = Color.white; light.a = LightningSpec.ScreenBrighten(strike.Storm);
                    Draw(lightningSolid, LightningLayer, view.center, 0, view.size * 1.1f, 0, 1, light, applyTween: false);
                }
                var branches = LightningSpec.Branches(strike.Strength);
                for (var i = 0; i < strike.Count; i++)
                {
                    var to = strike.To[i];
                    if (!view.Contains(to) && !view.Contains(strike.From[i])) continue;
                    var seed = LightningBolt.Hash(strike.Ids[i], 1);
                    if (strike.Sky[i])
                    {
                        var top = PillarTop(to, seed, glow, view);
                        LightningBolt.Build(top, to, seed, .14f, LightningBolt.Generations, boltPoints, 0);
                        DrawBolt(boltPoints, boltPoints.Length, core, glow, opacity);
                        // The storm spreads branches over its pillars so the screen holds strokes, not a white sheet.
                        var count = strike.Storm ? Mathf.Max(2, branches / 2) : branches;
                        for (var b = 0; b < count; b++)
                        {
                            LightningBolt.Branch(boltPoints, 0, boltPoints.Length, LightningBolt.Hash(strike.Ids[i], 10 + b), out var bFrom, out var bTo);
                            LightningBolt.Build(bFrom, bTo, LightningBolt.Hash(strike.Ids[i], 30 + b), .22f, 3, branchPoints, 0);
                            DrawBolt(branchPoints, branchPoints.Length, core * .55f, glow * .6f, opacity * .75f);
                        }
                    }
                    // Arc from the previous link (the hero for the first) to this target: the chain jumping on. In a storm the
                    // pillars carry the read; the many hops stay thin and unbranched.
                    if (!(strike.Storm && i == 0))
                    {
                        var weight = strike.Storm ? .45f : i == 0 ? .6f : 1f;
                        LightningBolt.Build(strike.From[i], to, LightningBolt.Hash(strike.Ids[i], 2), .2f, LightningBolt.Generations, boltPoints, 0);
                        DrawBolt(boltPoints, boltPoints.Length, core * weight, glow * weight, opacity * (strike.Storm ? .7f : i == 0 ? .8f : 1));
                        if (!strike.Storm)
                        {
                            LightningBolt.Branch(boltPoints, 0, boltPoints.Length, LightningBolt.Hash(strike.Ids[i], 3), out var aFrom, out var aTo);
                            LightningBolt.Build(aFrom, aTo, LightningBolt.Hash(strike.Ids[i], 4), .22f, 3, branchPoints, 0);
                            DrawBolt(branchPoints, branchPoints.Length, core * .5f, glow * .55f, opacity * .6f);
                        }
                    }
                    // Target flash: brightest on the strike frames, gone a few frames later.
                    var flashAge = Mathf.Clamp01((age - LightningSpec.Seconds(LightningSpec.StrikeFrames)) / LightningSpec.Seconds(6));
                    var flash = LightningSpec.Core; flash.a = 1 - flashAge;
                    if (flash.a > 0) Draw(lightningFlash, LightningLayer, to, 0, Vector2.one * glow * (strike.Sky[i] ? 4f : 1.6f), 0, 1, flash, applyTween: false);
                }
            }
        }

        // Pillars drop from well above the target but stop short of crossing the whole screen.
        static float PillarHeight(Vector2 target, Rect view) => Mathf.Clamp(view.yMax - target.y, view.height * .2f, view.height * .45f);

        void RecordStrike(LightningStrike strike, float core, float glow, float pixelsToWorld)
        {
            strike.Recorded = true;
            if (LightningStrikeSample == null) return;
            var record = new LightningStrikeRecord { Tick = strike.Tick, Links = strike.Count, AcceptedFrame = strike.AcceptedFrame, VisibleFrame = PresentedFrames, SkyBolts = strike.SkyCount, Storm = strike.Storm, Strength = strike.Strength,
                CorePixels = core / pixelsToWorld, GlowPixels = glow / pixelsToWorld, ScreenFlash = LightningSpec.ScreenBrighten(strike.Storm), FirstSkyTargetScreen = new Vector2(-1, -1), FirstSkyTopScreen = new Vector2(-1, -1) };
            for (var i = 0; i < strike.Count; i++)
            {
                if (!CameraWorldViewport.Contains(strike.To[i])) continue;
                record.OnScreenTargets++;
                if (strike.Sky[i] && record.FirstSkyTargetScreen.x < 0)
                {
                    record.FirstSkyTargetScreen = renderCamera.WorldToScreenPoint(strike.To[i]);
                    record.FirstSkyTopScreen = renderCamera.WorldToScreenPoint(PillarTop(strike.To[i], LightningBolt.Hash(strike.Ids[i], 1), glow, CameraWorldViewport));
                }
                if (strike.Sky[i] && !PillarClearOfHero(strike.To[i], PillarHeight(strike.To[i], CameraWorldViewport), glow)) record.CoveredSkyBolts++;
            }
            LightningStrikeSample(record);
        }

        void DisposeLightning()
        {
            foreach (var visual in new[] { lightningGlow, lightningCore, lightningFlash, lightningSolid, lightningScorch, stormSolid })
                if (visual != null) Destroy(visual.Texture);
        }
    }
}
