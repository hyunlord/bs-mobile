using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.WorldCapture
{
    [InitializeOnLoad]
    public static class WorldCapture
    {
        const string Active = "SowSiege.WorldCapture.Active";
        static bool attached;
        static WorldCapture()
        {
            if (SessionState.GetBool(Active, false)) ConfigureProfile();
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Active + ".deadline", float.MaxValue)) { Finish(false, "Capture timeout."); return; }
                if (attached) return;
                var app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>();
                if (app == null || app.Ui == null) return;
                attached = true; app.StartCoroutine(new Runner().Execute());
            };
        }
        static void ConfigureProfile()
        {
            RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
            RunCoordinator.PreferencePrefixOverride = "sowsiege.world-capture." + SessionState.GetString(Active + ".id", "") + ".";
        }
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit mode.");
            if (Directory.Exists(Output) && Directory.EnumerateFileSystemEntries(Output).Any()) throw new IOException("Capture output must be empty.");
            if (!File.Exists(Environment.GetEnvironmentVariable("WORLD_CAPTURE_TOKEN_FILE"))) throw new IOException("Private prototype token file required.");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Active + ".async", ShaderUtil.allowAsyncCompilation);
            SessionState.SetString(Active + ".id", Guid.NewGuid().ToString("N"));
            SessionState.SetBool(Active, true);
            SessionState.SetFloat(Active + ".deadline", (float)(EditorApplication.timeSinceStartup + 1800));
            attached = false; ConfigureProfile(); ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "World API QA"); EditorApplication.isPlaying = true;
        }
        internal static string Output => Environment.GetEnvironmentVariable("WORLD_CAPTURE_DIR") ?? throw new InvalidOperationException("Explicit owned artifact output required.");
        internal static void Finish(bool success, string reason)
        {
            if (!SessionState.GetBool(Active, false)) return;
            SessionState.SetBool(Active, false); ShaderUtil.allowAsyncCompilation = SessionState.GetBool(Active + ".async", true);
            File.WriteAllText(Path.Combine(Output, "result.txt"), (success ? "PASS " : "FAIL ") + reason);
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
        sealed class Runner
        {
            RunCoordinator app;
            public IEnumerator Execute() => Guard(Run());
            IEnumerator Guard(IEnumerator steps)
            {
                while (true)
                {
                    object next;
                    try { if (!steps.MoveNext()) break; next = steps.Current; }
                    catch (Exception error) { Finish(false, error.GetType().Name + ": " + error.Message); yield break; }
                    if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
                }
            }
            IEnumerator Run()
            {
                var deadline = Time.realtimeSinceStartupAsDouble + 45;
                while ((app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || Button("Title.Settings") == null)
                { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Title not ready."); yield return null; }
                Click("Title.Settings"); yield return null; Click("Settings.Barracks"); yield return null;
                var token = File.ReadAllText(Environment.GetEnvironmentVariable("WORLD_CAPTURE_TOKEN_FILE")).Trim();
                var input = app.Ui.GetComponentInChildren<InputField>(); if (input == null) throw new InvalidOperationException("Prototype connection field missing.");
                input.text = token; Click("Barracks.Connect"); yield return Ready("Barracks.Refresh");
                yield return TrainThroughUi();
                // Use the same public route as the bottom tab. No server state or Core game outcome is injected.
                app.NavigatePortraitMeta(PortraitMetaPage.Estate); yield return null; Click("Tab.WorldMap"); yield return null;
                Click("World.Home"); yield return Ready("World.Home");
                if (!app.Ui.GetComponentsInChildren<Button>().Any(b => b.name.StartsWith("World.Tile.", StringComparison.Ordinal))) throw new InvalidOperationException("No authoritative tiles rendered.");
                yield return Capture("01-city-390x844");
                Click("World.East"); yield return Ready("World.Home"); yield return Capture("02-east-390x844");
                Click("World.Home"); yield return Ready("World.Home");
                var tile = app.Ui.GetComponentsInChildren<Button>().First(b => b.name.StartsWith("World.Tile.", StringComparison.Ordinal)); tile.onClick.Invoke(); yield return null;
                var scroll = app.Ui.GetComponentInChildren<ScrollRect>(); Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0;
                yield return Capture("03-selection-390x844");
                PlayModeWindow.SetCustomRenderingResolution(1080, 2520, "World tall QA"); yield return null; yield return null;
                app.NavigatePortraitMeta(PortraitMetaPage.WorldMap); yield return null; yield return Capture("04-city-1080x2520");
                PlayModeWindow.SetCustomRenderingResolution(390, 844, "World march QA"); yield return null; yield return null;
                yield return GatherThroughUi();
                var args = Environment.GetCommandLineArgs(); var at = Array.IndexOf(args, "--server-url");
                if (at < 0 || at + 1 >= args.Length) throw new InvalidOperationException("Explicit server URL missing.");
                var client = new BarracksServerClient(args[at + 1], Path.Combine(Output, "contract-journal"));
                yield return client.Connect(token, false); yield return client.LoadWorldCity();
                var city = client.WorldCity ?? throw new InvalidOperationException("City GET failed.");
                yield return client.LoadWorldCity(true);
                if (client.WorldCity?.Id != city.Id || client.WorldCity.Generation != city.Generation) throw new InvalidOperationException("Idempotent city allocation changed identity.");
                yield return client.LoadWorldChunk(0, 0); var chunk = client.WorldChunk ?? throw new InvalidOperationException("Chunk GET failed.");
                yield return client.LoadWorldChunk(int.MaxValue, int.MaxValue);
                if (client.Status != "invalid-chunk" || client.WorldChunk != chunk) throw new InvalidOperationException("Invalid chunk did not preserve confirmed cache.");
                client.ClearAuthentication();
                if (client.WorldCity != null || client.WorldChunk != null) throw new InvalidOperationException("World cache survived logout.");
                File.WriteAllText(Path.Combine(Output, "api-proof.txt"), "cityGET=PASS cityPOST-idempotent=PASS chunkGET=PASS invalidChunk-preservesCache=PASS clearAuthentication=PASS\nNo fixture responses or gameplay state were injected. Prototype bearer is never written into capture output.\n");
                Finish(true, "Actual training, gathering, recall, returned resources and city/chunk UI checks completed; prototype auth, not native key-store proof.");
            }
            BarracksServerClient ObservedClient => app.WorldCaptureTransport;
            IEnumerator TrainThroughUi()
            {
                var client = ObservedClient;
                var available = client.Latest.Training.units.FirstOrDefault(u => u.quotes?.Any(q => q.count == 1 && q.canStart) == true);
                if (available == null) throw new InvalidOperationException("Prepare normal barracks prerequisites before capture.");
                var before = available.ready;
                Click("Barracks.Unit:" + available.unitId); yield return null;
                Click("Barracks.Train"); yield return null; Click("Barracks.Train"); yield return Ready("Barracks.Refresh");
                var deadline = Time.realtimeSinceStartupAsDouble + 600;
                while (client.Latest.Training.units.Single(u => u.unitId == available.unitId).ready <= before)
                {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Normal training did not complete.");
                    yield return new WaitForSecondsRealtime(5);
                    yield return Ready("Barracks.Refresh"); Click("Barracks.Refresh"); yield return Ready("Barracks.Refresh");
                }
                yield return Capture("05-training-complete-390x844");
                if (client.RecruitView.heroes.Count == 0)
                {
                    Click("Barracks.Recruit"); yield return null; Click("Recruit.Submit"); yield return null;
                    Click("Recruit.Submit"); yield return Ready("Recruit.Refresh");
                    if (client.RecruitView.heroes.Count == 0) throw new InvalidOperationException("No server-owned recruit after normal UI request.");
                    yield return Capture("06-owned-hero-390x844");
                }
            }
            IEnumerator GatherThroughUi()
            {
                Click("World.Home"); yield return Ready("World.Home");
                var client = ObservedClient;
                var target = client.WorldChunk.Targets.Where(t => t.Kind == "resource").OrderBy(t => Math.Abs(t.X-client.WorldCity.X)+Math.Abs(t.Y-client.WorldCity.Y)).FirstOrDefault();
                if (target == null) throw new InvalidOperationException("Home chunk has no resource for actual UI scenario.");
                var tileName = "World.Tile." + target.X + "." + target.Y;
                for (var attempt=0; Button(tileName)==null && attempt<16; attempt++)
                {
                    var tiles=app.Ui.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("World.Tile.",StringComparison.Ordinal)).Select(b=>b.name.Split('.')).ToArray();
                    var xs=tiles.Select(v=>int.Parse(v[2])).ToArray();var ys=tiles.Select(v=>int.Parse(v[3])).ToArray();
                    var direction=target.X<xs.Min()?"West":target.X>xs.Max()?"East":target.Y<ys.Min()?"North":"South";
                    Click("World."+direction);yield return Ready("World.Home");
                }
                Click(tileName);yield return null;
                var scroll=app.Ui.GetComponentInChildren<ScrollRect>();Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=0;
                yield return Capture("07-resource-stock-unavailable-390x844");
                var readyBefore=client.Latest.Training.units.Sum(u=>u.ready);
                var walletBefore=client.Latest.resources.Sum(r=>(long)r.amount)+(client.Latest.held?.Sum(r=>(long)r.amount)??0);
                Click("Gather.Submit");yield return null;Click("Gather.Submit");yield return Ready("March.Refresh");
                var march=client.WorldMarchResult?.March;
                if(client.WorldMarchResult?.Done!=true || march==null)throw new InvalidOperationException("Gather dispatch not confirmed.");
                if(client.Latest.Training.units.Sum(u=>u.ready)>=readyBefore)throw new InvalidOperationException("Dispatch did not reserve ready troops.");
                yield return Capture("08-march-outbound-390x844");
                yield return AwaitMarch(march.Id,"gathering");
                yield return Capture("09-march-gathering-390x844");
                yield return new WaitForSecondsRealtime(65);
                Click("March.Refresh");yield return Ready("March.Refresh");
                Click("March.Recall:"+march.Id);yield return null;Click("March.Recall:"+march.Id);yield return Ready("March.Refresh");
                yield return Capture("10-march-returning-390x844");
                yield return AwaitMarch(march.Id,"returned");
                // Refresh once more after the terminal march observation so account data reflects settlement.
                Click("March.Refresh");yield return Ready("March.Refresh");
                var walletAfter=client.Latest.resources.Sum(r=>(long)r.amount)+(client.Latest.held?.Sum(r=>(long)r.amount)??0);
                if(walletAfter<=walletBefore)throw new InvalidOperationException("Returned march did not increase wallet or held stock.");
                if(client.Latest.Training.units.Sum(u=>u.ready)!=readyBefore)throw new InvalidOperationException("Returned troop count differs from before dispatch.");
                yield return Capture("11-march-returned-390x844");
                File.WriteAllText(Path.Combine(Output,"march-proof.txt"),"normalTraining=PASS ownedHero=PASS dispatch=PASS gathering=PASS recall=PASS returned=PASS walletAndHeldDelta="+(walletAfter-walletBefore)+"\nServer clock and normal UI commands only. Missing node stock/cargo remain unavailable.\n");
            }
            IEnumerator AwaitMarch(string id,string state)
            {
                var deadline=Time.realtimeSinceStartupAsDouble+600;
                while(ObservedClient.WorldMarches?.SingleOrDefault(m=>m.Id==id)?.State!=state)
                {
                    if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("March stage not observed: "+state);
                    yield return new WaitForSecondsRealtime(5);Click("March.Refresh");yield return Ready("March.Refresh");
                }
            }
            Button Button(string name) => app?.Ui?.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);
            void Click(string name)
            {
                var button = Button(name); if (button == null || !button.interactable) throw new InvalidOperationException("Unavailable action: " + name);
                button.onClick.Invoke();
            }
            IEnumerator Ready(string name)
            {
                var deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (Button(name)?.interactable != true)
                { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Server action timeout."); yield return null; }
                yield return null;
            }
            IEnumerator Capture(string name)
            {
                var deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (ShaderUtil.anythingCompiling)
                { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Shader compilation timeout."); yield return null; }
                var settle = Time.realtimeSinceStartupAsDouble + .2;
                while (Time.realtimeSinceStartupAsDouble < settle) yield return null;
                yield return null; yield return null;
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null) throw new InvalidOperationException("Capture missing.");
                try
                {
                    var cyan = texture.GetPixels32().Count(c => c.r < 16 && c.g > 230 && c.b > 230);
                    File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
                    File.WriteAllText(Path.Combine(Output, name + ".txt"), "cyanPixels=" + cyan + "\n" + string.Join("\n", app.Ui.GetComponentsInChildren<Text>().Where(t => t.GetComponentInParent<InputField>() == null).Select(t => t.text)));
                    if (cyan != 0) throw new InvalidOperationException("Cyan fallback detected.");
                }
                finally { UnityEngine.Object.Destroy(texture); }
            }
        }
    }
}
