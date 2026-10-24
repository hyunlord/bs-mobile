using System;
using System.Collections.Generic;
using System.Linq;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class PortraitCardActions
    {
        readonly List<Button> buttons = new List<Button>();
        readonly List<bool> allowed = new List<bool>();
        public bool Enabled { get; private set; } = true;
        public IReadOnlyList<Button> Buttons => buttons;
        public void Add(Button button, bool available)
        { buttons.Add(button); allowed.Add(available); button.interactable = Enabled && available; }
        public void SetActionsEnabled(bool enabled)
        { Enabled = enabled; for (var i = 0; i < buttons.Count; i++) if (buttons[i] != null) buttons[i].interactable = enabled && allowed[i]; }
    }

    public static class PortraitRunPanels
    {
        public const string Glyphs = "휴경의 왕국 돌본 땅에 다시 봄이 깃든다 화면을 눌러 시작 화면을 눌러 장원으로 설정 성장 선택 카드 선택 금지 고정됨 상세 소리 켬 끔 고정할 금지할 카드를 누르세요 이미 카드입니다 다른 취소 다시 뽑기 진화 선택 잠시 멈춤 무기 도구 특허장 빈 칸 계속하기 출정을 포기하시겠어요 이번 출정은 포기로 정산됩니다 돌아가기 포기하고 정산 배경음 효과음 진동 화면 흔들림 피해 숫자 자동 조준 이동 방향 가까운 적 걷기와 피격 실험 다음 출정부터 빠른 걸음 접촉 즉시 피격 잠깐 무적과 밀려남 체력 한 대 피격 잔상 붉은 테 걷기 부위별 애니메이션 새 선택 보유 레벨 진화 진행 완료 조건 켜짐 꺼짐 출정 포기 B ×1.3 100 5~15 % ?";
        const float Gap = 12;
        public static Button RoundIcon(UiShell ui, Transform parent, string name, string glyph, Action action, float x, float y)
        {
            var rect = PortraitWidgets.Place(name, parent, x, y, 44, 44);
            Graphic graphic;
            if (glyph == "settings") graphic = rect.gameObject.AddComponent<PortraitSettingsGear>();
            else
            {
                var circle = rect.gameObject.AddComponent<PortraitBmShape>();
                circle.Shape = PortraitBmShape.Form.Circle; circle.color = new Color32(47,42,33,255);
                circle.Edge = new Color32(184,135,58,255); circle.Border = 2; graphic = circle;
                var label = PortraitWidgets.Label(ui, rect, glyph, 0, -1, 44, 44, 28);
                label.alignment = TextAnchor.MiddleCenter; label.color = new Color32(243,213,140,255);
            }
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            button.onClick.AddListener(() => action?.Invoke()); return button;
        }
        static float Width(UiShell ui) => ui.SafeRoot.rect.width > 1 ? ui.SafeRoot.rect.width : PortraitTokens.Width;
        static float Height(UiShell ui) => ui.SafeRoot.rect.height > 1 ? ui.SafeRoot.rect.height : PortraitTokens.Height;
        static RectTransform Root(UiShell ui, string name)
        { var root = UiShell.Rect(name, ui.Content); UiShell.Stretch(root); ui.Surface(root, "ui.panel").raycastTarget = true; return root; }
        static Text Text(UiShell ui, Transform parent, string value, float x, float y, float width, int size = PortraitTokens.Small)
        {
            var text = PortraitWidgets.Label(ui, parent, value, x, y, width, 24, size);
            text.resizeTextForBestFit = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.sizeDelta = new Vector2(width, Mathf.Max(24, text.preferredHeight + 4));
            return text;
        }
        static Button Button(UiShell ui, Transform parent, string name, string label, Action action, float x, float y, float width, float height = PortraitTokens.Action, bool enabled = true, string skin = "ui.button.secondary")
        {
            Button button = null;
            button = PortraitWidgets.Button(ui, parent, label, () => { if (button != null && button.interactable) action?.Invoke(); }, x, y, width, height, enabled, skin);
            button.name = name; return button;
        }
        static RectTransform Scroll(UiShell ui, RectTransform root, float top, float bottom)
        {
            var viewport = UiShell.Rect("Body viewport", root);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12, bottom); viewport.offsetMax = new Vector2(-12, -top);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var body = UiShell.Rect("Body", viewport); body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f, 1); body.sizeDelta = Vector2.zero;
            scroll.content = body; scroll.viewport = viewport; return body;
        }
        public static void Title(UiShell ui, bool firstRun, Action enter, Action settings)
        {
            var root = Root(ui, "Portrait.Title"); root.gameObject.AddComponent<RectMask2D>(); var width = Width(ui); var height = Height(ui);
            var background = UiShell.Rect("Kingdom illustrated landscape", root); UiShell.Stretch(background);
            background.gameObject.AddComponent<PortraitTitleBackdrop>().raycastTarget = false;
            var hit = UiShell.Rect("Title.Enter", root); UiShell.Stretch(hit);
            var hitImage = hit.gameObject.AddComponent<Image>(); hitImage.color = Color.clear;
            var tap = hit.gameObject.AddComponent<Button>(); tap.targetGraphic = hitImage; tap.onClick.AddListener(() => enter?.Invoke());
            PortraitWidgets.Art(ui, root, "o-crest", (width - 150) / 2, 92, 150, 167);
            var logo = Resources.Load<Texture2D>("Fallow/title-logo");
            if (logo != null)
            {
                var bounds = PortraitWidgets.Place("Kingdom logo bounds", root, 30, 260, width - 60, 84);
                var scale = Mathf.Min((width - 60) / logo.width, 84f / logo.height);
                var logoWidth = logo.width * scale; var logoHeight = logo.height * scale;
                var logoRect = PortraitWidgets.Place("Kingdom logo", bounds, (width - 60 - logoWidth) / 2, (84 - logoHeight) / 2, logoWidth, logoHeight);
                var image = logoRect.gameObject.AddComponent<RawImage>(); image.texture = logo; image.raycastTarget = false;
            }
            else Text(ui, root, "휴경의 왕국", 24, height * .2f, width - 48, PortraitTokens.Title).alignment = TextAnchor.MiddleCenter;
            PortraitWidgets.Art(ui, root, "o-wheat", 18, 548, 120, 116);
            PortraitWidgets.Art(ui, root, "o-wheat", width - 132, 560, 112, 108);
            PortraitWidgets.Art(ui, root, "hero", (width - 110) / 2, 470, 110, 139);
            var tagline = PortraitWidgets.Label(ui, root, "돌본 땅에 다시 봄이 깃든다", 20, 350, width - 40, 30, 18);
            tagline.color = new Color32(246,235,210,255); tagline.alignment = TextAnchor.MiddleCenter;
            var taglineShadow = tagline.gameObject.AddComponent<Shadow>(); taglineShadow.effectColor = new Color(0,0,0,.65f); taglineShadow.effectDistance = new Vector2(0,-2);
            TitleCaption(ui, root, "Title.Prompt", firstRun ? "화면을 눌러 시작" : "화면을 눌러 장원으로", 65, height - 154, width - 130, 64);
            var note = Text(ui, root, "첫 실행이면 바로 첫 판 · 이후에는 영지로", 20, height - 74, width - 40, 12); note.color = new Color32(233,221,191,255); note.alignment = TextAnchor.MiddleCenter;
            var settingsTarget = PortraitWidgets.Place("Title.Settings", root, width - 60, 52, 44, 44);
            var settingsGraphic = settingsTarget.gameObject.AddComponent<PortraitSettingsGear>();
            var settingsButton = settingsTarget.gameObject.AddComponent<Button>(); settingsButton.targetGraphic = settingsGraphic;
            settingsButton.onClick.AddListener(() => settings?.Invoke());
        }
        static void TitleCaption(UiShell ui, Transform parent, string name, string text, float x, float y, float width, float height)
        {
            var plate = PortraitWidgets.Place(name, parent, x, y, width, height);
            var paper = ui.Surface(plate, "ui.hint");
            paper.color = Color.white; paper.pixelsPerUnitMultiplier = 2;
            paper.raycastTarget = false;
            var label = PortraitWidgets.Label(ui, plate, text, 8, 4, width - 16, height - 8, PortraitTokens.Heading);
            label.color = new Color32(59, 42, 24, 255); label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false; label.resizeTextForBestFit = false;
        }

        public static PortraitCardActions Cards(UiShell ui, RunFrame frame, FirstPlayableFrame first, CardOfferView offer,
            ContentCatalog catalog, IReadOnlyDictionary<string, ContentDisplay> displays, Action<ReplayCommandKind, string> send, BmAdOffer ad = null, Action rewardedReroll = null)
        {
            var root = Overlay(ui, "Portrait.Cards"); var width = Width(ui); var height = Height(ui); var actions = new PortraitCardActions();
            var heading = PortraitWidgets.Place("Level ribbon", root, (width - 280) / 2, 58, 280, 64);
            var headingArt = ui.Surface(heading, "ui.button.primary"); headingArt.pixelsPerUnitMultiplier = 52f / 30;
            var title = Text(ui, heading, "레벨 " + frame.Level, 18, 5, 244, 24); title.alignment = TextAnchor.MiddleCenter; title.color = Color.white;
            var hint = Text(ui, heading, "하나를 고르면 바로 이어서 싸운다", 12, 35, 256, 11); hint.alignment = TextAnchor.MiddleCenter; hint.color = new Color32(216,225,245,255);
            var hasAd = ad != null && ad.Visible;
            var targetAction = ReplayCommandKind.ChooseCard;
            Action<ReplayCommandKind> arm = kind =>
            {
                targetAction = targetAction == kind ? ReplayCommandKind.ChooseCard : kind;
                hint.text = targetAction == ReplayCommandKind.BanCard ? "금지할 카드를 누르세요 · 금지를 다시 누르면 취소" : targetAction == ReplayCommandKind.LockCard ? "고정할 카드를 누르세요 · 고정을 다시 누르면 취소" : "하나를 고르면 바로 이어서 싸운다";
            };
            var body = Scroll(ui, root, 142, 108); var cardWidth = width - 28; float cursor = 0;
            for (var index = 0; index < offer.Cards.Count; index++)
            {
                var id = offer.Cards[index]; var detail = first.Cards.First(c => c.Id == id); var display = displays[id];
                var container = PortraitWidgets.Place("Card:" + id, body, 2, cursor, cardWidth, 132);
                var chooseRect = PortraitWidgets.Place("Card.Choose:" + id, container, 0, 0, cardWidth, 132);
                var weapon = catalog.Weapons.ContainsKey(id); var tool = catalog.Tools.ContainsKey(id);
                var category = weapon ? "rare" : tool ? "common" : "epic";
                var surface = ui.Surface(chooseRect, "ui.card." + category); surface.pixelsPerUnitMultiplier = 40f / 14; surface.raycastTarget = true;
                var choose = chooseRect.gameObject.AddComponent<Button>(); choose.targetGraphic = surface;
                choose.onClick.AddListener(() =>
                {
                    if (!choose.interactable) return;
                    if (targetAction == ReplayCommandKind.LockCard && offer.LockedCardId == id)
                    { hint.text = "이미 고정한 카드입니다 · 다른 카드를 누르세요"; return; }
                    var command = targetAction; targetAction = ReplayCommandKind.ChooseCard;
                    send(command, id);
                }); actions.Add(choose, true);
                var slot = PortraitWidgets.Place("Icon slot", chooseRect, 16, 16, 70, 70); ui.Surface(slot, "ui.slot").pixelsPerUnitMultiplier = 6;
                var icon = ui.Icon(slot, id, 46); PortraitWidgets.Position(icon.rectTransform, 12, 12, 46, 46);
                var tag = PortraitWidgets.Place("Category", chooseRect, 96, 16, tool || weapon ? 36 : 52, 20);
                tag.gameObject.AddComponent<Image>().color = weapon ? new Color32(44,79,150,255) : tool ? new Color32(79,107,46,255) : new Color32(107,62,143,255);
                var tagText = PortraitWidgets.Label(ui, tag, weapon ? "무기" : tool ? "도구" : "특허장", 0, 0, tag.rect.width, 20, 11); tagText.color = Color.white; tagText.alignment = TextAnchor.MiddleCenter;
                Text(ui, chooseRect, detail.CurrentLevel > 0 ? "Lv " + detail.CurrentLevel + " → " + detail.NextLevel : "새로 얻음", 102 + tag.rect.width, 14, cardWidth - 118 - tag.rect.width, 12);
                Text(ui, chooseRect, display.DisplayName, 96, 40, cardWidth - 112, 19);
                var description = Text(ui, chooseRect, display.EffectDescription, 96, 70, cardWidth - 112, 13);
                var y = Mathf.Max(104, 70 + description.rectTransform.rect.height + 6);
                var evolution = CardEvolutionLine(detail, first, displays);
                if (evolution.Length > 0)
                {
                    var band = PortraitWidgets.Place("Evolution ribbon", chooseRect, 96, y, cardWidth - 112, 30);
                    var gold = band.gameObject.AddComponent<Image>(); gold.color = new Color32(243,213,140,255); gold.raycastTarget = false;
                    var text = Text(ui, band, evolution, 8, 4, cardWidth - 128, 12);
                    band.sizeDelta = new Vector2(cardWidth - 112, text.rectTransform.rect.height + 8); y += band.rect.height + 4;
                }
                var cardHeight = Mathf.Max(154, y + 12); chooseRect.sizeDelta = new Vector2(cardWidth, cardHeight);
                container.sizeDelta = new Vector2(cardWidth, cardHeight); cursor += cardHeight + Gap;

            }
            body.sizeDelta = new Vector2(0, cursor);
            var footerWidth = (width - 44) / 3;
            var reroll = Button(ui, root, "Cards.Reroll", "다시 뽑기\n" + offer.Rerolls + "회 남음", () => send(ReplayCommandKind.RerollCards, null), 14, height - 100, footerWidth, 52, offer.Rerolls > 0);
            reroll.GetComponentInChildren<Text>().fontSize = 13; actions.Add(reroll, offer.Rerolls > 0);
            var ban = Button(ui, root, "Cards.Ban", "금지\n" + offer.Bans + "회 남음", () => arm(ReplayCommandKind.BanCard), 22 + footerWidth, height - 100, footerWidth, 52, offer.Bans > 0);
            ban.GetComponentInChildren<Text>().fontSize = 13; actions.Add(ban, offer.Bans > 0);
            var canLock = offer.Locks > 0 && offer.Cards.Any(id => id != offer.LockedCardId);
            var pin = Button(ui, root, "Cards.Lock", "고정\n" + offer.Locks + "회 남음", () => arm(ReplayCommandKind.LockCard), 30 + footerWidth * 2, height - 100, footerWidth, 52, canLock);
            pin.GetComponentInChildren<Text>().fontSize = 13; actions.Add(pin, canLock);
            if (hasAd)
            {
                reroll.gameObject.SetActive(false);
                var button = PortraitBmPanels.OptionalAdButton(ui, root, ad, rewardedReroll, 14, height - 100, footerWidth);
                button.name = "Cards.OptionalAd"; button.GetComponentInChildren<Text>().fontSize = 13; actions.Add(button, ad.Enabled);
            }
            return actions;
        }

        static string CardEvolutionLine(OfferedCardDetail card, FirstPlayableFrame frame, IReadOnlyDictionary<string, ContentDisplay> displays)
        {
            var lines = new List<string>();
            foreach (var evolution in frame.Evolutions)
            {
                if (!card.EvolutionIds.Contains(evolution.Id)) continue;
                var partners = new List<string>();
                foreach (var requirement in evolution.Requirements)
                    if (requirement.EquipmentId != card.Id) partners.Add(displays[requirement.EquipmentId].DisplayName);
                lines.Add((partners.Count > 0 ? string.Join(" + ", partners) + "와 함께 → " : "진화 → ") + displays[evolution.Id].DisplayName);
            }
            return string.Join("\n", lines);
        }

        static RectTransform Overlay(UiShell ui, string name)
        {
            var root = UiShell.Rect(name, ui.Content); UiShell.Stretch(root);
            var shade = root.gameObject.AddComponent<Image>(); shade.color = new Color32(26,23,18,220); shade.raycastTarget = true;
            return root;
        }

        public static void Pause(UiShell ui, RunFrame frame, FirstPlayableFrame first, ContentCatalog catalog,
            IReadOnlyDictionary<string, ContentDisplay> displays, Action resume, Action settings, Action requestAbandon,
            RunPreferences preferences = null, Action preferencesChanged = null)
        {
            var root = Overlay(ui, "Portrait.Pause"); var width = Width(ui); var height = Height(ui);
            var panel = PortraitWidgets.Place("Pause parchment", root, 10, 52, width - 20, height - 156); ui.Surface(panel, "ui.panel").pixelsPerUnitMultiplier = 44f / 18;
            Text(ui, root, "일시정지", 32, 72, 180, 24);
            var seconds = frame.Tick / frame.TickRate;
            Text(ui, root, (seconds / 60) + ":" + (seconds % 60).ToString("00") + " · 레벨 " + frame.Level, 200, 84, width - 232, 12);
            var body = Scroll(ui, root, 116, 178); float y = 0; var innerWidth = width - 64;
            var viewport = (RectTransform)body.parent;
            viewport.offsetMin = new Vector2(32, viewport.offsetMin.y); viewport.offsetMax = new Vector2(-32, viewport.offsetMax.y);
            y = Loadout(ui, body, "무기", frame.Equipment.Where(e => catalog.Weapons.ContainsKey(e.Id)).Select(e => e.Id).ToArray(), 5, displays, innerWidth, y, frame);
            y = Loadout(ui, body, "도구", frame.Equipment.Where(e => catalog.Tools.ContainsKey(e.Id)).Select(e => e.Id).ToArray(), 4, displays, innerWidth, y, frame);
            y = Loadout(ui, body, "특허장", first.Charters, 4, displays, innerWidth, y, frame);
            y = EvolutionProgress(ui, body, frame, first, catalog, displays, innerWidth, y);
            if (preferences != null)
            {
                y += 8;
                var musicBeforeMute = preferences.MusicVolume > 0 ? preferences.MusicVolume : .55f;
                var effectsBeforeMute = preferences.EffectsVolume > 0 ? preferences.EffectsVolume : .8f;
                InlinePreference(ui, body, "Pause.Sound", "소리", preferences.MusicVolume > 0 || preferences.EffectsVolume > 0, value =>
                {
                    if (!value) { musicBeforeMute = preferences.MusicVolume; effectsBeforeMute = preferences.EffectsVolume; }
                    preferences.MusicVolume = value ? musicBeforeMute : 0;
                    preferences.EffectsVolume = value ? effectsBeforeMute : 0; preferencesChanged?.Invoke();
                }, 0, y, innerWidth); y += 44;
                InlinePreference(ui, body, "Pause.Haptics", "진동", preferences.Haptics, value => { preferences.Haptics = value; preferencesChanged?.Invoke(); }, 0, y, innerWidth); y += 44;
                InlinePreference(ui, body, "Pause.Shake", "화면 흔들림", preferences.Shake, value => { preferences.Shake = value; preferencesChanged?.Invoke(); }, 0, y, innerWidth); y += 44;
                InlinePreference(ui, body, "Pause.DamageNumbers", "피해 숫자", preferences.DamageNumbers, value => { preferences.DamageNumbers = value; preferencesChanged?.Invoke(); }, 0, y, innerWidth); y += 44;
            }
            body.sizeDelta = new Vector2(0, y);
            Button(ui, root, "Pause.Settings", "상세 설정", settings, 32, height - 164, width - 64, 44);
            var abandonWidth = (width - 36) / 2.6f;
            Button(ui, root, "Pause.Abandon", "포기", requestAbandon, 14, height - 88, abandonWidth, 60);
            Button(ui, root, "Pause.Continue", "계속", resume, 22 + abandonWidth, height - 88, width - 36 - abandonWidth, 60, true, "ui.button.primary");
        }

        static void InlinePreference(UiShell ui, Transform parent, string name, string label, bool value, Action<bool> changed, float x, float y, float width)
        {
            var line = PortraitWidgets.Place(name + ".Divider", parent, x, y, width, 1); line.gameObject.AddComponent<Image>().color = new Color32(220,203,164,255);
            PortraitWidgets.Label(ui, parent, label, x, y, width - 64, 44, 14);
            var target = PortraitWidgets.Place(name, parent, x + width - 56, y, 56, 44);
            var hit = target.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var pill = PortraitWidgets.Place("State", target, 0, 7, 56, 30).gameObject.AddComponent<PortraitPill>(); pill.raycastTarget = false;
            var text = PortraitWidgets.Label(ui, target, value ? "켬" : "끔", 0, 7, 56, 30, 12); text.alignment = TextAnchor.MiddleCenter;
            Action refresh = () => { pill.color = value ? new Color32(44,79,150,255) : new Color32(239,227,198,255); text.color = value ? Color.white : new Color32(90,68,40,255); text.text = value ? "켬" : "끔"; };
            var button = target.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
            button.onClick.AddListener(() => { value = !value; refresh(); changed(value); }); refresh();
        }

        static float EvolutionProgress(UiShell ui, RectTransform body, RunFrame frame, FirstPlayableFrame first,
            ContentCatalog catalog, IReadOnlyDictionary<string, ContentDisplay> displays, float width, float y)
        {
            if (first.Evolutions.Count == 0) return y;
            Text(ui, body, "진화 진행", 0, y, width, PortraitTokens.Heading); y += 32;
            foreach (var evolution in first.Evolutions)
            {
                var relatedCard = new OfferedCardDetail(evolution.Id, "epic", 0, 0, 0, new[] { evolution.Id });
                var clue = catalog.WaveRuntime != null
                    ? UiRunPanels.WaveEvolutionClues(relatedCard, first, catalog.WaveRuntime, displays, true)
                    : displays[evolution.Id].DisplayName + " · " + (evolution.Activated ? "완료" : evolution.Available ? "선택 가능" : "조건");
                var levels = new List<string>();
                foreach (var requirement in evolution.Requirements)
                {
                    var equipment = frame.Equipment.FirstOrDefault(e => e.Id == requirement.EquipmentId);
                    var level = equipment?.Level ?? 0;
                    levels.Add(displays[requirement.EquipmentId].DisplayName + " · 레벨 " + level + "/" + requirement.MinimumLevel);
                }
                var panel = PortraitWidgets.Place("Evolution:" + evolution.Id, body, 0, y, width, 48);
                ui.Surface(panel, "ui.hint").pixelsPerUnitMultiplier = 2;
                var label = Text(ui, panel, clue + "\n" + string.Join(" · ", levels), 12, 8, width - 24, 12);
                var panelHeight = label.rectTransform.rect.height + 16;
                panel.sizeDelta = new Vector2(width, panelHeight); y += panelHeight + 12;
            }
            return y;
        }

        static float Loadout(UiShell ui, RectTransform body, string title, IReadOnlyList<string> ids, int count,
            IReadOnlyDictionary<string, ContentDisplay> displays, float width, float y, RunFrame frame = null)
        {
            var caption = Text(ui, body, title + " " + ids.Count + "/" + count, 0, y, width, 13);
            caption.color = title == "무기" ? new Color32(44,79,150,255) : title == "도구" ? new Color32(79,107,46,255) : new Color32(107,62,143,255);
            y += 24;
            var slotWidth = (width - 24) / 5; var rowHeight = title == "특허장" ? 44 : 48;
            for (var i = 0; i < count; i++)
            {
                var slot = PortraitWidgets.Place(title + ".Slot:" + i, body, i * (slotWidth + 6), y, slotWidth, rowHeight);
                var surface = ui.Surface(slot, "ui.slot"); surface.pixelsPerUnitMultiplier = 6;
                if (i >= ids.Count) { surface.color = new Color(1,1,1,.22f); continue; }
                var id = ids[i]; var icon = ui.Icon(slot, id, 32); PortraitWidgets.Position(icon.rectTransform, (slotWidth - 32) / 2, (rowHeight - 32) / 2, 32, 32);
                var equipment = frame?.Equipment.FirstOrDefault(e => e.Id == id);
                if (equipment == null) continue;
                var badge = PortraitWidgets.Label(ui, slot, equipment.Level.ToString(), slotWidth - 18, rowHeight - 18, 16, 18, 11); badge.color = Color.white; badge.alignment = TextAnchor.MiddleRight;
                var shadow = badge.gameObject.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(0,-1);
            }
            return y + rowHeight + 10;
        }

        public static void Settings(UiShell ui, RunPreferences preferences, AimMode aim, Action preferencesChanged,
            Action<AimMode> changeAim, Action back, bool showFeel = true, Action fakeAds = null, Action<bool> estatePrototype = null)
        {
            var root = Root(ui, "Portrait.Settings"); var width = Width(ui); var height = Height(ui);
            Text(ui, root, "설정", 12, 12, width - 24, PortraitTokens.Title);
            var body = Scroll(ui, root, 58, 76); var innerWidth = width - 24; float y = 0;
            Volume(ui, body, "배경음", preferences.MusicVolume, v => { preferences.MusicVolume = v; preferencesChanged(); }, innerWidth, y); y += 86;
            Volume(ui, body, "효과음", preferences.EffectsVolume, v => { preferences.EffectsVolume = v; preferencesChanged(); }, innerWidth, y); y += 86;
            Toggle(ui, body, "진동", preferences.Haptics, v => { preferences.Haptics = v; preferencesChanged(); }, innerWidth, ref y);
            Toggle(ui, body, "화면 흔들림", preferences.Shake, v => { preferences.Shake = v; preferencesChanged(); }, innerWidth, ref y);
            Toggle(ui, body, "피해 숫자", preferences.DamageNumbers, v => { preferences.DamageNumbers = v; preferencesChanged(); }, innerWidth, ref y);
            Text(ui, body, "자동 조준", 0, y, innerWidth, PortraitTokens.Heading); y += 32;
            Button move = null, nearest = null;
            Action<AimMode> selectAim = value =>
            {
                move.interactable = value != AimMode.Movement; nearest.interactable = value != AimMode.NearestEnemy;
                changeAim(value);
            };
            move = Button(ui, body, "Settings.AimMovement", "이동 방향", () => selectAim(AimMode.Movement), 0, y, (innerWidth - 8) / 2, 48, aim != AimMode.Movement);
            nearest = Button(ui, body, "Settings.AimNearest", "가까운 적", () => selectAim(AimMode.NearestEnemy), (innerWidth + 8) / 2, y, (innerWidth - 8) / 2, 48, aim != AimMode.NearestEnemy); y += 64;
            if (showFeel)
            {
                y += Text(ui, body, "걷기와 피격 실험 · 다음 출정부터", 0, y, innerWidth, PortraitTokens.Heading).rectTransform.rect.height + 12;
                Toggle(ui, body, "빠른 걸음 ×1.3", preferences.FeelSpeed, v => { preferences.FeelSpeed = v; preferencesChanged(); }, innerWidth, ref y);
                Toggle(ui, body, "접촉 즉시 피격 · 잠깐 무적과 밀려남", preferences.FeelContact, v => { preferences.FeelContact = v; preferencesChanged(); }, innerWidth, ref y);
                Toggle(ui, body, "체력 100 · 한 대 5~15", preferences.FeelHealth, v => { preferences.FeelHealth = v; preferencesChanged(); }, innerWidth, ref y);
                Toggle(ui, body, "피격 잔상 · 붉은 테 · 피해 숫자", preferences.FeelFeedback, v => { preferences.FeelFeedback = v; preferencesChanged(); }, innerWidth, ref y);
            }
            if(fakeAds != null) { Button(ui, body, "Settings.FakeAds", "개발 광고 시험", fakeAds, 0, y, innerWidth); y += 60; }
            if (estatePrototype != null) Toggle(ui, body, "영지 지도 시제품 (개발)", preferences.EstatePrototype, estatePrototype, innerWidth, ref y);
            body.sizeDelta = new Vector2(0, y + 8);
            Button(ui, root, "Settings.Back", "돌아가기", back, 12, height - 60, width - 24);
        }

        static void Toggle(UiShell ui, RectTransform parent, string label, bool value, Action<bool> changed, float width, ref float y)
        {
            Button button = null;
            button = Button(ui, parent, "Settings." + label, label + (value ? " · 켜짐" : " · 꺼짐"), () =>
            {
                value = !value; button.GetComponentInChildren<Text>().text = label + (value ? " · 켜짐" : " · 꺼짐"); changed(value);
            }, 0, y, width, 56);
            y += 64;
        }

        static void Volume(UiShell ui, RectTransform parent, string label, float value, Action<float> changed, float width, float y)
        {
            var slider = ui.Slider(parent, label, value, changed);
            var column = (RectTransform)slider.transform.parent; PortraitWidgets.Position(column, 0, y, width, 78);
            var title = column.GetComponentInChildren<Text>(); title.fontSize = PortraitTokens.Body;
            var titleLayout = title.GetComponent<LayoutElement>(); titleLayout.minHeight = 24; titleLayout.preferredHeight = 24;
            var sliderLayout = slider.GetComponent<LayoutElement>(); sliderLayout.minHeight = 44; sliderLayout.preferredHeight = 44;
            slider.handleRect.sizeDelta = Vector2.one * 36;
        }

        public static void ConfirmAbandon(UiShell ui, Action confirm, Action cancel)
        {
            var root = Root(ui, "Portrait.ConfirmAbandon"); var width = Width(ui); var height = Height(ui);
            Text(ui, root, "출정을 포기하시겠어요?", 24, height * .3f, width - 48, PortraitTokens.Title);
            Text(ui, root, "이번 출정은 포기로 정산됩니다.", 24, height * .3f + 76, width - 48);
            Button(ui, root, "Abandon.Confirm", "포기하고 정산", confirm, 12, height - 116, width - 24);
            Button(ui, root, "Abandon.Cancel", "돌아가기", cancel, 12, height - 60, width - 24);
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortraitSettingsGear : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear(); var center = rectTransform.rect.center;
            var radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 2;
            var cream = new Color32(246,235,210,255);
            Disc(vertices, center, radius, new Color32(0,0,0,64));
            Ring(vertices, center, radius - .5f, 1, new Color32(246,235,210,102));
            Ring(vertices, center, 3.2f, 1.8f, cream);
            for (var i = 0; i < 8; i++)
            {
                var angle = Mathf.PI * i / 4; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var perpendicular = new Vector2(-direction.y, direction.x) * .9f;
                var inner = center + direction * 6.5f; var outer = center + direction * 9.5f; var start = vertices.currentVertCount;
                vertices.AddVert(inner - perpendicular, cream, Vector2.zero); vertices.AddVert(inner + perpendicular, cream, Vector2.zero);
                vertices.AddVert(outer + perpendicular, cream, Vector2.zero); vertices.AddVert(outer - perpendicular, cream, Vector2.zero);
                vertices.AddTriangle(start, start + 1, start + 2); vertices.AddTriangle(start, start + 2, start + 3);
            }
        }
        static void Disc(VertexHelper vertices, Vector2 center, float radius, Color32 shade)
        {
            var start = vertices.currentVertCount; vertices.AddVert(center, shade, Vector2.zero);
            for (var i = 0; i <= 48; i++)
            {
                var angle = i * Mathf.PI * 2 / 48;
                vertices.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, shade, Vector2.zero);
                if (i > 0) vertices.AddTriangle(start, start + i, start + i + 1);
            }
        }
        static void Ring(VertexHelper vertices, Vector2 center, float radius, float thickness, Color32 shade)
        {
            var start = vertices.currentVertCount;
            for (var i = 0; i <= 48; i++)
            {
                var angle = i * Mathf.PI * 2 / 48; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices.AddVert(center + direction * radius, shade, Vector2.zero); vertices.AddVert(center + direction * (radius - thickness), shade, Vector2.zero);
                if (i == 0) continue;
                var previous = start + (i - 1) * 2; vertices.AddTriangle(previous, previous + 1, previous + 2); vertices.AddTriangle(previous + 1, previous + 3, previous + 2);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortraitPill : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear(); var rect = rectTransform.rect; var radius = rect.height / 2;
            vertices.AddVert(rect.center, color, Vector2.zero);
            const int segments = 24;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2 / segments;
                var center = Mathf.Cos(angle) >= 0 ? rect.xMax - radius : rect.xMin + radius;
                vertices.AddVert(new Vector2(center + Mathf.Cos(angle) * radius, rect.center.y + Mathf.Sin(angle) * radius), color, Vector2.zero);
                if (i > 0) vertices.AddTriangle(0, i, i + 1);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortraitTitleBackdrop : MaskableGraphic
    {
        static readonly float[] Stops = { 0, .34f, .52f, .60f, .61f, .78f, 1 };
        static readonly Color32[] Colors = { new Color32(27,35,64,255), new Color32(58,63,92,255), new Color32(142,110,90,255), new Color32(199,154,91,255), new Color32(110,138,69,255), new Color32(76,107,51,255), new Color32(46,66,32,255) };
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear(); var rect = rectTransform.rect;
            for (var i = 0; i < Stops.Length - 1; i++)
            {
                var start = vertices.currentVertCount; var top = rect.yMax - rect.height * Stops[i]; var bottom = rect.yMax - rect.height * Stops[i + 1];
                vertices.AddVert(new Vector2(rect.xMin, top), Colors[i], Vector2.zero); vertices.AddVert(new Vector2(rect.xMax, top), Colors[i], Vector2.zero);
                vertices.AddVert(new Vector2(rect.xMax, bottom), Colors[i + 1], Vector2.zero); vertices.AddVert(new Vector2(rect.xMin, bottom), Colors[i + 1], Vector2.zero);
                vertices.AddTriangle(start, start + 1, start + 2); vertices.AddTriangle(start, start + 2, start + 3);
            }
            Ellipse(vertices, rect, -60, 470, 520, 160, new Color32(91,122,58,255));
            Ellipse(vertices, rect, -120, 520, 360, 140, new Color32(77,108,49,255));
            Ellipse(vertices, rect, 200, 540, 320, 130, new Color32(69,98,44,255));
            Ellipse(vertices, rect, 162, 598, 66, 14, new Color32(0,0,0,85));
        }
        static void Ellipse(VertexHelper vertices, Rect rect, float x, float y, float width, float height, Color32 shade)
        {
            var scale = new Vector2(rect.width / 390, rect.height / 844);
            var center = new Vector2(rect.xMin + (x + width / 2) * scale.x, rect.yMax - (y + height / 2) * scale.y);
            var start = vertices.currentVertCount; vertices.AddVert(center, shade, Vector2.zero);
            const int segments = 48;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2 / segments;
                vertices.AddVert(center + new Vector2(Mathf.Cos(angle) * width * scale.x / 2, Mathf.Sin(angle) * height * scale.y / 2), shade, Vector2.zero);
                if (i > 0) vertices.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }

}
