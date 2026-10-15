using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Melee swing spec ("무기 연출 레퍼런스·이론", melee row; docs/design/m1-weapon-effect-sprites.md). Frames are 60Hz.
    // Each drawn element is one picture (painted role when the manifest has it, else a temporary generated texture) moved,
    // bent, scaled and faded by code.
    public static class BladeSpec
    {
        // The knight swing clip draws the sword fully back at 0.072s (frame 1 of 6 over 0.36s) and brings it through by
        // 0.216s; the arc waits for the draw-back and sweeps over the downswing (GiantBladeEnvelope.SweepSeconds .15s).
        public const int WindupFrames = 4;           // hero pulls the sword back (3–4)
        public const int HitHoldFrames = 4;          // struck enemies freeze (3–5) and are pushed along the swing
        public const float PushTangent = .75f, PushOutward = .55f;
        // Arc band: one curved ribbon centred on the drawn hero. Its outer edge is the Core reach (clipped to the visible
        // play area), its inner edge reaches in over the strike's hit enemies, never thinner than MinBand hero heights. The
        // picture is a bright blade edge along the outer part and a faint wash inward over the hits. One afterimage trails
        // it by AfterimageFrames.
        public const float MinBandHeroHeights = .45f, VisibleReachFill = .96f;
        public const int ArcColumns = 40, AfterimageFrames = 2;
        public const float AfterimageOpacity = .35f;
        // Evolution element: a giant sword falls point first, speeding up, onto the centre of the struck enemies, squashes
        // as it plants, stays, then splits in two and fades. It never overlaps the hero.
        public const int SwordFallFrames = 9, SwordSquashFrames = 2, SwordSplitFrames = 9;
        public const float SwordStaySeconds = .5f, SwordHeroHeights = 3.2f, SwordDropHeight = .6f, SwordHeroGap = .15f;
        public const float SwordSquash = .14f;       // plant: x wider, y shorter by this fraction on the first squash frame
        public const int DustPuffs = 8;
        public const float CrackRevealSeconds = 6 / 60f;
        public static readonly Color Core = new Color(1f, 1f, 1f);
        public static readonly Color Glow = new Color(.55f, .82f, 1f);   // steel white + sky blue
        public static float Seconds(int frames) => frames / 60f;
        public static float SwordLifetimeSeconds => Seconds(SwordFallFrames) + SwordStaySeconds + Seconds(SwordSplitFrames);
        // Height of the falling sword's tip above where it lands, t seconds after the strike (accelerating fall).
        public static float SwordDrop(float t, float height)
        {
            var fall = Mathf.Clamp01(t / Seconds(SwordFallFrames));
            return height * (1 - fall * fall);
        }
        // Squash on the planting frames: (x, y) scale.
        public static Vector2 SwordScale(float sinceLanding)
        {
            if (sinceLanding < 0) return Vector2.one;
            var frame = Mathf.FloorToInt(sinceLanding * 60);
            if (frame >= SwordSquashFrames) return Vector2.one;
            var k = SwordSquash * (1 - frame / (float)SwordSquashFrames);
            return new Vector2(1 + k, 1 - k);
        }
    }

    public sealed partial class WorldRenderer
    {
        public System.Action<BladeStrikeRecord> BladeStrikeSample;
        // Painted roles (docs/design/m1-weapon-effect-sprites.md); temporary generated pictures until they are in the manifest.
        const string SlashRole = "fx.iron_blade.slash", SwordRole = "fx.iron_blade.giant_sword", CrackRole = "fx.iron_blade.ground_crack", DustRole = "fx.common.dust.";
        ArtVisual bladeSlash, bladeSword, bladeCrack;
        readonly ArtVisual[] bladeDust = new ArtVisual[3];
        readonly List<Texture2D> temporaryEffectTextures = new List<Texture2D>(4);
        // Arc ribbons are curved meshes submitted with the batches (so the P5 mask and alpha cap see them).
        sealed class Ribbon { public Mesh Mesh; public Color Color; public bool Used; }
        readonly List<Ribbon> bladeRibbons = new List<Ribbon>(8);
        Material ribbonMaterial, ribbonOverEnemies;
        MaterialPropertyBlock ribbonProperties;   // created on first use (not allowed in a MonoBehaviour field initializer)
        static readonly Matrix4x4[] RibbonMatrix = { Matrix4x4.identity };
        readonly Vector4[] ribbonColor = new Vector4[1], ribbonUv = new Vector4[1];

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
            strike.HitCount = 0; strike.HitSum = Vector2.zero; strike.SwordPlaced = false; strike.Centre = strike.Origin;
            var visible = M1BladeVisibleBounds();
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
                if (!visible.Contains(hit.Origin) || strike.HitCount == strike.Hits.Length) continue;
                strike.Hits[strike.HitCount++] = hit.Origin; strike.HitSum += hit.Origin;
            }
        }

        // The arc follows the drawn hero (the sword is in its hands); its inner edge is the nearest hit from there.
        void CentreBladeArc(M1BladeStrike strike)
        {
            strike.Centre = RenderedLordPosition; strike.Inner = float.PositiveInfinity;
            for (var i = 0; i < strike.HitCount; i++) strike.Inner = Mathf.Min(strike.Inner, (strike.Hits[i] - strike.Centre).magnitude);
        }

        // Outer edge of the arc along a bearing: the Core reach, clipped to the visible play area.
        float BladeOuter(M1BladeStrike strike, float degrees, Rect visible)
            => Mathf.Min(strike.CoreRadius, GiantBladeEnvelope.VisibleReach(strike.Centre, M1Direction(degrees), visible) * BladeSpec.VisibleReachFill);

        float BladeInner(M1BladeStrike strike, float outer)
        {
            var inner = float.IsInfinity(strike.Inner) ? outer - HeroHeight * BladeSpec.MinBandHeroHeights : strike.Inner - HeroHeight * .1f;
            return Mathf.Max(0, Mathf.Min(inner, outer - HeroHeight * BladeSpec.MinBandHeroHeights));
        }

        void DrawBladeStrikes()
        {
            if (waveDefinition == null) return;
            var any = false;
            foreach (var strike in m1BladeStrikes)
                if (strike.EventId >= 0 && visualTime - strike.Accepted < BladeLingerSeconds) { any = true; break; }
            if (!any) return;
            EnsureLightningArt(); EnsureBladeArt();
            foreach (var strike in m1BladeStrikes)
            {
                if (strike.EventId < 0 || visualTime - strike.Accepted >= BladeLingerSeconds) continue;
                FitM1Blade(strike);   // kill envelope metrics (kills stay inside the drawn fan)
                if (strike.Evolved) DrawGiantSwordDrop(strike, visualTime - strike.Accepted);
                var age = visualTime - strike.Visible;
                if (age < 0 || age > GiantBladeEnvelope.LifetimeSeconds) continue;
                CentreBladeArc(strike);
                DrawBladeArc(strike, age, 1);
                var echo = age - BladeSpec.Seconds(BladeSpec.AfterimageFrames);
                if (echo > 0) DrawBladeArc(strike, echo, BladeSpec.AfterimageOpacity);
                if (!strike.Recorded) RecordBladeStrike(strike);
            }
        }

        // One curved ribbon from the trail's oldest end to the blade's leading edge; the picture runs tail (u 0) to tip
        // (u 1) along the arc and inner (v 0) to outer edge (v 1) across it. After the hold it disappears toward the tip.
        void DrawBladeArc(M1BladeStrike strike, float age, float opacity)
        {
            opacity *= GiantBladeEnvelope.Opacity(age);
            if (opacity <= 0) return;
            var half = GiantBladeEnvelope.CoreHalfAngle; var aim = strike.Aim;
            var start = aim - half;
            var lead = GiantBladeEnvelope.SweepAngle(aim, half, age);
            var hold = Mathf.Max(GiantBladeEnvelope.SweepSeconds, GiantBladeEnvelope.HoldSeconds);
            var fade = Mathf.Clamp01((age - hold) / GiantBladeEnvelope.FadeSeconds);
            var tail = Mathf.Lerp(start, lead, fade * fade);
            if (lead - tail < .5f) return;
            var visible = M1BladeVisibleBounds();
            var ribbon = NextRibbon();
            var columns = BladeSpec.ArcColumns;
            var vertices = new Vector3[(columns + 1) * 2]; var uvs = new Vector2[vertices.Length]; var triangles = new int[columns * 6];
            // The picture spans the whole swing: the drawn part shows the slice from the tail to the leading edge.
            for (var i = 0; i <= columns; i++)
            {
                var degrees = Mathf.Lerp(tail, lead, i / (float)columns);
                var outer = BladeOuter(strike, degrees, visible); var inner = BladeInner(strike, outer);
                var direction = M1Direction(degrees);
                vertices[i * 2] = strike.Centre + direction * inner; vertices[i * 2 + 1] = strike.Centre + direction * outer;
                var u = Mathf.InverseLerp(start, lead, degrees);
                uvs[i * 2] = new Vector2(u, 0); uvs[i * 2 + 1] = new Vector2(u, 1);
                if (i == columns) continue;
                var t = i * 6; var v = i * 2;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 3; triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            var mesh = ribbon.Mesh; mesh.Clear(); mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles; mesh.RecalculateBounds();
            var color = Color.white; color.a = opacity; ribbon.Color = color;
        }

        Ribbon NextRibbon()
        {
            foreach (var ribbon in bladeRibbons) if (!ribbon.Used) { ribbon.Used = true; return ribbon; }
            var created = new Ribbon { Mesh = new Mesh { name = "Blade arc ribbon", hideFlags = HideFlags.HideAndDontSave }, Used = true };
            created.Mesh.MarkDynamic(); bladeRibbons.Add(created);
            return created;
        }

        // Submitted with the batches: normal blend above enemies, alpha capped over them like every own weapon effect.
        void FlushBladeRibbons()
        {
            if (bladeRibbons.Count == 0) return;
            if (ribbonMaterial == null)
            {
                ribbonMaterial = new Material(shader) { name = "Blade arc ribbon", mainTexture = bladeSlash.Texture, renderQueue = 3000 + ProjectileLayer, hideFlags = HideFlags.HideAndDontSave };
                ribbonOverEnemies = EffectAlphaCap.Configure(ribbonMaterial, ProjectileLayer);
                ribbonProperties = new MaterialPropertyBlock();
            }
            var rect = bladeSlash.UvRects[0];
            ribbonUv[0] = new Vector4(rect.x, rect.y, rect.width, rect.height);
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var ribbon in bladeRibbons)
            {
                if (!ribbon.Used) continue;
                ribbon.Used = false;
                ribbonColor[0] = linear ? ribbon.Color.linear : ribbon.Color;
                ribbonProperties.SetTexture("_MainTex", bladeSlash.Texture);
                ribbonProperties.SetVector("_InstanceColor", ribbonColor[0]); ribbonProperties.SetVector("_InstanceUv", ribbonUv[0]);
                ribbonProperties.SetVector("_InstanceFlash", new Vector4(0, 0, 1, 0));
                var parameters = new RenderParams(ribbonMaterial) { camera = null, layer = 0, matProps = ribbonProperties, shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off };
                OcclusionProbe.Active?.Record(ProjectileLayer, ribbon.Mesh, RibbonMatrix, 1, bladeSlash.Texture, ribbonColor, ribbonUv);
                Graphics.RenderMesh(parameters, ribbon.Mesh, 0, Matrix4x4.identity);
                parameters.material = ribbonOverEnemies;
                Graphics.RenderMesh(parameters, ribbon.Mesh, 0, Matrix4x4.identity);
                DrawCalls += 2;
            }
        }

        // Where the giant sword plants: the centre of the strike's on-screen hits (or along the aim without any), moved
        // sideways off the hero and kept inside the visible play area.
        Vector2 SwordLanding(M1BladeStrike strike, float length, float width, out float shift)
        {
            var visible = M1BladeVisibleBounds();
            var crowd = strike.HitCount > 0 ? strike.HitSum / strike.HitCount : strike.Origin + M1Direction(strike.Aim) * (BladeOuter(strike, strike.Aim, visible) * .6f);
            var landing = crowd;
            var hero = SwordHeroBox(strike);
            if (SwordOverlapsHero(landing, length, width, hero))
            {
                var side = Mathf.Sign(landing.x - hero.center.x); if (side == 0) side = Mathf.Sign(M1Direction(strike.Aim).x); if (side == 0) side = 1;
                var away = hero.width * .5f + width * .5f + HeroHeight * BladeSpec.SwordHeroGap;
                landing.x = hero.center.x + side * away;
                if (landing.x - width * .5f < visible.xMin || landing.x + width * .5f > visible.xMax) landing.x = hero.center.x - side * away;
            }
            // The whole sword stays on screen, hilt included.
            landing.x = Mathf.Clamp(landing.x, visible.xMin + width * .5f, visible.xMax - width * .5f);
            landing.y = Mathf.Clamp(landing.y, visible.yMin + HeroHeight * .2f, visible.yMax - length * (1 + BladeSpec.SwordSquash));
            if (SwordOverlapsHero(landing, length, width, hero))
            {
                // Lowered under the top edge into the hero's column: step aside again.
                var side = Mathf.Sign(landing.x - hero.center.x); if (side == 0) side = 1;
                var away = hero.width * .5f + width * .5f + HeroHeight * BladeSpec.SwordHeroGap;
                landing.x = hero.center.x + side * away;
                if (landing.x - width * .5f < visible.xMin || landing.x + width * .5f > visible.xMax) landing.x = hero.center.x - side * away;
            }
            shift = (landing - crowd).magnitude;
            return landing;
        }

        // The hero's drawn silhouette box (or a figure-sized box at the strike origin before it is known).
        Rect SwordHeroBox(M1BladeStrike strike)
            => heroSilhouetteLocal.width > 0 ? HeroBox
                : Rect.MinMaxRect(strike.Origin.x - HeroHeight * .35f, strike.Origin.y - HeroHeight * .1f, strike.Origin.x + HeroHeight * .35f, strike.Origin.y + HeroHeight * 1.05f);

        // The planted sword's box (tip at the landing point) against the hero's box.
        static bool SwordOverlapsHero(Vector2 tip, float length, float width, Rect hero)
            => hero.Overlaps(Rect.MinMaxRect(tip.x - width * .5f, tip.y, tip.x + width * .5f, tip.y + length));

        float SwordLength => HeroHeight * BladeSpec.SwordHeroHeights;
        float SwordWidth => SwordLength * bladeSword.WorldSize.x / bladeSword.WorldSize.y;

        // t: seconds since the Core strike. Falls over SwordFallFrames, plants (squash, cracks, dust, shake), stays, splits.
        void DrawGiantSwordDrop(M1BladeStrike strike, float t)
        {
            var length = SwordLength; var width = SwordWidth;
            if (!strike.SwordPlaced)
            {
                strike.SwordAt = SwordLanding(strike, length, width, out strike.SwordShift); strike.SwordPlaced = true;
                strike.SwordClear = !SwordOverlapsHero(strike.SwordAt, length, width, SwordHeroBox(strike));
            }
            var landing = strike.SwordAt;
            var fall = BladeSpec.Seconds(BladeSpec.SwordFallFrames);
            var since = t - fall;
            var split = since - BladeSpec.SwordStaySeconds;
            if (split > BladeSpec.Seconds(BladeSpec.SwordSplitFrames)) return;
            var view = CameraWorldViewport;
            var drop = BladeSpec.SwordDrop(t, view.height * BladeSpec.SwordDropHeight);
            var tip = landing + Vector2.up * drop;
            if (since < 0)
            {
                // A faint streak above the falling sword (code motion, not part of the picture).
                var streak = BladeSpec.Glow; streak.a = .35f * Mathf.Clamp01(t / fall);
                Draw(lightningGlow, LightningLayer, tip + Vector2.up * (length * .9f), 0, new Vector2(length * 1.2f, width * .45f), 90, 1, streak, applyTween: false);
            }
            else if (!strike.ShakeFired) { strike.ShakeFired = true; impactShakeUntil = visualTime + GameVisualTokens.ImpactShakeSeconds; }
            if (since >= 0) DrawSwordImpact(strike, landing, length, since);
            var scale = BladeSpec.SwordScale(since);
            var size = new Vector2(width * scale.x, length * scale.y);
            if (split < 0) { Draw(bladeSword, ProjectileLayer, tip, 0, size, 0, 1, Color.white, applyTween: false); return; }
            // Split: the two halves slide apart, tilt and fade (the painted shard mask replaces the straight cut).
            var s = split / BladeSpec.Seconds(BladeSpec.SwordSplitFrames);
            var rect = bladeSword.UvRects[0];
            var color = Color.white; color.a = 1 - s * s;
            for (var side = -1; side <= 1; side += 2)
            {
                var halfRect = new Rect(side < 0 ? rect.x : rect.x + rect.width * .5f, rect.y, rect.width * .5f, rect.height);
                // Each half keeps the picture's pivot (near the point), so it tilts outward from where it stands.
                Draw(bladeSword, ProjectileLayer, tip + new Vector2(side * width * (.25f + .35f * s), 0), 0, new Vector2(width * .5f, length), -side * 8 * s, 1, color, applyTween: false, uv: halfRect);
            }
        }

        // Where the giant sword lands: the crack picture revealed from the centre, and dust puffs bursting out.
        void DrawSwordImpact(M1BladeStrike strike, Vector2 impact, float length, float since)
        {
            var end = BladeSpec.SwordStaySeconds + BladeSpec.Seconds(BladeSpec.SwordSplitFrames);
            if (since > end) return;
            var fade = 1 - Mathf.Clamp01((since - BladeSpec.SwordStaySeconds) / BladeSpec.Seconds(BladeSpec.SwordSplitFrames));
            var reveal = Mathf.Lerp(.3f, 1, Mathf.Clamp01(since / BladeSpec.CrackRevealSeconds));
            var crackWidth = length * .62f;
            var crack = Color.white; crack.a = fade;
            Draw(bladeCrack, ScorchLayer, impact, 0, new Vector2(crackWidth, crackWidth * bladeCrack.WorldSize.y / bladeCrack.WorldSize.x) * reveal, 0, 1, crack, applyTween: false);
            if (since > .6f) return;
            for (var k = 0; k < BladeSpec.DustPuffs; k++)
            {
                var seed = LightningBolt.Hash(strike.EventId, 90 + k);
                var direction = M1Direction(k * 360f / BladeSpec.DustPuffs + (seed & 63));
                direction.y *= .5f;   // 3/4 view: the burst spreads along the ground
                var travel = length * .32f * Mathf.Sqrt(Mathf.Clamp01(since / .35f));
                var puff = bladeDust[k % bladeDust.Length];
                var dust = new Color(.84f, .8f, .72f, .75f * (1 - since / .6f));
                var size = length * .2f * (.8f + since) * (.85f + ((seed >> 8) & 63) / 210f);
                Draw(puff, GameVisualTokens.WaveTransientCueLayer, impact + direction * travel, 0, puff.WorldSize * (size / Mathf.Max(puff.WorldSize.x, puff.WorldSize.y)), (seed >> 4) & 255, 1, dust, applyTween: false);
            }
        }

        static float BladeLingerSeconds => Mathf.Max(BladeSpec.Seconds(BladeSpec.WindupFrames) + GiantBladeEnvelope.LifetimeSeconds, BladeSpec.SwordLifetimeSeconds);
        float PixelsToWorld => renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
        // Visible hero height (not the padded overlap box): the size reference for weapon effects.
        float HeroHeight => heroFigureHeight > 0 ? heroFigureHeight : 2.2f * (heroBodyWidth > 0 ? heroBodyWidth : .6f);

        void EnsureBladeArt()
        {
            if (bladeSlash != null) return;
            bladeSlash = EffectArt(SlashRole, () => TemporarySlash());
            bladeSword = EffectArt(SwordRole, () => TemporarySword());
            bladeCrack = EffectArt(CrackRole, () => TemporaryCrack());
            for (var i = 0; i < bladeDust.Length; i++) bladeDust[i] = EffectArt(DustRole + (i + 1), () => impactDust ?? Resolve("feedback", "death", "default"));
        }

        // The painted role when the art manifest has it; otherwise the temporary picture (same pivot and proportions).
        ArtVisual EffectArt(string role, System.Func<ArtVisual> temporary)
            => art != null && art.TryResolveRole(role, out var painted) ? painted : temporary();

        Texture2D PaintTexture(string name, int width, int height, System.Func<int, int, Color> paint)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) pixels[y * width + x] = paint(x, y);
            texture.SetPixels32(pixels); texture.Apply(false, true);
            temporaryEffectTextures.Add(texture);
            return texture;
        }

        // Band picture: a bright blade edge along the outer part (white at the very edge over a thin ink line, saturated sky
        // blue below it) and a faint blue wash inward over the hits; thin and faint at the tail (u 0), full at the tip (u 1).
        ArtVisual TemporarySlash()
        {
            const int width = 256, height = 128;
            var ink = GameVisualTokens.Ink; var blue = new Color(.3f, .66f, 1f);
            var texture = PaintTexture("Temporary blade slash", width, height, (x, y) =>
            {
                var u = (x + .5f) / width; var v = (y + .5f) / height;
                var tailFade = Mathf.SmoothStep(0, 1, u / .3f);
                var edge = .18f + .14f * Mathf.Pow(u, 1.2f);         // bright edge: share of the band from the outer side
                var into = (1 - v) / edge;                            // 0 at the outer edge, 1 where the bright edge ends
                if (y == height - 1) { var line = ink; line.a = .9f * tailFade; return line; }
                Color color;
                if (into <= 1)
                {
                    color = Color.Lerp(BladeSpec.Core, blue, Mathf.SmoothStep(.2f, .7f, into));
                    color.a = Mathf.Lerp(1, .55f, Mathf.SmoothStep(.5f, 1, into));
                }
                else
                {
                    // Faint wash from the end of the bright edge to the inner edge, softened at the inner edge.
                    color = blue;
                    color.a = Mathf.Lerp(.55f, .14f, Mathf.SmoothStep(0, .12f, (1 - v) - edge)) * Mathf.SmoothStep(0, .06f, v);
                }
                color.a *= tailFade;
                return color;
            });
            return new ArtVisual(SlashRole, texture, new Vector2(width, height) / 384f, new Vector2(0, .5f));
        }

        // Two-handed sword, point down: steel blade lit from the upper left with a bright fuller, gold guard and pommel,
        // brown grip; charcoal ink outline. Pivot at the point.
        ArtVisual TemporarySword()
        {
            const int width = 140, height = 400; const float cx = width * .5f;
            int Part(float x, float y)
            {
                var dx = Mathf.Abs(x - cx);
                if (y >= 8 && y < 290) { var half = y < 48 ? 22 * (y - 8) / 40f : 22; if (dx <= half) return 1; }
                if (y >= 290 && y < 312 && dx <= 62 - (y - 290) * .3f) return 2;
                if (y >= 312 && y < 366 && dx <= 11) return 3;
                if ((new Vector2(x, y) - new Vector2(cx, 378)).magnitude <= 14) return 4;
                return 0;
            }
            var parts = new int[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) parts[y * width + x] = Part(x + .5f, y + .5f);
            int At(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? 0 : parts[y * width + x];
            var ink = GameVisualTokens.Ink; ink.a = 1;
            var texture = PaintTexture("Temporary giant sword", width, height, (x, y) =>
            {
                var part = At(x, y);
                if (part == 0)
                {
                    // 4px ink outline around the silhouette.
                    for (var dy = -4; dy <= 4; dy++) for (var dx = -4; dx <= 4; dx++)
                        if (dx * dx + dy * dy <= 16 && At(x + dx, y + dy) != 0) return ink;
                    return new Color(0, 0, 0, 0);
                }
                // 2px ink line between parts.
                for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++)
                { var other = At(x + dx, y + dy); if (other != 0 && other != part && part != 1 && dx * dx + dy * dy <= 4) return ink; }
                var left = x + .5f < cx;
                switch (part)
                {
                    case 1:
                        if (Mathf.Abs(x + .5f - cx) < 4 && y > 60 && y < 272) return new Color(.98f, .98f, .97f);
                        return left ? new Color(.9f, .9f, .9f) : new Color(.62f, .62f, .64f);
                    case 2: return y > 304 ? new Color(.96f, .82f, .45f) : new Color(.78f, .58f, .24f);
                    case 3: return (y / 9) % 2 == 0 ? new Color(.38f, .23f, .12f) : new Color(.3f, .17f, .09f);
                    default: return left && y > 378 ? new Color(.96f, .82f, .45f) : new Color(.8f, .6f, .26f);
                }
            });
            return new ArtVisual(SwordRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .02f));
        }

        // Radial ground cracks in a 3/4-view ellipse: ink cracks tapering outward, a warm brown lip along each.
        ArtVisual TemporaryCrack()
        {
            const int width = 256, height = 128;
            var segments = new List<(Vector2 a, Vector2 b, float wa, float wb)>(32);
            for (var k = 0; k < 8; k++)
            {
                var seed = LightningBolt.Hash(266, k);
                var angle = (k * 45 + ((seed & 255) / 255f - .5f) * 28) * Mathf.Deg2Rad;
                var reach = Mathf.Lerp(.62f, .95f, ((seed >> 8) & 255) / 255f);
                var previous = Vector2.zero; var steps = 3;
                for (var s = 1; s <= steps; s++)
                {
                    var t = s / (float)steps;
                    var bend = s < steps ? (((seed >> (12 + s * 3)) & 7) / 7f - .5f) * .35f : 0;
                    var direction = new Vector2(Mathf.Cos(angle + bend), Mathf.Sin(angle + bend));
                    var point = direction * (reach * t);
                    segments.Add((previous, point, Mathf.Lerp(5, 1, t - 1f / steps), Mathf.Lerp(5, 1, t)));
                    previous = point;
                }
            }
            var ink = GameVisualTokens.Ink; ink.a = 1; var lip = new Color(.55f, .44f, .33f);
            var texture = PaintTexture("Temporary ground crack", width, height, (x, y) =>
            {
                // Map to the unit circle of the ellipse (x radius 120px, y radius 60px).
                var p = new Vector2((x + .5f - width * .5f) / 120f, (y + .5f - height * .5f) / 60f);
                var best = float.PositiveInfinity; var widthAt = 1f;
                foreach (var (a, b, wa, wb) in segments)
                {
                    var ab = b - a; var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    var d = (p - (a + ab * t)); d.x *= 120; d.y *= 60;
                    var distance = d.magnitude;
                    if (distance < best) { best = distance; widthAt = Mathf.Lerp(wa, wb, t); }
                }
                if (best <= widthAt * .5f) return ink;
                if (best <= widthAt * .5f + 2) { var c = lip; c.a = .85f; return c; }
                return new Color(0, 0, 0, 0);
            });
            return new ArtVisual(CrackRole, texture, new Vector2(width, height) / 384f, new Vector2(.5f, .5f));
        }

        void DisposeBladeArt()
        {
            foreach (var texture in temporaryEffectTextures) if (texture != null) Destroy(texture);
            temporaryEffectTextures.Clear();
            foreach (var ribbon in bladeRibbons) if (ribbon.Mesh != null) Destroy(ribbon.Mesh);
            bladeRibbons.Clear();
            if (ribbonMaterial != null) Destroy(ribbonMaterial);
            if (ribbonOverEnemies != null) Destroy(ribbonOverEnemies);
            bladeSlash = bladeSword = bladeCrack = null;
        }

        void RecordBladeStrike(M1BladeStrike strike)
        {
            strike.Recorded = true;
            if (BladeStrikeSample == null) return;
            var visible = M1BladeVisibleBounds();
            var outer = BladeOuter(strike, strike.Aim, visible); var inner = BladeInner(strike, outer);
            // Hits the drawn band passes over: inside the Core fan, between the band's inner and outer edge on their bearing.
            var covered = 0;
            for (var i = 0; i < strike.HitCount; i++)
            {
                var offset = strike.Hits[i] - strike.Centre; var bearing = Angle(offset);
                if (Mathf.Abs(Mathf.DeltaAngle(strike.Aim, bearing)) > GiantBladeEnvelope.CoreHalfAngle + 1) continue;
                var edge = BladeOuter(strike, bearing, visible);
                if (offset.magnitude >= BladeInner(strike, edge) - HeroHeight * .05f && offset.magnitude <= edge + HeroHeight * .05f) covered++;
            }
            Vector2 Screen(Vector2 world) => renderCamera.WorldToScreenPoint(world);
            var crowd = strike.HitCount > 0 ? strike.HitSum / strike.HitCount : strike.Origin;
            var swordAt = strike.SwordAt;
            BladeStrikeSample(new BladeStrikeRecord { Tick = strike.Tick, AcceptedFrame = strike.AcceptedFrame, VisibleFrame = PresentedFrames, Evolved = strike.Evolved,
                Radius = strike.CoreRadius, HalfAngle = GiantBladeEnvelope.CoreHalfAngle, OriginScreen = Screen(strike.Centre), Aim = strike.Aim,
                HeroScreen = Screen(RenderedLordPosition),
                CoreRadiusPixels = strike.CoreRadius / PixelsToWorld,
                RadiusPixels = Screen(strike.Centre + M1Direction(strike.Aim) * outer) - Screen(strike.Centre),
                InnerPixels = inner / PixelsToWorld, HeroHeightPixels = HeroHeight / PixelsToWorld, ThicknessPixels = (outer - inner) / PixelsToWorld,
                Hits = strike.HitCount, HitsCovered = covered,
                SwordLengthPixels = strike.Evolved ? SwordLength / PixelsToWorld : 0, SwordWidthPixels = strike.Evolved ? SwordWidth / PixelsToWorld : 0,
                SwordFallFrames = strike.Evolved ? BladeSpec.SwordFallFrames : 0,
                SwordTipScreen = strike.Evolved ? Screen(swordAt) : new Vector2(-1, -1), CrowdScreen = Screen(crowd),
                SwordClearOfHero = !strike.Evolved || strike.SwordClear });
        }
    }

    public struct BladeStrikeRecord
    {
        public int Tick, AcceptedFrame, VisibleFrame, Hits, HitsCovered, SwordFallFrames;
        public bool Evolved, SwordClearOfHero;
        public float Radius, HalfAngle, Aim, HeroHeightPixels, ThicknessPixels, InnerPixels, CoreRadiusPixels, SwordLengthPixels, SwordWidthPixels;
        public Vector2 OriginScreen, HeroScreen, RadiusPixels, SwordTipScreen, CrowdScreen;
    }
}
