#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class FirstPlayableWorldCaptureTests
    {
        const int WarmupTicks = 15;
        const string Classification = "modified-input-stress-visual-qa-not-normal-play";

        [UnityTest, Timeout(300000)]
        public IEnumerator StressReplayProducesActualPortraitAndSquareWorldCaptures()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var fixturePath = Path.Combine(root, "artifacts/phase1b/replays/30000.ssreplay");
            Assert.That(File.Exists(fixturePath), Is.True, "Generate the canonical first-playable fixtures first.");
            var fixtureBytes = File.ReadAllBytes(fixturePath);
            ReplayDocument replay;
            using (var input = new MemoryStream(fixtureBytes, false)) replay = ReplayCodec.Read(input);
            var catalog = CanonicalContent.CreateCatalog();
            Assert.That(CanonicalContent.ProfileName, Is.EqualTo("first-playable"));
            Assert.That(catalog.FirstPlayable, Is.Not.Null);
            Assert.That(replay.Header.Options.DataHash, Is.EqualTo(CanonicalContent.DataHash));
            Assert.That(replay.Header.Options.Run.Seed, Is.EqualTo(30000));
            Assert.That(Object.FindObjectsByType<WorldRenderer>(FindObjectsSortMode.None), Is.Empty, "An existing live world would contaminate camera-null instanced captures.");
            var art = ArtCatalog.Load();
            var requested = new Dictionary<string, int> { { "early", 900 }, { "season", 6751 }, { "mid", 9000 }, { "late", 18000 } };
            var samples = new Dictionary<string, CapturedSample>();
            var trail = new List<Snapshot>();
            var checkpoints = replay.Checkpoints.ToDictionary(value => value.AppliedCommands);
            var checkedCheckpoints = 0;
            var session = new InteractiveSession(catalog, replay.Header.Options);
            var bossStart = catalog.FirstPlayable.Enemies.Values.Where(value => value.Rank == "boss").Min(value => value.FirstSpawnTick);
            var invulnerable = false; var spawnPermille = PlayerInput.Scale; var grantedLevels = 0;
            void VerifyCheckpoint()
            {
                if (!checkpoints.TryGetValue(session.NextSequence, out var checkpoint)) return;
                Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(checkpoint.Tick));
                Assert.That(session.ComputeStateHash(), Is.EqualTo(checkpoint.StateHash), "Fixture checkpoint " + checkpoint.AppliedCommands);
                checkedCheckpoints++;
            }
            CapturedSample Retain(string name, Snapshot snapshot)
            {
                return new CapturedSample
                {
                    Name = name, Trail = trail.ToArray(), Snapshot = snapshot,
                    AppliedCommands = session.NextSequence, StateHash = session.ComputeStateHash(),
                    Invulnerable = invulnerable, SpawnPermille = spawnPermille, GrantedLevels = grantedLevels
                };
            }
            VerifyCheckpoint();
            foreach (var command in replay.Commands)
            {
                session.Apply(command);
                if (command.Kind == ReplayCommandKind.SetInvulnerable) invulnerable = command.Value == 1;
                if (command.Kind == ReplayCommandKind.SetSpawnPermille) spawnPermille = command.Value;
                if (command.Kind == ReplayCommandKind.GrantLevel) grantedLevels++;
                VerifyCheckpoint();
                var tick = command.Tick + (command.Kind == ReplayCommandKind.Advance ? 1 : 0);
                var captureWindow = requested.Any(pair => !samples.ContainsKey(pair.Key) && tick >= pair.Value - WarmupTicks && tick <= pair.Value)
                    || !samples.ContainsKey("boss") && tick >= bossStart - WarmupTicks;
                if (captureWindow)
                {
                    var snapshot = new Snapshot(session.View.CaptureFrame(), session.View.CaptureFirstPlayable());
                    Assert.That(snapshot.FirstPlayable, Is.Not.Null);
                    trail.Add(snapshot); trail.RemoveAll(value => value.Frame.Tick < tick - WarmupTicks);
                    if (command.Kind == ReplayCommandKind.Advance)
                    {
                        foreach (var pair in requested)
                            if (tick == pair.Value && !samples.ContainsKey(pair.Key)) samples.Add(pair.Key, Retain(pair.Key, snapshot));
                        if (!samples.ContainsKey("boss") && BossVisibleInPortrait(snapshot, art)) samples.Add("boss", Retain("boss", snapshot));
                    }
                }
                if (session.NextSequence % 180 == 0) yield return null;
            }
            var summary = session.GetSummary();
            Assert.That(session.NextSequence, Is.EqualTo(replay.End.AppliedCommands));
            Assert.That(summary.Tick, Is.EqualTo(replay.End.Tick));
            Assert.That(summary.StateHash, Is.EqualTo(replay.End.StateHash));
            Assert.That(summary.EndReason, Is.EqualTo("duration"));
            Assert.That(replay.End.Kind, Is.EqualTo(ReplayEndKind.Duration));
            Assert.That(checkedCheckpoints, Is.EqualTo(replay.Checkpoints.Count));
            Assert.That(samples.Keys, Is.EquivalentTo(new[] { "early", "season", "mid", "late", "boss" }), "An actual visible active boss must be observed; no fabricated boss snapshot is allowed.");
            var output = Path.Combine(root, "artifacts/phase1b/m2-runtime"); Directory.CreateDirectory(output);
            var manifest = new CaptureManifest
            {
                classification = Classification, worldOnly = true, normalPlayEvidence = false,
                fixture = "artifacts/phase1b/replays/30000.ssreplay", fixtureSha256 = Hash(fixtureBytes), seed = replay.Header.Options.Run.Seed,
                profile = CanonicalContent.ProfileName, profileHash = CanonicalContent.ProfileHash, dataHash = CanonicalContent.DataHash,
                commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty,
                testSourceSha256 = Hash(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Tests/PlayMode/FirstPlayableWorldCaptureTests.cs"))),
                unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                backend = "Editor Mono", terminalTick = summary.Tick, terminalStateHash = summary.StateHash, verifiedCheckpoints = checkedCheckpoints,
                invulnerableCommandPresent = replay.Commands.Any(value => value.Kind == ReplayCommandKind.SetInvulnerable && value.Value == 1),
                debugSpawnCommandPresent = replay.Commands.Any(value => value.Kind == ReplayCommandKind.SetSpawnPermille),
                grantLevelCommandPresent = replay.Commands.Any(value => value.Kind == ReplayCommandKind.GrantLevel)
            };
            var rows = new List<CaptureRow>();
            var manifestPath = Path.Combine(output, "stress-replay-30000-manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            foreach (var name in new[] { "early", "season", "mid", "late", "boss" })
            {
                yield return Capture(samples[name], replay.Header.Options.Run.EstateId, 900, 1600, "portrait", output, rows);
                yield return Capture(samples[name], replay.Header.Options.Run.EstateId, 1080, 1080, "square", output, rows);
                manifest.captures = rows.ToArray(); File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            }
            manifest.captures = rows.ToArray();
            Assert.That(manifest.captures.Length, Is.EqualTo(10));
            manifest.complete = true;
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        static bool BossVisibleInPortrait(Snapshot snapshot, ArtCatalog art)
        {
            var boss = snapshot.FirstPlayable.Bosses.FirstOrDefault(value => value.Rank == "boss" && value.State == "active");
            if (boss == null) return false;
            var enemy = snapshot.Frame.Enemies.FirstOrDefault(value => value.Id == boss.EntityId);
            if (enemy == null) return false;
            var settings = CanonicalContent.Presentation.Camera; const float aspect = 900f / 1600;
            var halfHeight = Mathf.Clamp((snapshot.Frame.EstateExtent + settings.EstatePadding) / aspect, settings.MinHalfHeight, settings.MaxHalfHeight);
            var visual = art.Resolve("enemy", enemy.DefinitionId, "idle");
            return Mathf.Abs(enemy.Position.X - snapshot.Frame.Lord.Position.X) + visual.WorldSize.x * settings.WorldUnitsPerUnityUnit < halfHeight * aspect
                && Mathf.Abs(enemy.Position.Y - snapshot.Frame.Lord.Position.Y) + visual.WorldSize.y * settings.WorldUnitsPerUnityUnit < halfHeight;
        }

        static IEnumerator Capture(CapturedSample sample, string estate, int width, int height, string aspect, string output, List<CaptureRow> rows)
        {
            var owner = new GameObject("Stress replay world capture");
            var cameraOwner = new GameObject("Stress replay capture camera");
            var camera = cameraOwner.AddComponent<Camera>();
            var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
            var world = owner.AddComponent<WorldRenderer>();
            Texture2D image = null;
            try
            {
                var settings = CanonicalContent.Presentation.Camera;
                var frame = sample.Snapshot.Frame;
                world.Initialize(camera, new WorldCameraSettings(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), estate, frame.MapWidth, frame.MapHeight);
                var previous = sample.Trail[0].Frame;
                foreach (var snapshot in sample.Trail)
                {
                    var delta = (float)(snapshot.Frame.Tick - previous.Tick) / snapshot.Frame.TickRate;
                    world.Present(previous, snapshot.Frame, snapshot.FirstPlayable, 1, delta, new Rect(0, 0, width, height));
                    previous = snapshot.Frame; yield return null;
                }
                var beforeSample = sample.Trail.Length > 1 ? sample.Trail[sample.Trail.Length - 2].Frame : frame;
                for (var i = 0; i < 2; i++) { world.Present(beforeSample, frame, sample.Snapshot.FirstPlayable, 1, 0, new Rect(0, 0, width, height)); yield return null; }
                Assert.That(world.UnsupportedShapeCount, Is.Zero);
                Assert.That(world.SubmittedInstances, Is.GreaterThan(frame.Enemies.Count));
                Assert.That(world.DrawCalls, Is.GreaterThan(0));
                Assert.That(owner.GetComponentsInChildren<SpriteRenderer>(), Is.Empty, "Entity art must remain instanced.");
                var before = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                }
                finally { RenderTexture.active = before; }
                Assert.That(image.GetPixels32().Select(value => (value.r >> 4) * 256 + (value.g >> 4) * 16 + (value.b >> 4)).Distinct().Count(), Is.GreaterThan(20), "The GPU capture must contain authored textured pixels.");
                var filename = "stress-replay-30000-" + sample.Name + "-tick" + frame.Tick + "-" + aspect + ".png";
                var png = image.EncodeToPNG(); File.WriteAllBytes(Path.Combine(output, filename), png);
                var roles = ExpectedSnapshotRoles(sample.Snapshot, estate);
                Assert.That(roles.Length, Is.GreaterThan(1));
                var boss = sample.Snapshot.FirstPlayable.Bosses.FirstOrDefault(value => value.Rank == "boss" && value.State == "active");
                var bossOnScreen = false;
                if (boss != null)
                {
                    var actor = frame.Enemies.Single(value => value.Id == boss.EntityId);
                    var viewport = camera.WorldToViewportPoint(new Vector3((float)actor.Position.X / settings.WorldUnitsPerUnityUnit, (float)actor.Position.Y / settings.WorldUnitsPerUnityUnit, 0));
                    bossOnScreen = viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1;
                }
                if (sample.Name == "boss") Assert.That(bossOnScreen, Is.True, "Actual boss must be inside the production-follow camera.");
                rows.Add(new CaptureRow
                {
                    file = filename, pngSha256 = Hash(png), sample = sample.Name, tick = frame.Tick, appliedCommands = sample.AppliedCommands,
                    stateHash = sample.StateHash, width = width, height = height, warmupFromTick = sample.Trail[0].Frame.Tick,
                    invulnerable = sample.Invulnerable, debugSpawnPermille = sample.SpawnPermille, grantedLevels = sample.GrantedLevels,
                    enemies = frame.Enemies.Count, people = frame.People.Count, farms = frame.Farms.Count, buildings = frame.Buildings.Count,
                    persistentAttacks = sample.Snapshot.FirstPlayable.Attacks.Count, mapEvents = sample.Snapshot.FirstPlayable.MapEvents.Count,
                    season = frame.Season, level = frame.Level, submittedInstances = world.SubmittedInstances, drawCalls = world.DrawCalls,
                    activeEffects = world.ActiveEffects, droppedEffects = world.DroppedEffects, unsupportedShapes = world.UnsupportedShapeCount,
                    cameraPosition = camera.transform.position, cameraHalfHeight = camera.orthographicSize, bossOnScreen = bossOnScreen,
                    expectedSnapshotRoleIds = roles,
                    roleEvidence = "Catalog-only durable-state bindings; actor idle/death family references; transient feedback excluded. Not submitted or pixel-attributed roles.",
                    enemyDefinitionIds = frame.Enemies.Select(value => value.DefinitionId).Distinct().OrderBy(value => value).ToArray(),
                    attackForms = sample.Snapshot.FirstPlayable.Attacks.Select(value => value.Form).Distinct().OrderBy(value => value).ToArray(),
                    buildingStates = sample.Snapshot.FirstPlayable.BuildingProgress.Select(value => value.State).Distinct().OrderBy(value => value).ToArray(),
                    personActivities = sample.Snapshot.FirstPlayable.People.Select(value => value.Activity).Distinct().OrderBy(value => value).ToArray()
                });
            }
            finally
            {
                camera.targetTexture = null; target.Release(); Object.Destroy(target); if (image != null) Object.Destroy(image);
                Object.Destroy(owner); Object.Destroy(cameraOwner);
            }
            yield return null;
        }

        static string[] ExpectedSnapshotRoles(Snapshot snapshot, string estate)
        {
            var art = ArtCatalog.Load(); var roles = new HashSet<string>(StringComparer.Ordinal);
            void Add(string kind, string id, string state) => roles.Add(art.Resolve(kind, id, state).RoleId);
            var frame = snapshot.Frame; var firstPlayable = snapshot.FirstPlayable;
            Add("hero", firstPlayable.HeroId, frame.Lord.Health <= 0 ? "death" : "idle");
            foreach (var enemy in frame.Enemies) Add("enemy", enemy.DefinitionId, enemy.Health <= 0 ? "death" : "idle");
            foreach (var farm in frame.Farms) Add("crop", farm.SourceId, "stage" + farm.Stage);
            var construction = firstPlayable.BuildingProgress.ToDictionary(value => value.Id);
            foreach (var building in frame.Buildings) Add("building", building.SourceId, construction[building.Id].State);
            var activities = firstPlayable.People.ToDictionary(value => value.Id);
            foreach (var person in frame.People) Add("person", person.Role, activities[person.Id].Activity);
            foreach (var attack in firstPlayable.Attacks) Add("attack", attack.SourceId, attack.Form);
            foreach (var mapEvent in firstPlayable.MapEvents)
                Add("mapEvent", mapEvent.DefinitionId, mapEvent.MaxHealth > 0 && mapEvent.Health < mapEvent.MaxHealth ? "damaged" : "present");
            if (frame.Loot.Count > 0) Add("object", "loot", "default");
            if (frame.Remains.Count > 0) Add("object", "remains", "default");
            Add("terrain", estate, GameVisualTokens.SeasonNames[frame.Season]);
            Add("boundary", "edge", "default"); Add("boundary", "corner", "default");
            return roles.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        sealed class Snapshot
        {
            public readonly RunFrame Frame;
            public readonly FirstPlayableFrame FirstPlayable;
            public Snapshot(RunFrame frame, FirstPlayableFrame firstPlayable) { Frame = frame; FirstPlayable = firstPlayable; }
        }
        sealed class CapturedSample
        {
            public string Name, StateHash;
            public Snapshot Snapshot;
            public Snapshot[] Trail;
            public long AppliedCommands;
            public bool Invulnerable;
            public int SpawnPermille, GrantedLevels;
        }
        [Serializable] sealed class CaptureManifest
        {
            public string classification, fixture, fixtureSha256, profile, profileHash, dataHash, commit, sourceHash, testSourceSha256, unity, graphicsDevice, graphicsApi, backend, terminalStateHash;
            public bool complete, worldOnly, normalPlayEvidence, sourceDirty, invulnerableCommandPresent, debugSpawnCommandPresent, grantLevelCommandPresent;
            public int seed, terminalTick, verifiedCheckpoints;
            public CaptureRow[] captures;
        }
        [Serializable] sealed class CaptureRow
        {
            public string file, pngSha256, sample, stateHash, roleEvidence;
            public long appliedCommands;
            public bool invulnerable, bossOnScreen;
            public int tick, width, height, warmupFromTick, debugSpawnPermille, grantedLevels, enemies, people, farms, buildings, persistentAttacks, mapEvents, season, level, submittedInstances, drawCalls, activeEffects, droppedEffects, unsupportedShapes;
            public float cameraHalfHeight;
            public Vector3 cameraPosition;
            public string[] expectedSnapshotRoleIds, enemyDefinitionIds, attackForms, buildingStates, personActivities;
        }
    }
}
#endif
