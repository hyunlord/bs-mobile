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

namespace Game.AccountCapture
{
    public sealed class AccountCaptureRunner
    {
        RunCoordinator app;
        public IEnumerator Execute() => Guard(Run());
        IEnumerator Guard(IEnumerator steps)
        {
            while (true)
            {
                object next;
                try { if (!steps.MoveNext()) break; next = steps.Current; }
                catch (Exception error) { AccountCapture.Finish(false, error.GetType().Name + ": " + error.Message); yield break; }
                if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }
        IEnumerator Run()
        {
            var until = Time.realtimeSinceStartupAsDouble + 45;
            while ((app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || Button("Title.Settings") == null)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Title.Settings not ready."); yield return null; }
            Click("Title.Settings"); yield return null;
            if (Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_DEVICE_LOGIN") == "1")
            {
                Click("Settings.Account"); yield return null;
                if (string.IsNullOrEmpty(app.AccountAccessToken))
                {
                    var connect = app.Ui.GetComponentsInChildren<Button>().SingleOrDefault(b => b.interactable &&
                        (b.GetComponentInChildren<Text>().text == "게스트로 연결" || b.GetComponentInChildren<Text>().text == "이 기기로 로그인"));
                    if (connect == null) throw new InvalidOperationException("Device account connect action unavailable.");
                    connect.onClick.Invoke();
                    var deadline=Time.realtimeSinceStartupAsDouble+30;
                    while(string.IsNullOrEmpty(app.AccountAccessToken))
                    {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Device account authentication failed.");yield return null;}
                }
                yield return Capture("00-device-account-connected");
                app.Ui.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text=="돌아가기").onClick.Invoke();
                yield return null;Click("Settings.Barracks");yield return Ready();
            }
            else
            {
                Click("Settings.Barracks"); yield return null;
                var field = app.Ui.GetComponentInChildren<InputField>();
                if (field == null) throw new InvalidOperationException("Explicit server connection field missing.");
                field.text = File.ReadAllText(Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_TOKEN_FILE")).Trim();
                Click("Barracks.Connect"); yield return Ready();
            }
            Click("Barracks.Recruit"); yield return null; yield return Capture("01-recruit-roster");
            Click("Recruit.Submit"); yield return null; yield return Capture("02-recruit-odds");
            Scroll(0); yield return Capture("02-recruit-confirm");
            Click("Recruit.Submit"); yield return Ready("Recruit.Refresh");
            if (!app.Ui.GetComponentsInChildren<Text>().Any(t => t.text == "모집 결과가 확정되었습니다"))
                throw new InvalidOperationException("Recruit command did not return a confirmed result.");
            Scroll(0); yield return Capture("03-recruit-result");
            Click("Recruit.Back"); yield return null; Click("Barracks.Research"); yield return null;
            yield return Capture("04-research-tree");
            var selected = Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_RESEARCH_NODE");
            if (!string.IsNullOrEmpty(selected))
            {
                Click("Research.Node:" + selected); yield return null; Scroll(0); yield return Capture("05-research-detail");
                if (Button("Research.Start")?.interactable == true)
                {
                    Click("Research.Start"); yield return null; Scroll(0); yield return Capture("06-research-confirm");
                    Click("Research.Start"); yield return Ready("Research.Refresh"); Scroll(0); yield return Capture("07-research-response");
                    if (Button("Research.Complete")?.interactable != true) throw new InvalidOperationException("Research start did not create an active server job.");
                    Click("Research.Complete"); yield return Ready("Research.Refresh"); Scroll(0); yield return Capture("08-research-completion-check");
                    var finishDeadline=Time.realtimeSinceStartupAsDouble+150;
                    while (!Button("Research.Node:"+selected).GetComponentsInChildren<Text>().Any(t=>t.text.Contains(" · 완료")))
                    {
                        if(Time.realtimeSinceStartupAsDouble>finishDeadline)throw new TimeoutException("Research did not complete through real server time.");
                        yield return null;
                    }
                    Scroll(0); yield return Capture("09-research-complete");
                }
            }
            AccountCapture.Finish(true, "Live recruit command confirmed; research view captured. Research start requires a legitimately unlocked node; inspect screenshots/status for its separate result.");
        }
        void Scroll(float position)
        {
            Canvas.ForceUpdateCanvases();
            var scroll = app.Ui.GetComponentInChildren<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = position;
        }

        Button Button(string name) => app?.Ui?.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);
        void Click(string name)
        {
            var button = Button(name);
            if (button == null || !button.interactable) throw new InvalidOperationException("Unavailable action: " + name);
            button.onClick.Invoke();
        }
        IEnumerator Ready(string buttonName = "Barracks.Refresh")
        {
            var until = Time.realtimeSinceStartupAsDouble + 20;
            while (Button(buttonName)?.interactable != true)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Server response not ready."); yield return null; }
            yield return null;
        }
        IEnumerator Capture(string name)
        {
            var until = Time.realtimeSinceStartupAsDouble + 10;
            while (ShaderUtil.anythingCompiling)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Shader compilation."); yield return null; }
            yield return null; yield return null;
            // Selectable colour tints fade over ColorBlock.fadeDuration (0.1 s) in real time; batchmode frames can be far
            // shorter, so wait past the fade or a just-rebuilt disabled button is captured at its normal colour.
            var settle = Time.realtimeSinceStartupAsDouble + 0.2;
            while (Time.realtimeSinceStartupAsDouble < settle) yield return null;
            foreach (var id in name.StartsWith("00-",StringComparison.Ordinal) ? Array.Empty<string>() : new[] { "meta:grain", "meta:timber", "meta:iron", "slg:gem" })
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
                File.WriteAllBytes(Path.Combine(AccountCapture.Output, name + ".png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(AccountCapture.Output, name + ".txt"), $"size={image.width}x{image.height} cyanPixels={cyan} anythingCompilingBefore={before} anythingCompilingAfter={ShaderUtil.anythingCompiling}\n" + string.Join("\n", app.Ui.GetComponentsInChildren<Text>().Where(t => t.GetComponentInParent<InputField>() == null).Select(t => t.text)));
                if (cyan != 0 || image.width != 390 || image.height != 844) throw new InvalidOperationException("Cyan or capture size gate failed.");
            }
            finally { UnityEngine.Object.Destroy(image); }
        }
    }
}
