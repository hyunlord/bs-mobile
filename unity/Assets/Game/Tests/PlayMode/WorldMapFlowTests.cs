#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class WorldMapFlowTests
    {
        sealed class MemoryVault : IDeviceIdentityVault
        {
            public string Json;
            public string Load() => Json;
            public void Save(string record) => throw new NotSupportedException();
            public byte[] PublicKeySpki() => throw new NotSupportedException();
            public byte[] Sign(byte[] message) => throw new NotSupportedException();
        }
        sealed class NoAuthTransport : IDeviceAuthTransport
        {
            public IEnumerator Post(string path, string body, string bearer, Action<DeviceAuthResponse> receive) => throw new NotSupportedException();
        }
        [UnityTest, Timeout(120000)]
        public IEnumerator AuthenticatedAccountOpensWorldWithoutVisitingBarracks()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Portrait flow requires the wave-1a export.");
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty);
            const string account = "67e4d559-9394-4780-a9fe-02e6ba7b0811";
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            var endpoint = "http://127.0.0.1:" + port;
            var listener = new HttpListener(); listener.Prefixes.Add(endpoint + "/"); listener.Start();
            var requests = new ConcurrentQueue<string>();
            var city = "{\"targetId\":\"city:test\",\"generation\":1,\"kind\":\"city\",\"contentId\":\"slg:city\",\"x\":0,\"y\":0,\"accountId\":\"" + account + "\",\"allianceName\":null}";
            var chunk = "{\"worldId\":\"slg:test\",\"contentHash\":\"" + new string('a',64) + "\",\"chunkX\":0,\"chunkY\":0,\"tiles\":[{\"x\":0,\"y\":0,\"terrain\":\"plain\",\"passable\":true}],\"targets\":[" + city + "]}";
            var settledVersion = 1;
            var server = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); } catch (HttpListenerException) { break; } catch (ObjectDisposedException) { break; }
                    var path = context.Request.Url.AbsolutePath; requests.Enqueue(path);
                    if (path == "/v1/world/marches") settledVersion++;
                    var accountBody = "{\"done\":true,\"reason\":\"\",\"estate\":{\"version\":" + settledVersion + "},\"resources\":[{\"id\":\"meta:grain\",\"amount\":" + settledVersion + "}],\"domains\":{\"training\":{\"units\":[{\"unitId\":\"test:troop\",\"name\":\"검사 병력\",\"ready\":" + settledVersion + "}]}}}";
                    var returnedMarch = "[{\"marchId\":\"62e4d559-9394-4780-a9fe-02e6ba7b0811\",\"targetId\":\"resource:test\",\"generation\":1,\"operation\":\"gather\",\"heroId\":\"test:hero\",\"troops\":[{\"unitId\":\"test:troop\",\"count\":1}],\"path\":[{\"x\":0,\"y\":0}],\"startedAtMs\":1,\"dueAtMs\":2,\"state\":\"returned\",\"reason\":\"returned\"}]";
                    var body = path == "/v1/account" ? accountBody : path == "/v1/world/marches" ? returnedMarch
                        : path == "/v1/world/city" ? city : path == "/v1/world/chunks/0/0" ? chunk : "{}";
                    var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
                }
            });
            var oldEndpoint = Environment.GetEnvironmentVariable("SOW_ACCOUNT_SERVER");
            var oldDirectory = RunCoordinator.MetaDirectoryOverride; var oldPrefix = RunCoordinator.PreferencePrefixOverride;
            var id = Guid.NewGuid().ToString("N"); var prefix = "sowsiege.world-flow." + id + ".";
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/world-client/test-profiles", id));
            var width = Screen.width; var height = Screen.height;
            try
            {
                Environment.SetEnvironmentVariable("SOW_ACCOUNT_SERVER", endpoint);
                RunCoordinator.MetaDirectoryOverride = directory; RunCoordinator.PreferencePrefixOverride = prefix;
                PlayModeWindow.SetCustomRenderingResolution(390, 844, "World auth regression");
                yield return SceneManager.LoadSceneAsync("Boot");
                RunCoordinator app = null; var deadline = Time.realtimeSinceStartupAsDouble + 45;
                while ((app = Object.FindFirstObjectByType<RunCoordinator>()) == null || app.Ui == null || app.CurrentScreenName != "Title")
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                var record = new DeviceAuthRecord { accountId = account, keyId = Guid.NewGuid().ToString("D"), accessToken = new string('a',64), accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O") };
                app.SetAccountCaptureFixture(new DeviceAuthClient(new MemoryVault { Json = JsonUtility.ToJson(record) }, new NoAuthTransport()));
                app.NavigatePortraitMeta(PortraitMetaPage.WorldMap); yield return null;
                var connect = app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "World.Connect");
                connect.onClick.Invoke(); deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (!app.Ui.GetComponentsInChildren<Button>().Any(b => b.name == "World.Tile.0.0"))
                { Assert.That(app.CurrentScreenName, Is.EqualTo("WorldMap"), "Fresh transport must not redirect an authenticated account to settings."); Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                var transport = app.WorldCaptureTransport;
                while (transport.Busy || transport.Latest.estate.version < 2)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                var refresh = app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "March.Refresh");
                refresh.onClick.Invoke(); yield return null;
                while (transport.Busy || transport.Latest.estate.version < 3)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                Assert.That(transport.WorldMarches.Single().State, Is.EqualTo("returned"));
                Assert.That(transport.Latest.estate.version, Is.EqualTo(3), "Account projection must follow march settlement.");
                Assert.That(transport.Latest.resources.Single().amount, Is.EqualTo(3));
                Assert.That(transport.Latest.Training.units.Single().ready, Is.EqualTo(3));
                Assert.That(requests.ToArray(), Does.Contain("/v1/account"));
                Assert.That(requests.ToArray(), Does.Contain("/v1/world/city"));
                Assert.That(requests.ToArray(), Does.Contain("/v1/world/chunks/0/0"));
                Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t => t.text.Contains("정찰·전투는 아직 연결되지")), Is.True);
                Canvas.ForceUpdateCanvases();
                foreach (var name in new[] { "Tab.Estate", "Tab.WorldMap", "Tab.Alliance", "Tab.Shop", "Estate.Depart" })
                {
                    var button = app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == name);
                    Assert.That(button.interactable, Is.True, "Connected world must retain C navigation: " + name);
                    Assert.That(((RectTransform)button.transform).rect.height, Is.EqualTo(48).Within(.1f));
                }
                Assert.That(app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "Tab.WorldMap").GetComponent<Image>().sprite, Is.Not.Null, "Selected world tab must use the shared primary-button art.");
                app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "Tab.Shop").onClick.Invoke(); yield return null;
                Assert.That(app.CurrentScreenName, Is.EqualTo("Shop"));
                app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "Tab.WorldMap").onClick.Invoke(); yield return null;
                Assert.That(app.CurrentScreenName, Is.EqualTo("WorldMap"));
                app.Ui.GetComponentsInChildren<Button>().Single(b => b.name == "Estate.Depart").onClick.Invoke(); yield return null;
                Assert.That(app.CurrentScreenName, Is.EqualTo("Departure"));
            }
            finally
            {
                listener.Stop(); listener.Close();
                foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
                Environment.SetEnvironmentVariable("SOW_ACCOUNT_SERVER", oldEndpoint);
                RunCoordinator.MetaDirectoryOverride = oldDirectory; RunCoordinator.PreferencePrefixOverride = oldPrefix;
                foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(prefix + key);
                PlayerPrefs.Save(); PlayModeWindow.SetCustomRenderingResolution((uint)width,(uint)height,"Restored world test viewport");
                // Keep the isolated test directory as evidence until its owner creates a cleanup manifest.
            }
            yield return null;
            Assert.That(server.IsFaulted, Is.False, "Loopback fixture server failed.");
        }
    }
}
#endif
