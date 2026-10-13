using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SowSiege.Core;

namespace Game.View
{
    [Serializable] public sealed class HeroProductionManifest
    {
        public int schemaVersion;
        public string method;
        public float headHeightRatio;
        public HeroProductionClip[] clips;
    }
    [Serializable] public sealed class HeroProductionClip
    {
        public string id, direction, texture, authoredDirection;
        public int columns, rows, frames;
        public float fps, pivotX, pivotY, bodyHeightPixels, headHeightPixels;
        public bool mirror;
        [NonSerialized] public ArtVisual visual;
    }

    public sealed partial class WorldRenderer
    {
        HeroProductionManifest productionHero;
        readonly Dictionary<string, Texture2D> productionTextures = new Dictionary<string, Texture2D>();
        string productionDirection = "E", productionAction;
        bool productionFollowFacing,productionEightDirections;
        static readonly string[] ProductionDirections={"E","NE","N","NW","W","SW","S","SE"};
        Vector2 productionPreviousPosition;
        bool productionHasPosition, productionWasHit;
        float productionHitStarted=-100;
        string productionAttackDirection="E";
        public int HeroProductionTargetId {get;private set;}=-1;
        public int HeroProductionAttackTick {get;private set;}=-1;
        public Vector2 HeroProductionTargetDirection {get;private set;}
        public string HeroProductionDirection => productionDirection;
        public string HeroProductionSlashDirection => productionAttackDirection;
        public static string ProductionDirection(Vector2 delta)
        {
            var octant=Mathf.RoundToInt(Mathf.Atan2(delta.y,delta.x)*4/Mathf.PI);
            return ProductionDirections[(octant+8)%8];
        }
        public void AcceptHeroProductionAttack(WaveEvent value)
        {
            if(productionHero==null || value.Kind!="attack" ||
                (value.Source!="core:iron_blade" && value.Source!="core:sowing_sworddance")) return;
            if(value.Tick<=HeroProductionAttackTick) return;
            var delta=new Vector2(value.Target.X-value.Position.X,value.Target.Y-value.Position.Y);
            if(delta.sqrMagnitude==0) return;
            HeroProductionAttackTick=value.Tick;HeroProductionTargetId=value.SubjectId;HeroProductionTargetDirection=delta;
            productionAttackDirection=ProductionDirection(delta);NotifyHeroMotionAttack(value.Source=="core:sowing_sworddance");
        }
#if UNITY_EDITOR
        public void EditorPrepareProductionAttackTest(HeroProductionManifest manifest) {productionHero=manifest;}
        public void EditorAcceptProductionAttack(WaveEvent value,float time) {visualTime=time;AcceptHeroProductionAttack(value);}
        public float EditorProductionAttackStarted => motionAttackStarted;
        public bool EditorProductionAttackEvolved => motionAttackEvolved;
#endif
        float productionActionStarted;
        public string HeroProductionMethod => productionHero?.method ?? "cutout-baseline";
        public string HeroProductionAction { get; private set; }
        public int HeroProductionFrame { get; private set; }
        public long HeroProductionTextureBytes { get; private set; }

        public static void ValidateProductionManifest(HeroProductionManifest value, bool requireAuthoredFrames=false)
        {
            if (value == null || value.schemaVersion != 1 || value.clips == null || value.clips.Length == 0 ||
                (value.method != "3d-baked" && value.method != "frame-drawn"))
                throw new InvalidDataException("Unsupported hero production comparison manifest.");
            var identities=new HashSet<string>(StringComparer.Ordinal);
            var authoredEight=value.method=="frame-drawn" && Array.Exists(value.clips,c=>c!=null&&!string.IsNullOrEmpty(c.authoredDirection));
            if(requireAuthoredFrames && !authoredEight) throw new InvalidDataException("Actual combat hero review requires independently authored 2D frames.");
            foreach (var clip in value.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.id) || string.IsNullOrEmpty(clip.direction) ||
                    string.IsNullOrEmpty(clip.texture) || clip.columns < 1 || clip.rows < 1 || clip.frames < 1 ||
                    clip.frames > (long)clip.columns * clip.rows || !PositiveFinite(clip.fps) || !PositiveFinite(clip.bodyHeightPixels) ||
                    !PositiveFinite(clip.headHeightPixels) || clip.headHeightPixels >= clip.bodyHeightPixels ||
                    float.IsNaN(clip.pivotX) || float.IsNaN(clip.pivotY) || clip.pivotX < 0 || clip.pivotX > 1 || clip.pivotY < 0 || clip.pivotY > 1)
                    throw new InvalidDataException("Invalid production clip dimensions or measured landmarks.");
                if(!identities.Add(clip.direction+":"+clip.id)) throw new InvalidDataException("Duplicate production clip.");
                if(authoredEight && (clip.mirror || clip.authoredDirection!=clip.direction)) throw new InvalidDataException("Frame-drawn directions must be independently authored, never mirrored.");
            }
            if(authoredEight) foreach(var direction in ProductionDirections)
                foreach(var action in new[]{"idle","walk","slash-light","hit","death"})
                    if(!identities.Contains(direction+":"+action)) throw new InvalidDataException("Frame-drawn hero requires every action in eight authored directions.");
        }

        static bool PositiveFinite(float value) => value > 0 && !float.IsInfinity(value);

        public void ConfigureHeroProduction(string manifestPath, string direction, bool requireAuthoredFrames=false)
        {
            if (productionHero != null) throw new InvalidOperationException("Production comparison is already configured.");
            var value = JsonUtility.FromJson<HeroProductionManifest>(File.ReadAllText(manifestPath));
            ValidateProductionManifest(value,requireAuthoredFrames);
            var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) + Path.DirectorySeparatorChar;
            productionEightDirections=value.method=="frame-drawn" && Array.Exists(value.clips,c=>!string.IsNullOrEmpty(c.authoredDirection));
            productionFollowFacing = direction == "follow";
            productionDirection = productionFollowFacing ? "E" : direction;
            foreach (var clip in value.clips)
            {
                if (!productionFollowFacing && clip.direction != direction) continue;
                var file = Path.GetFullPath(Path.Combine(directory, clip.texture));
                if (!file.StartsWith(directory, StringComparison.Ordinal)) throw new InvalidDataException("Atlas is outside its authoring package.");
                if (!productionTextures.TryGetValue(file, out var texture))
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    if (!texture.LoadImage(File.ReadAllBytes(file), true)) throw new InvalidDataException("Invalid production atlas.");
                    productionTextures.Add(file, texture);
                    HeroProductionTextureBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                    batches.Add((GameVisualTokens.LordLayer, texture), new SpriteBatch(texture, shader, renderCamera, GameVisualTokens.LordLayer));
                }
                if (texture.width % clip.columns != 0 || texture.height % clip.rows != 0)
                    throw new InvalidDataException("Atlas grid must contain whole pixels.");
                var width = texture.width / clip.columns; var height = texture.height / clip.rows;
                var frames = new ArtPixelRect[clip.frames];
                for (var i = 0; i < frames.Length; i++) frames[i] = new ArtPixelRect { x = i % clip.columns * width, y = i / clip.columns * height, width = width, height = height };
                clip.visual = new ArtVisual(new ArtRoleDefinition { id = value.method + "." + clip.id + "." + direction,
                    frames = frames, pivot = new Vector2(clip.pivotX, clip.pivotY), worldSize = new Vector2(width, height),
                    frameMs = Mathf.Max(1, Mathf.RoundToInt(1000 / clip.fps)), tween = "none" }, texture);
            }
            productionHero = value;
            if (ProductionClip("walk") == null || ProductionClip("slash-light") == null)
                throw new InvalidDataException("Comparison requires walk and slash for the chosen direction.");
            if (productionFollowFacing)
            {
                productionDirection = "W";
                if (ProductionClip("walk") == null || ProductionClip("slash-light") == null)
                    throw new InvalidDataException("Following facing requires authored E/W walk and slash entries.");
                productionDirection = "E";
            }
        }

        HeroProductionClip ProductionClip(string id)
        {
            foreach (var clip in productionHero.clips)
                if (clip.direction == productionDirection && clip.id == id) return clip;
            return null;
        }

        bool DrawHeroProduction(ArtVisual reference, Vector2 position, bool walking, bool hit, bool dead, ActorFacingPose facing, Color color)
        {
            if (productionHero == null) return false;
            if(productionFollowFacing && productionHasPosition && walking && (position-productionPreviousPosition).sqrMagnitude>0.000001f)
                productionDirection=productionEightDirections ? ProductionDirection(position-productionPreviousPosition) : facing.SignedWidth<0?"W":"E";
            productionPreviousPosition=position;productionHasPosition=true;
            heroLiveRig?.Hide();
            var slash = ProductionClip(motionAttackEvolved ? "slash-heavy" : "slash-light") ?? ProductionClip("slash-light");
            var attackAge = visualTime - motionAttackStarted;
            var attack = attackAge >= 0 && attackAge < slash.frames / slash.fps;
            if(productionFollowFacing && attack) productionDirection=productionEightDirections ? productionAttackDirection : HeroProductionTargetDirection.x<0?"W":"E";
            if(hit && !productionWasHit) productionHitStarted=visualTime;
            productionWasHit=hit;
            var hitClip=ProductionClip("hit");
            var reacting=hit || hitClip!=null && visualTime-productionHitStarted<hitClip.frames/hitClip.fps;
            var id = dead ? "death" : reacting ? "hit" : attack ? slash.id : walking ? "walk" : "idle";
            var clip = ProductionClip(id);
            if (clip == null) { clip = ProductionClip("walk"); id = "idle-unavailable"; }
            if (id != productionAction) { productionAction = id; productionActionStarted = visualTime; }
            var time = id=="hit" ? visualTime-productionHitStarted : attack && id == slash.id ? attackAge : visualTime - productionActionStarted;
            var frame = ProductionFrame(id, time, clip.frames, clip.fps);
            HeroProductionAction = id; HeroProductionFrame = frame;
            // Match visible body height, not atlas padding; proportions remain wholly inside the authored image.
            var bodyHeight = reference.WorldSize.y * GameVisualTokens.HeroRigHeightScale * (167f / 252f);
            var size = clip.visual.WorldSize * (bodyHeight / clip.bodyHeightPixels);
            if (clip.mirror) size.x = -size.x;
            Draw(clip.visual, GameVisualTokens.LordLayer, position, 0, size, tint: color, uv: clip.visual.UvRects[frame]);
            return true;
        }

        public static int ProductionFrame(string action, float seconds, int frames, float fps)
        {
            var frame = Mathf.FloorToInt(Mathf.Max(0, seconds) * fps);
            return action == "walk" || action == "run" || action == "idle" ? frame % frames :
                action == "idle-unavailable" ? 0 : Mathf.Min(frame, frames - 1);
        }

        void DisposeHeroProduction()
        {
            if (productionHero != null) foreach (var clip in productionHero.clips) if (clip.visual != null) Destroy(clip.visual.Sprite);
            foreach (var texture in productionTextures.Values) Destroy(texture);
            productionTextures.Clear(); productionHero = null;
        }
    }
}
