using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class PortraitHud
    {
        readonly UiShell ui;readonly ContentCatalog catalog;
        readonly RectTransform root,hp,xp,pool,backing;
        readonly BufferedHudText health,level,time,season;
        static readonly string[] Seasons={"봄","여름","가을","겨울"};
        readonly UiDamageFeedback feedback;
        readonly RectTransform[] slots=new RectTransform[6];
        readonly string[] shown=new string[6];
        readonly BufferedHudText[] slotLevels=new BufferedHudText[6];
        readonly int[] shownLevels=new int[6];
        readonly RectTransform[] levelPlates=new RectTransform[6];
        readonly Dictionary<string,Image> icons=new Dictionary<string,Image>(StringComparer.Ordinal);
        readonly Text[] categoryLabels = new Text[2], overflowLabels = new Text[2];
        readonly int[] hiddenCounts = { -1, -1 };
        Vector2 layoutSize;long lastHealth=long.MinValue,lastMaximum,lastExperience,lastRequired;int lastLevel=-1,lastSecond=-1,lastSeason=-1;
        public float ReservedTopPixels=>(root.rect.height+PortraitTokens.HudInset)*ui.Canvas.scaleFactor;
        public PortraitHud(UiShell ui,ContentCatalog catalog,Action pause,bool hitFeedback)
        {
            this.ui=ui;this.catalog=catalog;ui.SynchronizeViewport();
            root=UiShell.Rect("HUD",ui.Content);root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);
            backing=PortraitWidgets.Place("Battle HUD parchment",root,4,-PortraitTokens.HudInset,382,84);
            ui.Surface(backing,"ui.panel").raycastTarget=false;
            var medal = PortraitWidgets.Place("Level medallion", root, 10, 0, 48, 48);
            PortraitWidgets.Label(ui, medal, "Lv", 8, 8, 24, 24, 18);
            var levelLabel = PortraitWidgets.Label(ui, medal, "", 30, 8, 24, 24, 18); levelLabel.alignment = TextAnchor.MiddleLeft;
            level = BufferedHudText.Create(levelLabel);
            var rail = PortraitWidgets.Place("Health", root, 62, 4, 170, 18); ui.Surface(rail, "ui.xp.rail").pixelsPerUnitMultiplier = 3.6f;
            hp = UiShell.Rect("Fill", rail); UiShell.Stretch(hp, 3); ui.Surface(hp, "ui.hp.fill").pixelsPerUnitMultiplier = 4.5f;
            var healthLabel = PortraitWidgets.Label(ui, rail, "", 8, 0, 154, 18, 13); healthLabel.color = Color.white;
            var healthShadow = healthLabel.gameObject.AddComponent<Shadow>(); healthShadow.effectColor = GameVisualTokens.Ink; healthShadow.effectDistance = new Vector2(0,-1); health = BufferedHudText.Create(healthLabel);
            var xpRail = PortraitWidgets.Place("Experience", root, 62, 26, 170, 12); ui.Surface(xpRail, "ui.xp.rail").pixelsPerUnitMultiplier = 3.6f;
            var xpBackground = UiShell.Rect("Blue XP track", xpRail); UiShell.Stretch(xpBackground, 2);
            xpBackground.gameObject.AddComponent<Image>().color = PortraitTokens.ExperienceTrack;
            xp = UiShell.Rect("Fill", xpRail); UiShell.Stretch(xp, 2);
            xp.gameObject.AddComponent<Image>().color = PortraitTokens.ExperienceFill;
            var seasonLabel = PortraitWidgets.Label(ui, root, "", 238, 0, 84, 18, 13); seasonLabel.name = "Current season"; seasonLabel.alignment = TextAnchor.MiddleCenter; seasonLabel.color = PortraitTokens.CreamButtonInk; season = BufferedHudText.Create(seasonLabel);
            var timer = PortraitWidgets.Label(ui, root, "", 238, 18, 84, 26, 20); timer.alignment = TextAnchor.MiddleCenter; timer.color = PortraitTokens.CreamButtonInk; time = BufferedHudText.Create(timer);
            if (pause != null)
            {
                var pauseButton = PortraitWidgets.Button(ui, root, "", pause, 332, 0, 48, 48, true, "ui.button.round.blue");
                pauseButton.name = "멈춤";
                for (var i = 0; i < 2; i++)
                {
                    var bar = PortraitWidgets.Place("Pause bar " + i, pauseButton.transform, 16 + i * 12, 15, 4, 18);
                    var image = bar.gameObject.AddComponent<Image>(); image.color = Color.white; image.raycastTarget = false;
                }
            }
            pool=UiShell.Rect("Equipment pool",root);pool.gameObject.SetActive(false);
            if(catalog!=null)
            {
                foreach(var id in catalog.Weapons.Keys)Prewarm(id);
                foreach(var id in catalog.Tools.Keys)Prewarm(id);

            }
            for (var group = 0; group < 2; group++)
            {
                categoryLabels[group] = PortraitWidgets.Label(ui, root, group == 0 ? "무기" : "도구", 10, 48, 34, 28, 12);
                categoryLabels[group].name = group == 0 ? "Weapon category" : "Tool category";
                overflowLabels[group] = PortraitWidgets.Label(ui, root, "", 136, 48, 40, 28, 14);
                overflowLabels[group].name = group == 0 ? "Weapon overflow" : "Tool overflow";
            }
            for(var i=0;i<slots.Length;i++)
            {
                slots[i]=PortraitWidgets.Place("Equipment "+i,root,46+(i%3)*30f,48,28,28);
                slots[i].gameObject.SetActive(false);
                var badgePlate=PortraitWidgets.Place("Level badge",slots[i],9,12,20,16);
                levelPlates[i]=badgePlate;badgePlate.gameObject.SetActive(false);
                badgePlate.gameObject.AddComponent<Image>().color=new Color32(239,227,198,255);
                var badge=PortraitWidgets.Label(ui,badgePlate,"",0,0,20,16,13);badge.color=PortraitTokens.CreamButtonInk;badge.alignment=TextAnchor.MiddleCenter;
                slotLevels[i]=BufferedHudText.Create(badge);
            }
            if(hitFeedback)feedback=new UiDamageFeedback(ui,hp);
            Layout();
        }
        void Prewarm(string id)
        {if(!icons.ContainsKey(id))icons.Add(id,ui.Icon(pool,id,28));}
        void Layout()
        {
            var size=ui.SafeRoot.rect.size;if(size==layoutSize)return;layoutSize=size;
            root.anchoredPosition=new Vector2(0,-PortraitTokens.HudInset);
            root.sizeDelta=new Vector2(0,Mathf.Min(PortraitTokens.HudHeight,size.y*.1f-PortraitTokens.HudInset));
            backing.sizeDelta=new Vector2(size.x-8,root.rect.height+PortraitTokens.HudInset);
            var y=Mathf.Max(44,root.rect.height-28);
            var groupWidth = (size.x - 20) / 2;
            for (var group = 0; group < 2; group++)
            {
                categoryLabels[group].rectTransform.anchoredPosition = new Vector2(10 + group * groupWidth, -y);
                overflowLabels[group].rectTransform.anchoredPosition = new Vector2(136 + group * groupWidth, -y);
            }
            for(var i=0;i<slots.Length;i++)slots[i].anchoredPosition=new Vector2(46+(i/3)*groupWidth+(i%3)*30,-y);
        }
        public void Present(RunFrame frame,FirstPlayableFrame first)
        {
            Layout();feedback?.Present(frame);
            if(frame.Lord.Health!=lastHealth||frame.Lord.MaxHealth!=lastMaximum)
            {
                lastHealth=frame.Lord.Health;lastMaximum=frame.Lord.MaxHealth;
                health.Begin();health.Append(lastHealth);health.Append("/");health.Append(lastMaximum);health.End();
                hp.anchorMax=new Vector2(lastMaximum>0?Mathf.Clamp01((float)lastHealth/lastMaximum):0,1);
            }
            if(frame.Level!=lastLevel){lastLevel=frame.Level;level.Begin();level.Append(lastLevel);level.End();}
            var seasonIndex=Mathf.Clamp(frame.Season,0,Seasons.Length-1);
            if(seasonIndex!=lastSeason){lastSeason=seasonIndex;season.Begin();season.Append(Seasons[seasonIndex]);season.End();}
            var second=frame.Tick/frame.TickRate;
            if(second!=lastSecond){lastSecond=second;time.Begin();time.Append(second/60,2);time.Append(":");time.Append(second%60,2);time.End();}
            if(frame.Experience!=lastExperience||frame.RequiredExperience!=lastRequired)
            {lastExperience=frame.Experience;lastRequired=frame.RequiredExperience;xp.anchorMax=new Vector2(lastRequired>0?Mathf.Clamp01((float)lastExperience/lastRequired):0,1);}
            for (var group = 0; group < 2; group++)
            {
                var count = 0;
                for (var i = 0; i < frame.Equipment.Count; i++)
                {
                    var item = frame.Equipment[i];
                    if (catalog == null || !(group == 0 ? catalog.Weapons.ContainsKey(item.Id) : catalog.Tools.ContainsKey(item.Id))) continue;
                    if (count < 3) SetSlot(group * 3 + count, item.Id, item.Level);
                    count++;
                }
                for (var index = Math.Min(count, 3); index < 3; index++) SetSlot(group * 3 + index, null, 0);
                var hidden = Math.Max(0, count - 3);
                if (hiddenCounts[group] == hidden) continue;
                hiddenCounts[group] = hidden;
                overflowLabels[group].text = hidden > 0 ? "+" + hidden : "";
            }
        }
        void SetSlot(int index,string id,int equipmentLevel)
        {
            levelPlates[index].gameObject.SetActive(equipmentLevel>0);
            if(shownLevels[index]!=equipmentLevel){shownLevels[index]=equipmentLevel;slotLevels[index].Begin();if(equipmentLevel>0)slotLevels[index].Append(equipmentLevel);slotLevels[index].End();}
            if(string.Equals(shown[index],id,StringComparison.Ordinal))return;
            var old=shown[index];if(old!=null&&icons.TryGetValue(old,out var previous)&&previous.transform.parent==slots[index])previous.transform.SetParent(pool,false);
            shown[index]=id;slots[index].gameObject.SetActive(id!=null);
            if(id==null)return;
            if(!icons.TryGetValue(id,out var icon)){Prewarm(id);icon=icons[id];}
            icon.transform.SetParent(slots[index],false);icon.transform.SetSiblingIndex(0);icon.rectTransform.anchorMin=icon.rectTransform.anchorMax=new Vector2(.5f,.5f);icon.rectTransform.anchoredPosition=Vector2.zero;
        }
    }
}
