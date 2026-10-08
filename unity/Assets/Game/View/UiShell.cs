using System;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class UiShell : MonoBehaviour
    {
        public RectTransform SafeRoot { get; private set; }
        public RectTransform TouchSurface { get; private set; }
        public RectTransform Content { get; private set; }
        public Font Font { get; private set; }
        public Canvas Canvas { get; private set; }
        private Rect lastSafe;
        private Vector2 lastScreen;
        private RectTransform stickBase, stickKnob;
        public void Initialize(Font font)
        {
            Font = font;
            Canvas = gameObject.AddComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(360, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0;
            gameObject.AddComponent<GraphicRaycaster>();
            TouchSurface = Rect("Movement surface", transform); Stretch(TouchSurface); var hit = TouchSurface.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            SafeRoot = Rect("Safe area", transform); Stretch(SafeRoot);
            Content = Rect("Content", SafeRoot); Stretch(Content);
            stickBase = Rect("Joystick", transform); stickBase.sizeDelta = new Vector2(96, 96); stickBase.gameObject.AddComponent<Image>().color = new Color(.2f,.3f,.35f,.18f);
            stickKnob = Rect("Knob", stickBase); stickKnob.sizeDelta = new Vector2(32,32); stickKnob.gameObject.AddComponent<Image>().color = GamePalette.Lord;
            foreach (var graphic in stickBase.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            stickBase.gameObject.SetActive(false); RefreshSafeArea();
        }
        public void RefreshSafeArea()
        {
            if (SafeRoot == null) return;
            var safe = Screen.safeArea; if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0,0,Screen.width,Screen.height);
            GetComponent<CanvasScaler>().referenceResolution = Screen.height / (float)Screen.width < 1.35f ? new Vector2(600,600) : new Vector2(360,720);
            lastSafe = safe; lastScreen = new Vector2(Screen.width,Screen.height);
            SafeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height); SafeRoot.anchorMax = new Vector2(safe.xMax / Screen.width,safe.yMax / Screen.height); SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
        }
        private void Update() { if (lastSafe != Screen.safeArea || lastScreen != new Vector2(Screen.width, Screen.height)) RefreshSafeArea(); }
        public void ShowStick(bool active, Vector2 origin, Vector2 offset)
        {
            stickBase.gameObject.SetActive(active); if (!active) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, origin, null, out var point); stickBase.anchoredPosition = point; stickKnob.anchoredPosition = offset / Canvas.scaleFactor;
        }
        public void Clear() { foreach (Transform child in Content) { child.gameObject.SetActive(false); Destroy(child.gameObject); } }
        public RectTransform Panel(string name)
        {
            var p = Rect(name, Content); Stretch(p,16); p.gameObject.AddComponent<Image>().color = GamePalette.Panel;
            var scrollObject = Rect("Scroll",p); Stretch(scrollObject,16); var scroll=scrollObject.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false;
            scrollObject.gameObject.AddComponent<Image>().color=Color.clear; scrollObject.gameObject.AddComponent<RectMask2D>();
            var body=Rect("Body",scrollObject); body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.sizeDelta=Vector2.zero;
            var layout=body.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childControlWidth=true;layout.childForceExpandWidth=true;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.content=body;scroll.viewport=scrollObject;
            return body;
        }
        public Text Label(Transform parent,string text,int size=16,float height=32)
        {
            var rect=Rect(text,parent); var label=rect.gameObject.AddComponent<Text>();label.font=Font;label.fontSize=size;label.text=text;label.color=GamePalette.Text;label.alignment=TextAnchor.MiddleLeft;label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Overflow;label.raycastTarget=false;
            var layout=rect.gameObject.AddComponent<LayoutElement>();layout.minHeight=height;return label;
        }
        public Button Button(Transform parent,string text,Action action,bool enabled=true)
        {
            var rect=Rect(text,parent);var image=rect.gameObject.AddComponent<Image>();image.color=GamePalette.Button;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.interactable=enabled;button.onClick.AddListener(()=>action());
            var colors=button.colors;colors.disabledColor=new Color(.6f,.63f,.64f);colors.pressedColor=new Color(.65f,.72f,.76f);button.colors=colors;
            var layout=rect.gameObject.AddComponent<LayoutElement>();layout.minHeight=48;layout.preferredHeight=48;
            var label=Label(rect,text,16,48);Stretch(label.rectTransform,8);label.alignment=TextAnchor.MiddleCenter;label.color=GamePalette.ButtonText;
            return button;
        }
        public static RectTransform Rect(string name,Transform parent) { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform; }
        public static void Stretch(RectTransform rect,float inset=0) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(inset,inset);rect.offsetMax=new Vector2(-inset,-inset);}
    }
}
