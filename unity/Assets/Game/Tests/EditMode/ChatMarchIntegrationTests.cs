using System;
using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ChatMarchIntegrationTests
    {
        [Test] public void DeletedAuthorRemainsReportableWithoutInventingAnAccountId()
        {
            const string json = "{\"role\":\"member\",\"alliance\":{\"id\":\"00000000-0000-0000-0000-000000000001\"},\"chat\":[{\"messageId\":\"00000000-0000-0000-0000-000000000002\",\"author\":null,\"text\":\"보관된 메시지\",\"atMs\":1}],\"blocked\":[]}";
            var view = BarracksServerClient.ParseAllianceView(json);
            Assert.That(view.chat[0].author, Is.Null.Or.Empty);
            Assert.That(view.chat[0].messageId, Is.EqualTo("00000000-0000-0000-0000-000000000002"));
        }
        [TestCase("{}")]
        [TestCase("{\"role\":\"member\",\"chat\":[],\"blocked\":[]}")]
        [TestCase("{\"role\":\"none\",\"chat\":[],\"blocked\":[\"not-an-account-id\"]}")]
        public void MalformedViewIsNotPromotedToServerState(string json)
        {
            Assert.Throws<FormatException>(() => BarracksServerClient.ParseAllianceView(json));
        }
        [Test] public void ReportAndBlockUseTheSmallApprovedAtlas()
        {
            var report = ChatMarchArt.Sprite("chat-report"); var block = ChatMarchArt.Sprite("chat-block");
            Assert.That(report.texture, Is.SameAs(block.texture));
            Assert.That(report.texture.width, Is.EqualTo(128)); Assert.That(report.texture.height, Is.EqualTo(56));
            Assert.That(report.rect.size, Is.EqualTo(new Vector2(48, 48))); Assert.That(block.rect.size, Is.EqualTo(new Vector2(48, 48)));
            Assert.That(report.border, Is.EqualTo(Vector4.zero));
        }
        [TestCase("target-card", 700, 432)]
        [TestCase("march-formation-panel", 700, 1020)]
        public void ResizablePaperKeepsSixteenPointCorners(string role, int width, int height)
        {
            var sprite = ChatMarchArt.Sprite(role);
            Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(width, height)));
            Assert.That(sprite.border, Is.EqualTo(new Vector4(32, 32, 32, 32)));
        }
        [TestCase("reward-iron")]
        [TestCase("march-troop-row-skirmish-proposed")]
        public void SupersededIronAndUnapprovedUnitAreNotRegistered(string role)
        {
            Assert.Throws<ArgumentException>(() => ChatMarchArt.Sprite(role));
        }
    }
}
