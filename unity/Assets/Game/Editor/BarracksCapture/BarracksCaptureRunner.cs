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
    public sealed class BarracksCaptureRunner
    {
        RunCoordinator app;
        public IEnumerator Execute() => Guard(Run());
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
            while ((app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || Button("Title.Settings") == null)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Title.Settings not ready."); yield return null; }
            Click("Title.Settings"); yield return null; Click("Settings.Barracks"); yield return null;
            var field = app.Ui.GetComponentInChildren<InputField>();
            if (field == null) throw new InvalidOperationException("Explicit server connection field missing.");
            field.text = File.ReadAllText(Environment.GetEnvironmentVariable("BARRACKS_CAPTURE_TOKEN_FILE")).Trim();
            Click("Barracks.Connect"); yield return Ready();
            if (Button("Barracks.CancelWaiting") != null) throw new InvalidOperationException("Empty queue must not show a cancellation button.");
            yield return Capture("01-connected");
            Click("Barracks.Train"); yield return null; yield return Capture("02-confirm"); Click("Barracks.Train");
            yield return Ready(); yield return Capture("03-active");
            Click("Barracks.Train"); yield return null; Click("Barracks.Train"); yield return Ready(); yield return Capture("04-waiting");
            if (Button("Barracks.Train")?.interactable != false || !app.Ui.GetComponentsInChildren<Text>().Any(t => t.name == "Barracks.Status" && t.text.Contains("대기열이 가득")))
                throw new InvalidOperationException("Full queue must disable training and explain why.");
            if (Button("Barracks.CancelWaiting")?.interactable != true) throw new InvalidOperationException("Server waiting queue was not created.");
            Click("Barracks.CancelWaiting"); yield return null; yield return Capture("05-cancel-confirm");
            Click("Barracks.ConfirmCancel"); yield return Ready(); yield return Capture("06-cancel-result");
            if (Button("Barracks.CancelWaiting") != null) throw new InvalidOperationException("Server waiting cancellation was not reflected.");
            BarracksCapture.Finish(true, "Live server connected, start, waiting queue and cancellation submitted through real UI.");
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
            var status = app.Ui.GetComponentsInChildren<Text>().Single(t => t.name == "Barracks.Status");
            if (status.GetComponentInParent<ScrollRect>() != null) throw new InvalidOperationException("Server reason is hidden in scrolling content.");
            var queue = app.Ui.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Barracks.Queue");
            var viewport = (RectTransform)queue.parent;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, queue);
            if (bounds.min.y < viewport.rect.yMin - 1 || bounds.max.y > viewport.rect.yMax + 1)
                throw new InvalidOperationException("Training queue is clipped by its viewport.");
            foreach (var id in new[] { "meta:grain", "meta:timber", "meta:iron", "slg:gem" })
            {
                var wallet = app.Ui.GetComponentsInChildren<Text>().Single(t => t.name == "Barracks.Wallet:" + id);
                if (string.IsNullOrWhiteSpace(wallet.text)) throw new InvalidOperationException("Missing server wallet label: " + id);
            }
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
            finally { UnityEngine.Object.Destroy(image); }
        }
    }
}
