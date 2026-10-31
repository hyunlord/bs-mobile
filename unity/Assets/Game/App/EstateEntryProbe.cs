#if DEVELOPMENT_BUILD && UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.View;
using Game.App.Generated;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // Explicit developer evidence capture; absent from Editor and non-development players.
    public sealed class EstateEntryProbe : MonoBehaviour
    {
        public static string ProfileDirectory { get; private set; }
        public static string PreferencesPrefix { get; private set; }
        string output;
        double deadline;
        bool finished;
        RunCoordinator app;
        readonly WaitForEndOfFrame rendered = new WaitForEndOfFrame();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--estate-entry-probe") < 0) return;
            int index = Array.IndexOf(args, "--estate-entry-output");
            if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
            { UnityEngine.Debug.LogError("Estate entry probe requires an absolute --estate-entry-output path."); Application.Quit(2); return; }
            string destination = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            { UnityEngine.Debug.LogError("Estate entry probe refuses non-empty output: " + destination); Application.Quit(2); return; }
            Directory.CreateDirectory(destination);
            ProfileDirectory = Path.Combine(destination, "isolated-account");
            PreferencesPrefix = "sowsiege.estate-entry-probe." + Guid.NewGuid().ToString("N") + ".";
            PlayerPrefs.SetInt(PreferencesPrefix + "introductionSeen", 1);
            PlayerPrefs.SetInt(PreferencesPrefix + "estatePrototype", 1);
            PlayerPrefs.Save();
            var owner = new GameObject("Estate entry development probe");
            DontDestroyOnLoad(owner);
            var probe = owner.AddComponent<EstateEntryProbe>();
            probe.output = destination;
            probe.deadline = Time.realtimeSinceStartupAsDouble + 180;
            Application.runInBackground = true;
            Screen.SetResolution(390, 844, FullScreenMode.Windowed);
        }

        IEnumerator Start()
        {
            File.WriteAllText(Path.Combine(output, "frames.csv"),
                "capture,frame,screen,width,height,cyanPixels,anythingCompilingBefore,anythingCompilingAfter,focused\n");
            var flow = Flow();
            while (!finished)
            {
                object next;
                try { if (!flow.MoveNext()) { Finish(null); yield break; } next = flow.Current; }
                catch (Exception error) { Finish(error.ToString()); yield break; }
                yield return next;
            }
        }

        IEnumerator Flow()
        {
            while (app == null || app.CurrentScreenName != "Title")
            {
                app = FindAnyObjectByType<RunCoordinator>();
                if (app != null && !string.IsNullOrEmpty(app.Error)) throw new InvalidOperationException(app.Error);
                yield return null;
            }
            UnityEngine.Debug.Log("EstateEntryProbe title contract: profile=" + CanonicalContent.ProfileName +
                "; isWave=" + app.IsWave + "; portrait=" + app.Ui.PortraitEnabled);
            if (!app.Ui.PortraitEnabled)
                throw new InvalidOperationException("Estate entry probe requires the wave-1a portrait build. The first-playable 시작 button starts combat, not the estate; no substitute click was attempted.");
            app.Ui.SynchronizeViewport();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Click("Title.Enter");
            if (app.CurrentScreenName != "EstateMap") throw new InvalidOperationException("Title did not enter EstateMap: " + app.CurrentScreenName);
            // Offset zero means the first completed player render after the title action.
            for (int frame = 0; frame <= 30; frame++)
            {
                yield return rendered;
                if (frame == 0 || frame == 1 || frame == 30) Capture("first-entry-" + frame);
                if (frame < 30) yield return null;
            }
            var field = app.EstateMap.State.Estate.Buildings.Single(building => building.Kind == "m3a:field");
            var view = app.Ui.GetComponentsInChildren<EstateMapView>().Single();
            view.Tapped?.Invoke(new Vector2Int(field.X, field.Y));
            yield return null;
            var build = app.Ui.GetComponentsInChildren<Button>().Single(item => item.name == "EstateMap.Build");
            var reason = app.Ui.GetComponentsInChildren<Text>().SingleOrDefault(item => item.name == "EstateMap.ConstructionReason");
            var eligibility = app.EstateMap.ConstructionPreview(field.Id);
            if (build.interactable || eligibility.Reason != "parent-required" || reason == null || reason.text != eligibility.Text)
                throw new InvalidOperationException("Fresh field prerequisite is not visible or build is enabled: reason=" + eligibility.Reason);
            yield return rendered;
            Capture("field-parent-required");
            File.WriteAllText(Path.Combine(output, "construction.csv"), "stage,buildingId,reason,level,elapsedSeconds,grain,timber\n");
            RecordConstruction("field-disabled", field.Id, eligibility.Reason);

            // Follow the first production path with ordinary buttons and real elapsed time. No development skip, grants or
            // direct state edits: the granary costs 30 timber / 60 s, then the field costs 10 grain + 10 timber / 30 s in r7.
            foreach (var kind in new[] { "meta:granary", "m3a:field" })
            {
                var building = app.EstateMap.State.Estate.Buildings.Single(item => item.Kind == kind);
                view = app.Ui.GetComponentsInChildren<EstateMapView>().Single();
                view.Tapped?.Invoke(new Vector2Int(building.X, building.Y));
                yield return null;
                Click("EstateMap.Build");
                yield return null;
                RecordConstruction("start", building.Id, app.EstateMap.LastConstructionReason);
                if (app.EstateMap.State.Estate.Active == null || app.EstateMap.State.Estate.Active.BuildingId != building.Id)
                    throw new InvalidOperationException("Normal build button did not start " + kind + ": config=" + app.EstateMap.Config.Version + " reason=" + app.EstateMap.LastConstructionReason);
                yield return rendered;
                Capture(kind == "meta:granary" ? "granary-construction" : "construction");
                var capturedStage = 1;
                while (app.EstateMap.Building(building.Id).Level == 0)
                {
                    var stage = EstateConstructionArt.Stage(app.EstateMap.State.Estate.Active);
                    if (stage > capturedStage)
                    {
                        // Observe the real clock only: no resource grants, direct state edits or time acceleration.
                        capturedStage = stage;
                        yield return rendered;
                        Capture((kind == "meta:granary" ? "granary" : "field") + "-construction-stage-" + stage);
                        RecordConstruction("art-stage-" + stage, building.Id, "observed");
                    }
                    yield return null;
                }
                RecordConstruction("complete", building.Id, "completed");
                yield return rendered;
                Capture(kind == "meta:granary" ? "granary-complete" : "field-complete");
            }
        }

        void RecordConstruction(string stage, int id, string reason)
        {
            var state = app.EstateMap.State;
            string row = stage + "," + id + "," + reason + "," + app.EstateMap.Building(id).Level + "," +
                state.Estate.ElapsedSeconds + "," + state.Wallet["meta:grain"] + "," + state.Wallet["meta:timber"];
            File.AppendAllText(Path.Combine(output, "construction.csv"), row + "\n");
            UnityEngine.Debug.Log("EstateEntryProbe construction: " + row);
        }

        void Click(string name)
        {
            var buttons = app.Ui.GetComponentsInChildren<Button>(true);
            var matches = buttons.Where(item => item.name == name && item.gameObject.activeInHierarchy).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("UI action unavailable: " + name + "; matches=" + matches.Length +
                    "; screen=" + app.CurrentScreenName + "; frame=" + Time.frameCount +
                    "; uiActive=" + app.Ui.gameObject.activeInHierarchy + "; buttons=" +
                    string.Join("|", buttons.Select(item => item.name + ":active=" + item.gameObject.activeInHierarchy + ":enabled=" + item.interactable)));
            var button = matches[0];
            if (!button.interactable) throw new InvalidOperationException("Disabled normal UI button: " + name);
            UnityEngine.Debug.Log("EstateEntryProbe UI action: " + name);
            button.onClick.Invoke();
        }

        void Capture(string name)
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) throw new InvalidOperationException("Player capture returned no texture.");
            int cyan = 0;
            try
            {
                foreach (var pixel in texture.GetPixels32())
                    if (pixel.r < 16 && pixel.g > 230 && pixel.b > 230) cyan++;
                File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
                string row = name + "," + Time.frameCount + "," + app.CurrentScreenName + "," + texture.width + "," + texture.height + "," + cyan + ",NA-Standalone,NA-Standalone," + Application.isFocused + "\n";
                File.AppendAllText(Path.Combine(output, "frames.csv"), row);
                UnityEngine.Debug.Log("EstateEntryProbe " + row.Trim());
            }
            finally { Destroy(texture); }
            if (cyan != 0) throw new InvalidOperationException(name + " contains " + cyan + " cyan pixels.");
        }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartupAsDouble > deadline) Finish("Timed out after 180 seconds.");
        }

        void Finish(string error)
        {
            if (finished) return;
            finished = true;
            File.WriteAllText(Path.Combine(output, "result.txt"), error == null
                ? "PASS: eight standalone player captures contain zero cyan pixels; fresh field prerequisite visible with disabled build; granary then field completed through normal UI and real time. Isolated account; no resource grants or time skips.\n"
                : "FAIL: " + error + "\n");
            foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(PreferencesPrefix + key);
            PlayerPrefs.Save();
            UnityEngine.Debug.Log("EstateEntryProbe finished: " + (error ?? "PASS"));
            Application.Quit(error == null ? 0 : 2);
        }
    }
}
#endif
