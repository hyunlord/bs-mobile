using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        bool visualReviewRequested;
        string visualReviewOutput;
        int visualReviewWidth, visualReviewHeight;
        string[] visualReviewEnemyIds;
        VisualReviewResult visualReviewResult;
        Rect visualReviewUsableArea;
        readonly List<VisualEnemySample> visualEnemyCandidates=new List<VisualEnemySample>();
        readonly Dictionary<string,VisualEnemySample> visualEnemySamples=new Dictionary<string,VisualEnemySample>(StringComparer.Ordinal);
        [Serializable] sealed class VisualReviewResult
        {
            public string mode, inputSha256, inputDataHash, renderedDataHash, historicalTerminalHash, commit, sourceHash, error;
            public bool historicalTerminalVerified, sourceDirty;
            public int width, height, requestedTick;
            public string[] requestedEnemyIds;
            public string boundary = "Static runtime replay snapshots after 30 presentation warmup frames. No real-time motion or performance measurement. Current mode re-simulates original commands under current rules and makes no historical hash equality claim. PNGs are actual native frame captures, not actor galleries.";
            public List<VisualReviewFrame> frames = new List<VisualReviewFrame>();
        }
        [Serializable] sealed class VisualReviewFrame
        {
            public int tick, enemies;
            public string file, stateHash;
            public Rect usableWorldScreenArea;
            public List<string> enemyKinds = new List<string>();
            public List<string> missingRequestedEnemyKinds = new List<string>();
            public List<VisualEnemySample> actualScreenSamples = new List<VisualEnemySample>();
        }
        [Serializable] sealed class VisualEnemySample
        {
            public string definitionId, cropFile;
            public int entityId;
            public Rect worldBounds, screenBounds;
            public bool hudUnobscured, noOtherEnemyBoundsOverlap;
            public float enemyBoundsOverlapFraction;
            public string boundary="Actual rendered quad AABB crop at native pixels; HUD excluded using actual RectTransforms; least enemy-AABB overlap preferred. Remaining scenery/actor occlusion is not pixel-tested, not an isolated or rescaled specimen.";
        }
        void CaptureVisualEnemyBounds(string definitionId,int entityId,Rect worldBounds)
        {
            var camera=Camera.main;
            var minimum=(Vector2)camera.WorldToScreenPoint(new Vector3(worldBounds.xMin,worldBounds.yMin,0));
            var maximum=(Vector2)camera.WorldToScreenPoint(new Vector3(worldBounds.xMax,worldBounds.yMax,0));
            var screen=Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
            if(screen.width<1||screen.height<1||screen.xMin<0||screen.yMin<0||screen.xMax>Screen.width||screen.yMax>Screen.height)return;
            visualEnemyCandidates.Add(new VisualEnemySample { definitionId=definitionId,entityId=entityId,worldBounds=worldBounds,screenBounds=screen });
        }
        Rect VisualWorldUsableArea()
        {
            var area=Screen.safeArea;
            var corners=new Vector3[4];
            foreach(var name in new[]{"HUD","Loadout"})
            {
                var rect=Ui.Content.Find(name) as RectTransform;
                if(rect==null||!rect.gameObject.activeInHierarchy)continue;
                rect.GetWorldCorners(corners);
                var bottom=RectTransformUtility.WorldToScreenPoint(null,corners[0]).y;
                var top=RectTransformUtility.WorldToScreenPoint(null,corners[2]).y;
                if(name=="HUD")area.yMax=Mathf.Min(area.yMax,bottom);
                else area.yMin=Mathf.Max(area.yMin,top);
            }
            return area;
        }
        static bool ContainsEntireRect(Rect area,Rect sample)
            => sample.xMin>=area.xMin&&sample.yMin>=area.yMin&&sample.xMax<=area.xMax&&sample.yMax<=area.yMax;
        void SelectVisualEnemySamples()
        {
            visualEnemySamples.Clear();
            foreach(var sample in visualEnemyCandidates)
            {
                var cropBounds=Rect.MinMaxRect(Mathf.Floor(sample.screenBounds.xMin),Mathf.Floor(sample.screenBounds.yMin),Mathf.Ceil(sample.screenBounds.xMax),Mathf.Ceil(sample.screenBounds.yMax));
                if(!ContainsEntireRect(visualReviewUsableArea,cropBounds))continue;
                var overlap=0f;
                foreach(var other in visualEnemyCandidates)
                {
                    if(other.entityId==sample.entityId)continue;
                    overlap+=Mathf.Max(0,Mathf.Min(sample.screenBounds.xMax,other.screenBounds.xMax)-Mathf.Max(sample.screenBounds.xMin,other.screenBounds.xMin))
                        *Mathf.Max(0,Mathf.Min(sample.screenBounds.yMax,other.screenBounds.yMax)-Mathf.Max(sample.screenBounds.yMin,other.screenBounds.yMin));
                }
                sample.hudUnobscured=true;
                sample.enemyBoundsOverlapFraction=Mathf.Clamp01(overlap/(sample.screenBounds.width*sample.screenBounds.height));
                sample.noOtherEnemyBoundsOverlap=overlap==0;
                if(visualEnemySamples.TryGetValue(sample.definitionId,out var prior)&&
                    (prior.enemyBoundsOverlapFraction<sample.enemyBoundsOverlapFraction||
                     prior.enemyBoundsOverlapFraction==sample.enemyBoundsOverlapFraction&&prior.entityId<sample.entityId))continue;
                visualEnemySamples[sample.definitionId]=sample;
            }
        }
        static string VisualArgument(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            if(index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || Array.LastIndexOf(args, name) != index)
                throw new ArgumentException(name + " requires exactly one value.");
            return args[index + 1];
        }
        void TryStartVisualReview()
        {
            var args = Environment.GetCommandLineArgs();
            if(Array.IndexOf(args, "--visual-review") < 0) return;
            visualReviewRequested = true;
            try
            {
                if(!IsWave || AutoplayCapture.Active != null || Array.IndexOf(args,"--wave-benchmark") >= 0)
                    throw new ArgumentException("Visual review requires wave-1a without autoplay or benchmark.");
                visualReviewOutput = Path.GetFullPath(VisualArgument(args,"--visual-review-output"));
                if(Directory.Exists(visualReviewOutput) && Directory.GetFileSystemEntries(visualReviewOutput).Length != 0)
                    throw new IOException("Visual review output must be empty.");
                Directory.CreateDirectory(visualReviewOutput);
                visualReviewWidth=int.Parse(VisualArgument(args,"-screen-width"),System.Globalization.CultureInfo.InvariantCulture);
                visualReviewHeight=int.Parse(VisualArgument(args,"-screen-height"),System.Globalization.CultureInfo.InvariantCulture);
                if(visualReviewWidth < 320 || visualReviewHeight < 320 || visualReviewWidth > 4096 || visualReviewHeight > 4096)
                    throw new ArgumentException("Visual viewport dimensions must be between 320 and 4096 pixels.");
                if(Array.IndexOf(args,"--visual-review-enemies") >= 0)
                    visualReviewEnemyIds=VisualArgument(args,"--visual-review-enemies").Split(',');
                var mode = VisualArgument(args,"--visual-review-catalog");
                if(mode != "historical" && mode != "current") throw new ArgumentException("Visual catalog must be historical or current.");
                var tick = int.Parse(VisualArgument(args,"--visual-review-tick"),System.Globalization.CultureInfo.InvariantCulture);
                if(tick < 60) throw new ArgumentOutOfRangeException("tick");
                var bytes = File.ReadAllBytes(VisualArgument(args,"--visual-review"));
                ReplayDocument replay;
                using(var stream = new MemoryStream(bytes)) replay = ReplayCodec.Read(stream);
                if(tick + 60 >= replay.End.Tick) throw new ArgumentException("Visual window must precede original terminal tick.");
                string inputHash;
                using(var sha = SHA256.Create()) inputHash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                visualReviewResult = new VisualReviewResult { mode=mode,inputSha256=inputHash,inputDataHash=replay.Header.Options.DataHash,
                    requestedTick=tick,requestedEnemyIds=visualReviewEnemyIds,commit=BuildIdentity.Commit,sourceHash=BuildIdentity.SourceHash,sourceDirty=BuildIdentity.SourceDirty };
                ContentCatalog catalog = FoundationBoot.Catalog; var hash = FoundationBoot.VerifiedDataHash;
                if(mode == "historical")
                {
                    catalog=null;hash=null;LoadFrozenWaveBenchmark(ref catalog,ref hash);
                    if(catalog == null || hash != replay.Header.Options.DataHash) throw new InvalidDataException("Build lacks the matching frozen historical catalog.");
                    var verified = ReplayRunner.Verify(catalog,hash,replay);
                    visualReviewResult.historicalTerminalHash=verified.StateHash;visualReviewResult.historicalTerminalVerified=true;
                }
                visualReviewResult.renderedDataHash=hash;
                runCatalog=catalog;
                Application.runInBackground=true;
                Ui.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled=false;
                StartCoroutine(GuardVisualReview(RenderVisualReview(replay,hash,tick)));
            }
            catch(Exception exception) { FinishVisualReview(exception); }
        }
        IEnumerator GuardVisualReview(IEnumerator work)
        {
            while(true)
            {
                bool more;
                try { more=work.MoveNext(); }
                catch(Exception exception) { FinishVisualReview(exception);yield break; }
                if(!more) { FinishVisualReview(null);yield break; }
                yield return work.Current;
            }
        }
        public static bool ApplyVisualReviewCommandBeforeTick(InteractiveSession session, IReadOnlyList<ReplayCommand> commands, ref int index, int targetTick)
        {
            if(index >= commands.Count || commands[index].Tick >= targetTick) return false;
            session.Apply(commands[index]);index++;return true;
        }
        IEnumerator RenderVisualReview(ReplayDocument replay,string hash,int targetTick)
        {
            yield return SceneManager.LoadSceneAsync("Run");
            Screen.SetResolution(visualReviewWidth,visualReviewHeight,FullScreenMode.Windowed);
            for(var resizeFrame=0;resizeFrame<60&&(Screen.width!=visualReviewWidth||Screen.height!=visualReviewHeight);resizeFrame++) yield return null;
            if(Screen.width!=visualReviewWidth||Screen.height!=visualReviewHeight) throw new InvalidOperationException("Native visual viewport did not reach the explicitly requested dimensions.");
            Ui.RefreshSafeArea();
            Session=new InteractiveSession(runCatalog,replay.Header.Options with { DataHash=hash });
            var commandIndex=0;
            world=new GameObject("Visual review world").AddComponent<WorldRenderer>();
            var settings=CanonicalContent.Presentation.Camera;
            CaptureSnapshots();previous=Frame;
            world.SetFallowChapter(runCatalog.WaveRuntime?.ChapterId=="meta:chapter_1");
            world.Initialize(Camera.main,new WorldCameraSettings(settings.WorldUnitsPerUnityUnit,settings.MinHalfHeight,settings.MaxHalfHeight,settings.EstatePadding,settings.FollowMilliseconds,settings.ZoomMilliseconds),runCatalog.Tuning.DefaultEstate,Frame.MapWidth,Frame.MapHeight);
            ShowHud();Ui.ShowStick(false,Vector2.zero,Vector2.zero);
            foreach(var tick in new[]{targetTick-60,targetTick,targetTick+60})
            {
                while(ApplyVisualReviewCommandBeforeTick(Session,replay.Commands,ref commandIndex,tick))
                {
                    if(commandIndex % 300 == 0) yield return null;
                }
                CaptureSnapshots();
                if(Frame.Tick != tick) throw new InvalidDataException("Original commands did not reach the requested tick.");
                previous=Frame;world.AcceptWave(runCatalog,Wave);world.AcceptFrame(Frame,FirstPlayable);hud.Present(Frame,FirstPlayable);
                Canvas.ForceUpdateCanvases();
                var visible=Screen.safeArea;
                visible.yMin+=Mathf.Min(UiTokens.BottomWorldInset*Ui.Canvas.scaleFactor,visible.height*.2f);
                visible.yMax=Mathf.Max(visible.yMin+1,visible.yMax-hud.ReservedTopPixels);
                visualEnemyCandidates.Clear();
                visualReviewUsableArea=VisualWorldUsableArea();
                for(var warm=0;warm<30;warm++)
                {
                    // Resume before camera rendering; EOF is capture-only, never a draw submission phase.
                    yield return null;
                    world.RenderedEnemyBoundsSample=warm==29?CaptureVisualEnemyBounds:null;
                    world.Present(Frame,Frame,FirstPlayable,1,1f/60,visible);
                    yield return new WaitForEndOfFrame();
                }
                if(Screen.width!=visualReviewWidth||Screen.height!=visualReviewHeight) throw new InvalidOperationException("Visual viewport changed before capture.");
                var filename="tick-"+Frame.Tick+".png";
                world.RenderedEnemyBoundsSample=null;
                SelectVisualEnemySamples();
                var row=new VisualReviewFrame { tick=Frame.Tick,enemies=Frame.Enemies.Count,file=filename,stateHash=Session.ComputeStateHash(),usableWorldScreenArea=visualReviewUsableArea };
                var texture=ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    if(texture.width!=visualReviewWidth||texture.height!=visualReviewHeight) throw new InvalidOperationException("Captured texture dimensions differ from requested native viewport.");
                    File.WriteAllBytes(Path.Combine(visualReviewOutput,filename),texture.EncodeToPNG());
                    var definitions=visualReviewEnemyIds == null ? new List<string>() : new List<string>(visualReviewEnemyIds);
                    if(visualReviewEnemyIds==null)foreach(var candidate in visualEnemyCandidates)
                        if(!definitions.Contains(candidate.definitionId))definitions.Add(candidate.definitionId);
                    definitions.Sort(StringComparer.Ordinal);
                    for(var index=0;index<definitions.Count;index++)
                    {
                        if(!visualEnemySamples.TryGetValue(definitions[index],out var sample))
                        { row.missingRequestedEnemyKinds.Add(definitions[index]);continue; }
                        var x=Mathf.FloorToInt(sample.screenBounds.xMin);var y=Mathf.FloorToInt(sample.screenBounds.yMin);
                        var width=Mathf.Min(texture.width-x,Mathf.CeilToInt(sample.screenBounds.xMax)-x);
                        var height=Mathf.Min(texture.height-y,Mathf.CeilToInt(sample.screenBounds.yMax)-y);
                        var crop=new Texture2D(width,height,TextureFormat.RGB24,false);
                        try
                        {
                            crop.SetPixels(texture.GetPixels(x,y,width,height));crop.Apply();
                            sample.cropFile="tick-"+Frame.Tick+"-enemy-"+sample.entityId+".png";
                            File.WriteAllBytes(Path.Combine(visualReviewOutput,sample.cropFile),crop.EncodeToPNG());
                        }
                        finally { Destroy(crop); }
                        row.actualScreenSamples.Add(sample);
                    }
                }
                finally { Destroy(texture); }
                foreach(var enemy in Frame.Enemies) if(!row.enemyKinds.Contains(enemy.DefinitionId)) row.enemyKinds.Add(enemy.DefinitionId);
                row.enemyKinds.Sort(StringComparer.Ordinal);visualReviewResult.frames.Add(row);
                visualReviewResult.width=Screen.width;visualReviewResult.height=Screen.height;
            }
        }
        void FinishVisualReview(Exception exception)
        {
            if(visualReviewResult == null) visualReviewResult=new VisualReviewResult();
            visualReviewResult.error=exception?.ToString();
            if(visualReviewOutput != null && Directory.Exists(visualReviewOutput))
                File.WriteAllText(Path.Combine(visualReviewOutput,"visual-review.json"),JsonUtility.ToJson(visualReviewResult,true));
            if(exception != null) UnityEngine.Debug.LogException(exception);
            Application.Quit(exception == null ? 0 : 1);
        }
    }
}
