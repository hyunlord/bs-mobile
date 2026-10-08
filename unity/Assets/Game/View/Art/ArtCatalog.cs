using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.View
{
    public sealed class ArtVisual
    {
        public string RoleId { get; }
        public Texture2D Texture { get; }
        public IReadOnlyList<Rect> UvRects { get; }
        public Vector2 Pivot { get; }
        public Vector2 WorldSize { get; }
        public int FrameMs { get; }
        public string Tween { get; }
        public Sprite Sprite { get; }
        internal ArtVisual(ArtRoleDefinition role, Texture2D texture)
        {
            RoleId = role.id; Texture = texture; Pivot = role.pivot; WorldSize = role.worldSize;
            FrameMs = role.frameMs; Tween = role.tween;
            var uv = new Rect[role.frames.Length];
            for (var i = 0; i < uv.Length; i++)
            {
                var f = role.frames[i];
                uv[i] = new Rect((float)f.x / texture.width, 1f - (float)(f.y + f.height) / texture.height,
                    (float)f.width / texture.width, (float)f.height / texture.height);
            }
            UvRects = Array.AsReadOnly(uv);
            var first = role.frames[0];
            Sprite = UnityEngine.Sprite.Create(texture, new Rect(first.x, texture.height - first.y - first.height, first.width, first.height),
                Pivot, first.width / WorldSize.x, 0, SpriteMeshType.FullRect);
            Sprite.name = RoleId;
        }
    }

    public sealed class ArtCatalog
    {
        static ArtCatalog cached;
        readonly Dictionary<string, ArtVisual> roles = new Dictionary<string, ArtVisual>(StringComparer.Ordinal);
        readonly Dictionary<string, ArtVisual> bindings = new Dictionary<string, ArtVisual>(StringComparer.Ordinal);
        public static ArtCatalog Load()
        {
            if (cached != null) return cached;
            var registry = Resources.Load<ArtRegistry>("FirstPlayableArt");
            if (registry == null || registry.manifest == null) throw new InvalidOperationException("FirstPlayableArt registry missing; run ArtPreparation.Prepare.");
            cached = FromJson(registry.manifest.text, registry.atlases);
            return cached;
        }
#if UNITY_EDITOR
        // Authoring changes affect future consumers; existing renderers keep their current catalog and sprites.
        public static void InvalidateCache() => cached = null;
#endif
        public ArtVisual Resolve(string kind, string contentId, string state)
        {
            var key = Key(kind, contentId, state);
            return bindings.TryGetValue(key, out var visual) ? visual : throw new InvalidOperationException("Missing art binding: " + key);
        }
        public ArtVisual ResolveRole(string roleId) => roles.TryGetValue(roleId, out var visual) ? visual : throw new InvalidOperationException("Missing art role: " + roleId);
        public static string Key(string kind, string contentId, string state) => kind + "|" + contentId + "|" + state;
        public static ArtManifest Parse(string json)
        {
            if (Regex.IsMatch(json, "\\\"(?:damage|range|radius|cooldown(?:Ticks)?|growth|health|speed|durationTicks)\\\"\\s*:", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Art manifest contains forbidden gameplay stat field.");
            var manifest = JsonUtility.FromJson<ArtManifest>(json);
            Validate(manifest);
            return manifest;
        }
        public static ArtCatalog FromJson(string json, Texture2D[] textures)
        {
            var manifest = Parse(json);
            if (textures == null || textures.Length != manifest.atlases.Length) throw new InvalidOperationException("Art atlas reference count mismatch.");
            var textureById = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            for (var i = 0; i < textures.Length; i++)
            {
                var spec = manifest.atlases[i]; var texture = textures[i];
                if (texture == null || texture.width != spec.width || texture.height != spec.height)
                    throw new InvalidOperationException("Art atlas dimensions mismatch: " + spec.id);
                textureById.Add(spec.id, texture);
            }
            var result = new ArtCatalog();
            foreach (var role in manifest.roles) result.roles.Add(role.id, new ArtVisual(role, textureById[role.atlas]));
            foreach (var binding in manifest.bindings) result.bindings.Add(Key(binding.kind, binding.contentId, binding.state), result.roles[binding.roleId]);
            return result;
        }
        public static void Validate(ArtManifest manifest)
        {
            if (manifest == null || manifest.schemaVersion != 1 || manifest.licenses == null || manifest.licenses.Length == 0 || manifest.atlases == null || manifest.atlases.Length == 0 || manifest.roles == null || manifest.roles.Length == 0 || manifest.bindings == null || manifest.bindings.Length == 0)
                throw new InvalidOperationException("Art manifest schemaVersion/arrays invalid.");
            var licenses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in manifest.licenses)
                if (l == null || string.IsNullOrWhiteSpace(l.id) || !licenses.Add(l.id) || string.IsNullOrWhiteSpace(l.origin) || string.IsNullOrWhiteSpace(l.source) || string.IsNullOrWhiteSpace(l.prompt) || string.IsNullOrWhiteSpace(l.commercialUse) || Regex.IsMatch(l.origin + " " + l.commercialUse, "cc0|public.domain", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Invalid/duplicate art license: " + l?.id);
            var atlases = new Dictionary<string, ArtAtlasDefinition>(StringComparer.Ordinal);
            foreach (var a in manifest.atlases)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.id) || atlases.ContainsKey(a.id)) throw new InvalidOperationException("Duplicate/empty atlas ID: " + a?.id);
                if (a.width <= 0 || a.height <= 0 || a.padding < 2 || (string.IsNullOrWhiteSpace(a.license) || !licenses.Contains(a.license)) || string.IsNullOrWhiteSpace(a.path) || !a.path.StartsWith("Assets/Art/", StringComparison.Ordinal) || !a.path.EndsWith(".png", StringComparison.Ordinal) || a.path.Contains("..")) throw new InvalidOperationException("Invalid atlas dimensions/padding/license/path: " + a.id);
                atlases.Add(a.id, a);
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in manifest.roles)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.id) || !ids.Add(r.id)) throw new InvalidOperationException("Duplicate/empty role ID: " + r?.id);
                if (string.IsNullOrEmpty(r.atlas) || !atlases.TryGetValue(r.atlas, out var a)) throw new InvalidOperationException("Missing atlas for role: " + r.id);
                if (r.frames == null || r.frames.Length == 0 || r.frameMs <= 0 || !ValidTween(r.tween) || !Finite(r.worldSize.x) || !Finite(r.worldSize.y) || r.worldSize.x <= 0 || r.worldSize.y <= 0 || !Finite(r.pivot.x) || !Finite(r.pivot.y) || r.pivot.x < 0 || r.pivot.x > 1 || r.pivot.y < 0 || r.pivot.y > 1) throw new InvalidOperationException("Invalid visual metadata: " + r.id);
                foreach (var f in r.frames)
                    if (f == null || f.width <= 0 || f.height <= 0 || f.x < a.padding || f.y < a.padding || (long)f.x + f.width + a.padding > a.width || (long)f.y + f.height + a.padding > a.height) throw new InvalidOperationException("Frame bounds/padding invalid: " + r.id);
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in manifest.bindings)
            {
                if (b == null) throw new InvalidOperationException("Null art binding.");
                var key = Key(b.kind, b.contentId, b.state);
                if (string.IsNullOrWhiteSpace(b.kind) || b.contentId == null || string.IsNullOrWhiteSpace(b.state) || b.kind.Contains("|") || b.contentId.Contains("|") || b.state.Contains("|") || !keys.Add(key)) throw new InvalidOperationException("Duplicate/invalid art binding: " + key);
                if (string.IsNullOrEmpty(b.roleId) || !ids.Contains(b.roleId)) throw new InvalidOperationException("Missing art role for binding: " + key + " -> " + b.roleId);
            }
        }
        static bool ValidTween(string value) => value == "none" || value == "walk" || value == "work" || value == "pulse" || value == "recoil";
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
