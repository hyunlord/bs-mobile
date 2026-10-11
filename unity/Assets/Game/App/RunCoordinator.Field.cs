using System;
using Game.View;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        double probeCoreMs, probeDrawMs, probeSnapshotMs, probeHudMs;
        int probeCoreTicks;
        bool probeLordBlocked;
        bool ProbeEnabled => NormalGcProbe.PreferencesPrefix != null;
        static long ProbeTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();
        static double ProbeElapsed(long start) => (System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;

        FieldOptions DepartureFieldOptions()
        {
            if(!IsWave)return null;
            var requested=AutoplayCapture.Active?.FieldVideo?.Topology;
            if(ProbeEnabled)requested=NormalGcProbe.RequestedTopology;
            if(requested!=null)return requested=="field"?new FieldOptions():null;
            if(FeelTrialAutomation.Active!=null||AutoplayCapture.Active?.FeelComparison!=null)return null;
            return new FieldOptions();
        }

        void BeginProbeFrame()
        {
            probeCoreMs=probeDrawMs=probeSnapshotMs=probeHudMs=0;
            probeCoreTicks=0;probeLordBlocked=false;
        }

        public NormalPlayMetrics ReadNormalPlayMetrics()
        {
            var camera=Camera.main;
            return new NormalPlayMetrics
            {
                CoreMs=probeCoreMs,DrawMs=probeDrawMs,SnapshotMs=probeSnapshotMs,HudMs=probeHudMs,CoreTicks=probeCoreTicks,
                Enemies=CurrentFrame?.Enemies.Count??0,VisibleEnemies=world!=null?world.VisibleEnemyCount:0,
                DrawCalls=world!=null?world.DrawCalls:0,SpriteInstances=world!=null?world.SubmittedInstances:0,
                CameraX=camera!=null?camera.transform.position.x:0,CameraY=camera!=null?camera.transform.position.y:0,
                CameraHalfHeight=camera!=null?camera.orthographicSize:0,
                LordX=CurrentFrame?.Lord.Position.X??0,LordY=CurrentFrame?.Lord.Position.Y??0,
                MapWidth=CurrentFrame?.MapWidth??0,MapHeight=CurrentFrame?.MapHeight??0,
                LordBoundaryBlocked=probeLordBlocked,CameraBoundaryClamped=world!=null&&world.CameraMapClampedThisFrame
            };
        }

        public string FinishNormalProbeRecording()
        {
            if(!ProbeEnabled||Session==null)throw new InvalidOperationException("Only the active normal probe may finish its recording.");
            var path=RecordedReplayPath;
            if(Session.View.Status==RunStatus.Completed&&!finished)CompleteRun();
            StopRecording();return path;
        }

        public ReplayVerification VerifyNormalProbeReplay(ReplayDocument replay)
        {
            if(!ProbeEnabled||runCatalog==null)throw new InvalidOperationException("Only the active normal probe may verify its recording.");
            return ReplayRunner.Verify(runCatalog,FoundationBoot.VerifiedDataHash,replay);
        }
    }
}
