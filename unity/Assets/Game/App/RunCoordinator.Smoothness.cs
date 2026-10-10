using SowSiege.Core;
using Game.App.Generated;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        readonly LordPresentationPredictor lordPrediction = new LordPresentationPredictor();
        readonly FrameSmoothnessDiagnostics smoothness = new FrameSmoothnessDiagnostics();
        float presentationUnits = 1000;
        public Vector2 PresentedLordPosition => world != null ? world.RenderedLordPosition : Vector2.zero;
        public void BeginSmoothnessDiagnostics() => smoothness.Begin();
        public void NotifySmoothnessEndOfFrame() => smoothness.EndOfFrame();
        public void WriteSmoothnessDiagnostics(string folder) => smoothness.Write(folder);

        void InitializeSmoothness()
        {
            presentationUnits = CanonicalContent.Presentation.Camera.WorldUnitsPerUnityUnit;
            var map = runCatalog.Tuning.World.Map;
            lordPrediction.Initialize(PresentationPoint(Frame.Lord.Position), new Vector2(map.Width, map.Height) / presentationUnits,
                map.LordSpeed * Frame.TickRate / presentationUnits, 1f / Frame.TickRate, Frame.Tick);
            world.RenderedEntitySample = smoothness.Entity;
            if(AutoplayCapture.Active != null) BeginSmoothnessDiagnostics();
        }
        Vector2 PresentationPoint(WorldPoint point) => new Vector2(point.X, point.Y) / presentationUnits;
        static Vector2 PresentationInput(PlayerInput input) => new Vector2(input.X, input.Y) / PlayerInput.Scale;
        void PredictLord(PlayerInput input)
        {
            if(!IsWave||runCatalog.WaveRuntime?.ChapterId!="meta:chapter_1") { world.PredictedLordPosition = null; return; }
            world.PredictedLordPosition = lordPrediction.Present(PresentationPoint(Frame.Lord.Position), PresentationInput(input),
                Time.unscaledDeltaTime * Speed, (float)accumulator, Paused);
        }
        void Update()
        {
            smoothness.BeginFrame(Frame?.Tick ?? 0, Paused);
            try { UpdateRunFrame(); }
            finally { smoothness.EndFrame(); }
        }
    }
}
