using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // M3a-4 (#370): the estate map's construction, queue, claim, specialization and zone flow, driven through Core.
    public sealed class EstateFlowTests
    {
        double now;
        EstateMapScreen Screen(MetaState account = null)
        {
            var config = CanonicalContent.CreateAccountEstateConfig(account?.Estate?.ConfigVersion); var catalog = CanonicalContent.CreateMetaCatalog();
            now = 0;
            return new EstateMapScreen(catalog, config, account ?? MetaEngine.NewGame(catalog), realtime: () => now);
        }
        static int Id(EstateMapScreen screen, string kind) => screen.State.Estate.Buildings.Single(b => b.Kind == kind).Id;
        static int Wallet(EstateMapScreen screen, string material) => screen.State.Wallet.TryGetValue(material, out var value) ? value : 0;

        [Test] public void HistoricalR5EstateOpensWithItsOwnConfigWithoutRewritingAccountBytes()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var historical = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r5");
            var account = AccountEstate.NewPrototype(catalog, historical, 0, 0);
            var before = MetaSaveCodec.Encode(account);
            var screen = Screen(account);
            Assert.That(screen.FromSave, Is.True);
            Assert.That(screen.Config.Version, Is.EqualTo("m3a-prototype-r5"));
            Assert.That(screen.Config.Zones.Single(z => z.OpenAtStart).Width, Is.EqualTo(14));
            screen.Select(Id(screen, "meta:granary"));
            Assert.That(screen.StartWork(Id(screen, "meta:granary")), Is.True);
            now = 61; Assert.That(screen.Advance(), Is.True);
            Assert.That(screen.Building(Id(screen, "meta:granary")).Level, Is.EqualTo(1));
            Assert.That(MetaSaveCodec.Encode(account), Is.EqualTo(before), "Commands affect only the prototype copy, not the historical account.");
            Assert.That(CanonicalContent.CreateAccountEstateConfig("unknown-version"), Is.Null);
        }

        [Test] public void FreshFieldRejectionRetainsCoreReasonWithoutGrantingResources()
        {
            var screen = Screen();
            var before = screen.State.Wallet.OrderBy(pair => pair.Key).ToArray();
            Assert.That(screen.StartWork(Id(screen, "m3a:field")), Is.False);
            Assert.That(screen.LastConstructionReason, Is.EqualTo("parent-required"));
            Assert.That(screen.State.Wallet.OrderBy(pair => pair.Key).ToArray(), Is.EqualTo(before));
            Assert.That(screen.State.Estate.Active, Is.Null);
        }

        [Test] public void ANewPrototypeEstateStartsWithTheConfigWallet()
        {
            var screen = Screen();
            Assert.That(screen.FromSave, Is.False);
            foreach (var pair in screen.Config.InitialWallet) Assert.That(Wallet(screen, pair.Key), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(screen.Building(Id(screen, "m3a:home")).Level, Is.EqualTo(1));
            Assert.That(screen.State.Estate.Buildings.Where(b => b.Kind != "m3a:home").All(b => b.Level == 0), Is.True);
        }

        [Test] public void ConstructionPreviewNamesTheParentAndNeverMutatesTheState()
        {
            var screen = Screen(); var field = Id(screen, "m3a:field"); var granary = Id(screen, "meta:granary");
            var before = MetaSaveCodec.Encode(screen.State);
            var blocked = screen.ConstructionPreview(field);
            Assert.That(blocked.Allowed, Is.False); Assert.That(blocked.Reason, Is.EqualTo("parent-required"));
            Assert.That(blocked.Text, Is.EqualTo("곡물창고 Lv1 필요"));
            Assert.That(screen.ConstructionPreview(granary).Allowed, Is.True);
            Assert.That(MetaSaveCodec.Encode(screen.State), Is.EqualTo(before), "Both accepted and rejected previews are read-only.");
            Assert.That(screen.LastConstructionReason, Is.Empty);
            Assert.That(screen.StartWork(granary), Is.True);
            Assert.That(screen.ConstructionPreview(field).Allowed, Is.True, "Core permits reserving a child while its parent is active.");
            now = 60; screen.Advance();
            Assert.That(screen.ConstructionPreview(field).Reason, Is.EqualTo("started"));
        }

        [Test] public void ConstructionQueuesAndCompletesWithRealTimeAndTheSkip()
        {
            var screen = Screen(); var granary = Id(screen, "meta:granary"); var forge = Id(screen, "meta:forge");
            var field = Id(screen, "m3a:field"); var lumber = Id(screen, "m3a:lumber_camp");
            var next = screen.NextStep(granary).Value;
            Assert.That(next.Seconds, Is.EqualTo(60)); Assert.That(screen.Amounts(next.Cost), Is.EqualTo("목재 30"));
            Assert.That(screen.StartWork(granary), Is.True); Assert.That(screen.Notice, Is.EqualTo("곡물창고 공사 시작"));
            Assert.That(Wallet(screen, "meta:timber"), Is.EqualTo(150));
            Assert.That(screen.StartWork(forge), Is.True); Assert.That(screen.Notice, Is.EqualTo("대장간 예약"));
            Assert.That(screen.StartWork(field), Is.True, "A child site may be reserved while its parent is being built.");
            Assert.That(screen.StartWork(lumber), Is.False); Assert.That(screen.Notice, Is.EqualTo("예약이 가득 찼습니다"));
            Assert.That(screen.WorkLine(), Is.EqualTo("공사 곡물창고 Lv 1 · 1:00 · 예약 2/2"));
            now = 10; Assert.That(screen.Advance(), Is.False, "Countdown within stage 1 refreshes in place.");
            Assert.That(EstateConstructionArt.Stage(screen.State.Estate.Active), Is.EqualTo(1));
            now = 30.9; Assert.That(screen.Advance(), Is.True, "Crossing the 33% visual boundary redraws stage 2.");
            Assert.That(screen.State.Estate.Active.RemainingSeconds, Is.EqualTo(30), "Real time counts whole seconds.");
            Assert.That(EstateConstructionArt.Stage(screen.State.Estate.Active), Is.EqualTo(2));
            Assert.That(screen.Building(granary).Level, Is.Zero, "A visual stage does not complete Core construction.");
            Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(granary));
            Assert.That(screen.State.Estate.Reservations.Length, Is.EqualTo(2));
            var stageTwo = screen.Plan(EstateDecorRules.Load(), EstateArt.Load());
            Assert.That(stageTwo.Items.Single(i => i.Source == "construction").Role, Is.EqualTo("construction-building-2-lit"));
            now = 31.9; Assert.That(screen.Advance(), Is.False, "Countdown within stage 2 refreshes in place.");
            Assert.That(screen.Plan(EstateDecorRules.Load(), EstateArt.Load()), Is.SameAs(stageTwo));
            now = 61; Assert.That(screen.Advance(), Is.True);
            Assert.That(screen.Building(granary).Level, Is.EqualTo(1)); Assert.That(screen.Notice, Is.EqualTo("곡물창고 Lv 1 완공"));
            Assert.That(screen.State.Estate.Active, Is.Null, "Auto start is off by default, so reservations wait.");
            Assert.That(screen.CancelReservation(field), Is.True); Assert.That(screen.State.Estate.Reservations.Length, Is.EqualTo(1));
            Assert.That(screen.StartReserved(), Is.True); Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(forge));
            Assert.That(screen.ToggleAutoStart(), Is.True); Assert.That(screen.State.Estate.AutoStart, Is.True); Assert.That(screen.Notice, Is.EqualTo("자동 착공을 켰습니다"));
            Assert.That(screen.SkipTime(120), Is.True); Assert.That(screen.Building(forge).Level, Is.EqualTo(1));
        }

        [Test] public void SiteStockIsClaimedOnceAndUnlocksTheFirstGoal()
        {
            var screen = Screen(); var granary = Id(screen, "meta:granary"); var field = Id(screen, "m3a:field");
            Assert.That(screen.StartWork(granary), Is.True); screen.SkipTime(60);
            Assert.That(screen.StartWork(field), Is.True); screen.SkipTime(30);
            Assert.That(screen.Building(field).Level, Is.EqualTo(1));
            Assert.That(screen.ClaimProduction(), Is.False); Assert.That(screen.Notice, Is.EqualTo("받을 재고가 없습니다"));
            Assert.That(screen.SkipTime(600), Is.True, "Stock appearing on an idle site redraws the page so the claim button shows (review on #371).");
            Assert.That(screen.StockTotal, Is.EqualTo(60), "6 grain a minute for ten minutes at granary level 1.");
            Assert.That(screen.SkipTime(60), Is.False, "More stock only refreshes the claim amount in place.");
            Assert.That(screen.StockTotal, Is.EqualTo(66));
            Assert.That(screen.Status(screen.Building(field)), Does.Contain("재고 곡물 66/720"));
            var grain = Wallet(screen, "meta:grain");
            Assert.That(screen.GoalReady, Is.False);
            Assert.That(screen.ClaimProduction(), Is.True); Assert.That(screen.Notice, Is.EqualTo("받음 · 곡물 66"));
            Assert.That(Wallet(screen, "meta:grain"), Is.EqualTo(grain + 66)); Assert.That(screen.StockTotal, Is.EqualTo(0));
            Assert.That(screen.GoalReady, Is.True); Assert.That(screen.GoalText(), Does.EndWith("· 달성"));
            var timber = Wallet(screen, "meta:timber");
            Assert.That(screen.ClaimGoal(), Is.True); Assert.That(Wallet(screen, "meta:timber"), Is.EqualTo(timber + 20));
            Assert.That(screen.ClaimGoal(), Is.False, "The next goal is not achieved yet; the first one paid once.");
            Assert.That(screen.GoalText(), Does.StartWith("목표 · 생산 터 2곳 완성"));
        }

        [Test] public void AMaxLevelBuildingTakesOneBranchFromTheSheet()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            var old = MetaEngine.NewGame(catalog);
            old = old with { ManorLevels = new Dictionary<string, int>(old.ManorLevels) { ["meta:granary"] = 3 } };
            var account = AccountEstate.Migrate(catalog, config, old, 0, 0);
            var screen = Screen(account); var granary = Id(screen, "meta:granary");
            Assert.That(screen.FromSave, Is.True);
            Assert.That(screen.NextStep(granary), Is.Null);
            var before = AccountEstate.StockCap(screen.Config, screen.State, "meta:grain");
            screen.Select(granary); screen.BeginSpecialize();
            Assert.That(screen.Specializing, Is.True); Assert.That(screen.BranchChoice, Is.EqualTo("granary:expedition-supply"));
            screen.ChooseBranch("granary:long-away");
            Assert.That(screen.BranchEffect("granary:long-away"), Is.EqualTo("보관 +1시간 · 재고 상한 +50%"));
            Assert.That(screen.ConfirmBranch(), Is.True);
            Assert.That(screen.Building(granary).Branch, Is.EqualTo("granary:long-away")); Assert.That(screen.Specializing, Is.False);
            Assert.That(screen.Notice, Is.EqualTo("곡물창고 · 장기 보관 창고"));
            Assert.That(AccountEstate.StockCap(screen.Config, screen.State, "meta:grain"), Is.GreaterThan(before));
            Assert.That(screen.BranchEffect("forge:supply-workshop"), Is.EqualTo("철재 생산 +20%p"));
            Assert.That(screen.BranchEffect("barracks:construction-support"), Is.EqualTo("공사 시간 −10%"));
            Assert.That(account.Estate.Buildings.Single(b => b.Id == granary).Branch, Is.Null, "The account the copy came from is never written.");
        }

        [Test] public void AClosedZoneExplainsItsUnlockCondition()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var screen = Screen(AccountEstate.NewPrototype(catalog, config, 0, 0));
            var zone = screen.Config.Zones.Single(z => z.Id == "zone:inner-village");
            Assert.That(screen.ZoneAt(new Vector2Int(45, 30)).Id, Is.EqualTo(zone.Id));
            Assert.That(screen.ZoneLabel(zone), Is.EqualTo("닫힌 구역"));
            Assert.That(screen.ZoneLabel(screen.Config.Zones.Single(z => z.Id == "zone:inner-riverside")), Is.EqualTo("탐사로 열 땅 · M3c"),
                "Exploration zones say so instead of showing an unreachable condition (#374).");
            // #379: the map carries no labels; a tap names the zone, and only the zone that can open next is marked.
            Assert.That(screen.ZoneName(zone), Is.EqualTo("마을 확장")); Assert.That(screen.ZoneName(screen.Config.Zones.Single(z => z.Id == "zone:inner-riverside")), Is.EqualTo("강가"));
            // r5 (#379): the outskirts around the lit start open first; the village waits behind them.
            Assert.That(screen.NextZone()?.Id, Is.EqualTo("zone:outskirts-east"));
            screen.SelectZone(zone.Id); Assert.That(screen.ZoneChoice, Is.EqualTo(zone.Id));
            Assert.That(screen.OpenZone(zone.Id), Is.False); Assert.That(screen.Notice, Is.EqualTo("열린 구역과 이어져 있지 않습니다"), "r5: the village lies behind the outskirts.");
            screen.SelectZone("zone:outskirts-east");
            Assert.That(screen.OpenZone("zone:outskirts-east"), Is.False); Assert.That(screen.Notice, Is.EqualTo("생산 터를 더 완성해야 합니다"));
            Assert.That(EstateMapScreen.Clock(65), Is.EqualTo("1:05")); Assert.That(EstateMapScreen.Clock(3725), Is.EqualTo("1:02:05"));
        }
    }
}
