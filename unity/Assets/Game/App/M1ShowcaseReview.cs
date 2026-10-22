using System;
using System.Collections.Generic;
using System.IO;
using Game.View;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class M1ShowcaseReview
    {
        [Serializable] sealed class Catalog { public string contract; public int secondsPerStage; public Entry[] entries; }
        [Serializable] public sealed class Entry { public string id, name, shape, cue, runtimeSupport; }
        struct Sample { public int index, tick; public float alpha, seconds; public Rect bounds, viewport; public bool visible; public string phase; }
        readonly List<Sample> samples = new List<Sample>(600);
        long lastEvent = -1;
        int sceneHits, sceneKills;
        public Entry Selected { get; private set; }
        public bool PerformanceOnly { get; private set; }
        Entry[] entries;
        public string Stage { get; private set; }
        public int StageIndex => Stage == "level1" ? 0 : Stage == "max" ? 1 : 2;
        public int EffectSeconds => 10;
        public string DisplayLabel { get; private set; }
        public static M1ShowcaseReview Parse(string[] args)
        {
            var position = Array.IndexOf(args,"--m1-weapon-showcase");
            if (position < 0)
            {
                if (Array.IndexOf(args,"--showcase-stage") >= 0 || Array.IndexOf(args,"--m1-showcase-catalog") >= 0) throw new ArgumentException("Showcase options require --m1-weapon-showcase.");
                return null;
            }
            var id = Value(args,"--m1-weapon-showcase");
            var all = id == "all";
            if (all)
            {
                Value(args,"--wave-benchmark");
                if (Array.IndexOf(args,"--offline-replay-capture") >= 0 || Array.IndexOf(args,"--wave-benchmark-capture") >= 0 || Array.IndexOf(args,"--autoplay-capture") >= 0) throw new ArgumentException("All-shape performance review must run without capture.");
            }
            else if (KnightReview.Parse(args) == null) throw new ArgumentException("Weapon proposals require an explicit knight replay benchmark.");
            var stage = Value(args,"--showcase-stage");
            if (stage != "level1" && stage != "max" && stage != "evolution") throw new ArgumentException("Showcase stage must be level1, max or evolution.");
            if (all && stage != "evolution") throw new ArgumentException("All-shape performance review requires evolution stage.");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Value(args,"--m1-showcase-catalog")));
            if (catalog == null || catalog.contract != "m1-weapon-visual-proposals-v1" || catalog.secondsPerStage != 10 || catalog.entries == null || catalog.entries.Length != 8)
                throw new InvalidDataException("Invalid eight-shape showcase catalog.");
            Entry selected = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var shapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id) || !entry.id.StartsWith("core:",StringComparison.Ordinal) || !ids.Add(entry.id) || string.IsNullOrEmpty(entry.shape) || !shapes.Add(entry.shape) || string.IsNullOrEmpty(entry.cue) || string.IsNullOrEmpty(entry.runtimeSupport))
                    throw new InvalidDataException("Showcase IDs, shapes and provenance must be unique and explicit.");
                if (entry.id == id) selected = entry;
            }
            if (all) selected = new Entry { id = "all", name = "8형태 동시 비용 검토", shape = "all", cue = "all", runtimeSupport = "Eight visual-only overlays; no overlay collision or damage." };
            if (selected == null) throw new ArgumentException("Unknown showcase catalog ID.");
            return new M1ShowcaseReview { Selected = selected, Stage = stage, PerformanceOnly = all, entries = catalog.entries, DisplayLabel = selected.name + " · " +
                (stage == "level1" ? "기본 형태" : stage == "max" ? "최대 형태" : "진화 형태") + "\n시각 제안 · 그림의 피해 판정 없음" };
        }
        static string Value(string[] args, string flag)
        {
            var at = Array.IndexOf(args,flag);
            if (at < 0 || at != Array.LastIndexOf(args,flag) || at + 1 >= args.Length || args[at + 1].StartsWith("--",StringComparison.Ordinal)) throw new ArgumentException("Expected one value for " + flag);
            return args[at + 1];
        }
        public void Configure(WorldRenderer world)
        {
            if (!PerformanceOnly) { world.ConfigureM1WeaponShowcase(Selected.shape,StageIndex,Selected.cue); return; }
            var shapes = new string[8]; var cues = new string[8];
            for (var i = 0; i < 8; i++) { shapes[i] = entries[i].shape; cues[i] = entries[i].cue; }
            world.ConfigureAllM1WeaponShowcase(shapes,cues);
        }
        public void Present(WorldRenderer world, float seconds) => world.M1ShowcaseTime = seconds;
        public void Record(int index, int tick, float alpha, WorldRenderer world, WaveRuntimeFrame frame)
        {
            if (index != samples.Count || samples.Count >= 600) throw new InvalidDataException("Showcase requires ordered, bounded 600-frame capture.");
            samples.Add(new Sample { index = index, tick = tick, alpha = alpha, seconds = world.M1ShowcaseTime, bounds = world.M1ShowcaseBounds,
                viewport = world.CameraWorldViewport, visible = world.M1ShowcaseVisible, phase = world.M1ShowcasePhase });
            ObserveEvents(frame);
        }
        public void ObserveEvents(WaveRuntimeFrame frame)
        {
            if (frame == null) return;
            for (var i = 0; i < frame.Events.Count; i++)
            {
                var value = frame.Events[i];
                if (value.Id <= lastEvent) continue;
                lastEvent = value.Id;
                if (value.Kind == "hit" && value.Amount > 0) sceneHits++;
                if (value.Kind == "enemy-killed") sceneKills++;
            }
        }
        public void Save(string directory, bool verified, string replaySha)
        {
            using var writer = new StreamWriter(Path.Combine(directory,"m1-weapon-showcase-frames.csv"));
            writer.WriteLine("frameIndex,tick,alpha,seconds,phase,visible,boundsX,boundsY,boundsWidth,boundsHeight,viewportX,viewportY,viewportWidth,viewportHeight");
            foreach (var row in samples) writer.WriteLine(FormattableString.Invariant($"{row.index},{row.tick},{row.alpha:R},{row.seconds:R},{row.phase},{row.visible},{row.bounds.x:R},{row.bounds.y:R},{row.bounds.width:R},{row.bounds.height:R},{row.viewport.x:R},{row.viewport.y:R},{row.viewport.width:R},{row.viewport.height:R}"));
            File.WriteAllText(Path.Combine(directory,"m1-weapon-showcase.json"),JsonUtility.ToJson(new Manifest {
                contentId = Selected.id, shape = Selected.shape, stage = Stage, runtimeSupport = Selected.runtimeSupport, cue = Selected.cue,
                seconds = PerformanceOnly ? 0 : 10, performanceOnly = PerformanceOnly, simultaneousShapes = PerformanceOnly ? 8 : 1, entries = PerformanceOnly ? entries : new[] { Selected },
                frames = samples.Count, replayVerified = verified, replaySha256 = replaySha, backgroundReplayHits = sceneHits, backgroundReplayKills = sceneKills
            },true));
        }
        [Serializable] sealed class Manifest
        {
            public string contract = "m1-weapon-showcase-v1", contentId, shape, stage, runtimeSupport, cue, replaySha256;
            public string classification = "Review-only visual interpretation; level and evolution are visual stages, not approved balance or implemented collision.";
            public string boundsMeaning = "Rotated sprite bounding-box union in world units, not opaque pixel coverage or proof of a half-screen acceptance gate.";
            public string damageMeaning = "Background replay hits/kills are authentic Core events; none are attributed to showcase overlay. Overlay has no collision and creates no deaths.";
            public bool performanceOnly;
            public string performanceMeaning = "For all-shape native review, seconds=0 means continuous benchmark duration, frames=0 means no image capture. Use benchmark.csv foreground-qualified measurements; all overlays are visual load only.";
            public Entry[] entries;
            public int simultaneousShapes;
            public int frames, seconds = 10, backgroundReplayHits, backgroundReplayKills;
            public bool replayVerified, userVisualApproval = false, defaultContentChanged = false;
        }
    }
}
