#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace Game.App
{
    // Development builds only (#372): `--estate-decor-batches <file.json>` opens the estate map prototype without touching the
    // saved preference, measures the render counters and the structural material-switch bound with the decoration on and off,
    // writes them as JSON and quits. The night queue runs it under the C05 quiet conditions after the other measurements.
    public sealed partial class RunCoordinator
    {
        bool forceEstatePrototype;
        const int DecorWarmupFrames = 60, DecorSampleFrames = 240;

        void TryStartEstateDecorMeasure()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "--estate-decor-batches");
            if (index < 0) return;
            if (!IsWave || index + 1 >= args.Length || !Ui.PortraitEnabled) { Fail(new ArgumentException("--estate-decor-batches needs wave-1a portrait and an output path.")); return; }
            StartCoroutine(MeasureEstateDecor(Path.GetFullPath(args[index + 1])));
        }

        IEnumerator MeasureEstateDecor(string output)
        {
            forceEstatePrototype = true; ShowPortraitEstate();
            var results = new List<string>();
            foreach (var decor in new[] { true, false })
            {
                if (estateMap.DecorEnabled != decor) { estateMap.ToggleDecor(); ShowEstateMap(); }
                for (var i = 0; i < DecorWarmupFrames; i++) yield return null;
                var samples = new Dictionary<string, List<long>> { ["Batches Count"] = new List<long>(), ["Draw Calls Count"] = new List<long>(), ["SetPass Calls Count"] = new List<long>() };
                var recorders = samples.Keys.ToDictionary(name => name, name => ProfilerRecorder.StartNew(ProfilerCategory.Render, name));
                try
                {
                    for (var i = 0; i < DecorSampleFrames; i++)
                    {
                        yield return null;
                        foreach (var pair in recorders) if (pair.Value.Valid) samples[pair.Key].Add(pair.Value.LastValue);
                    }
                }
                finally { foreach (var recorder in recorders.Values) recorder.Dispose(); }
                var view = Ui.GetComponentsInChildren<EstateMapView>().Single();
                results.Add($"\"{(decor ? "on" : "off")}\": {{\"materialBreaks\": {EstateMapView.MaterialBreaks(view.transform)}, " +
                    string.Join(", ", samples.Select(pair => $"\"{pair.Key}\": {Median(pair.Value)}")) + "}");
            }
            File.WriteAllText(output, "{\"commit\": \"" + BuildIdentity.Commit + "\", \"sourceDirty\": " + BuildIdentity.SourceDirty.ToString().ToLowerInvariant() +
                ", \"screen\": \"" + Screen.width + "x" + Screen.height + "\", \"frames\": " + DecorSampleFrames + ", " + string.Join(", ", results) +
                ", \"note\": \"median of per-frame render counters (-1 = recorder unavailable); materialBreaks is the structural bound\"}\n");
            Application.Quit();
        }

        static long Median(List<long> values) => values.Count == 0 ? -1 : values.OrderBy(v => v).ElementAt(values.Count / 2);
    }
}
#endif
