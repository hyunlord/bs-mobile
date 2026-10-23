using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // T1 dark-buff mark on enemies standing in the dark (#347): a thin dark outline or two eye glints. The user compares both
    // in the review scene; the design draft (m2-dark-estate-buildings.md 6.2) names the eye glint.
    public enum EstateDarkMarker { Eyes, Outline }

    // M2 dark estate View, first slice (#347). Shows only when a run's wave frame carries an estate (the opt-in profile
    // wave-2a-dark); every default profile has none, so nothing here runs and the shader's estate block stays off.
    // - Darkness: the fallow ground and growth shaders darken everything outside the lit circles (home and awakened buildings)
    //   and keep the hero's sight fallow grey, with the approved RIM 3 rim at the lit edge (Release
    //   m2-building-game-assets-20261019). Circles go to the shader as constants; no pass, render target or mask change.
    // - Buildings: Astra's sleeping/awake sheets at Core's slot positions. Waking cross-fades over the 1.4 s harvest bloom.
    // - Progress ring: one ring of dots above each sleeping building, filled from Core's progress only.
    // - T1 mark: enemies outside every lit circle (Core's own rule for the dark bonus).
    // - Building effects (user decisions 2026-10-23), from Core events and frames only. Core's watchtower hits at once; the View flies
    //   Astra's arrow from the tower top to the hit point in 0.15 s and shows the impact and the damage number on arrival. The well
    //   heal ring loops at the well while the hero stands in its light below full health, then holds its last frame and fades out
    //   within 0.2 s. The granary inward ring loops at the granary while the hero stands in its light.
    public sealed partial class WorldRenderer
    {
        public const string EstateArrowRole = "fx.estate.watchtower.arrow", EstateImpactRole = "fx.estate.watchtower.impact",
            EstateHealRingRole = "fx.estate.well.heal_ring", EstateMagnetRingRole = "fx.estate.granary.magnet_ring";
        const int EstateArrowCapacity = 16;
        struct EstateArrow { public Vector2 From, To; public float Started; public int Subject, Amount; public bool Live, Landed; }
        readonly EstateArrow[] estateArrows = new EstateArrow[EstateArrowCapacity];
        int estateArrowNext;
        readonly Dictionary<int, (bool on, float since)> estateRingStates = new Dictionary<int, (bool, float)>();
        ArtVisual estateArrowArt, estateImpactArt, estateHealArt, estateMagnetArt;
        public int EstateArrowDraws { get; private set; }
        public int EstateImpactDraws { get; private set; }
        public int EstateHealRingDraws { get; private set; }
        public int EstateMagnetRingDraws { get; private set; }
        public int EstateArrowNumbers { get; private set; }
        public const int EstateMaximumCircles = 9;
        static readonly int EstateActiveId = Shader.PropertyToID("_EstateActive"), EstateCirclesId = Shader.PropertyToID("_EstateCircles"),
            EstateCountId = Shader.PropertyToID("_EstateCircleCount"), EstateSightId = Shader.PropertyToID("_EstateSight"),
            EstatePixelsId = Shader.PropertyToID("_EstatePixelsPerUnit");
        readonly Vector4[] estateCircles = new Vector4[EstateMaximumCircles];
        readonly Dictionary<int, float> estateWoken = new Dictionary<int, float>();
        ShapeMeshes estateMeshes;
        ShapeBatch estateRings, estateEyes;
        bool estateShaderOn;
        public EstateDarkMarker DarkMarker { get; set; } = EstateDarkMarker.Eyes;
        public int EstateCircleCount { get; private set; }
        public int EstateBuildingDraws { get; private set; }
        public int EstateRingDraws { get; private set; }
        public int EstateDarkMarks { get; private set; }
        bool EstateActive => FallowActive && wave?.Estate != null;

        // --estate-dark-marker eyes|outline (review comparison); eyes when absent.
        public static EstateDarkMarker ParseDarkMarker(string[] args)
        {
            var index = Array.IndexOf(args, "--estate-dark-marker");
            if (index < 0) return EstateDarkMarker.Eyes;
            if (index + 1 >= args.Length) throw new ArgumentException("--estate-dark-marker requires eyes or outline.");
            switch (args[index + 1])
            {
                case "eyes": return EstateDarkMarker.Eyes;
                case "outline": return EstateDarkMarker.Outline;
                default: throw new ArgumentException("--estate-dark-marker must be eyes or outline.");
            }
        }

        // Core's lit rule (WaveEstateSystem.Lit): inside the lit circle of an active building, the home included.
        public static bool EstateLit(WaveEstateFrame estate, WorldPoint point)
        {
            for (var i = 0; i < estate.Buildings.Count; i++)
            {
                var b = estate.Buildings[i];
                if (b.Phase != WaveEstatePhase.Active) continue;
                long dx = (long)point.X - b.Position.X, dy = (long)point.Y - b.Position.Y;
                if (dx * dx + dy * dy <= (long)b.LitRadius * b.LitRadius) return true;
            }
            return false;
        }

        // Ring fill from Core state only: a paying building shows its confirming stay until paid, then (wake Both) its channel.
        public static float EstateWakeFraction(WaveEstateBuildingView b)
        {
            if (b.Phase == WaveEstatePhase.Active) return 1;
            if (b.ConfirmTicks > 0 && !b.Paid) return Mathf.Clamp01((float)b.Dwell / b.ConfirmTicks);
            return b.Required > 0 ? Mathf.Clamp01((float)b.Progress / b.Required) : 0;
        }
        // Stay complete but unpaid: the cost cannot be met yet; the ring dims.
        public static bool EstateWakeStalled(WaveEstateBuildingView b) => b.ConfirmTicks > 0 && !b.Paid && b.Dwell >= b.ConfirmTicks;

        void ResetEstate()
        {
            estateWoken.Clear(); estateRingStates.Clear();
            for (var i = 0; i < estateArrows.Length; i++) estateArrows[i].Live = false;
        }

        // Flight progress of a watchtower arrow: 0 at the shot, 1 on arrival (the hit, the impact and the number).
        public static float EstateArrowProgress(float elapsed) => Mathf.Clamp01(elapsed / GameVisualTokens.EstateArrowSeconds);
        // Heal ring opacity after its condition ends: the last frame fades to nothing within the fade time.
        public static float EstateHealFade(float elapsedSinceOff) => 1 - Mathf.Clamp01(elapsedSinceOff / GameVisualTokens.EstateHealFadeSeconds);
        public static bool EstateLordInside(WaveEstateBuildingView b, WorldPoint lord)
        {
            long dx = (long)lord.X - b.Position.X, dy = (long)lord.Y - b.Position.Y;
            return dx * dx + dy * dy <= (long)b.LitRadius * b.LitRadius;
        }

        void AcceptEstateEvent(WaveEvent value)
        {
            if (value.Kind == "estate-shot")
            {
                var tower = Resolve("estate", value.Source, "active");
                ref var arrow = ref estateArrows[estateArrowNext++ % EstateArrowCapacity];
                arrow.From = Point(value.Position) + Vector2.up * (tower.WorldSize.y * (1 - tower.Pivot.y) * GameVisualTokens.EstateArrowLaunchHeight);
                arrow.To = Point(value.Target) + Vector2.up * GameVisualTokens.EstateArrowHitRise;
                arrow.Started = visualTime; arrow.Subject = value.SubjectId; arrow.Amount = value.Amount; arrow.Live = true; arrow.Landed = false;
                return;
            }
            if (value.Kind != "estate-wake-complete") return;
            estateWoken[value.SubjectId] = visualTime;
            // The awakening reuses the harvest bloom (design 6.2) at the building.
            fallowHarvests.Add((Point(value.Position), visualTime));
        }

        void UpdateEstateShader(Vector2 lord)
        {
            if (!EstateActive)
            {
                if (estateShaderOn) { Shader.SetGlobalFloat(EstateActiveId, 0); estateShaderOn = false; }
                EstateCircleCount = 0;
                return;
            }
            var estate = wave.Estate; var count = 0;
            for (var i = 0; i < estate.Buildings.Count && count < EstateMaximumCircles; i++)
            {
                var b = estate.Buildings[i];
                if (b.Phase != WaveEstatePhase.Active) continue;
                var center = Point(b.Position);
                estateCircles[count++] = new Vector4(center.x, center.y, (float)b.LitRadius / settings.WorldUnitsPerUnityUnit, 0);
            }
            for (var i = count; i < EstateMaximumCircles; i++) estateCircles[i] = Vector4.zero;
            EstateCircleCount = count;
            Shader.SetGlobalVectorArray(EstateCirclesId, estateCircles);
            Shader.SetGlobalFloat(EstateCountId, count);
            Shader.SetGlobalVector(EstateSightId, new Vector4(lord.x, lord.y, (float)estate.SightRadius / settings.WorldUnitsPerUnityUnit, 0));
            Shader.SetGlobalFloat(EstatePixelsId, GameVisualTokens.EstateReviewPixelsPerUnit);
            Shader.SetGlobalFloat(EstateActiveId, 1);
            estateShaderOn = true;
        }

        void DrawEstate(RunFrame current, Vector2 lord)
        {
            EstateBuildingDraws = EstateRingDraws = EstateArrowDraws = EstateImpactDraws = EstateHealRingDraws = EstateMagnetRingDraws = 0;
            if (!EstateActive) return;
            DrawEstateEffects(current);
            if (estateRings == null)
            {
                estateMeshes = new ShapeMeshes();
                var shapeShader = Resources.Load<Shader>("WorldShape");
                estateRings = new ShapeBatch(estateMeshes[WorldShape.Circle], shapeShader, renderCamera, GameVisualTokens.WaveReadinessCueLayer);
                estateEyes = new ShapeBatch(estateMeshes[WorldShape.Circle], shapeShader, renderCamera, GameVisualTokens.WaveAttackEdgeLayer, wide: false);
                estateRings.BeginFrame(); estateEyes.BeginFrame();
            }
            var home = waveDefinition.EstateLight.HomeId;
            var estate = wave.Estate;
            for (var i = 0; i < estate.Buildings.Count; i++)
            {
                var b = estate.Buildings[i];
                if (b.DefinitionId == home) continue;   // the home stays the field's own cottage
                var at = Point(b.Position);
                var asleep = Resolve("estate", b.DefinitionId, "asleep");
                var awake = Resolve("estate", b.DefinitionId, "active");
                var shown = b.Phase == WaveEstatePhase.Active ? 1f : 0f;
                if (shown > 0 && estateWoken.TryGetValue(b.Id, out var woke)) shown = Mathf.Clamp01((visualTime - woke) / GameVisualTokens.FallowHarvestSeconds);
                if (shown < 1) Draw(asleep, GameVisualTokens.EstateBuildingLayer, at, 0, opacity: 1 - shown);
                if (shown > 0) Draw(awake, GameVisualTokens.EstateBuildingLayer, at, 0, opacity: shown);
                EstateBuildingDraws++;
                if (b.Phase == WaveEstatePhase.Active) continue;
                var fraction = EstateWakeFraction(b);
                var near = (Point(b.Position) - lord).magnitude <= GameVisualTokens.EstateRingShowWakeRadii * b.WakeRadius / settings.WorldUnitsPerUnityUnit;
                if (fraction <= 0 && !near) continue;
                DrawEstateRing(at + Vector2.up * (asleep.WorldSize.y * (1 - asleep.Pivot.y) + GameVisualTokens.EstateRingRise), fraction, EstateWakeStalled(b));
            }
        }

        void DrawEstateEffects(RunFrame current)
        {
            estateArrowArt ??= art.ResolveRole(EstateArrowRole); estateImpactArt ??= art.ResolveRole(EstateImpactRole);
            estateHealArt ??= art.ResolveRole(EstateHealRingRole); estateMagnetArt ??= art.ResolveRole(EstateMagnetRingRole);
            for (var i = 0; i < estateArrows.Length; i++)
            {
                ref var arrow = ref estateArrows[i];
                if (!arrow.Live) continue;
                var elapsed = visualTime - arrow.Started;
                if (elapsed < GameVisualTokens.EstateArrowSeconds)
                {
                    var direction = arrow.To - arrow.From;
                    var degrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    Draw(estateArrowArt, GameVisualTokens.EstateArrowLayer, Vector2.Lerp(arrow.From, arrow.To, EstateArrowProgress(elapsed)), 0, degrees: degrees);
                    EstateArrowDraws++;
                    continue;
                }
                if (!arrow.Landed)
                {
                    arrow.Landed = true;
                    if (ShowDamageNumbers && showTransientText && arrow.Amount > 0) { numbers?.AddAggregated(arrow.Subject, arrow.To, arrow.Amount); EstateArrowNumbers++; }
                }
                if (elapsed < GameVisualTokens.EstateArrowSeconds + GameVisualTokens.EstateImpactSeconds)
                {
                    Draw(estateImpactArt, GameVisualTokens.EstateArrowLayer, arrow.To, 0);
                    EstateImpactDraws++;
                }
                else arrow.Live = false;
            }
            var lordPoint = current.Lord.Position;
            var hurt = current.Lord.Health < current.Lord.MaxHealth;
            var estate = wave.Estate;
            for (var i = 0; i < estate.Buildings.Count; i++)
            {
                var b = estate.Buildings[i];
                if (b.Phase != WaveEstatePhase.Active || b.Effect is not (WaveEstateEffectKind.Heal or WaveEstateEffectKind.Magnet)) continue;
                var heal = b.Effect == WaveEstateEffectKind.Heal;
                var on = EstateLordInside(b, lordPoint) && (!heal || hurt);
                estateRingStates.TryGetValue(b.Id, out var state);
                if (on != state.on) { state = (on, visualTime); estateRingStates[b.Id] = state; }
                var visual = heal ? estateHealArt : estateMagnetArt;
                if (on)
                {
                    Draw(visual, GameVisualTokens.EstateEffectRingLayer, Point(b.Position), visualTime - state.since, applyTween: false);
                    if (heal) EstateHealRingDraws++; else EstateMagnetRingDraws++;
                    continue;
                }
                if (!heal || state.since <= 0) continue;
                var fade = EstateHealFade(visualTime - state.since);
                if (fade <= 0) continue;
                // Hold the last frame while fading.
                var last = (visual.UvRects.Count - 1) * visual.FrameMs / 1000f + .0001f;
                Draw(visual, GameVisualTokens.EstateEffectRingLayer, Point(b.Position), last, opacity: fade, applyTween: false);
                EstateHealRingDraws++;
            }
        }

        void DrawEstateRing(Vector2 center, float fraction, bool stalled)
        {
            var dots = GameVisualTokens.EstateRingDots;
            var filled = Mathf.FloorToInt(fraction * dots + .0001f);
            for (var i = 0; i < dots; i++)
            {
                // Clockwise from twelve o'clock.
                var angle = Mathf.PI / 2 - i * Mathf.PI * 2 / dots;
                var at = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * GameVisualTokens.EstateRingRadius;
                var on = i < filled;
                var color = on ? GameVisualTokens.EstateRim : GameVisualTokens.Ink;
                color.a = on ? (stalled ? .4f : 1) : .55f;
                var radius = on ? GameVisualTokens.EstateRingDotRadius : GameVisualTokens.EstateRingDotRadius * .8f;
                estateRings.Add(at, Vector2.one * radius, 0, color);
            }
            EstateRingDraws++;
        }

        // Called for each drawn enemy (after its body): the T1 mark when it stands outside every lit circle.
        void DrawEstateDarkMark(WorldPoint core, ArtVisual art, int layer, Vector2 position, float time, Vector2 size, bool moved, float hit,
            ActorFacingPose facing, ActorPose pose, Rect? uv, float upperOpacity)
        {
            if (!EstateActive || EstateLit(wave.Estate, core)) return;
            EstateDarkMarks++;
            if (DarkMarker == EstateDarkMarker.Outline)
            {
                DrawActor(art, layer, position, time, size, moved, -1, hit, tint: GameVisualTokens.EstateDarkOutline, upperOpacity: upperOpacity, facing: facing,
                    edgeTexels: GameVisualTokens.EstateDarkOutlineTexels, poseOverride: pose, uv: uv);
                return;
            }
            var bounds = ActorBounds(art, position, size, pose, facing);
            // Two small glints in the face: a little toward the facing side, below the top of the sprite's cell.
            var eye = new Vector2(bounds.center.x + bounds.width * GameVisualTokens.EstateEyeForward * facing.SignedWidth, bounds.yMin + bounds.height * GameVisualTokens.EstateEyeHeight);
            var spacing = bounds.width * GameVisualTokens.EstateEyeSpacing * .5f;
            var radius = Mathf.Max(GameVisualTokens.EstateEyeMinimumRadius, bounds.width * GameVisualTokens.EstateEyeRadius);
            var backing = GameVisualTokens.Ink; backing.a = .6f;
            for (var side = -1; side <= 1; side += 2)
            {
                var at = eye + Vector2.right * (side * spacing);
                estateEyes.Add(at, Vector2.one * radius * 1.6f, 0, backing);
                estateEyes.Add(at, Vector2.one * radius, 0, GameVisualTokens.EstateEyeGlint);
            }
        }

        void BeginEstateFrame()
        {
            EstateDarkMarks = 0;
            estateRings?.BeginFrame(); estateEyes?.BeginFrame();
        }

        void FlushEstate()
        {
            if (estateRings != null) { estateRings.Flush(); SubmittedInstances += estateRings.SubmittedInstances; DrawCalls += estateRings.DrawCalls; }
            if (estateEyes != null) { estateEyes.Flush(); SubmittedInstances += estateEyes.SubmittedInstances; DrawCalls += estateEyes.DrawCalls; }
        }

        void DisposeEstate()
        {
            if (estateShaderOn) { Shader.SetGlobalFloat(EstateActiveId, 0); estateShaderOn = false; }
            estateRings?.Dispose(); estateEyes?.Dispose(); estateMeshes?.Dispose();
            estateRings = estateEyes = null; estateMeshes = null; ResetEstate();
        }
    }
}
