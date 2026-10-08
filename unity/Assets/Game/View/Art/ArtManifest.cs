using System;
using UnityEngine;

namespace Game.View
{
    [Serializable] public sealed class ArtManifest
    {
        public int schemaVersion;
        public ArtLicenseDefinition[] licenses;
        public ArtAtlasDefinition[] atlases;
        public ArtRoleDefinition[] roles;
        public ArtBindingDefinition[] bindings;
    }
    [Serializable] public sealed class ArtLicenseDefinition { public string id, origin, source, prompt, commercialUse; }
    [Serializable] public sealed class ArtAtlasDefinition
    {
        public string id, path, license;
        public int width, height, padding;
    }
    [Serializable] public sealed class ArtRoleDefinition
    {
        public string id, atlas, tween;
        public ArtPixelRect[] frames;
        public Vector2 pivot, worldSize;
        public int frameMs;
    }
    [Serializable] public sealed class ArtPixelRect { public int x, y, width, height; }
    [Serializable] public sealed class ArtBindingDefinition { public string kind, contentId, state, roleId; }
}
