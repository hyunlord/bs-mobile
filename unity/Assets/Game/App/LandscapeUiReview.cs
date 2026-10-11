using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.App.Generated;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class LandscapeUiReview : MonoBehaviour
    {
        static LandscapeUiReview active;
        public static bool HoldCards => active != null && !active.finished;
        static readonly Vector2Int[] Sizes = { new Vector2Int(1920,1080), new Vector2Int(1440,900), new Vector2Int(1280,800) };
        RunCoordinator run;
        string folder;
        bool finished;
        readonly List<Shot> shots = new List<Shot>();
        [Serializable] public sealed class Element
        {
            public string path, kind, text;
            public Rect bounds, drawable;
            public float preferredWidth, preferredHeight, rectWidth, rectHeight;
            public int renderedLines, fontSize;
            public bool fullyInsideDrawable, preferredHeightFits, interactable;
        }
        [Serializable] public sealed class Shot
        {
            public string image, phase, scope;
            public int requestedWidth, requestedHeight, actualWidth, actualHeight, tick;
            public bool exactResolution, focused;
            public Rect worldOverlayBounds;
            public Element[] elements;
        }
        [Serializable] sealed class Report
        {
            public string commit, sourceHash, status, failure;
            public string scope = "Actual native production UI screenshots; live battle and naturally offered cards only. Not exhaustive catalog coverage, gameplay acceptance, or performance evidence. Text clipping and offscreen scroll content are reported separately by screenshot.";
            public Shot[] shots;
        }

        public static string ParseOutput(string[] args)
        {
            if (Array.IndexOf(args,"--landscape-ui-review") < 0) return null;
            var index = Array.IndexOf(args,"--landscape-ui-output");
            if(index < 0 || index+1 >= args.Length || args[index+1].StartsWith("--",StringComparison.Ordinal))
                throw new ArgumentException("Landscape UI review requires --landscape-ui-output with a new directory.");
            return Path.GetFullPath(args[index+1]);
        }
        public static void Create(RunCoordinator coordinator)
        {
            if(active != null || coordinator.Ui == null) return;
            var output = ParseOutput(Environment.GetCommandLineArgs());
            if(output == null) return;
            if(File.Exists(output) || Directory.Exists(output)) throw new IOException("Landscape UI review output must be a new directory.");
            Directory.CreateDirectory(output);
            active = coordinator.gameObject.AddComponent<LandscapeUiReview>();
            active.run = coordinator; active.folder = output;
            active.StartCoroutine(active.Review());
        }
        IEnumerator Review()
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 300;
            while(run.CurrentFrame == null || run.CurrentFrame.Status != RunStatus.Running || run.MenuOpen)
            {
                if(Time.realtimeSinceStartupAsDouble > deadline) { Complete("Timed out waiting for live battle HUD."); yield break; }
                yield return null;
            }
            foreach(var size in Sizes)
            {
                yield return Resize(size);
                if(run.CurrentFrame.Status != RunStatus.Running || run.MenuOpen) { Complete("Battle HUD was interrupted before all resolutions were captured."); yield break; }
                yield return new WaitForEndOfFrame();
                if(!Capture("battle",size,"live battle HUD")) yield break;
            }
            while(run.CurrentFrame.Status != RunStatus.AwaitingCard)
            {
                if(Time.realtimeSinceStartupAsDouble > deadline || run.CurrentFrame.Status == RunStatus.Completed)
                { Complete("No naturally earned card offer before timeout or run completion."); yield break; }
                yield return null;
            }
            foreach(var size in Sizes)
            {
                yield return Resize(size);
                if(run.CurrentFrame.Status != RunStatus.AwaitingCard) { Complete("Card offer was selected during review."); yield break; }
                var scroll = run.Ui.Content.GetComponentInChildren<ScrollRect>();
                if(scroll != null)scroll.verticalNormalizedPosition = 1;
                yield return null; Canvas.ForceUpdateCanvases();
                yield return new WaitForEndOfFrame();
                if(!Capture("cards-top",size,"naturally offered cards; full simultaneous layout, or scroll top when present")) yield break;
                if(scroll != null)
                {
                    scroll.verticalNormalizedPosition = 0;
                    yield return null; Canvas.ForceUpdateCanvases();
                    yield return new WaitForEndOfFrame();
                    if(!Capture("cards-bottom",size,"same naturally offered cards at scroll bottom; diagnostic only, cannot rescue top clipping")) yield break;
                }
            }
            Complete(null);
        }
        IEnumerator Resize(Vector2Int size)
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
            var deadline=Time.realtimeSinceStartupAsDouble+5;
            do { yield return null; } while((Screen.width!=size.x || Screen.height!=size.y) && Time.realtimeSinceStartupAsDouble<deadline);
            for(var i=0;i<4;i++) yield return null;
            Canvas.ForceUpdateCanvases();
        }
        bool Capture(string phase,Vector2Int requested,string scope)
        {
            try
            {
                var image = phase+"-"+requested.x+"x"+requested.y+".png";
                var shot = new Shot { image=image,phase=phase,scope=scope,requestedWidth=requested.x,requestedHeight=requested.y,
                    actualWidth=Screen.width,actualHeight=Screen.height,exactResolution=Screen.width==requested.x && Screen.height==requested.y,
                    focused=Application.isFocused,tick=run.CurrentFrame.Tick,worldOverlayBounds=run.Ui.GetWorldOverlayBounds(),elements=Measure(run.Ui.Content) };
                var texts=0;var buttons=0;
                foreach(var element in shot.elements){if(element.kind=="text")texts++;else if(element.kind=="button")buttons++;}
                if(texts==0 || buttons==0)throw new InvalidOperationException("Review surface has no text or buttons; cannot count an empty UI as captured.");
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(Path.Combine(folder,image),texture.EncodeToPNG()); }
                finally { Destroy(texture); }
                shots.Add(shot);
                File.WriteAllText(Path.Combine(folder,phase+"-"+requested.x+"x"+requested.y+".json"),JsonUtility.ToJson(shot,true));
                return true;
            }
            catch(Exception exception) { Complete(exception.ToString()); return false; }
        }
        public static Rect Intersection(Rect a,Rect b)
        {
            var left=Mathf.Max(a.xMin,b.xMin);var bottom=Mathf.Max(a.yMin,b.yMin);
            return new Rect(left,bottom,Mathf.Max(0,Mathf.Min(a.xMax,b.xMax)-left),Mathf.Max(0,Mathf.Min(a.yMax,b.yMax)-bottom));
        }
        public static bool ContainsBounds(Rect outer,Rect inner,float tolerance=.5f)
            => inner.width>0 && inner.height>0 && inner.xMin>=outer.xMin-tolerance && inner.yMin>=outer.yMin-tolerance && inner.xMax<=outer.xMax+tolerance && inner.yMax<=outer.yMax+tolerance;
        static Rect Bounds(RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            var first=RectTransformUtility.WorldToScreenPoint(null,corners[0]);
            var last=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
            return Rect.MinMaxRect(first.x,first.y,last.x,last.y);
        }
        static string ElementPath(Transform node,Transform root)
        {
            var result=node.name;
            while(node.parent!=null && node!=root){node=node.parent;result=node.name+"/"+result;}
            return result;
        }
        static Rect Drawable(RectTransform rect)
        {
            var result=new Rect(0,0,Screen.width,Screen.height);
            for(var node=rect.parent;node!=null;node=node.parent)
            {
                var mask=node.GetComponent<RectMask2D>();var stencil=node.GetComponent<Mask>();
                if(mask!=null && mask.isActiveAndEnabled || stencil!=null && stencil.isActiveAndEnabled)
                    result=Intersection(result,Bounds((RectTransform)node));
            }
            return result;
        }
        static Element[] Measure(RectTransform root)
        {
            var rows=new List<Element>();
            foreach(var text in root.GetComponentsInChildren<Text>())
            {
                var rect=text.rectTransform;var bounds=Bounds(rect);var drawable=Drawable(rect);
                rows.Add(new Element { path=ElementPath(text.transform,root),kind="text",text=text.text,bounds=bounds,drawable=drawable,
                    preferredWidth=text.preferredWidth,preferredHeight=text.preferredHeight,rectWidth=rect.rect.width,rectHeight=rect.rect.height,
                    renderedLines=text.cachedTextGenerator.lineCount,fontSize=text.fontSize,fullyInsideDrawable=ContainsBounds(drawable,bounds),
                    preferredHeightFits=text.preferredHeight<=rect.rect.height+.5f });
            }
            foreach(var button in root.GetComponentsInChildren<Button>())
            {
                var rect=(RectTransform)button.transform;var bounds=Bounds(rect);var drawable=Drawable(rect);
                var labels=button.GetComponentsInChildren<Text>();var label="";
                foreach(var text in labels)label+=(label.Length==0?"":"\n")+text.text;
                rows.Add(new Element { path=ElementPath(button.transform,root),kind="button",text=label,bounds=bounds,drawable=drawable,
                    rectWidth=rect.rect.width,rectHeight=rect.rect.height,fullyInsideDrawable=ContainsBounds(drawable,bounds),interactable=button.interactable });
            }
            return rows.ToArray();
        }
        void Complete(string failure)
        {
            if(finished)return;
            finished=true;
            var exact=true;var focused=true;foreach(var shot in shots){exact &= shot.exactResolution;focused &= shot.focused;}
            var clipped=false;
            foreach(var shot in shots)
                if(shot.phase!="cards-bottom")
                    foreach(var element in shot.elements)
                        if(!element.fullyInsideDrawable || element.kind=="text" && !element.preferredHeightFits)clipped=true;
            var report=new Report { commit=BuildIdentity.Commit,sourceHash=BuildIdentity.SourceHash,
                status=failure!=null?"failed":!exact?"requested-resolution-unavailable":!focused?"unfocused-capture":clipped?"clipped-or-offscreen-elements":"captured-needs-visual-review",failure=failure,shots=shots.ToArray() };
            File.WriteAllText(Path.Combine(folder,"landscape-ui-review.json"),JsonUtility.ToJson(report,true));
            UnityEngine.Debug.Log("Landscape UI review: "+report.status+"; "+folder);
            Application.Quit(failure!=null || !exact || !focused || clipped?1:0);
        }
    }
}
