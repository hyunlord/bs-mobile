using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class ArtPreparation
    {
        static string ManifestPath => "Assets/Art/" + (Game.App.Generated.CanonicalContent.ProfileName == "wave-1a" ? "wave1a" : "first-playable") + "-manifest.json";
        static string RegistryPath => "Assets/Resources/" + (Game.App.Generated.CanonicalContent.ProfileName == "wave-1a" ? "Wave1aArt" : "FirstPlayableArt") + ".asset";
        [MenuItem("Sow and Siege/Prepare First Playable Art")]
        public static void Prepare()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Arguments = "\"" + Path.Combine(root, Game.App.Generated.CanonicalContent.ProfileName == "wave-1a" ? "tools/wave1a-art.mjs" : "tools/first-playable-art.mjs") + "\" \"" + root + "\"";
            if(Game.App.Generated.CanonicalContent.ProfileName == "wave-1a")start.Arguments += " --source-only";
            RunValidator(start);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifestAsset == null) throw new InvalidOperationException("Missing canonical art manifest: " + ManifestPath);
            var manifest = ArtCatalog.Parse(manifestAsset.text);
            var textures = new Texture2D[manifest.atlases.Length];
            for (var i = 0; i < textures.Length; i++)
            {
                var atlas = manifest.atlases[i];
                AssetDatabase.ImportAsset(atlas.path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(atlas.path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing atlas importer: " + atlas.id);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(atlas.width, atlas.height));
                importer.isReadable = true; importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas.path);
                if (texture.width != atlas.width || texture.height != atlas.height) throw new InvalidOperationException("Imported atlas dimensions mismatch: " + atlas.id);
                ValidateAlphaPadding(manifest, atlas, texture);
                importer.isReadable = false; importer.SaveAndReimport();
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas.path);
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var registry = AssetDatabase.LoadAssetAtPath<ArtRegistry>(RegistryPath);
            if (registry == null) { registry = ScriptableObject.CreateInstance<ArtRegistry>(); AssetDatabase.CreateAsset(registry, RegistryPath); }
            registry.manifest = manifestAsset; registry.atlases = textures;
            if(Game.App.Generated.CanonicalContent.ProfileName == "wave-1a") registry.audioManifest = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/wave1a-audio.json");
            EditorUtility.SetDirty(registry); AssetDatabase.SaveAssets();
            ArtCatalog.InvalidateCache();
            if(Game.App.Generated.CanonicalContent.ProfileName == "wave-1a")
            { start.Arguments=start.Arguments.Replace(" --source-only", "");RunValidator(start); }
        }
        static void RunValidator(ProcessStartInfo start)
        {
            using(var process=Process.Start(start))
            {
                if(process==null)throw new InvalidOperationException("Could not start art contract validator.");
                var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0)throw new InvalidOperationException("Art contract validation failed: "+error+output);
                UnityEngine.Debug.Log("Art contract: "+output.Trim());
            }
        }
        static void ValidateAlphaPadding(ArtManifest manifest, ArtAtlasDefinition atlas, Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            // Only these authored atlases permit faint generation residue; legacy padding remains exact zero.
            var maximum = atlas.id == "wave1a-actors" || atlas.id == "wave1a-growth" || atlas.id == "wave1a-fx" || atlas.id == "wave1a-icons" || atlas.id == "wave1a-arcs" ? 16 : 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in manifest.roles)
            {
                if (role.atlas != atlas.id) continue;
                foreach (var frame in role.frames)
                {
                    var key = $"{frame.x}/{frame.y}/{frame.width}/{frame.height}";
                    if (!seen.Add(key)) continue;
                    var visible = false;
                    for (var y = frame.y - atlas.padding; y < frame.y + frame.height + atlas.padding; y++)
                        for (var x = frame.x - atlas.padding; x < frame.x + frame.width + atlas.padding; x++)
                        {
                            var alpha = pixels[(texture.height - 1 - y) * texture.width + x].a;
                            var inside = x >= frame.x && x < frame.x + frame.width && y >= frame.y && y < frame.y + frame.height;
                            if (!inside && alpha > maximum) throw new InvalidOperationException("Nontransparent atlas padding: " + role.id + " at " + x + "," + y);
                            if (inside && alpha > maximum) visible = true;
                        }
                    if (!visible) throw new InvalidOperationException("Empty art frame: " + role.id);
                }
            }
        }
    }
}
