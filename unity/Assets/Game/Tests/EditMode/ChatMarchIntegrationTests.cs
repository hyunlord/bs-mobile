using System;
using System.IO;
using System.Linq;
using Game.App;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class ChatMarchIntegrationTests
    {
        [Test] public void ConnectedChatKeepsSelectedCTabsAndHidesFullAccountIdentifiers()
        {
            const string author = "00000000-0000-0000-0000-000000000002";
            const string blocked = "00000000-0000-0000-0000-000000000003";
            var client = new BarracksServerClient("http://localhost:5080", Path.Combine(Path.GetTempPath(), "sow-chat-ui-" + Guid.NewGuid().ToString("N")));
            Assert.That(client.ConnectAuthenticated(new string('a', 64), Guid.NewGuid().ToString("D")).MoveNext(), Is.True);
            client.AcceptAllianceCommand("{\"done\":true,\"reason\":\"blocked\",\"accountVersion\":2,\"alliance\":{\"role\":\"member\",\"alliance\":{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"시험 연맹\"},\"chat\":[{\"messageId\":\"00000000-0000-0000-0000-000000000004\",\"author\":\"" + author + "\",\"text\":\"시험 메시지\"}],\"blocked\":[\"" + blocked + "\"]}}");
            var owner = new GameObject("Alliance C navigation test", typeof(RectTransform));
            var font = FontProvider.Create(new[] { PortraitMetaPanels.Glyphs });
            try
            {
                var ui = owner.AddComponent<UiShell>(); ui.Initialize(font, true); ui.SafeRoot.sizeDelta = new Vector2(390, 844);
                var selected = PortraitMetaPage.Alliance; var departed = false;
                PortraitMetaPanels.AllianceChat(ui, client, null, false, new AllianceChatActions
                    { Navigate = page => selected = page, Depart = () => departed = true });
                var buttons = owner.GetComponentsInChildren<Button>();
                foreach (var page in new[] { PortraitMetaPage.Estate, PortraitMetaPage.WorldMap, PortraitMetaPage.Alliance, PortraitMetaPage.Shop })
                {
                    var button = buttons.Single(value => value.name == "Tab." + page);
                    Assert.That(button.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(48));
                    Assert.That(button.image.sprite != null, Is.EqualTo(page == PortraitMetaPage.Alliance));
                    button.onClick.Invoke(); Assert.That(selected, Is.EqualTo(page));
                }
                buttons.Single(value => value.name == "Estate.Depart").onClick.Invoke(); Assert.That(departed, Is.True);
                var labels = string.Join("\n", owner.GetComponentsInChildren<Text>().Select(value => value.text));
                Assert.That(labels, Does.Not.Contain(author).And.Not.Contain(blocked));
                Assert.That(labels, Does.Contain("연맹원").And.Contain("차단한 연맹원 · 00000000"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(font); }
        }
        [Test] public void ReplayedAllianceReceiptCannotInventAnAccountProjectionOrRewriteTheJournal()
        {
            const string endpoint = "http://localhost:5080";
            var root = Path.Combine(Path.GetTempPath(), "sow-alliance-restart-" + Guid.NewGuid().ToString("N"));
            var account = Guid.NewGuid().ToString("D");
            var body = BarracksServerClient.AccountCommandJson(Guid.NewGuid(), 37, "alliance", "block",
                "{\"accountId\":\"00000000-0000-0000-0000-000000000002\"}");
            var journal = new PendingAccountCommand(root, endpoint + "\n" + account); journal.Write(body);
            var client = new BarracksServerClient(endpoint, root); client.PrepareAlliance();
            // Binding only; HTTP execution belongs to the separate live-server capture gate.
            Assert.That(client.ConnectAuthenticated(new string('a', 64), account).MoveNext(), Is.True);
            client.AcceptAllianceCommand("{\"done\":true,\"reason\":\"blocked\",\"accountVersion\":38,\"alliance\":{\"role\":\"none\",\"chat\":[],\"blocked\":[]}}");
            Assert.That(client.Latest, Is.Null, "A domain receipt is not the full account/training projection required for routing.");
            Assert.That(client.AllianceResult.reason, Is.EqualTo("blocked"));
            Assert.That(client.Uncertain, Is.True, "Only the shared transport may acknowledge the durable journal.");
            Assert.That(journal.Read(), Is.EqualTo(body));
            client.ClearAuthentication();
            Assert.That(client.AllianceLatest, Is.Null); Assert.That(client.AllianceResult, Is.Null);
            Assert.That(journal.Read(), Is.EqualTo(body));
            TestContext.WriteLine("Owned test journal retained for manifest-based cleanup: " + root);
        }
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
