#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.Debug
{
    public enum DebugIntent { Speed1, Speed2, Speed4, GrantLevel, ToggleInvulnerable, SpawnNormal, SpawnDouble, SpawnStress, AimMovement, AimNearest, VerifyFixtures, TraceStart, TraceStop }
    public readonly struct DebugSnapshot
    {
        public readonly int Seed, Speed, SpawnPermille, ActiveVisualProjectiles, DroppedEffects, UnsupportedShapes;
        public readonly RunFrame Frame;
        public readonly float FrameMilliseconds;
        public readonly string DataHash;
        public readonly bool Invulnerable;
        public readonly AimMode Aim;
        public DebugSnapshot(int seed, RunFrame frame, float frameMilliseconds, string dataHash, int speed, bool invulnerable, int spawnPermille, AimMode aim, int activeVisualProjectiles = 0, int droppedEffects = 0, int unsupportedShapes = 0)
        { Seed = seed; Frame = frame; FrameMilliseconds = frameMilliseconds; DataHash = dataHash; Speed = speed; Invulnerable = invulnerable; SpawnPermille = spawnPermille; Aim = aim; ActiveVisualProjectiles = activeVisualProjectiles; DroppedEffects = droppedEffects; UnsupportedShapes = unsupportedShapes; }
    }

    public sealed class DebugOverlay : MonoBehaviour
    {
        public const string RequiredGlyphs = "개발 메뉴 닫기 속도 레벨업 무적 전환 출현 이동 방향 조준 가까운 적 기기 재생 검증 이번 틱 공격 사건 표시 투사체 사람 밭 건물 효과 누락 지원 밖 도형 읽기 실패 통과 진단 추적 초 시작 중지 스트레스";
        Action<DebugIntent> send;
        Canvas canvas;
        RectTransform safeRoot;
        GameObject panel;
        UnityEngine.UI.Text metrics;
        readonly System.Collections.Generic.List<UnityEngine.UI.Image> backgrounds = new System.Collections.Generic.List<UnityEngine.UI.Image>();
        readonly System.Collections.Generic.List<UnityEngine.UI.Text> labels = new System.Collections.Generic.List<UnityEngine.UI.Text>();
        readonly System.Collections.Generic.List<UnityEngine.UI.Image> buttonBackgrounds = new System.Collections.Generic.List<UnityEngine.UI.Image>();
        readonly System.Collections.Generic.List<UnityEngine.UI.Text> buttonLabels = new System.Collections.Generic.List<UnityEngine.UI.Text>();
        Color buttonColor = new Color(.2f,.26f,.29f), buttonTextColor = Color.white;
        Color panelColor = Color.white, textColor = Color.black;
        public bool IsOpen { get; private set; }
        public Rect HitBounds
        {
            get
            {
                var safe = Screen.safeArea;

                return IsOpen ? safe : default;
            }
        }
        public bool BlocksPointer(Vector2 position) => HitBounds.Contains(position);
        public void Initialize(Font font, Action<DebugIntent> onIntent)
        {
            send = onIntent ?? throw new ArgumentNullException(nameof(onIntent));
            var root = new GameObject("DevelopmentOverlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(360,800); scaler.matchWidthOrHeight = 0;
            safeRoot = Rect("SafeArea",root.transform); Stretch(safeRoot);
            var panelRect = Rect("Panel",safeRoot); Stretch(panelRect); panel = panelRect.gameObject; Background(panelRect);
            Place(Button(panelRect,"개발 메뉴 닫기",font,()=>SetOpen(false)),8,8,48);
            var viewport = Rect("Viewport",panelRect); Stretch(viewport); viewport.offsetMin = new Vector2(8,8); viewport.offsetMax = new Vector2(-8,-64);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
            var scroller = viewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroller.horizontal = false; scroller.viewport = viewport;
            var content = Rect("Content",viewport); content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1); content.sizeDelta = new Vector2(0,1048); scroller.content = content;
            var label = Rect("Metrics",content); Place(label,0,0,260); metrics = Label(label,font,12,"");
            var names = new[] { "속도 ×1", "속도 ×2", "속도 ×4", "레벨업", "무적 전환", "출현 ×1", "출현 ×2", "진단 스트레스 출현 ×10", "이동 방향 조준", "가까운 적 조준", "기기 재생 5개 검증", "진단 추적 10초 시작", "진단 추적 중지" };
            var intents = new[] { DebugIntent.Speed1,DebugIntent.Speed2,DebugIntent.Speed4,DebugIntent.GrantLevel,DebugIntent.ToggleInvulnerable,DebugIntent.SpawnNormal,DebugIntent.SpawnDouble,DebugIntent.SpawnStress,DebugIntent.AimMovement,DebugIntent.AimNearest,DebugIntent.VerifyFixtures,DebugIntent.TraceStart,DebugIntent.TraceStop };
            for(var i=0;i<intents.Length;i++) { var intent=intents[i]; Place(Button(content,names[i],font,()=>send(intent)),0,268+i*56,48); }
            SetOpen(false); UpdateSafeArea();
        }
        public void SetColors(Color background, Color foreground, Color? button = null, Color? buttonText = null)
        {
            panelColor=background; textColor=foreground;
            if(button.HasValue)buttonColor=button.Value; if(buttonText.HasValue)buttonTextColor=buttonText.Value;
            foreach(var image in backgrounds) image.color=panelColor;
            foreach(var text in labels) text.color=textColor;
            foreach(var image in buttonBackgrounds)image.color=buttonColor;
            foreach(var text in buttonLabels)text.color=buttonTextColor;
        }
        public void Present(DebugSnapshot value)
        {
            if(metrics==null || value.Frame==null)return;
            var frame=value.Frame;
            var fps=value.FrameMilliseconds>0?1000f/value.FrameMilliseconds:0;
            metrics.text=$"seed {value.Seed} · tick {frame.Tick}\nFPS {fps:F1} · {value.FrameMilliseconds:F2} ms · ×{value.Speed}\n적 {frame.Counts.Enemies}\n이번 틱 공격 사건 {frame.Counts.Projectiles}\n표시 투사체 {value.ActiveVisualProjectiles}\n효과 누락 {value.DroppedEffects} · 지원 밖 도형 {value.UnsupportedShapes}\n사람 {frame.Counts.People} · 밭 {frame.Counts.Farms} · 건물 {frame.Counts.Buildings}\n무적 {value.Invulnerable} · 출현 {value.SpawnPermille}/1000\n조준 {value.Aim}\ndata {value.DataHash}";
        }
        public void SetOpen(bool value) { IsOpen=value; if(panel!=null)panel.SetActive(value); }
        void Update() => UpdateSafeArea();
        void UpdateSafeArea()
        {
            if(safeRoot==null)return;
            var safe=Screen.safeArea; safeRoot.anchorMin=new Vector2(safe.xMin/Screen.width,safe.yMin/Screen.height); safeRoot.anchorMax=new Vector2(safe.xMax/Screen.width,safe.yMax/Screen.height);
        }
        RectTransform Button(Transform parent,string title,Font font,Action callback)
        {
            var rect=Rect(title,parent); Background(rect);
            var image=rect.GetComponent<UnityEngine.UI.Image>(); image.color=buttonColor; buttonBackgrounds.Add(image);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic=image; button.onClick.AddListener(()=>callback());
            var textRect=Rect("Label",rect); Stretch(textRect); var text=Label(textRect,font,14,title); text.alignment=TextAnchor.MiddleCenter; text.color=buttonTextColor; buttonLabels.Add(text);
            return rect;
        }
        UnityEngine.UI.Text Label(RectTransform rect,Font font,int size,string value)
        {
            var text=rect.gameObject.AddComponent<UnityEngine.UI.Text>(); text.font=font; text.fontSize=size; text.text=value; text.color=textColor; text.raycastTarget=false; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Overflow; labels.Add(text); return text;
        }
        void Background(RectTransform rect) { var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=panelColor; backgrounds.Add(image); }
        static RectTransform Rect(string name,Transform parent) { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform; }
        static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero; }
        static void Place(RectTransform rect,float margin,float top,float height) { rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-top);rect.sizeDelta=new Vector2(-margin*2,height); }
    }
}
#endif
