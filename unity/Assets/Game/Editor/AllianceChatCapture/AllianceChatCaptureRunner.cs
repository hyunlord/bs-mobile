using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.AllianceChatCapture
{
    public sealed class AllianceChatCaptureRunner
    {
        RunCoordinator app;
        [Serializable] sealed class RestartCheckpoint { public int processId; public string commandSha256; }
        public IEnumerator Execute() => Guard(Run());
        IEnumerator Guard(IEnumerator steps)
        {
            while (true)
            {
                object next;
                try { if (!steps.MoveNext()) break; next = steps.Current; }
                catch (Exception error) { AllianceChatCapture.Finish(false, error.GetType().Name + ": " + error.Message); yield break; }
                if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }
        IEnumerator Run()
        {
            var message = Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_MESSAGE_ID");
            var author = Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_AUTHOR_ID");
            if (!Guid.TryParse(message, out _) || !Guid.TryParse(author, out _)) throw new InvalidOperationException("Real server message and author IDs required.");
            var until = Time.realtimeSinceStartupAsDouble + 45;
            while ((app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || Button("Title.Settings") == null)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Title not ready."); yield return null; }
            var phase = Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_RESTART_PHASE");
            if (!string.IsNullOrEmpty(phase) && phase != "A" && phase != "B") throw new InvalidOperationException("Restart phase must be A or B.");
            if (!string.IsNullOrEmpty(phase) && (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DROP_REPORT") != "1"
                || Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DEVICE_LOGIN") == "1"))
                throw new InvalidOperationException("Two-process restart requires prototype fault-injection mode.");
            if (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DROP_REPORT") == "1" || phase == "B")
                app.ConfigureAlliancePrototypeCapture(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_PENDING_ROOT"));
            if (phase == "B")
            {
                var checkpoint = JsonUtility.FromJson<RestartCheckpoint>(File.ReadAllText(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_CHECKPOINT")));
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                if (checkpoint == null || checkpoint.processId == pid || checkpoint.commandSha256 != PendingAccountCommand.Fingerprint(PersistedCommand()))
                    throw new InvalidOperationException("A different Editor process and unchanged persisted command are required.");
                yield return app.RestartAlliancePrototypeCapture(PrototypeBearer(), true);
                yield return Ready("Alliance.Refresh");
                File.WriteAllText(Path.Combine(AllianceChatCapture.Output, "restart-evidence.txt"), app.AlliancePrototypeRestartEvidence
                    + "phaseAProcess=" + checkpoint.processId + "\nphaseBProcess=" + pid + "\ncommandSha256=" + checkpoint.commandSha256 + "\n");
            }
            else
            {
                Click("Title.Settings"); yield return null;
                if (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DEVICE_LOGIN") == "1")
                {
                    Click("Settings.Account"); yield return null;
                    if (string.IsNullOrEmpty(app.AccountAccessToken))
                    {
                        var connect = app.Ui.GetComponentsInChildren<Button>().SingleOrDefault(b => b.interactable &&
                            b.GetComponentInChildren<Text>().text == "이 기기로 로그인");
                        if (connect == null) throw new InvalidOperationException("A previously provisioned device account is required.");
                        connect.onClick.Invoke();
                        var deadline = Time.realtimeSinceStartupAsDouble + 30;
                        while (string.IsNullOrEmpty(app.AccountAccessToken))
                        { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Device authentication failed."); yield return null; }
                    }
                    if (app.AuthenticatedAccountId != Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_ACCOUNT_ID"))
                        throw new InvalidOperationException("Authenticated account differs from the seeded alliance fixture.");
                    app.Ui.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "돌아가기").onClick.Invoke();
                    yield return null;
                    // Enter Alliance directly: no Barracks visit may prehydrate the domain transport.
                }
                else
                {
                    Click("Settings.Barracks"); yield return null;
                    var field = app.Ui.GetComponentInChildren<InputField>();
                    if (field == null) throw new InvalidOperationException("Server connection field missing.");
                    field.text = File.ReadAllText(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_TOKEN_FILE")).Trim();
                    Click("Barracks.Connect"); yield return Ready("Barracks.Refresh");
                }
                app.NavigatePortraitMeta(PortraitMetaPage.Alliance);
                yield return Ready("Alliance.Refresh");
                if (Button("Alliance.Menu:" + message) == null) throw new InvalidOperationException("Seeded server message missing.");
                yield return Capture("01-live-chat");
                Click("Alliance.Menu:" + message); yield return Capture("02-message-menu");
                Click("Alliance.Report"); yield return Capture("03-report-reasons");
                Click("Alliance.Report:other"); yield return Ready("Alliance.Refresh");
                if (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DROP_REPORT") == "1")
                {
                    if (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DEVICE_LOGIN") == "1")
                        throw new InvalidOperationException("Prototype fault-injection mode cannot substitute native authentication.");
                    if (Button("Alliance.Refresh")?.GetComponentInChildren<Text>().text != "같은 요청 확인")
                        throw new InvalidOperationException("Dropped response did not retain an uncertain journal.");
                    yield return Capture("03b-fault-injected-unresolved");
                    if (phase == "A")
                    {
                        var checkpoint = new RestartCheckpoint { processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                            commandSha256 = PendingAccountCommand.Fingerprint(PersistedCommand()) };
                        using (var output = new StreamWriter(new FileStream(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_CHECKPOINT"), FileMode.CreateNew)))
                            output.Write(JsonUtility.ToJson(checkpoint));
                        AllianceChatCapture.Finish(true, "Phase A only: dropped real server receipt, durable unresolved journal captured; orderly Editor exit. Phase B remains required.");
                        yield break;
                    }
                    yield return app.RestartAlliancePrototypeCapture(File.ReadAllText(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_TOKEN_FILE")).Trim());
                    yield return Ready("Alliance.Refresh");
                    File.WriteAllText(Path.Combine(AllianceChatCapture.Output, "restart-evidence.txt"), app.AlliancePrototypeRestartEvidence);
                }
            }
            if (!app.Ui.GetComponentsInChildren<Text>().Any(t => t.text.StartsWith("신고 접수 · ", StringComparison.Ordinal)))
                throw new InvalidOperationException("Server report receipt missing.");
            yield return Capture("04-report-receipt");
            Click("Alliance.Menu:" + message); Click("Alliance.Block"); yield return Ready("Alliance.Refresh");
            if (Button("Alliance.Menu:" + message) != null || Button("Alliance.Unblock:" + author) == null)
                throw new InvalidOperationException("Server block filtering not reflected.");
            yield return Capture("05-blocked");
            Click("Alliance.Unblock:" + author); yield return Ready("Alliance.Refresh");
            if (Button("Alliance.Menu:" + message) == null) throw new InvalidOperationException("Unblocked server message missing.");
            yield return Capture("06-unblocked");
            foreach (var enlarged in new[] { false, true })
            {
                app.Ui.Clear();
                var label = PortraitWidgets.Place("AssetQA.Label", app.Ui.Content, 16, 12, 358, 60).gameObject.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 16;
                label.text = "Asset QA only · 32 px borders\n" + (enlarged ? "resized" : "source at 2x density");
                Paper("target-card", 16, 76, enlarged ? 300 : 350, enlarged ? 260 : 216);
                Paper("march-formation-panel", 16, enlarged ? 350 : 304, enlarged ? 300 : 350, enlarged ? 390 : 510);
                yield return Capture(enlarged ? "08-paper-resized" : "07-paper-source-density");
            }
            AllianceChatCapture.Finish(true, "Live server report receipt, block/unblock UI and two paper border captures; no world stock/march flow claimed.");
        }
        static string PrototypeBearer() => File.ReadAllText(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_TOKEN_FILE")).Trim();
        static string PersistedCommand()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "--server-url");
            if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("Explicit prototype proxy endpoint required.");
            var journal = new PendingAccountCommand(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_PENDING_ROOT"),
                args[index + 1].TrimEnd('/') + "\nprototype-" + PendingAccountCommand.Fingerprint(PrototypeBearer()));
            return journal.Read() ?? throw new InvalidOperationException("Pending command was not persisted.");
        }
        void Paper(string role, float x, float y, float width, float height)
        {
            var image = PortraitWidgets.Place("AssetQA." + role, app.Ui.Content, x, y, width, height).gameObject.AddComponent<Image>();
            ChatMarchArt.Surface(image, role);
        }
        Button Button(string name) => app?.Ui?.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);
        void Click(string name)
        {
            var button = Button(name);
            if (button == null || !button.interactable) throw new InvalidOperationException("Unavailable action: " + name);
            button.onClick.Invoke();
        }
        IEnumerator Ready(string name)
        {
            var until = Time.realtimeSinceStartupAsDouble + 35;
            while (Button(name)?.interactable != true)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Server response not ready: " + name); yield return null; }
            yield return null;
        }
        IEnumerator Capture(string name)
        {
            var until = Time.realtimeSinceStartupAsDouble + 10;
            while (ShaderUtil.anythingCompiling)
            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Shader compilation."); yield return null; }
            yield return null; yield return null;
            var settle = Time.realtimeSinceStartupAsDouble + .2;
            while (Time.realtimeSinceStartupAsDouble < settle) yield return null;
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null) throw new InvalidOperationException("Screen capture returned null.");
            try
            {
                var cyan = image.GetPixels32().Count(c => c.r < 16 && c.g > 230 && c.b > 230);
                File.WriteAllBytes(Path.Combine(AllianceChatCapture.Output, name + ".png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(AllianceChatCapture.Output, name + ".txt"), $"size={image.width}x{image.height} cyanPixels={cyan} anythingCompiling={ShaderUtil.anythingCompiling}\n" + string.Join("\n", app.Ui.GetComponentsInChildren<Text>().Where(t => t.GetComponentInParent<InputField>() == null).Select(t => t.text)));
                if (cyan != 0 || image.width != 390 || image.height != 844) throw new InvalidOperationException("Cyan or capture size gate failed.");
            }
            finally { UnityEngine.Object.Destroy(image); }
        }
    }
}
