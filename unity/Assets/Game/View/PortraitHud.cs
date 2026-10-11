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
        static readonly string[] Seasons={"봄","여름","가을","겨울"};
        readonly UiDamageFeedback feedback;
        readonly RectTransform[] slots=new RectTransform[13];
        readonly string[] shown=new string[13];
        readonly Dictionary<string,Image> icons=new Dictionary<string,Image>(StringComparer.Ordinal);
        Vector2 layoutSize;long lastHealth=long.MinValue,lastMaximum,lastExperience,lastRequired;int lastLevel=-1,lastSecond=-1,lastSeason=-1;
        public float ReservedTopPixels=>root.rect.height*ui.Canvas.scaleFactor;
        public PortraitHud(UiShell ui,ContentCatalog catalog,Action pause,bool hitFeedback)
        {
            this.ui=ui;this.catalog=catalog;ui.SynchronizeViewport();
            root=UiShell.Rect("HUD",ui.Content);root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);
            var backing=PortraitWidgets.Place("Paper header backing",root,8,0,322,44);
            var paper=ui.Surface(backing,"ui.button.secondary");paper.pixelsPerUnitMultiplier=10;paper.color=new Color(1,1,1,.96f);
            PortraitWidgets.Label(ui,root,"새봄의 터",12,0,74,20,PortraitTokens.Small);
            var levelLabel=PortraitWidgets.Label(ui,root,"",12,22,68,18,PortraitTokens.Caption);level=BufferedHudText.Create(levelLabel);
            var rail=PortraitWidgets.Place("Health",root,88,5,174,17);ui.Surface(rail,"ui.hp.rail").pixelsPerUnitMultiplier=UiTokens.FallowFrameMultiplier;
            hp=UiShell.Rect("Fill",rail);UiShell.Stretch(hp,2);ui.Surface(hp,"ui.hp.fill");
            var healthLabel=PortraitWidgets.Label(ui,rail,"",3,0,168,17,PortraitTokens.Caption);healthLabel.alignment=TextAnchor.MiddleCenter;healthLabel.color=GameVisualTokens.Attack;
            var healthShadow=healthLabel.gameObject.AddComponent<Shadow>();healthShadow.effectColor=GameVisualTokens.Ink;healthShadow.effectDistance=new Vector2(0,-1);health=BufferedHudText.Create(healthLabel);
            var xpRail=PortraitWidgets.Place("Experience",root,88,28,174,7);ui.Surface(xpRail,"ui.xp.rail");
            xp=UiShell.Rect("Fill",xpRail);UiShell.Stretch(xp);ui.Surface(xp,"ui.xp.fill");
            var seasonLabel=PortraitWidgets.Label(ui,root,"",270,0,60,18,PortraitTokens.Caption);seasonLabel.name="Current season";seasonLabel.alignment=TextAnchor.MiddleCenter;season=BufferedHudText.Create(seasonLabel);
            var timer=PortraitWidgets.Label(ui,root,"",270,18,60,22,PortraitTokens.Small);timer.alignment=TextAnchor.MiddleCenter;time=BufferedHudText.Create(timer);
            if(pause!=null)PortraitWidgets.Button(ui,root,"멈춤",pause,334,0,44,44);
            pool=UiShell.Rect("Equipment pool",root);pool.gameObject.SetActive(false);
            if(catalog!=null)
            {
                foreach(var id in catalog.Weapons.Keys)Prewarm(id);
                foreach(var id in catalog.Tools.Keys)Prewarm(id);
                if(catalog.Runtime!=null)foreach(var id in catalog.Runtime.Charters.Keys)Prewarm(id);
            }
            for(var i=0;i<slots.Length;i++)
            {
                slots[i]=PortraitWidgets.Place("Equipment "+i,root,16+i*27.5f,50,26,26);
                ui.Surface(slots[i],"ui.slot");slots[i].gameObject.SetActive(false);
            }
            if(hitFeedback)feedback=new UiDamageFeedback(ui,hp);
            Layout();
        }
        void Prewarm(string id)
        {if(!icons.ContainsKey(id))icons.Add(id,ui.Icon(pool,id,26));}
        void Layout()
        {
            var size=ui.SafeRoot.rect.size;if(size==layoutSize)return;layoutSize=size;
            root.sizeDelta=new Vector2(0,Mathf.Min(PortraitTokens.HudHeight,size.y*.1f));
            var y=Mathf.Max(44,root.rect.height-26);
            for(var i=0;i<slots.Length;i++)slots[i].anchoredPosition=new Vector2(16+i*27.5f,-y);
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
            if(frame.Level!=lastLevel){lastLevel=frame.Level;level.Begin();level.Append("레벨 ");level.Append(lastLevel);level.End();}
            var seasonIndex=Mathf.Clamp(frame.Season,0,Seasons.Length-1);
            if(seasonIndex!=lastSeason){lastSeason=seasonIndex;season.Begin();season.Append(Seasons[seasonIndex]);season.End();}
            var second=frame.Tick/frame.TickRate;
            if(second!=lastSecond){lastSecond=second;time.Begin();time.Append(second/60,2);time.Append(":");time.Append(second%60,2);time.End();}
            if(frame.Experience!=lastExperience||frame.RequiredExperience!=lastRequired)
            {lastExperience=frame.Experience;lastRequired=frame.RequiredExperience;xp.anchorMax=new Vector2(lastRequired>0?Mathf.Clamp01((float)lastExperience/lastRequired):0,1);}
            var index=0;
            for(var i=0;i<frame.Equipment.Count&&index<13;i++)
            {
                var id=frame.Equipment[i].Id;
                if(catalog!=null&&!catalog.Weapons.ContainsKey(id)&&!catalog.Tools.ContainsKey(id))continue;
                SetSlot(index++,id);
            }
            if(first!=null)for(var i=0;i<first.Charters.Count&&index<13;i++)SetSlot(index++,first.Charters[i]);
            while(index<13)SetSlot(index++,null);
        }
        void SetSlot(int index,string id)
        {
            if(string.Equals(shown[index],id,StringComparison.Ordinal))return;
            var old=shown[index];if(old!=null&&icons.TryGetValue(old,out var previous)&&previous.transform.parent==slots[index])previous.transform.SetParent(pool,false);
            shown[index]=id;slots[index].gameObject.SetActive(id!=null);
            if(id==null)return;
            if(!icons.TryGetValue(id,out var icon)){Prewarm(id);icon=icons[id];}
            icon.transform.SetParent(slots[index],false);icon.rectTransform.anchorMin=icon.rectTransform.anchorMax=new Vector2(.5f,.5f);icon.rectTransform.anchoredPosition=Vector2.zero;
        }
    }
}
