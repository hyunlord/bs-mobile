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
    public static partial class PortraitMetaPanels
    {
        public static void Departure(UiShell ui, MetaCatalog catalog, MetaState state, ContentCatalog runCatalog,
            IReadOnlyDictionary<string, ContentDisplay> displays, string chapterId, Action<string> selectChapter,
            Action start, Action cards, Action vassals, Action back, bool pendingRun = false)
        {
            var root = Page(ui, "Departure", ""); var width = Width(ui) - 32;
            PortraitRunPanels.RoundIcon(ui, root, "Departure.Back", "‹", back, 16, 50);
            Caption(ui, root, "출정 준비", 70, 50, width - 60, 44, 22);
            var body = Body(ui, root, 106, 112); float y = 0; var chapters = catalog.Chapters.Where(c => c.Index <= 7).OrderBy(c => c.Index).ToArray();
            var step = (width - 44) / 6;
            for (var i = 0; i < chapters.Length; i++)
            {
                var chapter = chapters[i]; ChapterButton(ui, body, chapter, chapter.Id == chapterId, () => selectChapter(chapter.Id), i * step, 0);
            }
            var definition = chapters.First(c => c.Id == chapterId);
            var panel = Plate(ui, body, "Chapter details", "ui.panel", 0, 56, width, 192);
            Caption(ui, panel, "챕터 " + definition.Index + " · " + ChapterName(definition), 18, 14, width - 36, 30, 21, true);
            Caption(ui, panel, definition.Description, 18, 47, width - 36, 38, 13, true);
            string Name(string id) => displays.TryGetValue(id, out var display) ? display.DisplayName : "아직 알려지지 않은 적";
            var enemies = (definition.Index == 1 && runCatalog.WaveRuntime != null ? runCatalog.WaveRuntime.Enemies.Keys.Where(id => id != runCatalog.WaveRuntime.BossId) : definition.EnemyIds).ToArray();
            Caption(ui, panel, "나오는 적", 18, 89, 58, 44, 11, true);
            for (var i = 0; i < Math.Min(3, enemies.Length); i++)
            {
                var slot = Plate(ui, panel, "Chapter enemy:" + enemies[i], "ui.slot", 78 + i * 52, 88, 46, 46);
                var icon = ui.Icon(slot, enemies[i], 36); PortraitWidgets.Position(icon.rectTransform, 5, 5, 36, 36);
            }
            var bossId = definition.Index == 1 && runCatalog.WaveRuntime != null ? runCatalog.WaveRuntime.BossId : definition.BossId;
            Caption(ui, panel, "보스 · " + Name(bossId), 18, 135, width - 36, 23, 13, true);
            Caption(ui, panel, "보상 최대 · " + MetaScreens.Amounts(catalog, definition.RewardCaps), 18, 159, width - 36, 26, 11, true);
            y = 260; Caption(ui, body, "출전 가신", 0, y, width, 26, 16); y += 30;
            var slots = Slots(catalog, state); var cell = (width - 16) / 3;
            for (var i = 0; i < 3; i++)
            {
                var id = i < state.ActiveVassalIds.Length ? state.ActiveVassalIds[i] : null;
                var title = i >= slots ? "잠김 · 병영 성장" : id == null ? "가신 선택" : catalog.Vassals.First(v => v.Id == id).Name;
                var slot = Plate(ui, body, "Vassal slot:" + i, "ui.slot", i * (cell + 8), y, cell, 112);
                if (id != null && i < slots) Art(ui, slot, catalog.Vassals.First(v => v.Id == id).ArtRole, 8, 4, cell - 16, 90);
                Caption(ui, slot, title, 6, 77, cell - 12, 28, 11, false, TextAnchor.MiddleCenter);
                Hit(body, "Departure.VassalSlot:" + i, vassals, i * (cell + 8), y, cell, 112, i < slots);
            }
            y += 120;
            Caption(ui, body, "가신 능력 보너스는 이번 출정에 적용되지 않습니다.", 0, y, width, 28, 11); y += 30;
            Caption(ui, body, "시작 장비 · 영웅 기본", 0, y, width, 26, 16); y += 30;
            var equipment = new[] { "철날 검", "씨앗 자루" }; var equipmentArt = new[] { "e-iron_blade", "e-seed_bag" };
            for (var i = 0; i < 2; i++)
            {
                var half = (width - 8) / 2; var slot = Plate(ui, body, "Starting equipment:" + i, "ui.slot", i * (half + 8), y, half, 64);
                PortraitWidgets.Art(ui, slot, equipmentArt[i], 10, 15, 34, 34);
                Caption(ui, slot, i == 0 ? "무기" : "도구", 50, 7, half - 56, 21, 11);
                Caption(ui, slot, equipment[i], 50, 28, half - 56, 27, 14);
            }
            y += 70; ActionButton(ui, body, "Departure.CardPool", "이번 판 카드 보기", cards, 0, y, width, true, 44); y += 50; End(body, y);
            var available = definition.Index == 1 && definition.Index <= state.HighestClearedChapter + 1 && !pendingRun && state.PendingRun == null;
            ActionButton(ui, root, "Departure.Start", available ? "출정 시작" : "아직 출정할 수 없습니다", start, 20, Height(ui) - 102, Width(ui) - 40, available, 70, "ui.button.destructive");
        }

        static void ChapterButton(UiShell ui, Transform parent, MetaChapter chapter, bool selected, Action choose, float x, float y)
        {
            var rect = PortraitWidgets.Place("Departure.Chapter:" + chapter.Id, parent, x, y, 44, 44);
            // Same authored circular ink bounds as the in-game joystick, without its transparent atlas margins.
            var source = ArtCatalog.Load().ResolveRole("ui.joystick.base").Sprite;
            var bounds = new Rect(source.rect.x + 4, source.rect.y + 36, 174, 174);
            var paper = rect.gameObject.AddComponent<RawImage>(); paper.texture = source.texture;
            paper.uvRect = new Rect(bounds.x / source.texture.width, bounds.y / source.texture.height,
                bounds.width / source.texture.width, bounds.height / source.texture.height);
            paper.color = selected ? new Color32(235, 197, 98, 255) : Color.white;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
            button.onClick.AddListener(() => { if (button.interactable) choose(); });
            var number = PortraitWidgets.Label(ui, rect, chapter.Index.ToString(), 4, 4, 36, 36, 16);
            number.alignment = TextAnchor.MiddleCenter; number.color = GameVisualTokens.Ink; number.raycastTarget = false;
        }

        public static void CardPool(UiShell ui, ContentCatalog runCatalog, IReadOnlyDictionary<string, ContentDisplay> displays, Action back)
        {
            var root = Page(ui, "CardPool", "이번 판 카드 보기"); var width = Width(ui) - 36; var body = Body(ui, root); float y = 0;
            var ids = runCatalog.WaveRuntime == null ? Array.Empty<string>() : runCatalog.WaveRuntime.Gear.Keys.Concat(runCatalog.WaveRuntime.Items.Keys).Concat(runCatalog.WaveRuntime.Evolutions.Keys).ToArray();
            foreach (var id in ids)
            {
                if (!displays.TryGetValue(id, out var display)) continue;
                var icon = ui.Icon(body, id, 36); PortraitWidgets.Position(icon.rectTransform, 0, y, 36, 36);
                var labelHeight = Label(ui, body, display.DisplayName, 44, y, width - 44, 18); y += Mathf.Max(42, labelHeight);
                y += Label(ui, body, display.EffectDescription, 0, y, width) + 18;
            }
            End(body, y); ActionButton(ui, root, "CardPool.Back", "돌아가기", back, 18, Height(ui) - 62, width);
        }
    }
}
