using System;
using Game.App;
using Game.View;
using UnityEngine.UI;
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
        public void WaitingJobWithNullableServerTimesIsStillWaiting()
        {
            var job = JsonUtility.FromJson<BarracksJob>("{\"jobId\":\"pending\",\"unitId\":\"slg:levy\",\"count\":2,\"startedAtMilliseconds\":null,\"completesAtMilliseconds\":null,\"bonusPermille\":null}");
            Assert.That(job.Exists, Is.True);
            Assert.That(job.completesAtMilliseconds, Is.Zero);
        }

        [Test]
        public void UnconnectedClientCannotInventAccountStateOrQueueSpending()
        {
            var client = new BarracksServerClient("http://localhost:5080");
            Assert.That(client.Start("slg:levy", 1).MoveNext(), Is.False);
            Assert.That(client.CancelWaiting().MoveNext(), Is.False);
            Assert.That(client.Latest, Is.Null);
            Assert.That(client.Uncertain, Is.False);
            Assert.That(client.Connected, Is.False);
        }

        [Test]
        public void DevelopmentConnectUsesPasswordAndMinimum48PixelActions()
        {
            var owner = new GameObject("Barracks connection test", typeof(RectTransform));
            var font = FontProvider.Create(new[] { PortraitMetaPanels.BarracksGlyphs });
            try
            {
                var ui = owner.AddComponent<UiShell>(); ui.Initialize(font);
                ui.SafeRoot.sizeDelta = new Vector2(390, 844);
                PortraitMetaPanels.BarracksConnect(ui, "http://localhost:5080", "not-connected", false, (_, __) => { }, () => { });
                foreach (var button in owner.GetComponentsInChildren<Button>())
                {
                    Assert.That(button.GetComponent<RectTransform>().sizeDelta.y, Is.GreaterThanOrEqualTo(48), button.name);
                }
                var input = owner.GetComponentInChildren<InputField>();
                Assert.That(input.contentType, Is.EqualTo(InputField.ContentType.Password));
                Assert.That(input.text, Is.Empty);
                Assert.That(PortraitMetaPanels.BarracksStatus("protected-reserve"), Does.Contain("기초 생산시설"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(font); }
        }

        [Test]
        public void RefundBalanceAndCancellationPreviewUseAuthoritativeServerAmounts()
        {
            var view = JsonUtility.FromJson<BarracksView>("{\"pendingRefunds\":[{\"materialId\":\"meta:grain\",\"amount\":7}],\"waitingCancellationRefunds\":[{\"materialId\":\"meta:grain\",\"immediateAmount\":3,\"pendingAmount\":5}]}");
            Assert.That(PortraitMetaPanels.BarracksPendingRefundText(view), Is.EqualTo("환불 대기 곡물 7"));
            var preview = PortraitMetaPanels.BarracksRefundPreviewText(view);
            StringAssert.Contains("즉시 3 / 환불 대기 5", preview);
            StringAssert.Contains("공간이 생기면 돌려받습니다", preview);
            Assert.That(view.pendingRefunds[0].amount, Is.EqualTo(7), "Rendering does not settle or spend the server balance.");
            view.waitingCancellationRefunds[0].pendingAmount = 0;
            StringAssert.DoesNotContain("지갑이 가득", PortraitMetaPanels.BarracksRefundPreviewText(view));
        }

        [Test]
        public void SelectedQuotePreservesServerCostTimeAndOccupancyWithoutClientArithmetic()
        {
            var unit = JsonUtility.FromJson<BarracksUnit>("{\"unitId\":\"slg:levy\",\"quotes\":[{\"count\":3,\"totalCost\":[{\"materialId\":\"meta:grain\",\"amount\":23}],\"durationMilliseconds\":59001,\"estimatedCompletesAtMilliseconds\":1793365000000,\"occupiedAfter\":17,\"capacity\":60,\"canStart\":true,\"reason\":\"accepted\"},{\"count\":4,\"estimatedCompletesAtMilliseconds\":null,\"canStart\":false,\"reason\":\"capacity\"}]}");
            Assert.That(unit.quotes[0].totalCost[0].amount, Is.EqualTo(23));
            Assert.That(unit.quotes[0].durationMilliseconds, Is.EqualTo(59001));
            Assert.That(unit.quotes[0].occupiedAfter, Is.EqualTo(17));
            Assert.That(unit.quotes[1].canStart, Is.False);
            Assert.That(unit.quotes[1].estimatedCompletesAtMilliseconds, Is.Zero);
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
