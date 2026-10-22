using SowSiege.Core;
using Game.App.Generated;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        readonly RenderTickInterpolation renderInterpolation = new RenderTickInterpolation();
        readonly LordPresentationPredictor lordPrediction = new LordPresentationPredictor();
        readonly FrameSmoothnessDiagnostics smoothness = new FrameSmoothnessDiagnostics();
        partial void ConfigureNormalTrace(NormalPlayTrace trace);
        NormalPlayTrace normalTrace;
        bool normalTraceChecked;
        float presentationUnits = 1000;
        // Capture evidence for the knight sheets: the pose drawn each frame, kept in preallocated arrays (no capture-time
        // allocation) and written as hero-poses.csv with the smoothness diagnostics.
        const int HeroPoseCapacity = 1 << 15;
        readonly int[] heroPoseFrame = new int[HeroPoseCapacity], heroPoseTick = new int[HeroPoseCapacity], heroPoseIndex = new int[HeroPoseCapacity];
        readonly HeroClip[] heroPoseClip = new HeroClip[HeroPoseCapacity];
        readonly HeroAction[] heroPoseAction = new HeroAction[HeroPoseCapacity];
        int heroPoses;
        void ObserveHeroPose(HeroSheetPose pose)
        {
            if (heroPoses >= HeroPoseCapacity) return;
            heroPoseFrame[heroPoses] = Time.renderedFrameCount; heroPoseTick[heroPoses] = CurrentFrame?.Tick ?? -1;
            heroPoseClip[heroPoses] = pose.Clip; heroPoseAction[heroPoses] = pose.Action; heroPoseIndex[heroPoses] = pose.Frame; heroPoses++;
        }
        void WriteHeroPoses(string folder)
        {
            if (world == null || heroPoses == 0) return;
            using var file = new System.IO.StreamWriter(System.IO.Path.Combine(folder, "hero-poses.csv"));
            file.WriteLine("renderFrame,tick,clip,action,frame");
            for (var i = 0; i < heroPoses; i++) file.WriteLine($"{heroPoseFrame[i]},{heroPoseTick[i]},{heroPoseClip[i]},{heroPoseAction[i]},{heroPoseIndex[i]}");
            var s = world.HeroActions;
            file.WriteLine($"# started {s.Started}, keys {s.KeysShown}, full {s.FullShown}, dropped {s.Dropped}, replaced {s.Replaced}, cutByMovement {s.CutByMovement}, overflow {heroPoses >= HeroPoseCapacity}");
        }
        public Vector2 PresentedLordPosition => world != null ? world.RenderedLordPosition : Vector2.zero;
        public void BeginSmoothnessDiagnostics() => smoothness.Begin();
        public void SuspendSmoothnessDiagnostics() => smoothness.Suspend();
        public void NotifySmoothnessEndOfFrame() => smoothness.EndOfFrame();
        public void WriteSmoothnessDiagnostics(string folder) { smoothness.Write(folder); WriteHeroPoses(folder); }
        public void RefreshCapturePresentation()
        {
            if(AutoplayCapture.Active==null||CurrentFrame==null||CurrentFrame.Tick!=0||world==null||hud==null||Paused||screen!=UiScreen.Run)
                throw new System.InvalidOperationException("Capture presentation refresh requires the resumed tick-zero run.");
            hud.Present(CurrentFrame,FirstPlayable);
            Canvas.ForceUpdateCanvases();
            var visible=Screen.safeArea;
            var bottom=Mathf.Min(UiTokens.BottomWorldInset*Ui.Canvas.scaleFactor,visible.height*.2f);
            visible.yMin+=bottom;visible.yMax=Mathf.Max(visible.yMin+1,visible.yMax-hud.ReservedTopPixels);
            world.RefreshCaptureCamera(visible);
        }

        void InitializeSmoothness()
        {
            renderInterpolation.Reset();
            presentationUnits = CanonicalContent.Presentation.Camera.WorldUnitsPerUnityUnit;
            var map = runCatalog.Tuning.World.Map;
            var expectedLocomotion = activeFeelRules != null;
            var initialSpeed = expectedLocomotion ? (float)Session.LocomotionSpeedPerTick : map.LordSpeed;
            UpdatePredictionTopology();
            lordPrediction.Initialize(PresentationPoint(CurrentFrame.Lord.Position), new Vector2(map.Width, map.Height) / presentationUnits,
                initialSpeed * CurrentFrame.TickRate / presentationUnits, 1f / CurrentFrame.TickRate, CurrentFrame.Tick,
                expectedLocomotion, 2f / presentationUnits, activeFeelRules?.FasterLord == false ? 1f / presentationUnits : 0);
            world.RenderedEntitySample = ObserveRenderedEntity;
            heroPoses = 0;
            // Live captures record poses here; leave an offline review's own pose observer in place (composite with #266).
            if (AutoplayCapture.Active != null) world.HeroSheetPoseSampled = ObserveHeroPose;
            normalTrace?.BeginSession();
            if(AutoplayCapture.Active != null) BeginSmoothnessDiagnostics();
        }
        void ObserveRenderedEntity(string kind, int id, Vector2 position)
        {
            smoothness.Entity(kind, id, position);
            normalTrace?.Entity(kind, id, position);
        }
        Vector2 PresentationPoint(WorldPoint point) => new Vector2(point.X, point.Y) / presentationUnits;
        static Vector2 PresentationInput(PlayerInput input) => new Vector2(input.X, input.Y) / PlayerInput.Scale;
        void ObserveLordMovement(PlayerInput input)
        {
            UpdatePredictionTopology();
            float? expectedSpeed = activeFeelRules == null ? (float?)null : (float)Session.LocomotionSpeedPerTick * CurrentFrame.TickRate / presentationUnits;
            lordPrediction.Observe(PresentationPoint(CurrentFrame.Lord.Position), CurrentFrame.Tick, PresentationInput(input), expectedSpeed);
        }
        void PredictLord(PlayerInput input, float? presentationDelta = null)
        {
            if(!IsWave||runCatalog.WaveRuntime?.ChapterId!="meta:chapter_1") { world.PredictedLordPosition = null; return; }
            UpdatePredictionTopology();
            world.PredictedLordPosition = lordPrediction.Present(PresentationPoint(CurrentFrame.Lord.Position), PresentationInput(input),
                (presentationDelta ?? Time.unscaledDeltaTime) * Speed, (float)accumulator, Paused);
            normalTrace?.ObserveInput(PresentationInput(input), CurrentFrame.Tick, accumulator, Paused, PresentationPoint(CurrentFrame.Lord.Position), lordPrediction.Position, lordPrediction.Speed);
        }
        void UpdatePredictionTopology()
        {
            var arena = CurrentFrame.Field?.Arena;
            lordPrediction.SetTopology(CurrentFrame.Field != null, arena == null ? (Vector2?)null : PresentationPoint(arena.Center),
                arena == null ? 0 : arena.Radius / presentationUnits);
        }
        void Update()
        {
            RecordHeroMotionComparisonFrame();
            BeginLongFrame();
            if(ProbeEnabled)BeginProbeFrame();
            if (!normalTraceChecked) { normalTraceChecked = true; normalTrace = NormalPlayTrace.Create(this); if (normalTrace != null) ConfigureNormalTrace(normalTrace); }
            normalTrace?.BeginFrame();
            smoothness.BeginFrame(CurrentFrame?.Tick ?? 0, Paused);
            try { UpdateRunFrame(); }
            finally { smoothness.EndFrame(); normalTrace?.EndFrame(Camera.main, world != null ? world.CameraShakeOffset : Vector2.zero); EndLongFrame(); }
        }
    }
}
