using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Game.App;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    // Explicit local-development validation. Never reads the player's normal credential scope.
    public static class DeviceAuthRuntimeProbe
    {
        static readonly Stack<IEnumerator> Pending = new Stack<IEnumerator>();
        static AsyncOperation waiting;
        static string output, stage;
        static bool outputCreated;
        static double deadline;
#if UNITY_EDITOR_OSX
        [DllImport("SowDeviceIdentity")] static extern long SowIdentityLastStatus();
#endif
        public static void Run()
        {
            try
            {
                var endpoint = Argument("--device-auth-probe-endpoint");
                var id = Argument("--device-auth-probe-id");
                output = Path.GetFullPath(Argument("--device-auth-probe-output"));
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback
                    || !DeviceAuthHttp.ValidEndpoint(endpoint, true)) throw new ArgumentException();
                if (id.Length < 1 || id.Length > 64) throw new ArgumentException();
                foreach (var c in id) if (!char.IsLetterOrDigit(c) && c != '-') throw new ArgumentException();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (File.Exists(output)) throw new InvalidOperationException("Probe output already exists.");
                File.WriteAllText(output, "mode: actual-native-vault-and-local-server\nhead: " + Argument("--device-auth-probe-head") + "\n");
                outputCreated = true;
                Pending.Push(Verify(endpoint.TrimEnd('/'), id)); deadline = EditorApplication.timeSinceStartup + 180;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(false, error); }
        }
        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            throw new ArgumentException("Required probe argument missing.");
        }
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException();
                if (waiting != null && !waiting.isDone) return;
                waiting = null;
                if (Pending.Count == 0) { Finish(true, null); return; }
                var current = Pending.Peek();
                if (!current.MoveNext()) { Pending.Pop(); (current as IDisposable)?.Dispose(); return; }
                if (current.Current is IEnumerator nested) Pending.Push(nested);
                else if (current.Current is AsyncOperation operation) waiting = operation;
            }
            catch (Exception error) { Finish(false, error); }
        }
        public static IEnumerator DevelopmentFlow(string endpoint, string id, string reportPath)
        {
            output = Path.GetFullPath(reportPath);
            if (File.Exists(output)) throw new InvalidOperationException("Probe output already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, "mode: development-memory-vault-and-local-server\nprocess-persistence: NOT_SUPPORTED\n");
            yield return Verify(endpoint, id, true);
            File.AppendAllText(output, "result: PASS\n");
        }
        static IEnumerator Verify(string endpoint, string id, bool development = false)
        {
            var prefix = development ? "development-memory-" : "native-";
            stage = prefix + "empty-dedicated-scope";
            IDeviceIdentityVault vault = development ? new DevelopmentDeviceIdentityVault(endpoint, id)
                : new NativeDeviceIdentityVault(endpoint + "/runtime-probe/" + id);
            Require(string.IsNullOrEmpty(vault.Load())); Pass();
            stage = prefix + "key-public"; Require(vault.PublicKeySpki().Length > 64); Pass();
            stage = prefix + "fresh-signature";
            Require(vault.Sign(Encoding.UTF8.GetBytes("sow-runtime-probe-v1" )).Length == 64); Pass();
            stage = prefix + "save-reload";
            var empty = JsonUtility.ToJson(new DeviceAuthRecord()); vault.Save(empty); Require(vault.Load() == empty); Pass();
            var transport = new DeviceAuthHttp(endpoint, true);
            var client = new DeviceAuthClient(vault, transport);
            stage = "server-guest-create"; yield return client.CreateGuest(); Require(client.Connected); Pass();
            var account = client.AccountId;
            stage = "server-fresh-challenge-device-sign-in";
            var previousAccess = client.AccessToken;
            yield return client.SignIn(false);
            Require(client.Status == "connected" && client.Connected && client.AccountId == account && client.AccessToken != previousAccess); Pass();
            stage = "server-session-persist-reload";
            client = new DeviceAuthClient(vault, transport); Require(client.Connected && client.AccountId == account); Pass();
            stage = "server-delete"; yield return client.DeleteConfirmed();
            Require(client.DeletionPending && client.AccessToken == null && !string.IsNullOrEmpty(client.PurgeAt)); Pass();
            stage = "client-recreation-does-not-restore-deletion";
            client = new DeviceAuthClient(vault, transport); yield return client.SignIn(false);
            Require(client.DeletionPending && client.AccessToken == null); Pass();
            stage = "server-explicit-same-key-restore"; yield return client.SignIn(true);
            Require(client.Connected && client.Restored && !client.DeletionPending && client.AccountId == account); Pass();
            stage = "restored-session-persist-reload";
            client = new DeviceAuthClient(vault, transport);
            Require(client.Connected && client.Restored && client.AccountId == account); Pass();
        }
        static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
        static void Pass() => File.AppendAllText(output, stage + ": PASS\n");
        static void Finish(bool passed, Exception error)
        {
            EditorApplication.update -= Tick;
            long nativeStatus = 0;
#if UNITY_EDITOR_OSX
            try { nativeStatus = SowIdentityLastStatus(); } catch (Exception) { }
#endif
            // Exception messages and HTTP payloads may contain secrets; emit only type, stage and OS status.
            var result = passed ? "result: PASS\n" : "result: FAIL\nstage: " + stage + "\nerror-type: "
                + error?.GetType().Name + "\nnative-status: " + nativeStatus + "\n";
            if (outputCreated) File.AppendAllText(output, result);
            UnityEngine.Debug.Log("Device auth runtime probe " + (passed ? "PASS" : "FAIL") + "; inspect the probe status file.");
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
