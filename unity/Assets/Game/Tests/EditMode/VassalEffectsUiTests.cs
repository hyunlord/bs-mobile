using System;
using System.Globalization;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class VassalEffectsUiTests
    {
        const string OffNotice = "이번 출정에서는 가신 능력 보너스가 적용되지 않습니다.";
        const string CapNotice = "상한 도달 — 더 강화해도 전투 효과 없음";
        GameObject owner;
        Font font;
        UiShell ui;
        MetaCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Vassal effects UI regression", typeof(RectTransform));
            font = FontProvider.Create(new[] { PortraitMetaPanels.VassalEffectsGlyphs });
            ui = owner.AddComponent<UiShell>();
            ui.Initialize(font, true);
            catalog = CanonicalContent.CreateMetaCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(font);
        }

        [TestCase(0, "0%")]
        [TestCase(5, "0.5%")]
        [TestCase(97, "9.7%")]
        [TestCase(150, "15%")]
        public void PermilleLabelsKeepTenthsWithoutRoundingUp(int permille, string expected)
        {
            Assert.That(PortraitMetaPanels.VassalPercent(permille), Is.EqualTo(expected));
        }

        [Test]
        public void NullOptInKeepsHistoricalOffNoticeAndDoesNotAdvertiseAppliedEffects()
        {
            var state = MetaEngine.NewGame(catalog);
            ShowList(state, null);
            Assert.That(Texts(owner), Does.Contain(OffNotice));
            Assert.That(owner.GetComponentsInChildren<Transform>(true).Any(item => item.name.StartsWith("Vassal effect:", StringComparison.Ordinal)), Is.False);
            Assert.That(Texts(owner), Does.Not.Contain(CapNotice));
        }

        [Test]
        public void EnabledEmptyRosterShowsZeroAppliedEffectsDespiteOwnedUpgrades()
        {
            var state = MaximumState();
            var options = VassalRun.Create(catalog, state, CanonicalContent.CreateVassalRunRules());
            ShowList(state, options);
            Assert.That(Texts(owner), Does.Not.Contain(OffNotice));
            Assert.That(Texts(owner), Does.Not.Contain(CapNotice));
            Assert.That(Texts(owner), Does.Contain("편성 0 / " + options.Snapshot.EffectiveSlots + " · 판 안 능력"));
            foreach (var definition in catalog.Vassals)
            {
                var row = Row(definition.Id);
                var rule = options.Rules.Effects.Single(item => item.Ability == definition.Ability);
                Assert.That(Texts(row), Does.Contain("미편성"), definition.Id);
                Assert.That(Texts(row), Does.Contain(PortraitMetaPanels.VassalAbilityName(definition.Ability) + " 0%  /  상한 " + Percent(rule.Cap)), definition.Id);
                Assert.That(Texts(row).Any(text => text.Contains(" · 보유 +")), Is.True, definition.Id);
            }
        }

        [Test]
        public void SelectedRosterUsesCoreCappedEffectsAndKeepsUnselectedRowsAtZero()
        {
            var state = MaximumState(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience);
            var options = VassalRun.Create(catalog, state, CanonicalContent.CreateVassalRunRules());
            string selected = null;
            ShowList(state, options, id => selected = id);
            Assert.That(Texts(owner), Does.Not.Contain(OffNotice));
            Assert.That(Texts(owner), Does.Contain(CapNotice));
            foreach (var definition in catalog.Vassals)
            {
                var active = state.ActiveVassalIds.Contains(definition.Id);
                var effect = options.Snapshot.Effects.Single(item => item.Ability == definition.Ability);
                var rule = options.Rules.Effects.Single(item => item.Ability == definition.Ability);
                var value = active ? "+" + Percent(effect.Bonus) : "0%";
                Assert.That(Texts(Row(definition.Id)), Does.Contain(PortraitMetaPanels.VassalAbilityName(definition.Ability) + " " + value + "  /  상한 " + Percent(rule.Cap)), definition.Id);
                Button("Vassals.Select:" + definition.Id).onClick.Invoke();
                Assert.That(selected, Is.EqualTo(definition.Id));
            }
            var health = options.Snapshot.Effects.Single(item => item.Ability == MetaAbility.Health);
            Assert.That(health.Raw, Is.GreaterThan(health.Bonus), "Fixture must exercise a real cap, not just an ordinary selected value.");
        }

        [Test]
        public void DepartureShowsFrozenRosterAfterAccountSelectionAndLevelsChange()
        {
            var state = MaximumState(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience);
            var options = VassalRun.Create(catalog, state, CanonicalContent.CreateVassalRunRules());
            var originalIds = state.ActiveVassalIds.ToArray();
            foreach (var id in originalIds)
            {
                state.Vassals[id] = state.Vassals[id] with { Level = 1, Rank = 0 };
            }
            Array.Clear(state.ActiveVassalIds, 0, state.ActiveVassalIds.Length);
            var confirmed = 0;
            var changed = 0;
            PortraitMetaPanels.VassalDepartureConfirmation(ui, catalog, options, () => confirmed++, () => changed++, () => { });
            foreach (var id in originalIds)
            {
                var definition = catalog.Vassals.Single(item => item.Id == id);
                var effect = options.Snapshot.Effects.Single(item => item.Ability == definition.Ability);
                Assert.That(Texts(owner), Does.Contain(definition.Name));
                Assert.That(Texts(owner), Does.Contain(PortraitMetaPanels.VassalAbilityName(definition.Ability) + " +" + Percent(effect.Bonus)));
            }
            Assert.That(Texts(owner), Does.Contain("미편성 · 적용 없음"));
            Assert.That(Texts(owner), Does.Contain("진행 중 강화·편성 변경은\n다음 출정부터 반영됩니다."));
            Assert.That(confirmed, Is.Zero);
            Assert.That(changed, Is.Zero);
            Button("VassalDeparture.Confirm").onClick.Invoke();
            Button("VassalDeparture.Change").onClick.Invoke();
            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(1));
        }

        void ShowList(MetaState state, VassalRunOptions options, Action<string> select = null)
        {
            PortraitMetaPanels.Vassals(ui, catalog, state, catalog.Vassals[0].Id,
                select ?? (_ => { }), _ => { }, _ => { }, _ => { }, _ => { }, options);
        }

        MetaState MaximumState(params MetaAbility[] abilities)
        {
            var state = MetaEngine.NewGame(catalog);
            foreach (var building in catalog.ManorBuildings)
            {
                state.ManorLevels[building.Id] = building.MaxLevel;
            }
            state = state with
            {
                CompletedChallenges = catalog.Challenges.Select(item => item.Id).ToArray(),
                UnlockedContentIds = catalog.InitialContentIds.Concat(catalog.Challenges.SelectMany(item => item.UnlockContentIds)).Distinct(StringComparer.Ordinal).ToArray(),
                ActiveVassalIds = abilities.Select(ability => catalog.Vassals.Single(item => item.Ability == ability).Id).ToArray()
            };
            foreach (var definition in catalog.Vassals)
            {
                state.Vassals[definition.Id] = new(definition.MaxLevel, definition.MaxRank, 0,
                    definition.InitiallyUnlocked || catalog.Challenges.Any(item => item.UnlockVassalIds.Contains(definition.Id)));
            }
            return state;
        }

        GameObject Row(string id) => owner.GetComponentsInChildren<Transform>(true).Single(item => item.name == "Vassal effect:" + id).gameObject;
        Button Button(string name) => owner.GetComponentsInChildren<Button>(true).Single(item => item.name == name);
        static string[] Texts(GameObject root) => root.GetComponentsInChildren<Text>(true).Select(item => item.text).ToArray();
        static string Percent(int value) => (value / 10m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
