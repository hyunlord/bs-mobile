#if DEVELOPMENT_BUILD && UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.View;
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
            probe.deadline = Time.realtimeSinceStartupAsDouble + 90;
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
            Click("EstateMap.Build");
            yield return null; // Normal UI queues the account action for the following update.
            if (app.EstateMap.State.Estate.Active == null || app.EstateMap.State.Estate.Active.BuildingId != field.Id)
                throw new InvalidOperationException("Normal build button did not start field construction: config=" + app.EstateMap.Config.Version + " reason=" + app.EstateMap.LastConstructionReason);
            yield return rendered;
            Capture("construction");
        }

        void Click(string name)
        {
            var button = app.Ui.GetComponentsInChildren<Button>().Single(item => item.name == name);
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
            if (!finished && Time.realtimeSinceStartupAsDouble > deadline) Finish("Timed out after 90 seconds.");
        }

        void Finish(string error)
        {
            if (finished) return;
            finished = true;
            File.WriteAllText(Path.Combine(output, "result.txt"), error == null
                ? "PASS: four standalone player captures contain zero cyan pixels. Automated normal UI actions; isolated account; no resource grants.\n"
                : "FAIL: " + error + "\n");
            foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(PreferencesPrefix + key);
            PlayerPrefs.Save();
            UnityEngine.Debug.Log("EstateEntryProbe finished: " + (error ?? "PASS"));
            Application.Quit(error == null ? 0 : 2);
        }
    }
}
#endif
