using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
        public string id, direction, texture;
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
        bool productionFollowFacing;
        float productionActionStarted;
        public string HeroProductionMethod => productionHero?.method ?? "cutout-baseline";
        public string HeroProductionAction { get; private set; }
        public int HeroProductionFrame { get; private set; }
        public long HeroProductionTextureBytes { get; private set; }

        public static void ValidateProductionManifest(HeroProductionManifest value)
        {
            if (value == null || value.schemaVersion != 1 || value.clips == null || value.clips.Length == 0 ||
                (value.method != "3d-baked" && value.method != "frame-drawn"))
                throw new InvalidDataException("Unsupported hero production comparison manifest.");
            foreach (var clip in value.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.id) || string.IsNullOrEmpty(clip.direction) ||
                    string.IsNullOrEmpty(clip.texture) || clip.columns < 1 || clip.rows < 1 || clip.frames < 1 ||
                    clip.frames > (long)clip.columns * clip.rows || !PositiveFinite(clip.fps) || !PositiveFinite(clip.bodyHeightPixels) ||
                    !PositiveFinite(clip.headHeightPixels) || clip.headHeightPixels >= clip.bodyHeightPixels ||
                    float.IsNaN(clip.pivotX) || float.IsNaN(clip.pivotY) || clip.pivotX < 0 || clip.pivotX > 1 || clip.pivotY < 0 || clip.pivotY > 1)
                    throw new InvalidDataException("Invalid production clip dimensions or measured landmarks.");
            }
        }

        static bool PositiveFinite(float value) => value > 0 && !float.IsInfinity(value);

        public void ConfigureHeroProduction(string manifestPath, string direction)
        {
            if (productionHero != null) throw new InvalidOperationException("Production comparison is already configured.");
            var value = JsonUtility.FromJson<HeroProductionManifest>(File.ReadAllText(manifestPath));
            ValidateProductionManifest(value);
            var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) + Path.DirectorySeparatorChar;
            productionFollowFacing = direction == "follow";
            productionDirection = productionFollowFacing ? "E" : direction;
            foreach (var clip in value.clips)
            {
                if (productionFollowFacing ? clip.direction != "E" && clip.direction != "W" : clip.direction != direction) continue;
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
            if (productionFollowFacing) productionDirection = facing.SignedWidth < 0 ? "W" : "E";
            heroLiveRig?.Hide();
            var slash = ProductionClip(motionAttackEvolved ? "slash-heavy" : "slash-light") ?? ProductionClip("slash-light");
            var attackAge = visualTime - motionAttackStarted;
            var attack = attackAge >= 0 && attackAge < slash.frames / slash.fps;
            var id = dead ? "death" : hit ? "hit" : attack ? slash.id : walking ? "walk" : "idle";
            var clip = ProductionClip(id);
            if (clip == null) { clip = ProductionClip("walk"); id = "idle-unavailable"; }
            if (id != productionAction) { productionAction = id; productionActionStarted = visualTime; }
            var time = attack && id == slash.id ? attackAge : visualTime - productionActionStarted;
            var frame = Mathf.FloorToInt(Mathf.Max(0, time) * clip.fps);
            frame = id == "walk" || id == "run" || id == "idle" ? frame % clip.frames : id == "idle-unavailable" ? 0 : Mathf.Min(frame, clip.frames - 1);
            HeroProductionAction = id; HeroProductionFrame = frame;
            // Match visible body height, not atlas padding; proportions remain wholly inside the authored image.
            var bodyHeight = reference.WorldSize.y * GameVisualTokens.HeroRigHeightScale * (167f / 252f);
            var size = clip.visual.WorldSize * (bodyHeight / clip.bodyHeightPixels);
            if (clip.mirror) size.x = -size.x;
            Draw(clip.visual, GameVisualTokens.LordLayer, position, 0, size, tint: color, uv: clip.visual.UvRects[frame]);
            return true;
        }

        void DisposeHeroProduction()
        {
            if (productionHero != null) foreach (var clip in productionHero.clips) if (clip.visual != null) Destroy(clip.visual.Sprite);
            foreach (var texture in productionTextures.Values) Destroy(texture);
            productionTextures.Clear(); productionHero = null;
        }
    }
}
