using System;
using System.Collections.Generic;
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
        readonly Dictionary<string,Sprite> slicedSprites=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        private Rect lastSafe;
        private Vector2 lastScreen;
        private RectTransform stickBase, stickKnob;
        public void Initialize(Font font)
        {
            Font = font;
            Canvas = gameObject.AddComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(900, 1600); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0;
            gameObject.AddComponent<GraphicRaycaster>();
            TouchSurface = Rect("Movement surface", transform); Stretch(TouchSurface); var hit = TouchSurface.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            SafeRoot = Rect("Safe area", transform); Stretch(SafeRoot);
            Content = Rect("Content", SafeRoot); Stretch(Content);
            stickBase = Rect("Joystick", transform); stickBase.sizeDelta = Vector2.one * UiTokens.StickRadius * 2; StickSurface(stickBase, "ui.joystick.base", new Rect(4,36,174,174));
            stickKnob = Rect("Knob", stickBase); stickKnob.sizeDelta = Vector2.one * UiTokens.StickThumbDiameter; StickSurface(stickKnob, "ui.joystick.thumb", new Rect(6,5,153,153));
            foreach (var graphic in stickBase.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            stickBase.gameObject.SetActive(false); RefreshSafeArea();
        }
        public void RefreshSafeArea()
        {
            if (SafeRoot == null) return;
            var safe = Screen.safeArea; if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0,0,Screen.width,Screen.height);
            GetComponent<CanvasScaler>().referenceResolution = Screen.height / (float)Screen.width < 1.35f ? new Vector2(900,900) : new Vector2(900,1600);
            lastSafe = safe; lastScreen = new Vector2(Screen.width,Screen.height);
            SafeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height); SafeRoot.anchorMax = new Vector2(safe.xMax / Screen.width,safe.yMax / Screen.height); SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
        }
        private void Update() { if (lastSafe != Screen.safeArea || lastScreen != new Vector2(Screen.width, Screen.height)) RefreshSafeArea(); }
        public void ShowStick(bool active, Vector2 origin, Vector2 offset)
        {
            stickBase.gameObject.SetActive(active); if (!active) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, origin, null, out var point); stickBase.anchoredPosition = point; stickKnob.anchoredPosition = StickVisualOffset(offset, Canvas.scaleFactor);
        }
        public static Vector2 StickVisualOffset(Vector2 screenOffset, float canvasScale)
        {
            var normalized = Vector2.ClampMagnitude(screenOffset / Mathf.Max(.001f, canvasScale) / UiTokens.StickRadius, 1);
            return normalized * (UiTokens.StickRadius - UiTokens.StickThumbDiameter * .5f);
        }
        void StickSurface(RectTransform parent, string role, Rect inkBounds)
        {
            var source = ArtCatalog.Load().ResolveRole(role).Sprite;
            var rect = new Rect(source.rect.x + inkBounds.x, source.rect.y + inkBounds.y, inkBounds.width, inkBounds.height);
            var sprite = Sprite.Create(source.texture, rect, new Vector2(.5f,.5f), 100);
            slicedSprites.Add(role + " centered", sprite);
            var image = parent.gameObject.AddComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        }
        public void Clear() { foreach (Transform child in Content) { child.gameObject.SetActive(false); Destroy(child.gameObject); } }
        public RectTransform Panel(string name)
        {
            var p = Rect(name, Content); Stretch(p,UiTokens.Padding); Surface(p,"ui.panel").raycastTarget=true;
            var scrollObject = Rect("Scroll",p); Stretch(scrollObject,UiTokens.PanelInset); var scroll=scrollObject.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false;
            scrollObject.gameObject.AddComponent<Image>().color=Color.clear; scrollObject.gameObject.AddComponent<RectMask2D>();
            var body=Rect("Body",scrollObject); body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.sizeDelta=Vector2.zero;
            var layout=body.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=UiTokens.Gap;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childControlWidth=true;layout.childForceExpandWidth=true;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.content=body;scroll.viewport=scrollObject;
            return body;
        }
        public Text Label(Transform parent,string text,int size=UiTokens.Body,float height=40)
        {
            var rect=Rect(text,parent); var label=rect.gameObject.AddComponent<Text>();label.font=Font;label.fontSize=size;label.text=text;label.color=GameVisualTokens.Ink;label.alignment=TextAnchor.MiddleLeft;label.lineSpacing=1.25f;label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Overflow;label.raycastTarget=false;
            var layout=rect.gameObject.AddComponent<LayoutElement>();layout.minHeight=height;return label;
        }
        public Button Button(Transform parent,string text,Action action,bool enabled=true)
        {
            var rect=Rect(text,parent);var image=Surface(rect,"ui.button.primary");image.raycastTarget=true;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.interactable=enabled;button.onClick.AddListener(()=>action());
            var colors=button.colors;colors.disabledColor=new Color(.6f,.63f,.64f);colors.pressedColor=new Color(.65f,.72f,.76f);button.colors=colors;
            var layout=rect.gameObject.AddComponent<LayoutElement>();layout.minHeight=UiTokens.TouchHeight;layout.preferredHeight=UiTokens.TouchHeight;
            var label=Label(rect,text,UiTokens.Body,UiTokens.TouchHeight);Stretch(label.rectTransform,8);label.alignment=TextAnchor.MiddleCenter;label.color=GameVisualTokens.Attack;
            return button;
        }
        public Image Surface(Transform parent,string roleId)
        {
            var image=parent.gameObject.AddComponent<Image>();ApplySurface(image,roleId);image.raycastTarget=false;return image;
        }
        void ApplySurface(Image image,string roleId)
        {
            var source=ArtCatalog.Load().ResolveRole(roleId).Sprite;
            var framed=roleId=="ui.panel"||roleId=="ui.hint"||roleId.StartsWith("ui.card.",StringComparison.Ordinal)||roleId.StartsWith("ui.button.",StringComparison.Ordinal)||roleId.EndsWith(".rail",StringComparison.Ordinal)||roleId.EndsWith(".fill",StringComparison.Ordinal);
            if(!framed){image.sprite=source;return;}
            if(!slicedSprites.TryGetValue(roleId,out var sprite))
            {
                var rect=source.rect;var x=rect.width*UiTokens.FrameBorderFraction;var y=rect.height*UiTokens.FrameBorderFraction;
                sprite=Sprite.Create(source.texture,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(x,y,x,y));
                sprite.name=roleId+" sliced";slicedSprites.Add(roleId,sprite);
            }
            image.sprite=sprite;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=UiTokens.FramePixelsPerUnitMultiplier;
        }
        void OnDestroy()
        {
            foreach(var sprite in slicedSprites.Values)if(sprite!=null){if(Application.isPlaying)Destroy(sprite);else DestroyImmediate(sprite);}
            slicedSprites.Clear();
        }
        public Image Icon(Transform parent,string contentId,float size=UiTokens.IconSize)
        {
            var rect=Rect("Icon "+contentId,parent);rect.sizeDelta=Vector2.one*size;
            var layout=rect.gameObject.AddComponent<LayoutElement>();layout.minWidth=layout.preferredWidth=size;layout.minHeight=layout.preferredHeight=size;
            var image=rect.gameObject.AddComponent<Image>();image.sprite=ArtCatalog.Load().Resolve("icon",contentId,"default").Sprite;image.preserveAspect=true;image.raycastTarget=false;return image;
        }
        public Button Toggle(Transform parent,string text,bool value,Action<bool> changed)
        {
            var button=Button(parent,text,()=>{});ApplySurface(button.image,"ui.button.secondary");button.GetComponentInChildren<Text>().color=GameVisualTokens.Ink;
            var mark=Rect("Selected",button.transform);mark.anchorMin=mark.anchorMax=new Vector2(1,.5f);mark.pivot=new Vector2(1,.5f);mark.anchoredPosition=new Vector2(-UiTokens.Padding,0);mark.sizeDelta=Vector2.one*40;
            Surface(mark,"ui.confirm");mark.gameObject.SetActive(value);
            button.onClick.AddListener(()=>{value=!value;mark.gameObject.SetActive(value);changed(value);});return button;
        }
        public Slider Slider(Transform parent,string text,float value,Action<float> changed)
        {
            var column=Rect(text,parent);var layout=column.gameObject.AddComponent<VerticalLayoutGroup>();layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.spacing=8;
            var title=Label(column,text+" · "+Mathf.RoundToInt(Mathf.Clamp01(value)*100)+"%");
            var rect=Rect("Slider",column);rect.gameObject.AddComponent<LayoutElement>().preferredHeight=UiTokens.TouchHeight;
            var hit=rect.gameObject.AddComponent<Image>();hit.color=Color.clear;
            var rail=Rect("Rail",rect);rail.anchorMin=new Vector2(0,.35f);rail.anchorMax=new Vector2(1,.65f);rail.offsetMin=rail.offsetMax=Vector2.zero;Surface(rail,"ui.xp.rail");
            var fill=Rect("Fill",rail);Stretch(fill);Surface(fill,"ui.xp.fill");
            var handleArea=Rect("Handle area",rect);Stretch(handleArea,UiTokens.Padding);
            var handle=Rect("Handle",handleArea);handle.sizeDelta=Vector2.one*64;var thumb=Surface(handle,"ui.joystick.thumb");
            var slider=rect.gameObject.AddComponent<Slider>();slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=thumb;slider.minValue=0;slider.maxValue=1;slider.SetValueWithoutNotify(Mathf.Clamp01(value));
            slider.onValueChanged.AddListener(v=>{title.text=text+" · "+Mathf.RoundToInt(v*100)+"%";changed(v);});return slider;
        }
        public Text Hint(string text)
        {
            var rect=Rect("Hint",Content);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.18f);rect.sizeDelta=new Vector2(720,120);Surface(rect,"ui.hint");
            var label=Label(rect,text,UiTokens.Body,80);Stretch(label.rectTransform,UiTokens.Padding);label.alignment=TextAnchor.MiddleCenter;return label;
        }
        public static RectTransform Rect(string name,Transform parent) { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform; }
        public static void Stretch(RectTransform rect,float inset=0) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(inset,inset);rect.offsetMax=new Vector2(-inset,-inset);}
    }
}
