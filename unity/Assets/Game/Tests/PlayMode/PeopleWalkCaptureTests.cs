#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class PeopleWalkCaptureTests
    {
        const string Filter = "Tests.PlayMode.PeopleWalkCaptureTests.ActualPeopleObservationDoesNotChangeCore";
        const int Width = 390, Height = 844;
        [UnityTest, Timeout(600000)]
        public IEnumerator ActualPeopleObservationDoesNotChangeCore()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEOPLE_QA_REPLAY")))
            {
                yield return RecordedRange();
                yield break;
            }
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000,
                catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var owner = new GameObject("Actual people observation contract");
            var cameraOwner = new GameObject("Actual people camera");
            var camera = cameraOwner.AddComponent<Camera>();
            var target = new RenderTexture(Width, Height, 24); camera.targetTexture = target;
            var world = owner.AddComponent<WorldRenderer>();
            var observed = new List<int>();
            try
            {
                Initialize(world, camera, session.View.CaptureFrame(), catalog.Tuning.DefaultEstate);
                world.PeopleWalkObserved = (id, role, frame, position) => observed.Add(id);
                for (var i = 0; i < 20; i++)
                {
                    var previous = session.View.CaptureFrame();
                    session.Apply(new ReplayCommand(session.NextSequence, previous.Tick, ReplayCommandKind.Advance, new PlayerInput(1000, 0)));
                    var current = session.View.CaptureFrame(); var hash = session.ComputeStateHash(); observed.Clear();
                    var snapshot = session.View.CaptureFirstPlayable();
                    if (catalog.WaveRuntime != null)
                    {
                        var wave = ((IWaveRunView)session.View).CaptureWaveRuntime(); world.AcceptWave(catalog, wave);
                        snapshot = WavePresentation.Envelope(catalog, current, session.View.CaptureCards(), wave);
                        Assert.That(current.People, Is.Empty, "Wave does not initialize estate people; its workgroup soldiers are a different rendering path.");
                    }
                    world.Present(previous, current, snapshot, 1, 1f / current.TickRate, new Rect(0, 0, Width, Height));
                    yield return null;
                    Assert.That(session.ComputeStateHash(), Is.EqualTo(hash));
                    Assert.That(observed.All(id => current.People.Any(p => p.Id == id)), Is.True, "No fabricated observer entity.");
                    foreach (var person in current.People.Where(p => p.Role == "peasant" || p.Role == "returning" || p.Role == "vassal"))
                        Assert.That(observed.Count(id => id == person.Id), Is.EqualTo(1), "Observe the actual submitted person once.");
                }
            }
            finally { world.PeopleWalkObserved = null; camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(owner); Object.Destroy(cameraOwner); }
            yield return null;
        }

        static IEnumerator RecordedRange()
        {
            Assert.That(Environment.GetEnvironmentVariable("PEOPLE_QA_FILTERED_RUN"), Is.EqualTo("1"));
            var args = Environment.GetCommandLineArgs(); var filter = Array.IndexOf(args, "-testFilter");
            Assert.That(filter >= 0 && filter + 1 < args.Length && args[filter + 1] == Filter, Is.True, "Optional export must run alone with the exact test filter.");
            Assert.That(CanonicalContent.ProfileName, Is.EqualTo("first-playable"), "Wave has no estate people; never fabricate wave gameplay evidence.");
            Assert.That(int.TryParse(Environment.GetEnvironmentVariable("PEOPLE_QA_START_TICK"), out var start) && start > 0, Is.True);
            Assert.That(int.TryParse(Environment.GetEnvironmentVariable("PEOPLE_QA_END_TICK"), out var end) && end >= start && end - start < 1800, Is.True);
            var scan = Environment.GetEnvironmentVariable("PEOPLE_QA_SCAN_ONLY") == "1";
            var outputArgument = Environment.GetEnvironmentVariable("PEOPLE_QA_OUTPUT");
            Assert.That(string.IsNullOrWhiteSpace(outputArgument), Is.False);
            var output = Path.GetFullPath(outputArgument);
            Assert.That(File.Exists(output) || Directory.Exists(output), Is.False, "Fresh output only.");
            var path = Path.GetFullPath(Environment.GetEnvironmentVariable("PEOPLE_QA_REPLAY")); var bytes = File.ReadAllBytes(path);
            ReplayDocument replay; using (var stream = new MemoryStream(bytes, false)) replay = ReplayCodec.Read(stream);
            Assert.That(replay.Header.Options.DataHash, Is.EqualTo(CanonicalContent.DataHash));
            Assert.That(end, Is.LessThanOrEqualTo(replay.End.Tick));
            Assert.That(Object.FindObjectsByType<WorldRenderer>(FindObjectsSortMode.None), Is.Empty);
            var session = new InteractiveSession(CanonicalContent.CreateCatalog(), replay.Header.Options);
            Assert.That(session.View.CaptureFrame().TickRate, Is.EqualTo(30));
            var checkpoints = replay.Checkpoints.ToDictionary(c => c.AppliedCommands); var checkedCount = 0;
            void Check()
            {
                if (!checkpoints.TryGetValue(session.NextSequence, out var c)) return;
                Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(c.Tick));
                Assert.That(session.ComputeStateHash(), Is.EqualTo(c.StateHash)); checkedCount++;
            }
            Check(); Directory.CreateDirectory(output);
            var manifest = new Manifest { classification = "actual-command FP GPU replay; debug inputs retained; offline 60fps; not normal-play or performance proof",
                replay = path, replaySha256 = Hash(bytes), commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty,
                startTick = start, endTick = end, scanOnly = scan, dataHash = CanonicalContent.DataHash,
                debugCommands = replay.Commands.Where(c => c.Kind != ReplayCommandKind.Advance && c.Kind != ReplayCommandKind.ChooseCard && c.Kind != ReplayCommandKind.SetAimMode).Select(c => c.Kind.ToString()).Distinct().ToArray() };
            var manifestPath = Path.Combine(output, "sequence.json");
            var owner = new GameObject("Actual people replay"); var cameraOwner = new GameObject("Actual people replay camera");
            var camera = cameraOwner.AddComponent<Camera>(); var target = new RenderTexture(Width, Height, 24); camera.targetTexture = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false); var world = owner.AddComponent<WorldRenderer>();
            var observations = new List<Observation>(); var written = 0; var priorCaptureRate = Time.captureFramerate;
            using var csv = new StreamWriter(Path.Combine(output, "actual-people.csv"));
            csv.WriteLine("frame,tick,alpha,id,role,selectedFrame,x,y,viewportX,viewportY,centreInViewport");
            try
            {
                Time.captureFramerate = 60;
                Initialize(world, camera, session.View.CaptureFrame(), replay.Header.Options.Run.EstateId);
                world.PeopleWalkObserved = (id, role, selected, position) => observations.Add(new Observation { id = id, role = role, selectedFrame = selected, x = position.x, y = position.y });
                yield return PlayModeCaptureGuard.WaitForShaders("People recorded range setup");
                foreach (var command in replay.Commands)
                {
                    var previous = session.View.CaptureFrame(); session.Apply(command); Check(); var current = session.View.CaptureFrame();
                    if (command.Kind == ReplayCommandKind.Advance && current.Tick >= Math.Max(1, start - 15) && current.Tick <= end)
                    {
                        Assert.That(current.Tick, Is.EqualTo(previous.Tick + 1));
                        for (var sub = 1; sub <= 2; sub++)
                        {
                            var alpha = sub * .5f; observations.Clear();
                            world.Present(previous, current, session.View.CaptureFirstPlayable(), alpha, 1f / 60, new Rect(0, 0, Width, Height));
                            yield return null;
                            Assert.That(world.UnsupportedShapeCount, Is.Zero);
                            if (current.Tick < start) continue;
                            var file = "frame-" + written.ToString("D6") + ".png";
                            if (!scan)
                            {
                                var active = RenderTexture.active;
                                try { RenderTexture.active = target; PlayModeCaptureGuard.ReadPixels(image, new Rect(0, 0, Width, Height), 0, 0, "People:" + file); image.Apply(); }
                                finally { RenderTexture.active = active; }
                                File.WriteAllBytes(Path.Combine(output, file), image.EncodeToPNG());
                            }
                            foreach (var o in observations)
                            {
                                var viewport = camera.WorldToViewportPoint(new Vector3(o.x, o.y, 0));
                                o.viewportX = viewport.x; o.viewportY = viewport.y; o.centreInViewport = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
                                csv.WriteLine(string.Join(",", new[] { written.ToString(), current.Tick.ToString(), F(alpha), o.id.ToString(), o.role, o.selectedFrame.ToString(), F(o.x), F(o.y), F(o.viewportX), F(o.viewportY), o.centreInViewport ? "1" : "0" }));
                            }
                            manifest.frames.Add(new Frame { file = scan ? null : file, tick = current.Tick, appliedCommands = session.NextSequence, stateHash = session.ComputeStateHash(), alpha = alpha,
                                cameraPosition = camera.transform.position, cameraHalfHeight = camera.orthographicSize, drawCalls = world.DrawCalls, observations = observations.ToArray() });
                            written++;
                            if (written % 120 == 0) { csv.Flush(); File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true)); }
                        }
                    }
                    if (session.NextSequence % 180 == 0) yield return null;
                }
                Assert.That(session.NextSequence, Is.EqualTo(replay.End.AppliedCommands));
                Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(replay.End.Tick));
                Assert.That(session.ComputeStateHash(), Is.EqualTo(replay.End.StateHash));
                Assert.That(checkedCount, Is.EqualTo(replay.Checkpoints.Count));
                Assert.That(written, Is.EqualTo((end - start + 1) * 2));
                manifest.complete = true; manifest.terminalHash = session.ComputeStateHash(); manifest.verifiedCheckpoints = checkedCount;
            }
            finally
            {
                Time.captureFramerate = priorCaptureRate; world.PeopleWalkObserved = null;
                csv.Flush(); File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
                camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(image); Object.Destroy(owner); Object.Destroy(cameraOwner);
            }
            yield return null;
        }
        static void Initialize(WorldRenderer world, Camera camera, RunFrame initial, string estate)
        {
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var c = CanonicalContent.Presentation.Camera;
            world.Initialize(camera, new WorldCameraSettings(c.WorldUnitsPerUnityUnit, c.MinHalfHeight, c.MaxHalfHeight, c.EstatePadding, c.FollowMilliseconds, c.ZoomMilliseconds), estate, initial.MapWidth, initial.MapHeight);
        }
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        [Serializable] sealed class Observation
        {
            public int id, selectedFrame; public string role; public float x, y, viewportX, viewportY; public bool centreInViewport;
        }
        [Serializable] sealed class Frame
        {
            public string file, stateHash; public int tick, drawCalls; public long appliedCommands; public float alpha, cameraHalfHeight; public Vector3 cameraPosition; public Observation[] observations;
        }
        [Serializable] sealed class Manifest
        {
            public string classification, replay, replaySha256, commit, sourceHash, dataHash, terminalHash;
            public string visibilityBasis = "Actual WorldRenderer post-DrawActor selection and centre viewport only; not pixel visibility or occlusion. Movement must be established from positions, not selectedFrame alone.";
            public string[] debugCommands; public bool sourceDirty, scanOnly, complete; public int startTick, endTick, verifiedCheckpoints;
            public List<Frame> frames = new List<Frame>();
        }
    }
}
#endif
