using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>Editor-only entry point; runtime uses one pre-baked SpriteBatch quad.</summary>
    public static class HeroBAtlasBaker
    {
        [MenuItem("Sow and Siege/Art/Bake articulated hero B preview")]
        public static void Bake()
        {
            var repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("HERO_B_NODE") ?? "node",
                Arguments = "tools/art-authoring/hero-b/bake.mjs",
                WorkingDirectory = repository,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start the hero B authoring baker.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill();
                    throw new TimeoutException("Hero B authoring bake exceeded 60 seconds.");
                }
                var output = stdout.GetAwaiter().GetResult();
                var error = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("Hero B bake failed: " + error);
                UnityEngine.Debug.Log(output);
            }
            AssetDatabase.Refresh();
        }
    }
}
