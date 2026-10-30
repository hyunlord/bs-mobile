using System;
using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BarracksServerClientTests
    {
        [TestCase("http://127.0.0.1:5080", true)]
        [TestCase("http://localhost:5080/", true)]
        [TestCase("https://127.0.0.1:5080", true)]
        [TestCase("http://example.com", false)]
        [TestCase("http://localhost:5080/?token=secret", false)]
        [TestCase("http://user:secret@localhost:5080", false)]
        [TestCase("file:///tmp/server", false)]
        public void PrototypeEndpointIsExplicitLoopbackWithoutCredentials(string url, bool expected)
        { Assert.That(BarracksServerClient.ValidEndpoint(url), Is.EqualTo(expected)); }

        [Test]
        public void PublicAccountResourcesAndTrainingArraysDecodeWithoutDictionaryOrCoreState()
        {
            const string json = "{\"done\":true,\"reason\":\"accepted\",\"estate\":{\"version\":7},\"resources\":[{\"id\":\"meta:grain\",\"amount\":100}],\"domains\":{\"training\":{\"barracksLevel\":1,\"capacity\":60,\"occupied\":10,\"batchLimit\":10,\"serverUtcMilliseconds\":2000,\"units\":[{\"unitId\":\"slg:levy\",\"name\":\"징집 창병\",\"unlocked\":true,\"ready\":10,\"costPerUnit\":[{\"materialId\":\"meta:grain\",\"amount\":8}]}],\"active\":null,\"waiting\":null}}}";
            var reply = JsonUtility.FromJson<BarracksReply>(json);
            Assert.That(reply.estate.version, Is.EqualTo(7));
            Assert.That(reply.resources[0].amount, Is.EqualTo(100));
            Assert.That(reply.Training.units[0].costPerUnit[0].materialId, Is.EqualTo("meta:grain"));
            Assert.That(reply.Training.HasActive, Is.False);
            Assert.That(reply.Training.HasWaiting, Is.False);
        }

        [Test]
        public void TrainingEnvelopeKeepsIdentityAndEscapesPayload()
        {
            var id = Guid.Parse("c3d4fcb6-3b8e-41b7-8f43-482dd12fcfad");
            var first = BarracksServerClient.StartJson(id, 17, "slg:levy", 10);
            Assert.That(first, Is.EqualTo(BarracksServerClient.StartJson(id, 17, "slg:levy", 10)));
            StringAssert.Contains("\"expectedVersion\":17", first);
            StringAssert.Contains("\"domain\":\"training\"", first);
            StringAssert.Contains("\"operation\":\"start\"", first);
            StringAssert.Contains("\"count\":10", first);
            var escaped = BarracksServerClient.StartJson(id, 17, "bad\"unit", 1);
            StringAssert.Contains("bad\\\"unit", escaped);
            StringAssert.DoesNotContain("token", first);
        }

        [Test]
        public void PrototypeTokenRequiresExactOpaqueHexLength()
        {
            Assert.That(BarracksServerClient.ValidToken(new string('a', 64)), Is.True);
            Assert.That(BarracksServerClient.ValidToken(new string('a', 63)), Is.False);
            Assert.That(BarracksServerClient.ValidToken(new string('z', 64)), Is.False);
        }
    }
}
