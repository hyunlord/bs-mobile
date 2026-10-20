using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using SowSiege.Core;
using UnityEngine;
using Game.View;

namespace Game.App
{
    public sealed partial class M1CombatReview
    {
        [Serializable] public sealed class Composition { public int version, delayTicks, stepTicks, spacing, radius; public string placement; public bool rehitEachImpact; public int bounces = -1, lureTicks = -1, bounceMinDistance = -1; }
        [Serializable] public sealed class Gear { public string id, designRef, kind, aim; public int damage, range, cooldownTicks, speed, count, lifetimeTicks, knockback, workTicks, workRadius, rewardExperience, capacity; public Composition composition; }
        [Serializable] public sealed class Arena { public string enemyId; public int spawnPeriodTicks, enemyCap, enemySpeed, enemyHealth, durationTicks, enemyDamage, suppressedUntilTick, baseExperience, spawnRadius, recycleRadius, enemyBodyWidth; }
        [Serializable] public sealed class Case
        {
            public string contract, replaySha256, baselineDataHash, baselineCatalogHash, catalogHash, id, designRef, proposedContract, stage, classification, shape, audioCue, stateHash, scope;
            public int captureStartTick, endTick, positiveHits, kills, windowHits, windowKills, maximumEnemies;
            public Gear gear; public Arena arena; public Combined combined; public Source[] sources;
        }
        public Case Definition { get; private set; }
        public string SidecarSha256 { get; private set; }
        // Review comparison only (user 2026-10-20): the evolved horn wave variant, current (default), x or y (HornWaveSpec).
        public string HornEvolvedVariant { get; private set; } = "current";
        struct EventSample { public WaveEvent value; public int frame, snapshot; public bool visible; public Vector2 screen; }
        readonly List<EventSample> events = new List<EventSample>(8192);
        readonly Dictionary<int,int> selectedHits = new Dictionary<int,int>(1024);
        long lastEvent = -1;
        int firstHit = -1, firstDeath = -1, frameCount;
        public static M1CombatReview Parse(string[] args)
        {
            var at = Array.IndexOf(args,"--m1-combat-case");
            if (at < 0) return null;
            if (at != Array.LastIndexOf(args,"--m1-combat-case") || at + 1 >= args.Length || !Path.IsPathRooted(args[at+1]) || Array.IndexOf(args,"--wave-benchmark") < 0)
                throw new ArgumentException("Combat review requires one absolute case sidecar and explicit replay benchmark.");
            foreach (var flag in new[] { "--knight-review", "--m1-weapon-showcase", "--camera-scale", "--camera-scale-window", "--m1-review-seconds", "--crowd-replay-benchmark", "--hero-motion-window", "--autoplay-capture" })
                if (Array.IndexOf(args,flag) >= 0) throw new ArgumentException("Combat case cannot mix review contracts: " + flag);
            var bytes = File.ReadAllBytes(args[at+1]);
            var value = JsonUtility.FromJson<Case>(System.Text.Encoding.UTF8.GetString(bytes));
            if (value == null || string.IsNullOrEmpty(value.replaySha256) || string.IsNullOrEmpty(value.catalogHash) || string.IsNullOrEmpty(value.baselineCatalogHash)) throw new InvalidDataException("Invalid combat review sidecar.");
            if (value.contract == "m1-combat-combined-v1") ValidateCombined(value,args);
            else
            {
                if (value.contract != "m1-combat-case-v1" || value.gear == null || value.arena == null || value.classification != "hypothesis" || value.proposedContract != "m1-review-composition-v1" || value.captureStartTick != 300 || value.endTick != 600) throw new InvalidDataException("Invalid combat review sidecar.");
                value.gear.composition = NormalizeComposition(value.gear.composition);
            }
            var variantAt = Array.IndexOf(args, "--review-horn-evolved");
            var hornVariant = variantAt < 0 ? "current" : variantAt + 1 < args.Length ? args[variantAt + 1] : "";
            if (hornVariant != "current" && hornVariant != "x" && hornVariant != "y") throw new ArgumentException("--review-horn-evolved takes current, x or y.");
            using var sha = SHA256.Create();
            return new M1CombatReview { Definition = value, SidecarSha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), HornEvolvedVariant = hornVariant };
        }
        static Composition NormalizeComposition(Composition value)
        {
            // JsonUtility materializes inline JSON null as an empty serializable object.
            if (value == null || value.version == 0 && string.IsNullOrEmpty(value.placement) &&
                value.delayTicks == 0 && value.stepTicks == 0 && value.spacing == 0 && value.radius == 0) return null;
            if (value.version != 1 || (value.placement != "nearest-target" && value.placement != "forward-line" && value.placement != "ricochet" && value.placement != "lure-bomb") ||
                (value.placement == "lure-bomb") != (value.lureTicks >= 1) ||
                (value.bounceMinDistance >= 1 && value.placement != "ricochet") ||
                (value.placement == "ricochet") != (value.bounces >= 0) ||
                value.delayTicks < 0 || value.stepTicks < 0 || value.spacing < 0 || value.radius <= 0)
                throw new InvalidDataException("Malformed nonempty combat composition.");
            return value;
        }
        public void ValidateReplayOptions(InteractiveOptions options)
        {
            var a = IsCombined ? Definition.combined.arena : Definition.arena;
            if (a.spawnRadius == 0 && a.recycleRadius == 0)
            {
                if (options.Field != null) throw new InvalidDataException("Legacy showcase requires no field override.");
                return;
            }
            if (options.Field == null || options.Field.Version != FieldOptions.ReviewVersion || options.Field.SpawnRadius != a.spawnRadius || options.Field.RecycleRadius != a.recycleRadius)
                throw new InvalidDataException("Showcase radius metadata differs from replay options.");
        }
        public ContentCatalog Apply(ContentCatalog baseline, string baselineDataHash, string replaySha)
        {
            var d = Definition;
            if (d.baselineDataHash != baselineDataHash || d.baselineCatalogHash != ReviewShowcaseCatalog.Hash(baseline) || d.replaySha256 != replaySha) throw new InvalidDataException("Combat fixture or baseline identity mismatch.");
            if (IsCombined) return ApplyCombined(baseline);
            var g = d.gear; var a = d.arena;
            if (!Enum.TryParse<WaveAttackKind>(g.kind,true,out var kind) || !Enum.IsDefined(typeof(WaveAttackKind),kind)) throw new InvalidDataException("Unknown review attack kind.");
            var c = g.composition;
            var gear = new WaveGearDefinition(g.id,g.designRef,kind,g.damage,g.range,g.cooldownTicks,g.speed,g.count,g.lifetimeTicks,g.knockback,g.workTicks,g.workRadius,g.rewardExperience,g.capacity,
                Aim:string.IsNullOrEmpty(g.aim)?null:g.aim, Composition:c==null?null:new WaveAttackComposition(c.version,c.placement,c.delayTicks,c.stepTicks,c.spacing,c.radius,c.rehitEachImpact?true:(bool?)null,c.bounces>=0?c.bounces:(int?)null,c.lureTicks>=1?c.lureTicks:(int?)null,c.bounceMinDistance>=1?c.bounceMinDistance:(int?)null));
            var result = ReviewShowcaseCatalog.Create(baseline,new WaveShowcaseCase(d.id,d.designRef,d.proposedContract,d.stage,d.classification,gear,
                new WaveShowcaseArena(a.enemyId,a.spawnPeriodTicks,a.enemyCap,a.enemySpeed,a.enemyHealth,a.durationTicks,a.enemyDamage,a.suppressedUntilTick,a.baseExperience,a.spawnRadius,a.recycleRadius,a.enemyBodyWidth)));
            if (ReviewShowcaseCatalog.Hash(result) != d.catalogHash) throw new InvalidDataException("Derived combat catalog hash mismatch.");
            return result;
        }
        public void Record(int index, int tick, WaveRuntimeFrame wave, WorldRenderer world)
        {
            if (index != frameCount++) throw new InvalidDataException("Combat frames must be consecutive.");
            foreach (var e in wave.Events)
            {
                if (!SelectEvent(e)) continue;
                AddEvent(index,tick,e,world.M1CombatScreenPoint(e.Position),world.M1CombatPointVisible(e.Position));
            }
        }
        bool IsSelectedDamage(WaveEvent e) => e.Source == Definition.id && e.Amount > 0 &&
            (e.Kind == "hit" || e.Kind == "group-hit" || e.Kind == "trained-group-hit");
        bool SelectEvent(WaveEvent e)
        {
            if (e.Id <= lastEvent) return false;
            lastEvent = e.Id;
            if (e.Tick < Definition.captureStartTick || e.Tick >= Definition.endTick) return false;
            if (IsSelectedDamage(e)) selectedHits[e.SubjectId] = e.Tick;
            var attributedDeath = e.Kind == "enemy-killed" && selectedHits.TryGetValue(e.SubjectId,out var hitTick) && hitTick == e.Tick;
            if (e.Kind == "enemy-killed") selectedHits.Remove(e.SubjectId);
            return e.Source == Definition.id || attributedDeath;
        }
        void AddEvent(int index, int tick, WaveEvent e, Vector2 point, bool visible)
        {
            if (events.Count == events.Capacity) throw new InvalidDataException("Combat event evidence capacity exceeded.");
            events.Add(new EventSample { value=e,frame=index,snapshot=tick,visible=visible,screen=point });
            if (visible && IsSelectedDamage(e) && firstHit<0) firstHit=index;
            if (visible && e.Kind=="enemy-killed" && firstDeath<0) firstDeath=index;
        }
#if UNITY_EDITOR
        public void EditorRecordEvent(int index, WaveEvent value, bool visible)
        {
            if (SelectEvent(value)) AddEvent(index,value.Tick+1,value,Vector2.zero,visible);
        }
#endif
        public void Save(string directory, bool verified)
        {
            if (IsCombined) { SaveCombined(directory,verified); return; }
            SaveFeelLogs(directory);
            using var writer = new StreamWriter(Path.Combine(directory,"m1-combat-events.csv"));
            writer.WriteLine("frameIndex,snapshotTick,eventId,eventTick,kind,source,subjectId,amount,worldX,worldY,screenX,screenY,inViewport");
            foreach(var s in events) { var e=s.value; writer.WriteLine(FormattableString.Invariant($"{s.frame},{s.snapshot},{e.Id},{e.Tick},{e.Kind},{e.Source},{e.SubjectId},{e.Amount},{e.Position.X},{e.Position.Y},{s.screen.x:R},{s.screen.y:R},{s.visible}")); }
            File.WriteAllText(Path.Combine(directory,"m1-combat-review.json"),JsonUtility.ToJson(new Manifest { definition=Definition,sidecarSha256=SidecarSha256,hornEvolvedVariant=HornEvolvedVariant,replayVerified=verified,frames=frameCount,firstVisibleHitFrame=firstHit,firstVisibleDeathFrame=firstDeath,presentation=presentation,musterPresentation=musterRecords.ToArray() },true));
        }
        [Serializable] sealed class Manifest
        {
            public string contract="m1-combat-review-v1", sidecarSha256, hornEvolvedVariant;
            public string interpretation="Isolated proposed Core composition, preconfigured stage rather than earned evolution. Hits use the selected source; deaths retain enemy-definition Source and are linked only to a selected-source hit on the same subject and tick. Visibility tests event center against viewport; it is not pixel segmentation or proof of readable reaction. A 12-frame sheet must start at the reported event frame, or be N/A if negative.";
            public Case definition; public bool replayVerified; public int frames,firstVisibleHitFrame,firstVisibleDeathFrame;
            public PresentationSummary presentation; public MusterRecord[] musterPresentation;
        }
    }
}
