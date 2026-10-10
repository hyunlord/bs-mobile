using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class UiHud
    {
        readonly UiShell ui;
        readonly ContentCatalog catalog;
        readonly RectTransform root, hpFill, xpFill, equipment;
        readonly Text season, health, experience;
        readonly List<string> equipmentIds = new List<string>();
        readonly FallowHud fallow;
        public float ReservedTopPixels => fallow != null ? fallow.ReservedTopPixels : root.rect.height * ui.Canvas.scaleFactor;
        public UiHud(UiShell ui) : this(ui,null,null) { }
        public UiHud(UiShell ui,ContentCatalog catalog,Action openSettings)
        {
            this.ui=ui;this.catalog=catalog;
            if(ArtCatalog.ProfileName=="wave-1a"&&catalog?.WaveRuntime?.ChapterId=="meta:chapter_1")
            {fallow=new FallowHud(ui,catalog,openSettings);return;}
            root=UiShell.Rect("HUD",ui.Content);root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);root.sizeDelta=new Vector2(0,160);
            root.gameObject.AddComponent<Image>().color=Color.clear;
            var info=Place("Vitals backing",root,16,0,720,96);ui.Surface(info,"ui.hint");
            season=ui.Label(info,"",UiTokens.Small,28);Position(season.rectTransform,24,10,672,28);
            health=ui.Label(info,"",UiTokens.Caption,28);Position(health.rectTransform,24,38,330,28);
            experience=ui.Label(info,"",UiTokens.Caption,28);Position(experience.rectTransform,370,38,330,28);
            hpFill=Bar(info,"Health","ui.hp.rail","ui.hp.fill",24);
            xpFill=Bar(info,"Experience","ui.xp.rail","ui.xp.fill",370);
            if(openSettings!=null){var button=ui.Button(root,"설정",openSettings);Position((RectTransform)button.transform,752,4,128,88);}
            equipment=Place("Equipment",root,24,104,832,48);var grid=equipment.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=Vector2.one*44;grid.spacing=Vector2.one*4;grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=17;
        }
        static RectTransform Place(string name,Transform parent,float x,float y,float width,float height)
        {var rect=UiShell.Rect(name,parent);Position(rect,x,y,width,height);return rect;}
        static void Position(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        RectTransform Bar(Transform parent,string name,string railRole,string fillRole,float x)
        {
            var rail=Place(name,parent,x,72,326,10);ui.Surface(rail,railRole);
            var fill=UiShell.Rect("Fill",rail);UiShell.Stretch(fill);ui.Surface(fill,fillRole);return fill;
        }
        public void Present(RunFrame frame)=>Present(frame,null);
        public void Present(RunFrame frame,FirstPlayableFrame firstPlayable)
        {
            if(fallow!=null){fallow.Present(frame);return;}
            var seconds=(frame.SeasonTicksRemaining+frame.TickRate-1)/frame.TickRate;
            season.text=new[]{"봄","여름","가을","겨울"}[Mathf.Clamp(frame.Season,0,3)]+$" · {seconds/60:00}:{seconds%60:00} 남음";
            health.text=$"체력 {frame.Lord.Health}/{frame.Lord.MaxHealth}";experience.text=$"레벨 {frame.Level} · {frame.Experience}/{frame.RequiredExperience}";
            hpFill.anchorMax=new Vector2(frame.Lord.MaxHealth>0?Mathf.Clamp01((float)frame.Lord.Health/frame.Lord.MaxHealth):0,1);
            xpFill.anchorMax=new Vector2(frame.RequiredExperience>0?Mathf.Clamp01((float)frame.Experience/frame.RequiredExperience):0,1);
            if (EquipmentMatches(frame, firstPlayable)) return;
            equipmentIds.Clear();
            for (var i = 0; i < frame.Equipment.Count; i++)
            {
                var id = frame.Equipment[i].Id;
                if (IsEquipment(id)) equipmentIds.Add(id);
            }
            if (firstPlayable != null)
                for (var i = 0; i < firstPlayable.Charters.Count; i++) equipmentIds.Add(firstPlayable.Charters[i]);
            for (var i = equipment.childCount - 1; i >= 0; i--)
            {
                var child = equipment.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child); else UnityEngine.Object.DestroyImmediate(child);
            }
            for (var i = 0; i < equipmentIds.Count; i++) ui.Icon(equipment,equipmentIds[i],44);
        }
        bool IsEquipment(string id) => catalog == null || catalog.Weapons.ContainsKey(id) || catalog.Tools.ContainsKey(id);
        bool EquipmentMatches(RunFrame frame, FirstPlayableFrame firstPlayable)
        {
            var index = 0;
            for (var i = 0; i < frame.Equipment.Count; i++)
            {
                var id = frame.Equipment[i].Id;
                if (!IsEquipment(id)) continue;
                if (index >= equipmentIds.Count || !string.Equals(equipmentIds[index++], id, StringComparison.Ordinal)) return false;
            }
            if (firstPlayable != null)
                for (var i = 0; i < firstPlayable.Charters.Count; i++)
                    if (index >= equipmentIds.Count || !string.Equals(equipmentIds[index++], firstPlayable.Charters[i], StringComparison.Ordinal)) return false;
            return index == equipmentIds.Count;
        }
    }
}
