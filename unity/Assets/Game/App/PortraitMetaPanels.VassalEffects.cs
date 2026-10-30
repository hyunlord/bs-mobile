using System;
using System.Globalization;
using System.Linq;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        public const string VassalEffectsGlyphs = "편성보유이번적용값최대체력농지성장아군피해판안경험치상한도달더강화해도전투효과없음변경다음출정부터반영됩니다확인하면아래능력이고정진행중미편성인장켜짐목록";

        public static string VassalPercent(int permille) =>
            (permille / 10m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        public static string VassalAbilityName(MetaAbility ability) => ability switch
        {
            MetaAbility.Attack => "공격",
            MetaAbility.Health => "최대체력",
            MetaAbility.Movement => "이동",
            MetaAbility.Growth => "농지 성장",
            MetaAbility.Allies => "아군 피해·체력",
            MetaAbility.Experience => "판 안 경험치",
            _ => throw new ArgumentOutOfRangeException(nameof(ability))
        };

        static string AppliedVassalValue(int bonus) => (bonus > 0 ? "+" : "") + VassalPercent(bonus);

        static void VassalEffectsList(UiShell ui, MetaCatalog catalog, MetaState state, string selectedId,
            Action<string> select, Action<PortraitMetaPage> navigate, VassalRunOptions preview)
        {
            var root = Page(ui, "Vassals", "");
            root.GetComponent<Image>().color = new Color32(20, 26, 31, 255);
            VassalResourceHeader(ui, root, catalog, state);
            Plate(ui, root, "Vassal title", "ui.panel", 8, 50, Width(ui) - 16, 44);
            Caption(ui, root, "가신", 20, 58, Width(ui) - 40, 28, 20, true, TextAnchor.MiddleCenter);
            var body = Body(ui, root, 102, 70);
            var width = Width(ui) - 40;
            Caption(ui, body, "편성 " + preview.Snapshot.Roster.Count + " / " + preview.Snapshot.EffectiveSlots + " · 판 안 능력", 2, 0, width, 27, 18);
            Caption(ui, body, "보유 능력과 이번 편성의 적용값", 2, 29, width, 22, 14);
            var y = 56f;
            string cappedName = null;
            foreach (var definition in catalog.Vassals)
            {
                var value = state.Vassals[definition.Id];
                var active = preview.Snapshot.Roster.Any(member => member.Id == definition.Id);
                var rule = preview.Rules.Effects.Single(effect => effect.Ability == definition.Ability);
                var effect = preview.Snapshot.Effects.Single(item => item.Ability == definition.Ability);
                var owned = 0;
                if (value.Unlocked)
                {
                    var individual = VassalRun.Create(catalog, state with { ActiveVassalIds = new[] { definition.Id } }, preview.Rules);
                    owned = individual.Snapshot.Effects.Single(item => item.Ability == definition.Ability).Bonus;
                }
                var row = Plate(ui, body, "Vassal effect:" + definition.Id, active ? "ui.card.rare" : "ui.card.common", 2, y, width, 84);
                var portrait = Art(ui, row, definition.ArtRole, 8, 8, 48, 60);
                if (!value.Unlocked) { LockedPortrait(portrait); }
                Caption(ui, row, definition.Name, 66, 7, width - 164, 22, 16, true);
                Caption(ui, row, active ? "편성됨" : value.Unlocked ? "미편성" : "잠김", width - 79, 7, 66, 22, 14, true, TextAnchor.MiddleRight);
                Caption(ui, row, "Lv " + value.Level + " · 승급 " + value.Rank + " · 보유 " + AppliedVassalValue(owned), 66, 29, width - 76, 21, 14, true);
                Caption(ui, row, VassalAbilityName(definition.Ability) + " " + AppliedVassalValue(active ? effect.Bonus : 0) + "  /  상한 " + VassalPercent(rule.Cap), 66, 51, width - 76, 23, 14, true);
                var id = definition.Id;
                Hit(body, "Vassals.Select:" + id, () => select(id), 2, y, width, 84);
                if (active && effect.Raw >= rule.Cap && (cappedName == null || id == selectedId))
                {
                    cappedName = definition.Name + " · " + VassalAbilityName(definition.Ability);
                }
                y += 88;
            }
            if (cappedName != null)
            {
                Caption(ui, body, cappedName, 2, y + 8, width, 22, 14);
                var warning = Caption(ui, body, "상한 도달 — 더 강화해도 전투 효과 없음", 2, y + 31, width, 24, 14);
                warning.color = new Color32(235, 197, 125, 255);
                y += 61;
            }
            Caption(ui, body, "편성 변경은 다음 출정부터 반영됩니다.", 2, y, width, 23, 14);
            End(body, y + 23);
            VassalNavigation(ui, root, navigate);
        }

        static void VassalResourceHeader(UiShell ui, RectTransform root, MetaCatalog catalog, MetaState state)
        {
            var bar = Plate(ui, root, "Vassal resources", "ui.panel", 8, 6, Width(ui) - 16, 40);
            var cell = (Width(ui) - 20) / 4;
            var resources = catalog.Materials.Take(4).ToArray();
            for (var i = 0; i < resources.Length; i++)
            {
                var resource = resources[i];
                state.Wallet.TryGetValue(resource.Id, out var amount);
                if (i < 3) { MaterialArt(ui, bar, resource.Id, resource.ArtRole, 8 + i * cell, 8, 24); }
                Caption(ui, bar, (i == 3 ? resource.Name + " " : "") + amount, (i == 3 ? 8 : 37) + i * cell, 8, cell - (i == 3 ? 12 : 39), 22, 14, true);
            }
        }

        static void VassalNavigation(UiShell ui, RectTransform root, Action<PortraitMetaPage> navigate)
        {
            var pages = new[] { PortraitMetaPage.Estate, PortraitMetaPage.Vassals, PortraitMetaPage.Challenges, PortraitMetaPage.Shop, PortraitMetaPage.Departure };
            var names = new[] { "영지", "가신", "도전", "상점", "출정" };
            var cell = (Width(ui) - 116) / 4;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i]; var x = i < 4 ? 8 + i * cell : Width(ui) - 100;
                var width = i < 4 ? cell : 92;
                if (i == 1 || i == 4)
                {
                    ActionButton(ui, root, "Tab." + page, names[i], () => navigate(page), x, Height(ui) - 56, width, true, 48, "ui.button.primary");
                }
                else
                {
                    Caption(ui, root, names[i], x, Height(ui) - 44, width, 24, 14, false, TextAnchor.MiddleCenter);
                    Hit(root, "Tab." + page, () => navigate(page), x, Height(ui) - 56, width, 48);
                }
            }
        }

        public static void VassalDepartureConfirmation(UiShell ui, MetaCatalog catalog, VassalRunOptions options,
            Action confirm, Action change, Action back)
        {
            if (options == null) { throw new ArgumentNullException(nameof(options)); }
            var root = UiShell.Rect("Portrait.VassalDepartureConfirmation", ui.Content); UiShell.Stretch(root);
            var shade = root.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .65f);
            var dismiss = root.gameObject.AddComponent<Button>(); dismiss.targetGraphic = shade; dismiss.onClick.AddListener(() => back());
            var panelWidth = Width(ui) - 40;
            var top = Mathf.Max(64, (Height(ui) - 580) / 2);
            var panel = Plate(ui, root, "Departure snapshot", "ui.panel", 20, top, panelWidth, 580);
            panel.gameObject.AddComponent<Button>().interactable = false;
            Caption(ui, panel, "출정 확인", 20, 16, panelWidth - 40, 28, 20, true, TextAnchor.MiddleCenter);
            Caption(ui, panel, "편성 " + options.Snapshot.Roster.Count + " / " + options.Snapshot.EffectiveSlots, 20, 55, panelWidth - 40, 24, 16, true, TextAnchor.MiddleCenter);
            Caption(ui, panel, "출정하면 아래 능력이 고정됩니다.", 20, 88, panelWidth - 40, 24, 14, true);
            var y = 120f;
            foreach (var member in options.Snapshot.Roster)
            {
                var definition = catalog.Vassals.Single(item => item.Id == member.Id);
                var effect = options.Snapshot.Effects.Single(item => item.Ability == definition.Ability);
                Art(ui, panel, definition.ArtRole, 22, y, 48, 60);
                Caption(ui, panel, definition.Name, 78, y + 5, panelWidth - 98, 23, 16, true);
                Caption(ui, panel, VassalAbilityName(definition.Ability) + " " + AppliedVassalValue(effect.Bonus), 78, y + 30, panelWidth - 98, 23, 14, true);
                y += 64;
            }
            y = Mathf.Max(y + 12, 320);
            Caption(ui, panel, "미편성 · 적용 없음", 20, y, panelWidth - 40, 23, 14, true); y += 25;
            var inactive = options.Snapshot.Effects.Where(effect => !options.Snapshot.Roster.Any(member => options.Rules.Vassals.Any(definition => definition.Id == member.Id && definition.Ability == effect.Ability))).ToArray();
            for (var i = 0; i < inactive.Length; i += 2)
            {
                var text = string.Join(" · ", inactive.Skip(i).Take(2).Select(effect => VassalAbilityName(effect.Ability) + " 0%"));
                Caption(ui, panel, text, 20, y, panelWidth - 40, 23, 14, true); y += 24;
            }
            Caption(ui, panel, "진행 중 강화·편성 변경은\n다음 출정부터 반영됩니다.", 20, Mathf.Max(411, y + 8), panelWidth - 40, 48, 14, true);
            var buttonWidth = (panelWidth - 60) / 2;
            ActionButton(ui, panel, "VassalDeparture.Change", "편성 변경", change, 20, 500, buttonWidth, true, 48);
            ActionButton(ui, panel, "VassalDeparture.Confirm", "출정", confirm, panelWidth - 20 - buttonWidth, 500, buttonWidth, true, 48, "ui.button.primary");
        }
    }
}
