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
        public const string Glyphs = "휴경의 왕국 돌본 땅에 다시 봄이 깃든다 화면을 눌러 시작 화면을 눌러 장원으로 설정 성장 선택 카드 선택 금지 고정됨 고정 다시 뽑기 진화 선택 잠시 멈춤 무기 도구 특허장 빈 칸 계속하기 출정을 포기하시겠어요 이번 출정은 포기로 정산됩니다 돌아가기 포기하고 정산 배경음 효과음 진동 화면 흔들림 피해 숫자 자동 조준 이동 방향 가까운 적 걷기와 피격 실험 다음 출정부터 빠른 걸음 접촉 즉시 피격 잠깐 무적과 밀려남 체력 한 대 피격 잔상 붉은 테 걷기 부위별 애니메이션 새 선택 보유 레벨 진화 진행 완료 조건 켜짐 꺼짐 출정 포기 B ×1.3 100 5~15 % ?";
        const float Gap = 8, CardWidth = 112;
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
        static Button Button(UiShell ui, Transform parent, string name, string label, Action action, float x, float y, float width, float height = PortraitTokens.Action, bool enabled = true)
        {
            Button button = null;
            button = PortraitWidgets.Button(ui, parent, label, () => { if (button != null && button.interactable) action?.Invoke(); }, x, y, width, height, enabled);
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
            var root = Root(ui, "Portrait.Title"); var width = Width(ui); var height = Height(ui);
            var background = Resources.Load<Texture2D>("Fallow/title-background");
            if (background != null)
            {
                var art = UiShell.Rect("Kingdom illustration", root); UiShell.Stretch(art);
                var image = art.gameObject.AddComponent<RawImage>(); image.texture = background; image.raycastTarget = false;
                var fit = art.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectRatio = (float)background.width / background.height; fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            }
            var hit = UiShell.Rect("Title.Enter", root); UiShell.Stretch(hit);
            var hitImage = hit.gameObject.AddComponent<Image>(); hitImage.color = Color.clear;
            var tap = hit.gameObject.AddComponent<Button>(); tap.targetGraphic = hitImage; tap.onClick.AddListener(() => enter?.Invoke());
            var logo = Resources.Load<Texture2D>("Fallow/title-logo");
            if (logo != null)
            {
                var logoRect = PortraitWidgets.Place("Kingdom logo", root, 30, height * .13f, width - 60, 140);
                var image = logoRect.gameObject.AddComponent<RawImage>(); image.texture = logo; image.raycastTarget = false;
                var fit = logoRect.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectRatio = (float)logo.width / logo.height; fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            }
            else Text(ui, root, "휴경의 왕국", 24, height * .2f, width - 48, PortraitTokens.Title).alignment = TextAnchor.MiddleCenter;
            TitleCaption(ui, root, "Title.Tagline", "돌본 땅에 다시 봄이 깃든다", 28, height * .39f, width - 56, 48);
            TitleCaption(ui, root, "Title.Prompt", firstRun ? "화면을 눌러 시작" : "화면을 눌러 장원으로", 44, height - 132, width - 88, 56);
            Button(ui, root, "Title.Settings", "설정", settings, width - 68, 12, 56);
        }
        static void TitleCaption(UiShell ui, Transform parent, string name, string text, float x, float y, float width, float height)
        {
            var plate = PortraitWidgets.Place(name, parent, x, y, width, height);
            var paper = ui.Surface(plate, "ui.hint");
            paper.color = UiTokens.FallowCharcoal; paper.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
            paper.raycastTarget = false;
            var label = PortraitWidgets.Label(ui, plate, text, 8, 4, width - 16, height - 8, PortraitTokens.Heading);
            label.color = GameVisualTokens.Attack; label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false; label.resizeTextForBestFit = false;
        }

        public static PortraitCardActions Cards(UiShell ui, RunFrame frame, FirstPlayableFrame first, CardOfferView offer,
            ContentCatalog catalog, IReadOnlyDictionary<string, ContentDisplay> displays, Action<ReplayCommandKind, string> send)
        {
            var root = Root(ui, "Portrait.Cards"); var width = Width(ui); var height = Height(ui); var actions = new PortraitCardActions();
            Text(ui, root, "레벨 " + frame.Level + " · 성장 선택", 12, 12, width - 24, PortraitTokens.Title);
            Text(ui, root, "무기와 도구를 함께 골라 영지를 키우세요.", 12, 48, width - 24);
            var body = Scroll(ui, root, 82, 76);
            var cardWidth = Mathf.Min(CardWidth, (width - 24 - Gap * 2) / 3);
            var left = Mathf.Max(0, (width - 24 - cardWidth * 3 - Gap * 2) * .5f);
            var maxHeight = 410f;
            for (var index = 0; index < offer.Cards.Count; index++)
            {
                var id = offer.Cards[index]; var detail = first.Cards.First(c => c.Id == id); var display = displays[id];
                var x = left + index * (cardWidth + Gap); var container = PortraitWidgets.Place("Card:" + id, body, x, 0, cardWidth, 410);
                var chooseRect = PortraitWidgets.Place("Card.Choose:" + id, container, 0, 0, cardWidth, 310);
                var category = catalog.Weapons.ContainsKey(id) ? "rare" : catalog.Tools.ContainsKey(id) ? "common" : "epic";
                var surface = ui.Surface(chooseRect, "ui.card." + category); surface.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier; surface.raycastTarget = true;
                var choose = chooseRect.gameObject.AddComponent<Button>(); choose.targetGraphic = surface;
                choose.onClick.AddListener(() => { if (choose.interactable) send(ReplayCommandKind.ChooseCard, id); }); actions.Add(choose, true);
                var icon = ui.Icon(chooseRect, id, 40); PortraitWidgets.Position(icon.rectTransform, (cardWidth - 40) * .5f, 12, 40, 40);
                float y = 60;
                y += Text(ui, chooseRect, display.DisplayName, 18, y, cardWidth - 36, 16).rectTransform.rect.height + 4;
                y += Text(ui, chooseRect, catalog.WaveRuntime?.Evolutions.ContainsKey(id) == true ? "진화 선택" : detail.CurrentLevel > 0 ? "보유 레벨 " + detail.CurrentLevel : "새 선택", 18, y, cardWidth - 36).rectTransform.rect.height + 4;
                y += Text(ui, chooseRect, display.EffectDescription, 18, y, cardWidth - 36).rectTransform.rect.height + 8;
                var changes = catalog.WaveRuntime == null ? "" : UiRunPanels.WaveUpgradeChanges(detail, catalog.WaveRuntime, frame.TickRate);
                if (changes.Length > 0) y += Text(ui, chooseRect, changes, 18, y, cardWidth - 36).rectTransform.rect.height + 8;
                var evolution = catalog.WaveRuntime == null ? display.EvolutionHint : UiRunPanels.WaveEvolutionClues(detail, first, catalog.WaveRuntime, displays, true);
                if (evolution.Length > 0)
                {
                    var band = PortraitWidgets.Place("Evolution ribbon", chooseRect, 16, y, cardWidth - 32, 40);
                    var gold = band.gameObject.AddComponent<Image>(); gold.color = new Color32(220, 185, 105, 255); gold.raycastTarget = false;
                    var text = Text(ui, band, evolution, 4, 6, cardWidth - 40);
                    band.sizeDelta = new Vector2(cardWidth - 32, text.rectTransform.rect.height + 12); y += band.rect.height + 8;
                }
                y = Mathf.Max(302, y + 18); chooseRect.sizeDelta = new Vector2(cardWidth, y);
                var ban = Button(ui, container, "Card.Ban:" + id, "금지 · " + offer.Bans, () => send(ReplayCommandKind.BanCard, id), 0, y + 8, cardWidth, 44, offer.Bans > 0); actions.Add(ban, offer.Bans > 0);
                var canLock = offer.Locks > 0 && offer.LockedCardId != id;
                var pin = Button(ui, container, "Card.Lock:" + id, offer.LockedCardId == id ? "고정됨" : "고정 · " + offer.Locks, () => send(ReplayCommandKind.LockCard, id), 0, y + 56, cardWidth, 44, canLock); actions.Add(pin, canLock);
                container.sizeDelta = new Vector2(cardWidth, y + 100); maxHeight = Mathf.Max(maxHeight, y + 100);
            }
            body.sizeDelta = new Vector2(0, maxHeight + 12);
            var reroll = Button(ui, root, "Cards.Reroll", "다시 뽑기 · " + offer.Rerolls, () => send(ReplayCommandKind.RerollCards, null), 12, height - 60, width - 24, 48, offer.Rerolls > 0);
            actions.Add(reroll, offer.Rerolls > 0); return actions;
        }

        public static void Pause(UiShell ui, RunFrame frame, FirstPlayableFrame first, ContentCatalog catalog,
            IReadOnlyDictionary<string, ContentDisplay> displays, Action resume, Action settings, Action requestAbandon)
        {
            var root = Root(ui, "Portrait.Pause"); var width = Width(ui); var height = Height(ui);
            Text(ui, root, "잠시 멈춤", 12, 12, width - 24, PortraitTokens.Title);
            var body = Scroll(ui, root, 58, 180); float y = 0;
            y = Loadout(ui, body, "무기", frame.Equipment.Where(e => catalog.Weapons.ContainsKey(e.Id)).Select(e => e.Id).ToArray(), 5, displays, width - 24, y);
            y = Loadout(ui, body, "도구", frame.Equipment.Where(e => catalog.Tools.ContainsKey(e.Id)).Select(e => e.Id).ToArray(), 4, displays, width - 24, y);
            y = Loadout(ui, body, "특허장", first.Charters, 4, displays, width - 24, y);
            y = EvolutionProgress(ui, body, frame, first, catalog, displays, width - 24, y);
            body.sizeDelta = new Vector2(0, y);
            Button(ui, root, "Pause.Settings", "설정", settings, 12, height - 168, width - 24);
            Button(ui, root, "Pause.Abandon", "출정 포기", requestAbandon, 12, height - 112, width - 24);
            Button(ui, root, "Pause.Continue", "계속하기", resume, 12, height - 56, width - 24);
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
                ui.Surface(panel, "ui.hint").pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                var label = Text(ui, panel, clue + "\n" + string.Join("\n", levels), 16, 16, width - 32);
                var panelHeight = label.rectTransform.rect.height + 32;
                panel.sizeDelta = new Vector2(width, panelHeight); y += panelHeight + 12;
            }
            return y;
        }

        static float Loadout(UiShell ui, RectTransform body, string title, IReadOnlyList<string> ids, int count,
            IReadOnlyDictionary<string, ContentDisplay> displays, float width, float y)
        {
            Text(ui, body, title, 0, y, width, PortraitTokens.Heading); y += 32;
            var slotWidth = (width - (count - 1) * 6) / count; float rowHeight = 82;
            for (var i = 0; i < count; i++)
            {
                var slot = PortraitWidgets.Place(title + ".Slot:" + i, body, i * (slotWidth + 6), y, slotWidth, 82);
                ui.Surface(slot, "ui.slot");
                var label = "빈 칸";
                if (i < ids.Count)
                {
                    var id = ids[i]; label = displays.TryGetValue(id, out var display) ? display.DisplayName : id;
                    var icon = ui.Icon(slot, id, 30); PortraitWidgets.Position(icon.rectTransform, (slotWidth - 30) * .5f, 6, 30, 30);
                }
                var text = Text(ui, slot, label, 10, 40, slotWidth - 20);
                text.color = GameVisualTokens.Attack;
                rowHeight = Mathf.Max(rowHeight, text.rectTransform.rect.height + 50);
            }
            for (var i = body.childCount - count; i < body.childCount; i++)
                ((RectTransform)body.GetChild(i)).sizeDelta = new Vector2(slotWidth, rowHeight);
            return y + rowHeight + 16;
        }

        public static void Settings(UiShell ui, RunPreferences preferences, AimMode aim, Action preferencesChanged,
            Action<AimMode> changeAim, Action back, bool showFeel = true)
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
                Toggle(ui, body, "걷기 B · 부위별 애니메이션", preferences.HeroRigPreview, v => { preferences.HeroRigPreview = v; preferencesChanged(); }, innerWidth, ref y);
            }
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
}
