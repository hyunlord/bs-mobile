using System;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public static class PortraitWidgets
    {
        public static RectTransform Place(string name,Transform parent,float x,float y,float width,float height)
        {
            var rect=UiShell.Rect(name,parent);Position(rect,x,y,width,height);return rect;
        }
        public static void Position(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        public static Text Label(UiShell ui,Transform parent,string text,float x,float y,float width,float height,int size=PortraitTokens.Body)
        {var label=ui.Label(parent,text,size,height);Position(label.rectTransform,x,y,width,height);label.lineSpacing=1.15f;return label;}
        public static Button Button(UiShell ui,Transform parent,string text,Action action,float x,float y,float width,float height=PortraitTokens.Action,bool enabled=true)
        {
            var button=ui.Button(parent,text,action,enabled);FallowUiSurface.Button(ui,button,true);
            Position((RectTransform)button.transform,x,y,width,height);button.GetComponentInChildren<Text>().fontSize=PortraitTokens.Body;return button;
        }
    }
}
