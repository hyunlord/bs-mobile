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
            var root = Page(ui, "Departure", "출정 준비"); var width = Width(ui) - 36;
            ActionButton(ui, root, "Departure.Back", "뒤로", back, Width(ui) - 74, 10, 56);
            var body = Body(ui, root); float y = 0; var chapters = catalog.Chapters.Where(c => c.Index <= 7).OrderBy(c => c.Index).ToArray();
            for (var i = 0; i < chapters.Length; i++)
            {
                var chapter = chapters[i]; var selected = chapter.Id == chapterId;
                ChapterButton(ui, body, chapter, selected, () => selectChapter(chapter.Id), i % 4 * 58, i / 4 * 56);
            }
            y = Mathf.Ceil(chapters.Length / 4f) * 56 + 8;
            var definition = chapters.First(c => c.Id == chapterId);
            y += Label(ui, body, ChapterName(definition), 0, y, width, 22) + 4;
            y += Label(ui, body, definition.Index == 1 ? definition.Description : "앞으로 만날 곳 · " + definition.Description, 0, y, width) + 8;
            foreach (var terrain in definition.Terrain.Take(3))
            { Art(ui, body, terrain.ArtRole, 0, y, width, 90); y += 98; }
            string Name(string id) => displays.TryGetValue(id, out var display) ? display.DisplayName : "아직 알려지지 않은 적";
            var enemies = definition.Index == 1 && runCatalog.WaveRuntime != null ? runCatalog.WaveRuntime.Enemies.Keys.Where(id => id != runCatalog.WaveRuntime.BossId) : definition.EnemyIds;
            y += Label(ui, body, "적 · " + string.Join(" · ", enemies.Select(Name)), 0, y, width) + 8;
            var bossId = definition.Index == 1 && runCatalog.WaveRuntime != null ? runCatalog.WaveRuntime.BossId : definition.BossId;
            y += Label(ui, body, "보스 · " + Name(bossId), 0, y, width, 16) + 8;
            y += Label(ui, body, "재료 상한 · " + MetaScreens.Amounts(catalog, definition.RewardCaps), 0, y, width) + 12;
            y += Label(ui, body, "동행 기록", 0, y, width, 18) + 4;
            var slots = Slots(catalog, state); var cell = (width - 16) / 3;
            for (var i = 0; i < 3; i++)
            {
                var id = i < state.ActiveVassalIds.Length ? state.ActiveVassalIds[i] : null;
                var title = i >= slots ? "잠김" : id == null ? "가신 선택" : catalog.Vassals.First(v => v.Id == id).Name;
                if (id != null && i < slots) Art(ui, body, catalog.Vassals.First(v => v.Id == id).ArtRole, i * (cell + 8), y, cell, 82);
                ActionButton(ui, body, "Departure.VassalSlot:" + i, title, vassals, i * (cell + 8), y + 86, cell, i < slots, 56);
            }
            y += 152;
            if (slots < 3) y += Label(ui, body, "다음 자리 · 병영 레벨 " + ((slots - catalog.Economy.BaseVassalSlots + 1) * catalog.Economy.BarracksLevelsPerSlot), 0, y, width);
            y += Label(ui, body, "이번 출정에서는 가신 능력 보너스가 적용되지 않습니다.", 0, y, width) + 12;
            y += Label(ui, body, "기본 장비 · 철날 검 + 씨앗 자루", 0, y, width, 16) + 8;
            ActionButton(ui, body, "Departure.CardPool", "이번 판 카드 보기", cards, 0, y, width); y += 56; End(body, y);
            var available = definition.Index == 1 && definition.Index <= state.HighestClearedChapter + 1 && !pendingRun && state.PendingRun == null;
            ActionButton(ui, root, "Departure.Start", available ? "출정 시작" : "아직 출정할 수 없습니다", start, 18, Height(ui) - 62, width, available);
        }

        static void ChapterButton(UiShell ui, Transform parent, MetaChapter chapter, bool selected, Action choose, float x, float y)
        {
            var rect = PortraitWidgets.Place("Departure.Chapter:" + chapter.Id, parent, x, y, 48, 48);
            // Same authored circular ink bounds as the in-game joystick, without its transparent atlas margins.
            var source = ArtCatalog.Load().ResolveRole("ui.joystick.base").Sprite;
            var bounds = new Rect(source.rect.x + 4, source.rect.y + 36, 174, 174);
            var paper = rect.gameObject.AddComponent<RawImage>(); paper.texture = source.texture;
            paper.uvRect = new Rect(bounds.x / source.texture.width, bounds.y / source.texture.height,
                bounds.width / source.texture.width, bounds.height / source.texture.height);
            paper.color = selected ? new Color32(235, 197, 98, 255) : Color.white;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
            button.onClick.AddListener(() => { if (button.interactable) choose(); });
            var number = PortraitWidgets.Label(ui, rect, chapter.Index.ToString(), 6, 6, 36, 36, 18);
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
