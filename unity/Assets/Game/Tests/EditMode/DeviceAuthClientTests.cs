using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class DeviceAuthClientTests
    {
        const string Account = "11111111-1111-1111-1111-111111111111", Key = "22222222-2222-2222-2222-222222222222";
        sealed class Vault : IDeviceIdentityVault
        {
            public string Json = "";
            public byte[] Signed;
            public bool FailSave;
            public string Load() => Json;
            public void Save(string value) { if (FailSave) throw new InvalidOperationException(); Json = value; }
            public byte[] PublicKeySpki() => new byte[] { 1 };
            public byte[] Sign(byte[] message) { Signed = message; return new byte[64]; }
        }
        sealed class Transport : IDeviceAuthTransport
        {
            public readonly Queue<DeviceAuthResponse> Replies = new Queue<DeviceAuthResponse>();
            public readonly List<string> Paths = new List<string>();
            public string Bearer;
            public IEnumerator Post(string path, string json, string bearer, Action<DeviceAuthResponse> receive)
            { Paths.Add(path); Bearer = bearer; receive(Replies.Dequeue()); yield break; }
        }
        static DeviceAuthRecord SignedIn() => new DeviceAuthRecord { accountId = Account, keyId = Key,
            accessToken = new string('a', 64), refreshToken = new string('b', 64),
            accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"), refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(10).ToString("O") };
        static void Run(IEnumerator routine)
        { while (routine.MoveNext()) if (routine.Current is IEnumerator nested) Run(nested); }
        static DeviceAuthResponse Reply(string json, long code = 200) => new DeviceAuthResponse { Code = code, Json = json };

        [Test] public void SignedMessageUsesCanonicalGuidAndExactNewlines()
        {
            Assert.That(Encoding.UTF8.GetString(DeviceSignature.Message(Key.ToUpperInvariant(), "NONCE")), Is.EqualTo("sow-auth-v1\n" + Key + "\nNONCE"));
            Assert.Throws<FormatException>(() => DeviceSignature.Message("bad", "nonce"));
        }
        [Test] public void DerSignaturePadsEachIntegerAndRejectsNegativeOrTrailingEncoding()
        {
            var result = DeviceSignature.P1363(new byte[] { 48, 6, 2, 1, 1, 2, 1, 127 });
            Assert.That(result.Length, Is.EqualTo(64)); Assert.That(result[31], Is.EqualTo(1)); Assert.That(result[63], Is.EqualTo(127));
            Assert.Throws<FormatException>(() => DeviceSignature.P1363(new byte[] { 48, 6, 2, 1, 128, 2, 1, 1 }));
            Assert.Throws<FormatException>(() => DeviceSignature.P1363(new byte[] { 48, 7, 2, 1, 1, 2, 1, 1, 0 }));
            Assert.Throws<FormatException>(() => DeviceSignature.P1363(new byte[] { 48, 7, 2, 2, 0, 1, 2, 1, 1 }));
        }
        [Test] public void ProductionEndpointRequiresHttpsAndRejectsEmbeddedCredentialsOrPaths()
        {
            Assert.That(DeviceAuthHttp.ValidEndpoint("http://127.0.0.1:5082", true), Is.True);
            Assert.That(DeviceAuthHttp.ValidEndpoint("http://127.0.0.1:5082", false), Is.False);
            Assert.That(DeviceAuthHttp.ValidEndpoint("https://example.invalid", false), Is.True);
            Assert.That(DeviceAuthHttp.ValidEndpoint("https://secret@example.invalid", false), Is.False);
            Assert.That(DeviceAuthHttp.ValidEndpoint("https://example.invalid/v1", false), Is.False);
        }
        [Test] public void LostCreationResponseIsPersistedAndCannotCreateDuplicateOnRetryOrRestart()
        {
            var vault = new Vault(); var transport = new Transport(); transport.Replies.Enqueue(Reply("", 0));
            var client = new DeviceAuthClient(vault, transport); Run(client.CreateGuest()); Run(client.CreateGuest());
            var restarted = new DeviceAuthClient(vault, transport); Run(restarted.CreateGuest());
            Assert.That(transport.Paths.Count, Is.EqualTo(1)); Assert.That(restarted.CreationPending, Is.True);
            Assert.That(restarted.Status, Is.EqualTo("creation-unknown"));
        }
        [Test] public void StorageFailurePreventsSendingCreation()
        {
            var vault = new Vault { FailSave = true }; var transport = new Transport();
            var client = new DeviceAuthClient(vault, transport); Run(client.CreateGuest());
            Assert.That(transport.Paths, Is.Empty); Assert.That(client.Busy, Is.False);
        }
        [Test] public void DeletedSessionSurvivesRestartWithoutTokensOrAutomaticRestoration()
        {
            var vault = new Vault { Json = JsonUtility.ToJson(SignedIn()) }; var transport = new Transport();
            var purge = DateTimeOffset.UtcNow.AddDays(30).ToString("O"); transport.Replies.Enqueue(Reply("{\"purgeAt\":\"" + purge + "\"}"));
            var client = new DeviceAuthClient(vault, transport); Run(client.DeleteConfirmed());
            Assert.That(transport.Bearer, Is.EqualTo(new string('a', 64))); Assert.That(client.AccessToken, Is.Null);
            var restarted = new DeviceAuthClient(vault, transport); Run(restarted.SignIn(false)); Run(restarted.CreateGuest());
            Assert.That(transport.Paths.Count, Is.EqualTo(1)); Assert.That(restarted.PurgeAt, Is.EqualTo(purge));
            Assert.That(restarted.DeletionPending, Is.True); Assert.That(vault.Json, Does.Not.Contain(new string('b', 64)));
        }
        [Test] public void LostDeletionResponseStillPreventsAutomaticSignIn()
        {
            var vault = new Vault { Json = JsonUtility.ToJson(SignedIn()) }; var transport = new Transport(); transport.Replies.Enqueue(Reply("", 0));
            var client = new DeviceAuthClient(vault, transport); Run(client.DeleteConfirmed());
            var restarted = new DeviceAuthClient(vault, transport); Run(restarted.SignIn(false));
            Assert.That(restarted.DeletionPending, Is.True); Assert.That(restarted.AccessToken, Is.Null); Assert.That(transport.Paths.Count, Is.EqualTo(1));
        }
        [Test] public void ExplicitRestoreSignsServerNonceAndPreservesRestoredIndicator()
        {
            var record = SignedIn(); record.deletionPending = true; record.accessToken = record.refreshToken = null;
            var vault = new Vault { Json = JsonUtility.ToJson(record) }; var transport = new Transport();
            transport.Replies.Enqueue(Reply("{\"keyId\":\"" + Key + "\",\"nonce\":\"ABC\",\"expiresAt\":\"" + DateTimeOffset.UtcNow.AddMinutes(1).ToString("O") + "\"}"));
            var restored = SignedIn(); restored.restored = true; transport.Replies.Enqueue(Reply(JsonUtility.ToJson(restored)));
            var client = new DeviceAuthClient(vault, transport); Run(client.SignIn(true));
            Assert.That(Encoding.UTF8.GetString(vault.Signed), Is.EqualTo("sow-auth-v1\n" + Key + "\nABC"));
            Assert.That(client.Restored, Is.True); Assert.That(client.DeletionPending, Is.False); Assert.That(client.Connected, Is.True);
        }
    }
}
