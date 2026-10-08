using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class UiHud
    {
        private readonly Text season,health,experience;
        private readonly RectTransform hpFill,xpFill;
        public UiHud(UiShell ui)
        {
            var root=UiShell.Rect("HUD",ui.Content);root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);root.anchoredPosition=Vector2.zero;root.sizeDelta=new Vector2(0,118);
            root.gameObject.AddComponent<Image>().color=GamePalette.Ground;
            var layout=root.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(16,16,8,8);layout.spacing=4;layout.childForceExpandHeight=false;
            season=ui.Label(root,"",16,24);hpFill=Bar(root,GamePalette.Danger);health=ui.Label(root,"",14,20);xpFill=Bar(root,GamePalette.KillXp);experience=ui.Label(root,"",14,20);
        }
        private static RectTransform Bar(Transform parent,Color color)
        {
            var rect=UiShell.Rect("Bar",parent);rect.gameObject.AddComponent<Image>().color=GamePalette.Stroke;rect.gameObject.AddComponent<LayoutElement>().preferredHeight=6;
            var fill=UiShell.Rect("Fill",rect);UiShell.Stretch(fill);fill.gameObject.AddComponent<Image>().color=color;return fill;
        }
        public void Present(RunFrame frame)
        {
            var seconds=(frame.SeasonTicksRemaining+frame.TickRate-1)/frame.TickRate;
            season.text=new[]{"봄","여름","가을","겨울"}[Mathf.Clamp(frame.Season,0,3)]+$" · {seconds/60:00}:{seconds%60:00} 남음";
            health.text=$"체력 {frame.Lord.Health} / {frame.Lord.MaxHealth}";experience.text=$"레벨 {frame.Level} · 경험치 {frame.Experience} / {frame.RequiredExperience}";
            hpFill.anchorMax=new Vector2(Mathf.Clamp01((float)frame.Lord.Health/frame.Lord.MaxHealth),1);xpFill.anchorMax=new Vector2(Mathf.Clamp01((float)frame.Experience/frame.RequiredExperience),1);
        }
    }
}
