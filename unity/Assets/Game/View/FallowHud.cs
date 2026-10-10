using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public sealed class FallowHud
    {
        readonly UiShell ui;
        readonly ContentCatalog catalog;
        readonly RectTransform root, hpFill, xpFill;
        readonly RectTransform footer;
        readonly Text weaponCaption,toolCaption;
        RectTransform[] topParts;
        Vector2[] topPositions;
        Vector2 layoutSize;
        float layoutScale;
        readonly Text health, level, timer;
        readonly RectTransform[] slots = new RectTransform[9];
        readonly string[] shown = new string[9];
        readonly Text[] ranks = new Text[9];
        readonly List<EquipmentView> weapons = new List<EquipmentView>(16);
        readonly List<EquipmentView> tools = new List<EquipmentView>(16);
        readonly BufferedHudText healthValue, levelValue, timerValue;
        readonly BufferedHudText[] rankValues = new BufferedHudText[9];
        readonly Dictionary<string, Image>[] icons = new Dictionary<string, Image>[9];
        public float ReservedTopPixels => root.rect.height * ui.Canvas.scaleFactor;

        public FallowHud(UiShell ui, ContentCatalog catalog, Action pause)
        {
            this.ui = ui;
            this.catalog = catalog;
            root = UiShell.Rect("HUD", ui.Content);
            root.anchorMin = new Vector2(0, 1); root.anchorMax = Vector2.one;
            root.pivot = new Vector2(.5f, 1); root.sizeDelta = new Vector2(0, UiTokens.FallowHudHeight);
            var place = Rect("Location", root, 20, 12, 256, 52);
            ui.Surface(place, "ui.hint").pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
            Label(place, "새봄의 터", UiTokens.Heading, 28, 0, 204, 52, GameVisualTokens.Ink);
            Label(root, "휴경의 왕국", UiTokens.Caption, 32, 66, 260, 28, GameVisualTokens.Attack, true);
            var vitals = Rect("Vitals", root, 310, 20, 334, 68);
            hpFill = Bar(vitals, "Health", "ui.hint", "ui.hint", 0, 318, 26);
            hpFill.GetComponent<Image>().color = GameVisualTokens.Attack;
            health = Label(vitals, "", UiTokens.Caption, 8, 0, 302, 26, GameVisualTokens.Ink);
            health.alignment = TextAnchor.MiddleRight;
            xpFill = Bar(vitals, "Experience", "ui.hint", "ui.hint", 36, 212, 12);
            xpFill.GetComponent<Image>().color = GameVisualTokens.Ally;
            level = Label(vitals, "", UiTokens.Caption, 220, 28, 112, 28, GameVisualTokens.Attack, true);
            timer = Label(root, "", UiTokens.Heading, 672, 16, 126, 50, GameVisualTokens.Attack, true);
            timer.alignment = TextAnchor.MiddleCenter;
            healthValue = BufferedHudText.Create(health); levelValue = BufferedHudText.Create(level); timerValue = BufferedHudText.Create(timer);
            if(pause != null)
            {
                var button = ui.Button(root, "잠시\n멈춤", pause);
                FallowUiSurface.Button(ui, button, true);
                Position((RectTransform)button.transform, 806, 8, 80, 80);
                button.GetComponentInChildren<Text>().fontSize = UiTokens.Caption;
            }
            footer = UiShell.Rect("Loadout", ui.Content);
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(.5f, 0); footer.sizeDelta = new Vector2(0, UiTokens.FallowFooterHeight);
            var surface = ui.Surface(footer, "ui.panel"); surface.color = UiTokens.FallowCharcoal; surface.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
            weaponCaption=Label(footer, "무기", UiTokens.Caption, 26, 12, 280, 30, GameVisualTokens.Attack);
            toolCaption=Label(footer, "도구", UiTokens.Caption, 508, 12, 280, 30, GameVisualTokens.Attack);
            for(var i = 0; i < slots.Length; i++)
            {
                var x = i < 5 ? 24 + i * 86 : 506 + (i - 5) * 92;
                slots[i] = Rect("Slot " + i, footer, x, 50, UiTokens.FallowSlotSize, UiTokens.FallowSlotSize);
                var slot = ui.Surface(slots[i], "ui.card.common"); slot.color = UiTokens.FallowCharcoal; slot.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
                ranks[i] = Label(slots[i], "", UiTokens.Caption, 45, 48, 27, 26, GameVisualTokens.Attack, true);
                ranks[i].alignment = TextAnchor.LowerRight;
                rankValues[i] = BufferedHudText.Create(ranks[i]);
                icons[i] = new Dictionary<string, Image>(StringComparer.Ordinal);
                foreach (var gear in catalog.WaveRuntime.Gear.Values)
                {
                    var isWeapon = catalog.Weapons.ContainsKey(gear.Id);
                    if ((i < 5) != isWeapon) continue;
                    var icon = ui.Icon(slots[i], gear.Id, UiTokens.FallowSlotSize - 12);
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    icon.rectTransform.anchoredPosition = Vector2.zero; icon.gameObject.SetActive(false);
                    icons[i].Add(gear.Id, icon);
                }
                ranks[i].transform.SetAsLastSibling();
            }
        }

        public void Present(RunFrame frame)
        {
            RefreshLayout();
            var seconds = frame.Tick / frame.TickRate;
            timerValue.Begin(); timerValue.Append(seconds / 60, 2); timerValue.Append(":"); timerValue.Append(seconds % 60, 2); timerValue.End();
            healthValue.Begin(); healthValue.Append(frame.Lord.Health); healthValue.Append(" / "); healthValue.Append(frame.Lord.MaxHealth); healthValue.End();
            levelValue.Begin(); levelValue.Append("레벨 "); levelValue.Append(frame.Level); levelValue.End();
            hpFill.anchorMax = new Vector2(frame.Lord.MaxHealth > 0 ? Mathf.Clamp01((float)frame.Lord.Health / frame.Lord.MaxHealth) : 0, 1);
            xpFill.anchorMax = new Vector2(frame.RequiredExperience > 0 ? Mathf.Clamp01((float)frame.Experience / frame.RequiredExperience) : 0, 1);
            weapons.Clear(); tools.Clear();
            for (var equipmentIndex = 0; equipmentIndex < frame.Equipment.Count; equipmentIndex++)
            {
                var gear = frame.Equipment[equipmentIndex];
                if(catalog.Weapons.ContainsKey(gear.Id)) weapons.Add(gear);
                else if(catalog.Tools.ContainsKey(gear.Id)) tools.Add(gear);
            }
            for(var i = 0; i < slots.Length; i++)
            {
                var group = i < 5 ? weapons : tools;
                var index = i < 5 ? i : i - 5;
                var gear = index < group.Count ? group[index] : null;
                var id = gear?.Id;
                if(!string.Equals(shown[i], id, StringComparison.Ordinal))
                {
                    if (shown[i] != null && icons[i].TryGetValue(shown[i], out var prior)) prior.gameObject.SetActive(false);
                    if (id != null && icons[i].TryGetValue(id, out var icon)) icon.gameObject.SetActive(true);
                    shown[i] = id;
                }
                rankValues[i].Begin(); if (gear != null) rankValues[i].Append(gear.Level); rankValues[i].End();
            }
        }

        void RefreshLayout()
        {
            var screen=new Vector2(Screen.width,Screen.height);
            var scale=ui.Canvas.scaleFactor;
            if(topParts==null)
            {
                topParts=new RectTransform[root.childCount];topPositions=new Vector2[root.childCount];
                for(var i=0;i<root.childCount;i++){topParts[i]=(RectTransform)root.GetChild(i);topPositions[i]=topParts[i].anchoredPosition;}
            }
            if(layoutSize==screen&&layoutScale==scale)return;
            layoutSize=screen;layoutScale=scale;
            var wide=screen.x>screen.y;
            var factor=wide?Mathf.Min(1,screen.y*UiTokens.LandscapeHudFraction/(scale*UiTokens.FallowHudHeight)):1;
            root.sizeDelta=new Vector2(0,UiTokens.FallowHudHeight*factor);
            var width=ui.SafeRoot.rect.width;
            for(var i=0;i<topParts.Length;i++)
            {
                var point=topPositions[i];var x=point.x;
                if(wide)x=x>=650?width-(900-x)*factor:x>=300?(width-topParts[i].rect.width*factor)*.5f:x*factor;
                topParts[i].localScale=Vector3.one*factor;
                topParts[i].anchoredPosition=new Vector2(x,point.y*factor);
            }
            var footerHeight=wide?screen.y*UiTokens.LandscapeLoadoutFraction/scale:UiTokens.FallowFooterHeight;
            footer.sizeDelta=new Vector2(0,footerHeight);
            var slotSize=wide?Mathf.Min(footerHeight-10,UiTokens.FallowSlotSize):UiTokens.FallowSlotSize;
            Position(weaponCaption.rectTransform,wide?12:26,wide?(footerHeight-24)*.5f:12,wide?48:280,wide?24:30);
            Position(toolCaption.rectTransform,wide?width*.55f:508,wide?(footerHeight-24)*.5f:12,wide?48:280,wide?24:30);
            weaponCaption.fontSize=toolCaption.fontSize=wide?16:UiTokens.Caption;
            for(var i=0;i<slots.Length;i++)
            {
                var x=wide?(i<5?64+i*(width*.46f-64)/5:width*.55f+52+(i-5)*(width*.45f-64)/4):(i<5?24+i*86:506+(i-5)*92);
                Position(slots[i],x,wide?(footerHeight-slotSize)*.5f:50,slotSize,slotSize);
                Position(ranks[i].rectTransform,wide?slotSize-27:45,wide?slotSize-26:48,27,26);
                ranks[i].fontSize=wide?14:UiTokens.Caption;
                foreach(var icon in icons[i].Values)icon.rectTransform.sizeDelta=Vector2.one*(slotSize-(wide?10:12));
            }
        }

        RectTransform Bar(Transform parent, string name, string railRole, string fillRole, float y, float width, float height)
        {
            var rail = Rect(name, parent, 0, y, width, height); var backing = ui.Surface(rail, railRole);
            backing.color = UiTokens.FallowCharcoal; backing.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
            var fill = UiShell.Rect("Fill", rail); UiShell.Stretch(fill, 3); ui.Surface(fill, fillRole).pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier; return fill;
        }
        Text Label(Transform parent, string value, int size, float x, float y, float width, float height, Color color, bool shadow = false)
        {
            var label = ui.Label(parent, value, size, height); Position(label.rectTransform, x, y, width, height); label.color = color;
            if(shadow) { var effect = label.gameObject.AddComponent<Shadow>(); effect.effectColor = GameVisualTokens.Ink; effect.effectDistance = new Vector2(1, -2); }
            return label;
        }
        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        { var rect = UiShell.Rect(name, parent); Position(rect, x, y, width, height); return rect; }
        static void Position(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    }
}
