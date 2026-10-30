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

namespace Game.BarracksCapture
{
    public sealed class BarracksCaptureRunner : MonoBehaviour
    {
        RunCoordinator app;
        void Start() { ShaderUtil.allowAsyncCompilation = false; StartCoroutine(Guard(Run())); }
        IEnumerator Guard(IEnumerator steps)
        {
            while (true)
            {
                object next;
                try { if (!steps.MoveNext()) break; next = steps.Current; }
                catch (Exception error) { BarracksCapture.Finish(false, error.GetType().Name + ": " + error.Message); yield break; }
                if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }
        IEnumerator Run()
        {
            var until = Time.realtimeSinceStartupAsDouble + 45;
            while ((app = FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || Button("Title.Settings") == null)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Title.Settings not ready."); yield return null; }
            Click("Title.Settings"); yield return null; Click("Settings.Barracks"); yield return null;
            var field = app.Ui.GetComponentInChildren<InputField>();
            if (field == null) throw new InvalidOperationException("Explicit server connection field missing.");
            field.text = File.ReadAllText(Environment.GetEnvironmentVariable("BARRACKS_CAPTURE_TOKEN_FILE")).Trim();
            Click("Barracks.Connect"); yield return Ready(); yield return Capture("01-connected");
            Click("Barracks.Train"); yield return null; yield return Capture("02-confirm"); Click("Barracks.Train");
            yield return Ready(); yield return Capture("03-active");
            Click("Barracks.Train"); yield return null; Click("Barracks.Train"); yield return Ready(); yield return Capture("04-waiting");
            if (Button("Barracks.CancelWaiting") == null) throw new InvalidOperationException("Server waiting queue was not created.");
            BarracksCapture.Finish(true, "Live server connected, start and waiting queue submitted through real UI.");
        }
        Button Button(string name) => app?.Ui?.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);
        void Click(string name)
        {
            var button = Button(name);
            if (button == null || !button.interactable) throw new InvalidOperationException("Unavailable action: " + name);
            button.onClick.Invoke();
        }
        IEnumerator Ready()
        {
            var until = Time.realtimeSinceStartupAsDouble + 20;
            while (Button("Barracks.Refresh")?.interactable != true)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Server response not ready."); yield return null; }
            yield return null;
        }
        IEnumerator Capture(string name)
        {
            var until = Time.realtimeSinceStartupAsDouble + 10;
            while (ShaderUtil.anythingCompiling)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Shader compilation."); yield return null; }
            yield return null; yield return null;
            var before = ShaderUtil.anythingCompiling;
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null) throw new InvalidOperationException("Screen capture returned null.");
            try
            {
                var cyan = image.GetPixels32().Count(c => c.r < 16 && c.g > 230 && c.b > 230);
                File.WriteAllBytes(Path.Combine(BarracksCapture.Output, name + ".png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(BarracksCapture.Output, name + ".txt"), $"size={image.width}x{image.height} cyanPixels={cyan} anythingCompilingBefore={before} anythingCompilingAfter={ShaderUtil.anythingCompiling}\n" + string.Join("\n", app.Ui.GetComponentsInChildren<Text>().Where(t => t.GetComponentInParent<InputField>() == null).Select(t => t.text)));
                if (cyan != 0 || image.width != 390 || image.height != 844) throw new InvalidOperationException("Cyan or capture size gate failed.");
            }
            finally { Destroy(image); }
        }
    }
}
