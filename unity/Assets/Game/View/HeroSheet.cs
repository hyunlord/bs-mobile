using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Full-body knight sprite sheets (Astra knight-game-sheets-r4, docs/design/gdd/06-feedback.md "공격 동작"). View only:
    // Core events choose the pose; nothing here changes when damage happens.
    public enum HeroAction { None, Slash, Aim, Cast, Blow, Throw }
    public enum HeroClip { Idle, Walk, Hit, Death, Action, Key }

    public static class HeroSheetSpec
    {
        // Frame counts and frame times are not fixed here: every clip reads its manifest role (HeroSheetTimings), so a clip
        // redrawn under the same role (the walk, 2026-10-17) plays with its own frames and frameMs.
        // Hero hit (user decision 2026-10-17): no full-body red tint; the r5 recoil pose, brightened for its first
        // HitFlashFrames display frames, with a red outline when hit feedback is on.
        public const int HitFlashFrames = 2;
        public const float HitFlash = .8f;
        public const float MinRepeatSeconds = .4f;   // the same action starts at most every 0.4 s
        public const string KnightId = "core:frontier_knight", RolePrefix = "hero.core.frontier_knight.";
        public static readonly string[] ActionNames = { "", "slash", "aim", "cast", "blow", "throw" };
        public static float Seconds(int ticks60) => ticks60 / 60f;
        // The pack tool writes frameMs as the delivered 60 Hz tick count rounded to milliseconds (10 ticks -> 167 ms).
        public static int TicksOf(int frameMs) => Mathf.RoundToInt(frameMs * 60 / 1000f);
        // The sheets were fitted to the previous knight's figure (its 188x242 px frame = 0.6 world units tall, figure 209 px)
        // at the delivered 115 px body reference: in sheet pixels that frame is FigureFootprintPx. The shadow and camera
        // margins use it instead of the whole cell. Its share comes from each role's own manifest cell and foot baseline, so a
        // clip delivered in a different cell (a redrawn walk) keeps the same footprint in world units.
        public const float BodyReferencePx = 115;
        public static readonly Vector2 FigureFootprintPx = new Vector2(BodyReferencePx * 188f / 209, BodyReferencePx * 242f / 209);
        public static Vector2 FigureShareOfCell(Vector2 cellPx, float footPivotY)
        {
            if (cellPx.x <= 0 || cellPx.y <= 0 || footPivotY < 0 || footPivotY >= 1)
                throw new ArgumentException($"A knight cell needs a positive size and a foot baseline inside it ({cellPx.x}x{cellPx.y} px, pivot y {footPivotY}).");
            // The figure stands on the foot baseline, so it must fit in the cell above it.
            if (FigureFootprintPx.x > cellPx.x || FigureFootprintPx.y > cellPx.y * (1 - footPivotY))
                throw new ArgumentException($"The {FigureFootprintPx.x:0.#}x{FigureFootprintPx.y:0.#} px knight figure does not fit a {cellPx.x}x{cellPx.y} px cell above its foot baseline (pivot y {footPivotY}).");
            return new Vector2(FigureFootprintPx.x / cellPx.x, FigureFootprintPx.y / cellPx.y);
        }
        public static Vector2 FigureSize(ArtVisual visual) => Vector2.Scale(visual.WorldSize, FigureShareOfCell(visual.CellPixels, visual.Pivot.y));

        // Weapon -> action (06-feedback mapping). The guard ring throws only in its evolved stage, on its hits; the spore fan
        // and every other source have no action.
        // Start events: the runtime's attack, a review composition's cast tell (review-impact-tell) or a chain's first link.
        static bool Starts(string kind) => kind == "attack" || kind == "review-impact-tell" || kind == "chain-link";
        public static HeroAction ActionFor(string weaponId, bool evolved, string kind)
        {
            switch (weaponId)
            {
                case "core:iron_blade": case "core:ditch_pike": return Starts(kind) ? HeroAction.Slash : HeroAction.None;
                case "core:canal_bow": case "core:granary_sling": return Starts(kind) ? HeroAction.Aim : HeroAction.None;
                case "core:storm_fork": return Starts(kind) ? HeroAction.Cast : HeroAction.None;
                case "core:muster_horn": return Starts(kind) ? HeroAction.Blow : HeroAction.None;
                case "core:ward_orbit": return evolved && kind == "hit" ? HeroAction.Throw : HeroAction.None;
                default: return HeroAction.None;
            }
        }
    }

    // One clip's frame count and per-frame time (60 Hz ticks) from its manifest role. The last frame of a clip that outlasts
    // it (the hit, which lasts as long as the hero hit) holds.
    public readonly struct HeroClipTiming
    {
        public readonly int Frames, Ticks;
        public HeroClipTiming(int frames, int frameMs)
        {
            if (frames < 1 || HeroSheetSpec.TicksOf(frameMs) < 1) throw new ArgumentException($"A knight clip needs frames and a frameMs of at least one tick ({frames} frames, {frameMs} ms).");
            Frames = frames; Ticks = HeroSheetSpec.TicksOf(frameMs);
        }
        public float FrameSeconds => HeroSheetSpec.Seconds(Ticks);
        public float Seconds => HeroSheetSpec.Seconds(Frames * Ticks);
        public int FrameAt(float elapsed) => Mathf.Min(Frames - 1, Mathf.FloorToInt(Mathf.Max(0, elapsed) / FrameSeconds));
    }

    // The knight's clips as the art manifest registers them. role(suffix) gives the frame count and frameMs of
    // RolePrefix + suffix (idle, walk, hit, action.<name>, key.<name>). A key clip's frame time is its moving hold.
    public sealed class HeroSheetTimings
    {
        readonly HeroClipTiming[] actions = new HeroClipTiming[6], keys = new HeroClipTiming[6];
        public HeroClipTiming Idle { get; }
        public HeroClipTiming Walk { get; }
        public HeroClipTiming Hit { get; }
        public HeroClipTiming Action(HeroAction action) => actions[(int)action];
        public HeroClipTiming Key(HeroAction action) => keys[(int)action];
        public HeroSheetTimings(Func<string, (int Frames, int FrameMs)> role)
        {
            HeroClipTiming Of(string suffix) { var r = role(suffix); return new HeroClipTiming(r.Frames, r.FrameMs); }
            Idle = Of("idle"); Walk = Of("walk"); Hit = Of("hit");
            for (var a = 1; a < actions.Length; a++) { actions[a] = Of("action." + HeroSheetSpec.ActionNames[a]); keys[a] = Of("key." + HeroSheetSpec.ActionNames[a]); }
        }
    }

    public readonly struct HeroSheetPose
    {
        public readonly HeroClip Clip; public readonly HeroAction Action; public readonly int Frame;
        public HeroSheetPose(HeroClip clip, HeroAction action, int frame) { Clip = clip; Action = action; Frame = frame; }
        public override string ToString() => Clip == HeroClip.Action || Clip == HeroClip.Key ? $"{Clip}:{Action}[{Frame}]" : $"{Clip}[{Frame}]";
    }

    // Chooses the knight's pose each frame. Rules (06-feedback):
    // - Moving: an accepted attack shows that action's key pose for 7/60 s, then walking resumes. Standing: the full action.
    // - Starting to move during a full action ends it at once (user approved 2026-10-17).
    // - Same tick: evolved before higher damage before lower weapon id. During an action a higher-priority attack replaces it
    //   from its start; equal or lower ones are dropped. The same action starts at most every 0.4 s (dropped, not queued).
    // - A hero hit overrides the action, which does not resume afterwards.
    public sealed class HeroActionSelector
    {
        HeroAction current; bool key; float started; int startedTick = -1; bool evolved; int damage; string source;
        readonly float[] lastStart = new float[6];
        float hitStarted; bool wasHit; float walkPhase;
        // Seconds since the current hero hit began, or -1 when no hit is shown.
        public float HitAge(float time) => wasHit ? time - hitStarted : -1;
        public int Started { get; private set; }
        public int KeysShown { get; private set; }
        public int FullShown { get; private set; }
        public int Dropped { get; private set; }
        public int Replaced { get; private set; }
        public int CutByMovement { get; private set; }

        // Set from the art manifest when the sheets load (WorldRenderer.EnsureHeroSheet).
        public HeroSheetTimings Timings { get; set; }
        public HeroActionSelector(HeroSheetTimings timings = null) { Timings = timings; Reset(); }
        HeroSheetTimings Clips => Timings ?? throw new InvalidOperationException("Knight sheet timings are not loaded.");
        public void Reset()
        {
            current = HeroAction.None; key = false; started = 0; startedTick = -1; source = null; evolved = false; damage = 0;
            for (var i = 0; i < lastStart.Length; i++) lastStart[i] = float.NegativeInfinity;
            hitStarted = 0; wasHit = false; walkPhase = 0;
            Started = KeysShown = FullShown = Dropped = Replaced = CutByMovement = 0;
        }

        static int Compare(bool evolvedA, int damageA, string sourceA, bool evolvedB, int damageB, string sourceB)
        {
            if (evolvedA != evolvedB) return evolvedA ? 1 : -1;
            if (damageA != damageB) return damageA > damageB ? 1 : -1;
            return -string.CompareOrdinal(sourceA, sourceB);   // lower id first
        }

        bool Active(float time, bool moving)
        {
            if (current == HeroAction.None) return false;
            if (key) return time < started + Clips.Key(current).Seconds;
            return time < started + Clips.Action(current).Seconds;
        }

        // Offers one Core attack. Returns whether it started (or replaced) the shown action.
        public bool Offer(int tick, string weaponSource, HeroAction action, bool isEvolved, int weaponDamage, float time, bool moving)
        {
            if (action == HeroAction.None) return false;
            if (wasHit) { Dropped++; return false; }   // the hit pose wins; attacks during it are not shown afterwards
            var active = Active(time, moving);
            if (active && Compare(isEvolved, weaponDamage, weaponSource, evolved, damage, source) <= 0) { Dropped++; return false; }
            var sameTickChoice = active && tick == startedTick;
            if (!sameTickChoice && time - lastStart[(int)action] < HeroSheetSpec.MinRepeatSeconds) { Dropped++; return false; }
            if (active) Replaced++;
            current = action; key = moving; started = time; startedTick = tick;
            evolved = isEvolved; damage = weaponDamage; source = weaponSource; lastStart[(int)action] = time;
            Started++; if (key) KeysShown++; else FullShown++;
            return true;
        }

        // walkDistance: distance moved since the last call; stride: distance of one walk cycle at the hero's base speed
        // (playback follows the actual speed). hit: the hero hit flash is on.
        public HeroSheetPose Pose(float time, bool moving, bool hit, bool dead, float walkDistance, float stride)
        {
            if (moving && stride > 0) walkPhase = Mathf.Repeat(walkPhase + walkDistance / stride, 1);
            if (dead) { wasHit = false; current = HeroAction.None; return new HeroSheetPose(HeroClip.Death, HeroAction.None, 0); }
            if (hit)
            {
                if (!wasHit) { wasHit = true; hitStarted = time; current = HeroAction.None; }
                return new HeroSheetPose(HeroClip.Hit, HeroAction.None, Clips.Hit.FrameAt(time - hitStarted));
            }
            wasHit = false;
            if (current != HeroAction.None && !key && moving) { current = HeroAction.None; CutByMovement++; }
            if (Active(time, moving))
            {
                if (key) return new HeroSheetPose(HeroClip.Key, current, (int)current - 1);
                return new HeroSheetPose(HeroClip.Action, current, Clips.Action(current).FrameAt(time - started));
            }
            current = HeroAction.None;
            var walk = Clips.Walk; var idle = Clips.Idle;
            if (moving) return new HeroSheetPose(HeroClip.Walk, HeroAction.None, Mathf.Min(walk.Frames - 1, Mathf.FloorToInt(walkPhase * walk.Frames)));
            return new HeroSheetPose(HeroClip.Idle, HeroAction.None, Mathf.FloorToInt(Mathf.Max(0, time) / idle.FrameSeconds) % idle.Frames);
        }
    }

    public sealed partial class WorldRenderer
    {
        // Lord speed per Core tick at base speed (set by the run); the walk cycle plays faster or slower with the actual speed.
        public float HeroLocomotionPerTick { get; set; }
        public Action<HeroSheetPose> HeroSheetPoseSampled { get; set; }
        public Action<float, float, bool> HeroSheetDrawn { get; set; }   // hit age (-1 none), flash, red outline drawn
        public HeroActionSelector HeroActions { get; } = new HeroActionSelector();
        bool heroSheetChecked, heroSheetAvailable;
        ArtVisual heroSheetIdle, heroSheetWalk, heroSheetHit, heroSheetDeath;
        readonly ArtVisual[] heroSheetActions = new ArtVisual[6], heroSheetKeys = new ArtVisual[6];

        // The knight sprite sheets replace the cutout B rig (#268): the B rig draws only when a comparison or benchmark asks for
        // it explicitly, or when the hero has no sheet roles. Decided each frame, after the art is loaded.
        public bool HeroRigExplicit { get; set; }
        bool heroRigActive, heroSheetOn;
        // Review modes ask for their own hero explicitly: the motion comparison, the production comparison and the concept
        // review keep their rig or picture, so the sheet stays off for them.
        bool HeroReviewExplicit => HeroRigExplicit || HeroMotionPreviewMode != 0 || productionHero != null || heroConceptVisual != null;
        bool HeroSheetShown(string heroId) => !HeroReviewExplicit && EnsureHeroSheet(heroId);
        void SelectHeroRig(string heroId)
        {
            heroSheetOn = HeroSheetShown(heroId);
            heroRigActive = !heroSheetOn && (heroKnightEnabled || HeroRigPreviewEnabled || HeroMotionPreviewMode != 0);
        }

        bool EnsureHeroSheet(string heroId)
        {
            if (heroId != HeroSheetSpec.KnightId) return false;
            if (heroSheetChecked) return heroSheetAvailable;
            if (art == null) return false;
            heroSheetChecked = true;
            if (!art.TryResolveRole(HeroSheetSpec.RolePrefix + "action.slash", out _)) return heroSheetAvailable = false;
            heroSheetIdle = Resolve("hero", heroId, "idle"); heroSheetWalk = Resolve("hero", heroId, "walk");
            heroSheetHit = Resolve("hero", heroId, "hit"); heroSheetDeath = Resolve("hero", heroId, "death");
            for (var a = 1; a < heroSheetActions.Length; a++)
            {
                heroSheetActions[a] = art.ResolveRole(HeroSheetSpec.RolePrefix + "action." + HeroSheetSpec.ActionNames[a]);
                heroSheetKeys[a] = art.ResolveRole(HeroSheetSpec.RolePrefix + "key." + HeroSheetSpec.ActionNames[a]);
            }
            // The footprint is checked here, once, for every knight clip: a cell the figure does not fit fails at load. Indexed
            // calls only: this runs inside the first presented frame, which must not allocate.
            HeroSheetSpec.FigureSize(heroSheetIdle); HeroSheetSpec.FigureSize(heroSheetWalk);
            HeroSheetSpec.FigureSize(heroSheetHit); HeroSheetSpec.FigureSize(heroSheetDeath);
            for (var a = 1; a < heroSheetActions.Length; a++) { HeroSheetSpec.FigureSize(heroSheetActions[a]); HeroSheetSpec.FigureSize(heroSheetKeys[a]); }
            // Frame counts and frameMs come from the manifest roles, so a redrawn clip under the same role needs no code change.
            HeroActions.Timings = new HeroSheetTimings(suffix => { var v = art.ResolveRole(HeroSheetSpec.RolePrefix + suffix); return (v.UvRects.Count, v.FrameMs); });
            return heroSheetAvailable = true;
        }

        // A Core attack the knight may act on: weapon id (an evolution's attack counts as its first input weapon, evolved).
        // One offer per (weapon, tick): a cast emits several tells or links in its tick. A review ricochet's bounce is a tell that
        // leaves from an impact the same weapon landed in the same tick (the impact comes first), not a new cast.
        int heroOfferTick = -1; string heroOfferWeapon; (int Tick, string Source, WorldPoint Point) heroLastImpact = (-1, null, default);
        void OfferHeroAction(WaveEvent value, int tick)
        {
            if (value.Kind == "review-impact") { heroLastImpact = (value.Tick, value.Source, value.Position); return; }
            if (!heroSheetAvailable || waveDefinition == null || value.Source == null) return;
            if (value.Kind == "review-impact-tell" && heroLastImpact.Tick == value.Tick && heroLastImpact.Source == value.Source && heroLastImpact.Point.X == value.Position.X && heroLastImpact.Point.Y == value.Position.Y) return;
            var weapon = value.Source; var evolved = false;
            if (waveDefinition.Evolutions.TryGetValue(value.Source, out var evolution) && evolution.InputIds.Length > 0) { weapon = evolution.InputIds[0]; evolved = true; }
            else if (value.Kind == "hit") evolved = HeroWeaponEvolved(weapon);
            var action = HeroSheetSpec.ActionFor(weapon, evolved, value.Kind);
            if (action == HeroAction.None || (tick == heroOfferTick && weapon == heroOfferWeapon)) return;
            heroOfferTick = tick; heroOfferWeapon = weapon;
            var damage = waveDefinition.Gear.TryGetValue(weapon, out var gear) ? gear.Damage : 0;
            HeroActions.Offer(tick, weapon, action, evolved, damage, visualTime, heroSheetMoving);
        }

        bool HeroWeaponEvolved(string weapon)
        {
            if (wave == null) return false;
            foreach (var id in wave.Evolutions)
                if (waveDefinition.Evolutions.TryGetValue(id, out var e) && e.InputIds.Length > 0 && e.InputIds[0] == weapon) return true;
            return false;
        }

        bool heroSheetMoving;

        // The hero's footprint for shadow and camera margins: the figure share of this role's own cell, or the visual's own size.
        Vector2 HeroFigureSize(string heroId, ArtVisual visual) => HeroSheetShown(heroId) ? HeroSheetSpec.FigureSize(visual) : visual.WorldSize;

        bool DrawHeroSheet(string heroId, Vector2 lord, Vector2 movement, bool walking, bool hit, bool dead, ActorFacingPose facing, bool hitOutline, int tickRate)
        {
            if (!heroSheetOn) return false;
            heroSheetMoving = walking;
            var stride = HeroLocomotionPerTick > 0 ? HeroLocomotionPerTick * tickRate / settings.WorldUnitsPerUnityUnit * HeroActions.Timings.Walk.Seconds : 0;
            var pose = HeroActions.Pose(visualTime, walking, hit, dead, movement.magnitude, stride);
            var visual = pose.Clip switch
            {
                HeroClip.Walk => heroSheetWalk, HeroClip.Hit => heroSheetHit, HeroClip.Death => heroSheetDeath,
                HeroClip.Action => heroSheetActions[(int)pose.Action], HeroClip.Key => heroSheetKeys[(int)pose.Action], _ => heroSheetIdle
            };
            var frame = pose.Clip == HeroClip.Key ? 0 : pose.Frame;
            // Left/right mirror the single 3/4 view (facing keeps the last horizontal direction); up/down use the same sheet.
            var size = visual.WorldSize; size.x *= facing.SignedWidth;
            var hitAge = HeroActions.HitAge(visualTime);
            var flash = hitAge >= 0 && hitAge < HeroSheetSpec.HitFlashFrames / 60f ? HeroSheetSpec.HitFlash : 0;
            Draw(visual, GameVisualTokens.LordLayer, lord, 0, size, 0, 1, null, flash, uv: visual.UvRects[frame], applyTween: false);
            if (hitAge >= 0 && hitOutline)
                Draw(visual, GameVisualTokens.LordLayer, lord, 0, size, 0, 1, GameVisualTokens.Hostile, 0, uv: visual.UvRects[frame], edgeTexels: GameVisualTokens.WaveEdgeTexels, applyTween: false);
            HeroSheetDrawn?.Invoke(hitAge, flash, hitAge >= 0 && hitOutline);
            HeroSheetPoseSampled?.Invoke(pose);
            return true;
        }
    }
}
