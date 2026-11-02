using System;
using System.IO;
using Game.App;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class AccountDomainClientTests
    {
        const string Recruit = "{\"poolFamilyId\":\"test\",\"poolVersion\":\"v1\",\"policyHash\":\"hash\",\"costGems\":17,\"guaranteeAfterMisses\":4,\"guaranteedRarity\":\"epic\",\"duplicateShards\":{\"common\":9,\"epic\":31},\"base\":[{\"heroId\":\"slg:a\",\"rarity\":\"common\",\"weight\":1,\"scale\":2,\"probability\":0.5}],\"next\":[{\"heroId\":\"slg:a\",\"rarity\":\"common\",\"weight\":0,\"scale\":2,\"probability\":0}],\"pity\":4,\"drawsUntilGuarantee\":1,\"gems\":99,\"heroes\":{\"slg:a\":0,\"slg:unknown_future_hero\":23},\"last\":null}";
        const string Research = "{\"serverUtcMs\":1001,\"nodes\":[{\"id\":\"slg:research\",\"category\":\"economy\",\"level\":0,\"maxLevel\":1,\"status\":\"locked\",\"lockReasons\":[\"proof:slg:proof\"],\"cost\":{\"meta:grain\":3,\"future:material\":29},\"seconds\":123,\"domain\":\"economy\",\"value\":7}],\"active\":[],\"effects\":[],\"proofs\":[]}";

        [Test] public void DictionaryProjectionKeepsUnknownHeroAndResourceIdsAndNullLast()
        {
            var view = AccountDomainViews.Read("{\"domains\":{\"recruit\":" + Recruit + ",\"research\":" + Research + "}}");
            Assert.That(view.recruit.heroes["slg:unknown_future_hero"], Is.EqualTo(23));
            Assert.That(view.recruit.heroes.ContainsKey("slg:a"), Is.True, "Zero shards still means owned.");
            Assert.That(view.recruit.duplicateShards["epic"], Is.EqualTo(31));
            Assert.That(view.recruit.last, Is.Null);
            Assert.That(view.recruit.baseOdds[0].probability, Is.EqualTo(.5));
            Assert.That(view.research.nodes[0].cost["future:material"], Is.EqualTo(29));
        }
        [Test] public void MissingServerPriceIsRejectedInsteadOfDisplayingFreeRecruit()
        {
            Assert.Throws<FormatException>(() => AccountDomainViews.Read("{\"domains\":{\"recruit\":" + Recruit.Replace("\"costGems\":17,", "") + "}}"));
            Assert.Throws<FormatException>(() => AccountDomainViews.Read("{\"domains\":{\"recruit\":" + Recruit.Replace("\"probability\":0.5", "\"probability\":2") + "}}"));
        }
        [Test] public void DuplicateWrongTypeAndOverflowAreRejected()
        {
            Assert.Throws<FormatException>(() => AccountDomainViews.Read("{\"domains\":{},\"domains\":{}}"));
            Assert.Throws<FormatException>(() => AccountDomainViews.Read("{\"domains\":{\"recruit\":" + Recruit.Replace("\"costGems\":17", "\"costGems\":\"17\"") + "}}"));
            Assert.Throws<OverflowException>(() => AccountDomainViews.Read("{\"domains\":{\"recruit\":" + Recruit.Replace("\"costGems\":17", "\"costGems\":2147483648") + "}}"));
        }
        [Test] public void JournalRejectsMissingVersionAndNonObjectPayload()
        {
            var body = BarracksServerClient.AccountCommandJson(Guid.NewGuid(), 37, "recruit", "recruit", "{}");
            Assert.Throws<FormatException>(() => PendingAccountCommand.Domain(body.Replace("\"expectedVersion\":37,", "")));
            Assert.Throws<FormatException>(() => PendingAccountCommand.Domain(body.Replace("\"payload\":{}", "\"payload\":null")));
            Assert.Throws<FormatException>(() => PendingAccountCommand.Domain(body.Replace("\"domain\":\"recruit\"", "\"domain\":\"recruit\",\"domain\":\"research\"")));
        }
        [Test] public void DomainOnlyReplyCannotRouteToAnAccountPanel()
        {
            var partial = UnityEngine.JsonUtility.FromJson<BarracksReply>("{\"estate\":{\"version\":9}}");
            Assert.That(BarracksServerClient.HasAccountProjection(partial), Is.False);
            var full = UnityEngine.JsonUtility.FromJson<BarracksReply>("{\"estate\":{\"version\":9},\"resources\":[],\"domains\":{\"training\":{\"units\":[]}}}");
            Assert.That(BarracksServerClient.HasAccountProjection(full), Is.True);
        }
        [Test] public void OldTrainingOnlyReplyRemainsReadable()
        {
            var view = AccountDomainViews.Read("{\"domains\":{\"training\":{\"units\":[]}}}");
            Assert.That(view.recruit, Is.Null); Assert.That(view.research, Is.Null);
        }
        [Test] public void RestartReusesExactBodyAndOtherAccountsCannotReadIt()
        {
            var root = Path.Combine(Path.GetTempPath(), "sow-account-journal-" + Guid.NewGuid().ToString("N"));
            var body = BarracksServerClient.AccountCommandJson(Guid.NewGuid(), 37, "recruit", "recruit", "{\"poolVersion\":\"v9\"}");
            var first = new PendingAccountCommand(root, "https://example.invalid\naccount-a");
            first.Write(body);
            Assert.That(new PendingAccountCommand(root, "https://example.invalid\naccount-a").Read(), Is.EqualTo(body));
            Assert.That(new PendingAccountCommand(root, "https://example.invalid\naccount-b").Read(), Is.Null);
            first.Write(null);
            Assert.That(first.Read(), Is.Null);
            TestContext.WriteLine("Owned test journal retained for manifest-based cleanup: " + root);
        }
        [Test] public void ChangingAccountsPreservesOldJournalWithoutBlockingNewAccount()
        {
            const string endpoint = "http://localhost:5080";
            var root = Path.Combine(Path.GetTempPath(), "sow-switch-journal-" + Guid.NewGuid().ToString("N"));
            var accountA = Guid.NewGuid().ToString("D"); var accountB = Guid.NewGuid().ToString("D");
            var body = BarracksServerClient.AccountCommandJson(Guid.NewGuid(), 13, "recruit", "recruit", "{}");
            var journal = new PendingAccountCommand(root, endpoint + "\n" + accountA); journal.Write(body);
            var client = new BarracksServerClient(endpoint, root);
            // Advance binding only; nested HTTP enumerators are deliberately not run in this unit test.
            Assert.That(client.ConnectAuthenticated(new string('a',64), accountA).MoveNext(), Is.True);
            Assert.That(client.Uncertain, Is.True);
            client.ClearAuthentication();
            Assert.That(client.ConnectAuthenticated(new string('b',64), accountB).MoveNext(), Is.True);
            Assert.That(client.Uncertain, Is.False);
            Assert.That(client.MatchesCredentials(new string('b',64), accountB), Is.True);
            Assert.That(journal.Read(), Is.EqualTo(body));
            client.ClearAuthentication();
            Assert.That(client.ConnectAuthenticated(new string('a',64), accountA).MoveNext(), Is.True);
            Assert.That(client.Uncertain, Is.True);
            Assert.That(journal.Read(), Is.EqualTo(body));
            TestContext.WriteLine("Owned test journal retained for manifest-based cleanup: " + root);
        }
        [Test] public void InvalidPendingBodyFailsBeforePersistence()
        {
            var root = Path.Combine(Path.GetTempPath(), "sow-invalid-journal-" + Guid.NewGuid().ToString("N"));
            var journal = new PendingAccountCommand(root, "account-a");
            Assert.Throws<FormatException>(() => journal.Write("{}"));
            Assert.That(Directory.Exists(root), Is.False);
        }
    }
}
