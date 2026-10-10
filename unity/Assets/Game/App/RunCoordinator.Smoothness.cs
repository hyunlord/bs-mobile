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
        public Vector2 PresentedLordPosition => world != null ? world.RenderedLordPosition : Vector2.zero;
        public void BeginSmoothnessDiagnostics() => smoothness.Begin();
        public void SuspendSmoothnessDiagnostics() => smoothness.Suspend();
        public void NotifySmoothnessEndOfFrame() => smoothness.EndOfFrame();
        public void WriteSmoothnessDiagnostics(string folder) => smoothness.Write(folder);
        public void RefreshCapturePresentation()
        {
            if(AutoplayCapture.Active==null||Frame==null||Frame.Tick!=0||world==null||hud==null||Paused||screen!=UiScreen.Run)
                throw new System.InvalidOperationException("Capture presentation refresh requires the resumed tick-zero run.");
            hud.Present(Frame,FirstPlayable);
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
            lordPrediction.Initialize(PresentationPoint(Frame.Lord.Position), new Vector2(map.Width, map.Height) / presentationUnits,
                map.LordSpeed * Frame.TickRate / presentationUnits, 1f / Frame.TickRate, Frame.Tick);
            world.RenderedEntitySample = ObserveRenderedEntity;
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
        void PredictLord(PlayerInput input)
        {
            if(!IsWave||runCatalog.WaveRuntime?.ChapterId!="meta:chapter_1") { world.PredictedLordPosition = null; return; }
            world.PredictedLordPosition = lordPrediction.Present(PresentationPoint(Frame.Lord.Position), PresentationInput(input),
                Time.unscaledDeltaTime * Speed, (float)accumulator, Paused);
            normalTrace?.ObserveInput(PresentationInput(input), Frame.Tick, accumulator, Paused, PresentationPoint(Frame.Lord.Position), lordPrediction.Position, lordPrediction.Speed);
        }
        void Update()
        {
            if (!normalTraceChecked) { normalTraceChecked = true; normalTrace = NormalPlayTrace.Create(this); if (normalTrace != null) ConfigureNormalTrace(normalTrace); }
            normalTrace?.BeginFrame();
            smoothness.BeginFrame(Frame?.Tick ?? 0, Paused);
            try { UpdateRunFrame(); }
            finally { smoothness.EndFrame(); normalTrace?.EndFrame(Camera.main, world != null ? world.CameraShakeOffset : Vector2.zero); }
        }
    }
}
