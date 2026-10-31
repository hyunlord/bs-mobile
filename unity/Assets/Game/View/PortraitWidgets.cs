using System;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public static class PortraitWidgets
    {
        static Font headingFont;
        static Font buttonFont;
        static Font HeadingFont => headingFont != null ? headingFont : headingFont = Resources.Load<Font>("Fonts/GowunBatang/GowunBatang-Bold");
        static Font ButtonFont => buttonFont != null ? buttonFont : buttonFont = Resources.Load<Font>("Fonts/GowunBatang/GowunBatang-Regular");
        public static RectTransform Place(string name,Transform parent,float x,float y,float width,float height)
        {
            var rect=UiShell.Rect(name,parent);Position(rect,x,y,width,height);return rect;
        }
        public static void Position(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        public static Text Label(UiShell ui,Transform parent,string text,float x,float y,float width,float height,int size=PortraitTokens.Body)
        {var label=ui.Label(parent,text,size,height);if(size>=PortraitTokens.Heading&&HeadingFont!=null)label.font=HeadingFont;Position(label.rectTransform,x,y,width,height);label.lineSpacing=1.15f;return label;}
        public static Image Art(UiShell ui,Transform parent,string assetName,float x,float y,float width,float height)
        {
            var rect=Place(assetName,parent,x,y,width,height);
            var image=rect.gameObject.AddComponent<Image>();image.sprite=PortraitArt.Get(assetName);image.preserveAspect=true;image.raycastTarget=false;return image;
        }
        public static Button Button(UiShell ui,Transform parent,string text,Action action,float x,float y,float width,float height=PortraitTokens.Action,bool enabled=true,string role="ui.button.secondary")
        {
            var button=ui.Button(parent,text,action,enabled);ui.ApplySurface(button.image,role);
            var colors=button.colors;colors.disabledColor=Color.white;button.colors=colors; // PortraitButtonLabel tints the image instead
            Position((RectTransform)button.transform,x,y,width,height);
            var label=button.GetComponentInChildren<Text>();label.fontSize=PortraitTokens.Body;
            var cream=role=="ui.button.secondary";
            var font=cream?HeadingFont:ButtonFont;if(font!=null)label.font=font;
            UiShell.Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(8,4);label.rectTransform.offsetMax=new Vector2(-8,-4);
            label.lineSpacing=1;label.verticalOverflow=VerticalWrapMode.Truncate;
            label.gameObject.AddComponent<PortraitButtonLabel>().Initialize(button,label,cream?PortraitTokens.CreamButtonInk:GameVisualTokens.Attack);
            return button;
        }
    }
    public sealed class PortraitButtonLabel : MonoBehaviour
    {
        Button button;
        Text label;
        Color active;
        bool wasInteractable;
        public void Initialize(Button owner,Text text,Color ink)
        {button=owner;label=text;active=ink;Refresh();}
        void LateUpdate()
        {if(button!=null&&button.IsInteractable()!=wasInteractable)Refresh();}
        void Refresh()
        {
            wasInteractable=button.IsInteractable();
            var ink=wasInteractable?active:PortraitTokens.DisabledButtonInk;
            label.color=ink;
            if(button.image!=null)button.image.color=wasInteractable?Color.white:PortraitTokens.DisabledButtonTint;
        }
    }
}
