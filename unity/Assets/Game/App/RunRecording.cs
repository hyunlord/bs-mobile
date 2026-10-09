using System;
using System.IO;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class RunRecording : IDisposable
    {
        readonly MemoryStream live = new MemoryStream();
        long commands;
        int tick;
        bool finished;
        bool disposed;
        public string SessionId { get; }
        public string DirectoryPath { get; }
        public string ReplayPath { get; }

        public RunRecording(string persistentRoot, InteractiveOptions options, string buildIdentity)
        {
            SessionId = Guid.NewGuid().ToString("N");
            DirectoryPath = Path.Combine(persistentRoot, "runs", SessionId);
            ReplayPath = Path.Combine(DirectoryPath, "run.ssreplay");
            Directory.CreateDirectory(DirectoryPath);
            ReplayCodec.WriteHeader(live, ReplayCodec.Header(options));
            var identity = new RecordingIdentity { sourceHash = Generated.BuildIdentity.SourceHash, sourceDirty = Generated.BuildIdentity.SourceDirty, sessionId = SessionId, seed = options.Run.Seed, build = buildIdentity, dataHash = options.DataHash };
            File.WriteAllText(Path.Combine(DirectoryPath, "recording.json"), JsonUtility.ToJson(identity, true));
        }

        public void WriteAccepted(ReplayCommand command)
        {
            using var scope = RunProfilerMarkers.RecordingWrite.Auto();
            RequireLive();
            if (command.Sequence != commands || command.Tick != tick) throw new InvalidOperationException("Recording command sequence does not match accepted input.");
            ReplayCodec.WriteCommand(live, command);
            commands++;
            if (command.Kind == ReplayCommandKind.Advance) tick++;
        }

        public void Checkpoint(InteractiveSession session)
        {
            using var scope = RunProfilerMarkers.RecordingCheckpoint.Auto();
            if (finished) return;
            RequireLive();
            var summary = session.GetSummary();
            var kind = EndKind(summary);
            Snapshot(session, new ReplayEnd(session.NextSequence, summary.Tick, kind, summary.StateHash));
            if (kind != ReplayEndKind.Quit) finished = true;
        }

        public void Finish(InteractiveSession session, ReplayEndKind kind)
        {
            using var scope = RunProfilerMarkers.RecordingFinish.Auto();
            if (finished) return;
            RequireLive();
            var summary = session.GetSummary();
            if (kind != EndKind(summary)) throw new InvalidOperationException("Recording terminal kind disagrees with Core.");
            Snapshot(session, new ReplayEnd(session.NextSequence, summary.Tick, kind, summary.StateHash));
            finished = true;
        }

        void Snapshot(InteractiveSession session, ReplayEnd end)
        {
            if (session.NextSequence != commands || end.Tick != tick) throw new InvalidOperationException("Recording omitted an accepted command.");
            var temporary = ReplayPath + ".tmp";
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                live.WriteTo(output);
                ReplayCodec.WriteEnd(output, end);
                output.Flush(true);
            }
            if (File.Exists(ReplayPath)) File.Replace(temporary, ReplayPath, null);
            else File.Move(temporary, ReplayPath);
        }

        static ReplayEndKind EndKind(RunSummary summary) => summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit;
        void RequireLive()
        {
            if (disposed || finished) throw new InvalidOperationException("Recording is closed.");
        }
        public void Dispose() { if (disposed) return; live.Dispose(); disposed = true; }
        [Serializable]
        sealed class RecordingIdentity { public string sourceHash; public bool sourceDirty; public string sessionId; public int seed; public string build; public string dataHash; }
    }
}
