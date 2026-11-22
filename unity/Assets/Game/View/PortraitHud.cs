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
        readonly RectTransform root,hp,xp,pool;
        readonly BufferedHudText health,level,time,season;
        readonly bool vassalLayout;
        readonly RectTransform vassalBacking;
        readonly RectTransform[] foldedGroups=new RectTransform[2];
        readonly BufferedHudText[] foldedCounts=new BufferedHudText[2];
        readonly int[] lastFolded={-1,-1};
        static readonly float[] VassalSlotX={42,100,236,294};
        static readonly string[] Seasons={"봄","여름","가을","겨울"};
        readonly UiDamageFeedback feedback;
        readonly RectTransform[] slots=new RectTransform[13];
        readonly string[] shown=new string[13];
        readonly BufferedHudText[] slotLevels=new BufferedHudText[13];
        readonly int[] shownLevels=new int[13];
        readonly Dictionary<string,Image> icons=new Dictionary<string,Image>(StringComparer.Ordinal);
        Vector2 layoutSize;long lastHealth=long.MinValue,lastMaximum,lastExperience,lastRequired;int lastLevel=-1,lastSecond=-1,lastSeason=-1;
        public float ReservedTopPixels=>(root.rect.height+PortraitTokens.HudInset)*ui.Canvas.scaleFactor;
        public PortraitHud(UiShell ui,ContentCatalog catalog,Action pause,bool hitFeedback,VassalRunOptions vassals=null)
        {
            this.ui=ui;this.catalog=catalog;vassalLayout=vassals!=null;ui.SynchronizeViewport();
            root=UiShell.Rect("HUD",ui.Content);root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);
            if(vassalLayout)
            {
                vassalBacking=PortraitWidgets.Place("Vassal HUD backing",root,0,-PortraitTokens.HudInset,390,84);
                ui.Surface(vassalBacking,"ui.panel");
            }
            var medal = PortraitWidgets.Place("Level medallion", root, 10, 0, 48, 48);
            if(!vassalLayout)ui.Surface(medal, "ui.medallion").type = Image.Type.Simple;
            else PortraitWidgets.Label(ui,medal,"Lv",8,5,24,28,18);
            var levelLabel = PortraitWidgets.Label(ui, medal, "", vassalLayout?30:0, vassalLayout?5:0, vassalLayout?24:48, vassalLayout?28:48, 18); levelLabel.alignment = TextAnchor.MiddleCenter;
            level = BufferedHudText.Create(levelLabel);
            var rail = PortraitWidgets.Place("Health", root, 62, 4, 170, 18); ui.Surface(rail, "ui.xp.rail").pixelsPerUnitMultiplier = 3.6f;
            hp = UiShell.Rect("Fill", rail); UiShell.Stretch(hp, 3); ui.Surface(hp, "ui.hp.fill").pixelsPerUnitMultiplier = 4.5f;
            var healthLabel = PortraitWidgets.Label(ui, rail, "", 8, 0, 154, 18, 11); healthLabel.color = Color.white;
            var healthShadow = healthLabel.gameObject.AddComponent<Shadow>(); healthShadow.effectColor = GameVisualTokens.Ink; healthShadow.effectDistance = new Vector2(0,-1); health = BufferedHudText.Create(healthLabel);
            var xpRail = PortraitWidgets.Place("Experience", root, 62, 26, 170, 12); ui.Surface(xpRail, "ui.xp.rail").pixelsPerUnitMultiplier = 3.6f;
            xp = UiShell.Rect("Fill", xpRail); UiShell.Stretch(xp, 2); ui.Surface(xp, "ui.xp.fill").pixelsPerUnitMultiplier = 4.5f;
            var seasonLabel = PortraitWidgets.Label(ui, root, "", 238, 2, 84, 18, 11); seasonLabel.name = "Current season"; seasonLabel.alignment = TextAnchor.MiddleCenter; seasonLabel.color = new Color32(243,213,140,255); season = BufferedHudText.Create(seasonLabel);
            var timer = PortraitWidgets.Label(ui, root, "", 238, vassalLayout?-4:20, vassalLayout?90:84, 24, vassalLayout?14:20); timer.alignment = TextAnchor.MiddleCenter; timer.color = vassalLayout?GameVisualTokens.Ink:Color.white; time = BufferedHudText.Create(timer);
            if(vassalLayout)
            {
                seasonLabel.gameObject.SetActive(false);
                var status=PortraitWidgets.Label(ui,root,"가신 "+vassals.Snapshot.Roster.Count+" · 켜짐",238,24,90,24,14);
                status.name="Vassal snapshot status";status.alignment=TextAnchor.MiddleCenter;
                for(var group=0;group<2;group++)
                {
                    var x=8+group*194;
                    PortraitWidgets.Label(ui,root,group==0?"무기":"도구",x,48,30,28,14);
                    foldedGroups[group]=PortraitWidgets.Place("Folded equipment "+group,root,x+150,48,30,28);
                    PortraitWidgets.Label(ui,foldedGroups[group],"+",0,0,12,28,16);
                    var count=PortraitWidgets.Label(ui,foldedGroups[group],"",10,0,20,28,16);
                    foldedCounts[group]=BufferedHudText.Create(count);foldedGroups[group].gameObject.SetActive(false);
                }
            }
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
                if(catalog.Runtime!=null)foreach(var id in catalog.Runtime.Charters.Keys)Prewarm(id);
            }
            for(var i=0;i<(vassalLayout?4:slots.Length);i++)
            {
                slots[i]=PortraitWidgets.Place("Equipment "+i,root,vassalLayout?VassalSlotX[i]:10+i*28f,52,vassalLayout?28:26,vassalLayout?28:26);
                if(!vassalLayout)ui.Surface(slots[i],"ui.slot").pixelsPerUnitMultiplier=9.6f;slots[i].gameObject.SetActive(false);
                var badge=PortraitWidgets.Label(ui,slots[i],"",vassalLayout?34:16,vassalLayout?0:15,vassalLayout?20:14,vassalLayout?28:16,vassalLayout?13:11);badge.color=vassalLayout?GameVisualTokens.Ink:Color.white;badge.alignment=vassalLayout?TextAnchor.MiddleLeft:TextAnchor.MiddleRight;
                if(!vassalLayout){var shadow=badge.gameObject.AddComponent<Shadow>();shadow.effectColor=Color.black;shadow.effectDistance=new Vector2(0,-1);}slotLevels[i]=BufferedHudText.Create(badge);
            }
            if(hitFeedback)feedback=new UiDamageFeedback(ui,hp);
            Layout();
        }
        void Prewarm(string id)
        {if(!icons.ContainsKey(id))icons.Add(id,ui.Icon(pool,id,vassalLayout?28:16));}
        void Layout()
        {
            var size=ui.SafeRoot.rect.size;if(size==layoutSize)return;layoutSize=size;
            root.anchoredPosition=new Vector2(0,-PortraitTokens.HudInset);
            root.sizeDelta=new Vector2(0,Mathf.Min(PortraitTokens.HudHeight,size.y*.1f-PortraitTokens.HudInset));
            var y=Mathf.Max(44,root.rect.height-(vassalLayout?28:26));
            for(var i=0;i<(vassalLayout?4:slots.Length);i++)slots[i].anchoredPosition=new Vector2(vassalLayout?VassalSlotX[i]:10+i*28f,-y);
            if(vassalLayout)
            {
                vassalBacking.sizeDelta=new Vector2(size.x,root.rect.height+PortraitTokens.HudInset);
                for(var group=0;group<2;group++)foldedGroups[group].anchoredPosition=new Vector2(158+group*194,-y);
            }
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
            var seasonChanged=seasonIndex!=lastSeason;
            if(seasonChanged){lastSeason=seasonIndex;season.Begin();season.Append(Seasons[seasonIndex]);season.End();}
            var second=frame.Tick/frame.TickRate;
            if(second!=lastSecond||vassalLayout&&seasonChanged){lastSecond=second;time.Begin();if(vassalLayout){time.Append(Seasons[seasonIndex]);time.Append(" · ");}time.Append(second/60,2);time.Append(":");time.Append(second%60,2);time.End();}
            if(frame.Experience!=lastExperience||frame.RequiredExperience!=lastRequired)
            {lastExperience=frame.Experience;lastRequired=frame.RequiredExperience;xp.anchorMax=new Vector2(lastRequired>0?Mathf.Clamp01((float)lastExperience/lastRequired):0,1);}
            if(vassalLayout){PresentVassalEquipment(frame);return;}
            var index=0;
            for(var i=0;i<frame.Equipment.Count&&index<13;i++)
            {
                var id=frame.Equipment[i].Id;
                if(catalog!=null&&!catalog.Weapons.ContainsKey(id)&&!catalog.Tools.ContainsKey(id))continue;
                SetSlot(index++,id,frame.Equipment[i].Level);
            }
            if(first!=null)for(var i=0;i<first.Charters.Count&&index<13;i++)SetSlot(index++,first.Charters[i],0);
            while(index<13)SetSlot(index++,null,0);
        }
        void PresentVassalEquipment(RunFrame frame)
        {
            var weapons=0;var tools=0;
            for(var i=0;i<frame.Equipment.Count;i++)
            {
                var item=frame.Equipment[i];
                if(catalog!=null&&catalog.Weapons.ContainsKey(item.Id))
                {if(weapons<2)SetSlot(weapons,item.Id,item.Level);weapons++;}
                else if(catalog!=null&&catalog.Tools.ContainsKey(item.Id))
                {if(tools<2)SetSlot(2+tools,item.Id,item.Level);tools++;}
            }
            for(var i=Math.Min(weapons,2);i<2;i++)SetSlot(i,null,0);
            for(var i=Math.Min(tools,2);i<2;i++)SetSlot(2+i,null,0);
            SetFolded(0,Math.Max(0,weapons-2));SetFolded(1,Math.Max(0,tools-2));
        }
        void SetFolded(int group,int count)
        {
            if(lastFolded[group]==count)return;lastFolded[group]=count;
            foldedGroups[group].gameObject.SetActive(count>0);
            foldedCounts[group].Begin();foldedCounts[group].Append(count);foldedCounts[group].End();
        }
        void SetSlot(int index,string id,int equipmentLevel)
        {
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
