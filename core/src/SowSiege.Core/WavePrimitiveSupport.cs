using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public static class WavePrimitiveSupport
    {
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]> Contracts { get; } = new Dictionary<string, IReadOnlyDictionary<string, string>[]>(StringComparer.Ordinal)
        {
            ["unit:event-gate"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "attack-kill-near-growth", ["scope"] = "self", ["consume"] = "none" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "repair-completed", ["scope"] = "self", ["consume"] = "none" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "harvest-near-building", ["scope"] = "self", ["consume"] = "none" },
            },
            ["unit:attack-shape"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "orbit", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "chain", ["anchor"] = "lord", ["aim"] = "nearest-enemy", ["collision"] = "damage", ["repeatTarget"] = "never-in-chain", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "homing-projectile", ["anchor"] = "lord", ["aim"] = "nearest-enemy", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "ground-slam", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "expanding-wave", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "orbit", ["anchor"] = "building", ["aim"] = "facing", ["collision"] = "knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "ground-slam", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
            },
            ["unit:visible-marker"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
            },
            ["unit:attack-variant"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "wet-target", ["action"] = "wet-priority-chain", ["anchor"] = "target", ["consumeCondition"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "ripe-contact", ["action"] = "emit-fragments", ["anchor"] = "contact", ["consumeCondition"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "water-empty", ["action"] = "dry-strike", ["anchor"] = "lord", ["consumeCondition"] = "false" },
            },
            ["unit:status-apply"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "hit", ["target"] = "enemy", ["status"] = "brief-stop", ["scope"] = "attack-target", ["expire"] = "duration" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["target"] = "enemy", ["status"] = "wet", ["scope"] = "water-path", ["expire"] = "duration" },
            },
            ["unit:projectile-lifecycle"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["growthCollision"] = "ignore", ["onTargetLost"] = "expire", ["tracking"] = "locked-target" },
            },
            ["unit:harvest-contact"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "hit", ["target"] = "ripe-land", ["scope"] = "attack-footprint", ["sourceFilter"] = "owned-source", ["sourceIds"] = "$seed" },
            },
            ["unit:remnant-create"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "impact", ["stock"] = "new-cycle", ["remnantKey"] = "grain-patch" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "impact", ["stock"] = "new-cycle", ["remnantKey"] = "seasonal-pool" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "building", ["placement"] = "near-empty-ground", ["stock"] = "new-cycle", ["remnantKey"] = "joinery-frame" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "people", ["placement"] = "impact", ["stock"] = "available-workers", ["remnantKey"] = "levy-company" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "attack-footprint", ["stock"] = "new-cycle", ["remnantKey"] = "grain-patch" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "building", ["placement"] = "existing-first", ["stock"] = "new-cycle", ["remnantKey"] = "sheltered-frame" },
            },
            ["unit:growth-cycle"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "always", ["completion"] = "growth-complete", ["restart"] = "replant", ["restore"] = "restart" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "season", ["workGate"] = "always", ["completion"] = "growth-complete", ["restart"] = "new-input", ["restore"] = "restart" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "within-work-radius", ["completion"] = "shipment-complete", ["restart"] = "new-input", ["restore"] = "remaining-foundation" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "kill", ["workGate"] = "participating-kill", ["completion"] = "mission-return", ["restart"] = "after-return", ["restore"] = "survivors-only" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "linked-building-complete", ["completion"] = "growth-complete", ["restart"] = "replant", ["restore"] = "remaining-foundation" },
            },
            ["unit:completion-ledger"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "growth-complete", ["oncePer"] = "growth-cycle", ["channels"] = "xp", ["dedupeKey"] = "source-cycle", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "ripe-contact" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "growth-complete", ["oncePer"] = "growth-cycle", ["channels"] = "xp", ["dedupeKey"] = "source-cycle", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "work-complete" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "shipment-complete", ["oncePer"] = "shipment", ["channels"] = "xp", ["dedupeKey"] = "source-shipment", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "cargo-arrival" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "mission-return", ["oncePer"] = "mission", ["channels"] = "xp", ["dedupeKey"] = "group-mission", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "mission-return" },
            },
            ["unit:stock-cycle"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["resource"] = "water", ["supply"] = "rain-or-finite-source", ["consumers"] = "attack|growth", ["exclusive"] = "true", ["empty"] = "dry-strike", ["repairCreatesStock"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["resource"] = "timber", ["supply"] = "original-finite-ruin", ["consumers"] = "shipment|sale|meta-export", ["exclusive"] = "true", ["empty"] = "wait", ["repairCreatesStock"] = "false" },
            },
            ["unit:completed-structure-attack"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["on"] = "enemy-approach", ["anchor"] = "completed-building", ["collision"] = "damage-knockback", ["requires"] = "live-complete" },
            },
            ["unit:ally-task"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["task"] = "recruit", ["target"] = "lord", ["source"] = "available-workers", ["entityUnit"] = "group", ["capPolicy"] = "shared-active-group-budget", ["onCap"] = "reuse-existing" },
            },
            ["unit:mission-cycle"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["start"] = "actual-enemy-engagement", ["progress"] = "participating-kills", ["finish"] = "engagement-ended-and-returned", ["returnTarget"] = "lord", ["emptyTripReward"] = "false", ["training"] = "alternating-front-rank" },
            },
            ["unit:equipment-scope"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "universal", ["toolIds"] = "none", ["weaponIds"] = "none" },
            },
            ["unit:target-routing"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "seed-eaten", ["actor"] = "enemy", ["selection"] = "side-route", ["fallback"] = "default", ["persistent"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "water-contact", ["actor"] = "remnant", ["selection"] = "first-young-plot", ["fallback"] = "wait", ["persistent"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "attack-ready", ["actor"] = "attack", ["selection"] = "raider-in-range", ["fallback"] = "default", ["persistent"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "hit", ["actor"] = "enemy", ["selection"] = "side-route", ["fallback"] = "default", ["persistent"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "recruit", ["actor"] = "group", ["selection"] = "available-returned-group", ["fallback"] = "wait", ["persistent"] = "false" },
            },
            ["unit:resource-routing"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["resource"] = "water", ["source"] = "carried-stock", ["destination"] = "first-young-plot", ["transform"] = "none", ["exclusive"] = "true" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "mission-start", ["resource"] = "food", ["source"] = "existing-storage", ["destination"] = "field-meal", ["transform"] = "none", ["exclusive"] = "true" },
            },
            ["unit:group-formation"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "harvest-complete", ["action"] = "reposition-existing-guard", ["anchor"] = "harvest-site", ["newMembers"] = "none", ["deadMembers"] = "never-revive" },
            },
            ["unit:geometry-modifier"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enemy-front", ["change"] = "front-corner-orbit", ["source"] = "attack", ["preserveGrowthIdentity"] = "true" },
            },
            ["unit:stat-modifier"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["stat"] = "pickup-radius", ["direction"] = "increase", ["amount"] = "inherit" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["stat"] = "move-speed", ["direction"] = "increase", ["amount"] = "inherit" },
            },
            ["unit:evolution-replace"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["inputIds"] = "$input0|$input1", ["replace"] = "weapon-attack", ["slot"] = "weapon-input", ["preserveRemnants"] = "true", ["resourcePriority"] = "evolution-first", ["duplicateRewards"] = "false" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["inputIds"] = "$input0|$input1", ["replace"] = "both-tool-activations", ["slot"] = "first-tool-input", ["preserveRemnants"] = "true", ["resourcePriority"] = "evolution-first", ["duplicateRewards"] = "false" },
            },
            ["unit:attack-anchor"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["when"] = "repair-in-progress", ["anchor"] = "nearest-repair-building", ["fallback"] = "lord", ["transfer"] = "move-existing-no-duplicate" },
            },
            ["unit:growth-protect"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["target"] = "adjacent-land", ["anchor"] = "building", ["protection"] = "first-raid-block", ["requires"] = "completed-live-building", ["afterUse"] = "repair-before-reuse" },
            },
            ["unit:paired-growth"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["first"] = "building-frame", ["second"] = "seed-plot", ["placement"] = "existing-first-else-empty", ["workGate"] = "within-work-radius", ["secondGate"] = "first-complete-alive", ["destroyFirst"] = "pause-second-until-repair", ["reward"] = "original-source-cycle-once" },
            },
            ["unit:enemy-tell"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "strike", ["target"] = "lord", ["locksTarget"] = "false", ["escape"] = "intercept" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "theft", ["target"] = "seed", ["locksTarget"] = "false", ["escape"] = "intercept" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "theft", ["target"] = "ripe", ["locksTarget"] = "false", ["escape"] = "intercept" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "line", ["before"] = "charge", ["target"] = "lord", ["locksTarget"] = "true", ["escape"] = "side-step" },
            },
            ["unit:enemy-pressure"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "pursue", ["action"] = "melee", ["exit"] = "none", ["counter"] = "kite" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "seed", ["movement"] = "pursue", ["action"] = "consume-seed", ["exit"] = "none", ["counter"] = "intercept" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "ripe", ["movement"] = "pursue", ["action"] = "consume-ripe", ["exit"] = "none", ["counter"] = "intercept" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "charge-locked-line", ["action"] = "melee", ["exit"] = "after-attack", ["counter"] = "side-step" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "hold-front", ["action"] = "frontal-block", ["exit"] = "none", ["counter"] = "rear-attack" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "standoff", ["action"] = "shoot-lane", ["exit"] = "none", ["counter"] = "close-range" },
            },
            ["unit:boss-phases"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["transitions"] = "ordered-cycle", ["actions"] = "water-lanes|locked-charge", ["leaveEscapeLane"] = "true", ["recoveryWindow"] = "true" },
            },
            ["unit:chapter-route"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["bossId"] = "$boss", ["order"] = "1", ["entry"] = "before-run", ["completion"] = "chapter-clear" },
            },
            ["unit:map-route-trace"] = new IReadOnlyDictionary<string, string>[] {
                new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "lord-work-path", ["trace"] = "sprout-track", ["connect"] = "adjacent-work-segments", ["enemyResponse"] = "attract-to-connected-region", ["playerChoice"] = "finish-region-or-move-out" },
            },
        };
        public static IReadOnlyDictionary<string, WavePrimitiveProgram> Resolve(WaveRuntimeDefinition definition)
        {
            var programs = definition.Programs ?? WaveLegacyCompiler.Compile(definition);
            var ids = definition.Gear.Keys.Concat(definition.Items.Keys).Concat(definition.Evolutions.Keys).Concat(definition.Enemies.Keys).Append(definition.ChapterId).OrderBy(id => id, StringComparer.Ordinal);
            if (!ids.SequenceEqual(programs.Keys.OrderBy(id => id, StringComparer.Ordinal))) { throw new ArgumentException("Primitive program roster mismatch."); }
            foreach (var program in programs.Values) { Validate(program); }
            return programs;
        }
        private static void RequireDependencies(WavePrimitiveProgram program)
        {
            void Require(params string[] units)
            {
                if (units.Any(unit => !program.Has(unit))) { throw new ArgumentException("Incomplete primitive state substrate: " + string.Join(",", units)); }
            }
            void Expect(string unit, string parameter, string value)
            {
                if (!program.Is(unit, parameter, value)) { throw new ArgumentException("Unsupported substrate parameter: " + unit + "." + parameter); }
            }
            var remnant = program.Value("unit:remnant-create", "remnantKey");
            if (remnant == "seasonal-pool")
            {
                Require("unit:stock-cycle", "unit:growth-cycle");
                Expect("unit:stock-cycle", "resource", "water"); Expect("unit:growth-cycle", "drivers", "season");
            }
            if (remnant == "joinery-frame")
            {
                Require("unit:stock-cycle", "unit:growth-cycle");
                Expect("unit:stock-cycle", "resource", "timber"); Expect("unit:growth-cycle", "drivers", "time");
            }
            if (remnant == "levy-company" || program.Has("unit:ally-task") || program.Has("unit:mission-cycle"))
            {
                Require("unit:remnant-create", "unit:ally-task", "unit:mission-cycle", "unit:growth-cycle");
                Expect("unit:remnant-create", "remnantKey", "levy-company"); Expect("unit:growth-cycle", "drivers", "kill");
            }
            if (remnant == "sheltered-frame" || program.Has("unit:paired-growth") || program.Has("unit:growth-protect"))
            {
                Require("unit:remnant-create", "unit:paired-growth", "unit:growth-protect", "unit:growth-cycle", "unit:evolution-replace");
                Expect("unit:remnant-create", "remnantKey", "sheltered-frame");
            }
            if (program.Has("unit:enemy-pressure") || program.Has("unit:boss-phases")) { Require("unit:enemy-tell"); }
            if (program.Has("unit:evolution-replace")) { Require("unit:event-gate", "unit:attack-shape"); }
        }
        public static void Validate(WavePrimitiveProgram program)
        {
            if (program.Params.Count == 0) { throw new ArgumentException("Empty primitive program."); }
            RequireDependencies(program);
            foreach (var unit in program.Params)
            {
                if (!Contracts.TryGetValue(unit.Key, out var shapes) || !shapes.Any(shape => shape.Count == unit.Value.Count && shape.All(p => unit.Value.TryGetValue(p.Key, out var value) && p.Value == value)))
                { throw new ArgumentException("Unsupported primitive parameters: " + unit.Key); }
            }
        }
    }
}
