using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    // Historical enum recipes are translated once. Gameplay consumes the resulting units.
    internal static class WaveLegacyCompiler
    {
        internal static IReadOnlyDictionary<string, WavePrimitiveProgram> Compile(WaveRuntimeDefinition definition)
        {
            var result = new Dictionary<string, WavePrimitiveProgram>(StringComparer.Ordinal);
            foreach (var gear in definition.Gear.Values) { result.Add(gear.Id, Gear(gear.Kind)); }
            foreach (var item in definition.Items.Values) { result.Add(item.Id, Item(item.Kind)); }
            foreach (var evolution in definition.Evolutions.Values) { result.Add(evolution.Id, Evolution(evolution.Kind)); }
            foreach (var enemy in definition.Enemies.Values) { result.Add(enemy.Id, Enemy(enemy.Kind)); }
            result.Add(definition.ChapterId, Chapter());
            return result;
        }
        private static WavePrimitiveProgram Gear(WaveAttackKind kind) => kind switch
        {
            WaveAttackKind.Arc => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
            }),
            WaveAttackKind.Orbit => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "orbit", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
            }),
            WaveAttackKind.Chain => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "chain", ["anchor"] = "lord", ["aim"] = "nearest-enemy", ["collision"] = "damage", ["repeatTarget"] = "never-in-chain", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:attack-variant"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "wet-target", ["action"] = "wet-priority-chain", ["anchor"] = "target", ["consumeCondition"] = "false" },
                ["unit:status-apply"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "hit", ["target"] = "enemy", ["status"] = "brief-stop", ["scope"] = "attack-target", ["expire"] = "duration" },
            }),
            WaveAttackKind.Homing => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "homing-projectile", ["anchor"] = "lord", ["aim"] = "nearest-enemy", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:projectile-lifecycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["growthCollision"] = "ignore", ["onTargetLost"] = "expire", ["tracking"] = "locked-target" },
            }),
            WaveAttackKind.HarvestArc => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:harvest-contact"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "hit", ["target"] = "ripe-land", ["scope"] = "attack-footprint", ["sourceFilter"] = "owned-source", ["sourceIds"] = "$seed" },
                ["unit:attack-variant"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "ripe-contact", ["action"] = "emit-fragments", ["anchor"] = "contact", ["consumeCondition"] = "false" },
            }),
            WaveAttackKind.SeedFan => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "impact", ["stock"] = "new-cycle", ["remnantKey"] = "grain-patch" },
                ["unit:growth-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "always", ["completion"] = "growth-complete", ["restart"] = "replant", ["restore"] = "restart" },
                ["unit:completion-ledger"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "growth-complete", ["oncePer"] = "growth-cycle", ["channels"] = "xp", ["dedupeKey"] = "source-cycle", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "ripe-contact" },
            }),
            WaveAttackKind.WaterFan => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "impact", ["stock"] = "new-cycle", ["remnantKey"] = "seasonal-pool" },
                ["unit:growth-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "season", ["workGate"] = "always", ["completion"] = "growth-complete", ["restart"] = "new-input", ["restore"] = "restart" },
                ["unit:completion-ledger"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "growth-complete", ["oncePer"] = "growth-cycle", ["channels"] = "xp", ["dedupeKey"] = "source-cycle", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "work-complete" },
                ["unit:stock-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["resource"] = "water", ["supply"] = "rain-or-finite-source", ["consumers"] = "attack|growth", ["exclusive"] = "true", ["empty"] = "dry-strike", ["repairCreatesStock"] = "false" },
                ["unit:attack-variant"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["condition"] = "water-empty", ["action"] = "dry-strike", ["anchor"] = "lord", ["consumeCondition"] = "false" },
                ["unit:status-apply"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["target"] = "enemy", ["status"] = "wet", ["scope"] = "water-path", ["expire"] = "duration" },
            }),
            WaveAttackKind.ConstructionSlam => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "ground-slam", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "building", ["placement"] = "near-empty-ground", ["stock"] = "new-cycle", ["remnantKey"] = "joinery-frame" },
                ["unit:growth-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "within-work-radius", ["completion"] = "shipment-complete", ["restart"] = "new-input", ["restore"] = "remaining-foundation" },
                ["unit:completion-ledger"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "shipment-complete", ["oncePer"] = "shipment", ["channels"] = "xp", ["dedupeKey"] = "source-shipment", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "cargo-arrival" },
                ["unit:stock-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["resource"] = "timber", ["supply"] = "original-finite-ruin", ["consumers"] = "shipment|sale|meta-export", ["exclusive"] = "true", ["empty"] = "wait", ["repairCreatesStock"] = "false" },
                ["unit:completed-structure-attack"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["on"] = "enemy-approach", ["anchor"] = "completed-building", ["collision"] = "damage-knockback", ["requires"] = "live-complete" },
            }),
            WaveAttackKind.MusterWave => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "expanding-wave", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage-knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:visible-marker"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "ground", ["state"] = "attack-footprint", ["bind"] = "self" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "people", ["placement"] = "impact", ["stock"] = "available-workers", ["remnantKey"] = "levy-company" },
                ["unit:growth-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "kill", ["workGate"] = "participating-kill", ["completion"] = "mission-return", ["restart"] = "after-return", ["restore"] = "survivors-only" },
                ["unit:completion-ledger"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["event"] = "mission-return", ["oncePer"] = "mission", ["channels"] = "xp", ["dedupeKey"] = "group-mission", ["rewardTiming"] = "completed-work-only", ["identityPersistence"] = "retry-repair-recovery-save-load", ["claimGate"] = "mission-return" },
                ["unit:ally-task"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["task"] = "recruit", ["target"] = "lord", ["source"] = "available-workers", ["entityUnit"] = "group", ["capPolicy"] = "shared-active-group-budget", ["onCap"] = "reuse-existing" },
                ["unit:mission-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["start"] = "actual-enemy-engagement", ["progress"] = "participating-kills", ["finish"] = "engagement-ended-and-returned", ["returnTarget"] = "lord", ["emptyTripReward"] = "false", ["training"] = "alternating-front-rank" },
            }),
            _ => throw new ArgumentException("Unknown historical gear recipe.")
        };
        private static WavePrimitiveProgram Item(WaveItemKind kind) => kind switch
        {
            WaveItemKind.SeedDetour => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:target-routing"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "seed-eaten", ["actor"] = "enemy", ["selection"] = "side-route", ["fallback"] = "default", ["persistent"] = "false" },
            }),
            WaveItemKind.CarryWater => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:target-routing"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "water-contact", ["actor"] = "remnant", ["selection"] = "first-young-plot", ["fallback"] = "wait", ["persistent"] = "false" },
                ["unit:resource-routing"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["resource"] = "water", ["source"] = "carried-stock", ["destination"] = "first-young-plot", ["transform"] = "none", ["exclusive"] = "true" },
            }),
            WaveItemKind.HarvestGuard => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:group-formation"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "harvest-complete", ["action"] = "reposition-existing-guard", ["anchor"] = "harvest-site", ["newMembers"] = "none", ["deadMembers"] = "never-revive" },
            }),
            WaveItemKind.FrontOrbit => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:geometry-modifier"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enemy-front", ["change"] = "front-corner-orbit", ["source"] = "attack", ["preserveGrowthIdentity"] = "true" },
            }),
            WaveItemKind.RaiderAim => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:target-routing"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "attack-ready", ["actor"] = "attack", ["selection"] = "raider-in-range", ["fallback"] = "default", ["persistent"] = "false" },
            }),
            WaveItemKind.FieldMeal => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "linked", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:resource-routing"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "mission-start", ["resource"] = "food", ["source"] = "existing-storage", ["destination"] = "field-meal", ["transform"] = "none", ["exclusive"] = "true" },
            }),
            WaveItemKind.PickupRadius => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "universal", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:stat-modifier"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["stat"] = "pickup-radius", ["direction"] = "increase", ["amount"] = "inherit" },
            }),
            WaveItemKind.MoveSpeed => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:equipment-scope"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "universal", ["toolIds"] = "none", ["weaponIds"] = "none" },
                ["unit:stat-modifier"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["stat"] = "move-speed", ["direction"] = "increase", ["amount"] = "inherit" },
            }),
            _ => throw new ArgumentException("Unknown historical item recipe.")
        };
        private static WavePrimitiveProgram Evolution(WaveEvolutionKind kind) => kind switch
        {
            WaveEvolutionKind.PlantingArc => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:event-gate"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "attack-kill-near-growth", ["scope"] = "self", ["consume"] = "none" },
                ["unit:evolution-replace"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["inputIds"] = "$input0|$input1", ["replace"] = "weapon-attack", ["slot"] = "weapon-input", ["preserveRemnants"] = "true", ["resourcePriority"] = "evolution-first", ["duplicateRewards"] = "false" },
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "melee-fan", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "land", ["placement"] = "attack-footprint", ["stock"] = "new-cycle", ["remnantKey"] = "grain-patch" },
            }),
            WaveEvolutionKind.RepairOrbit => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:event-gate"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "repair-completed", ["scope"] = "self", ["consume"] = "none" },
                ["unit:evolution-replace"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["inputIds"] = "$input0|$input1", ["replace"] = "weapon-attack", ["slot"] = "weapon-input", ["preserveRemnants"] = "true", ["resourcePriority"] = "evolution-first", ["duplicateRewards"] = "false" },
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "orbit", ["anchor"] = "building", ["aim"] = "facing", ["collision"] = "knockback", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:attack-anchor"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["when"] = "repair-in-progress", ["anchor"] = "nearest-repair-building", ["fallback"] = "lord", ["transfer"] = "move-existing-no-duplicate" },
            }),
            WaveEvolutionKind.ShelteredPlot => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:event-gate"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "equipment-offer", ["condition"] = "harvest-near-building", ["scope"] = "self", ["consume"] = "none" },
                ["unit:evolution-replace"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["inputIds"] = "$input0|$input1", ["replace"] = "both-tool-activations", ["slot"] = "first-tool-input", ["preserveRemnants"] = "true", ["resourcePriority"] = "evolution-first", ["duplicateRewards"] = "false" },
                ["unit:attack-shape"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "ground-slam", ["anchor"] = "lord", ["aim"] = "facing", ["collision"] = "damage", ["repeatTarget"] = "per-activation", ["on"] = "automatic-attack" },
                ["unit:remnant-create"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "automatic-attack", ["target"] = "building", ["placement"] = "existing-first", ["stock"] = "new-cycle", ["remnantKey"] = "sheltered-frame" },
                ["unit:growth-cycle"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["drivers"] = "time", ["workGate"] = "linked-building-complete", ["completion"] = "growth-complete", ["restart"] = "replant", ["restore"] = "remaining-foundation" },
                ["unit:growth-protect"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "enter", ["target"] = "adjacent-land", ["anchor"] = "building", ["protection"] = "first-raid-block", ["requires"] = "completed-live-building", ["afterUse"] = "repair-before-reuse" },
                ["unit:paired-growth"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["first"] = "building-frame", ["second"] = "seed-plot", ["placement"] = "existing-first-else-empty", ["workGate"] = "within-work-radius", ["secondGate"] = "first-complete-alive", ["destroyFirst"] = "pause-second-until-repair", ["reward"] = "original-source-cycle-once" },
            }),
            _ => throw new ArgumentException("Unknown historical evolution recipe.")
        };
        private static WavePrimitiveProgram Enemy(WaveEnemyKind kind) => kind switch
        {
            WaveEnemyKind.Pursuer => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "strike", ["target"] = "lord", ["locksTarget"] = "false", ["escape"] = "intercept" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "pursue", ["action"] = "melee", ["exit"] = "none", ["counter"] = "kite" },
            }),
            WaveEnemyKind.SeedThief => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "theft", ["target"] = "seed", ["locksTarget"] = "false", ["escape"] = "intercept" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "seed", ["movement"] = "pursue", ["action"] = "consume-seed", ["exit"] = "none", ["counter"] = "intercept" },
            }),
            WaveEnemyKind.RipeGrazer => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "theft", ["target"] = "ripe", ["locksTarget"] = "false", ["escape"] = "intercept" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "ripe", ["movement"] = "pursue", ["action"] = "consume-ripe", ["exit"] = "none", ["counter"] = "intercept" },
            }),
            WaveEnemyKind.Charger => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "line", ["before"] = "charge", ["target"] = "lord", ["locksTarget"] = "true", ["escape"] = "side-step" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "charge-locked-line", ["action"] = "melee", ["exit"] = "after-attack", ["counter"] = "side-step" },
            }),
            WaveEnemyKind.Shield => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "strike", ["target"] = "lord", ["locksTarget"] = "false", ["escape"] = "intercept" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "hold-front", ["action"] = "frontal-block", ["exit"] = "none", ["counter"] = "rear-attack" },
            }),
            WaveEnemyKind.Ranged => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "entity-pose", ["before"] = "strike", ["target"] = "lord", ["locksTarget"] = "false", ["escape"] = "intercept" },
                ["unit:enemy-pressure"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = "lord", ["movement"] = "standoff", ["action"] = "shoot-lane", ["exit"] = "none", ["counter"] = "close-range" },
            }),
            WaveEnemyKind.FloodBoss => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:enemy-tell"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["shape"] = "line", ["before"] = "charge", ["target"] = "lord", ["locksTarget"] = "true", ["escape"] = "side-step" },
                ["unit:boss-phases"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["transitions"] = "ordered-cycle", ["actions"] = "water-lanes|locked-charge", ["leaveEscapeLane"] = "true", ["recoveryWindow"] = "true" },
            }),
            _ => throw new ArgumentException("Unknown historical enemy recipe.")
        };
        private static WavePrimitiveProgram Chapter() => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["unit:chapter-route"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["bossId"] = "$boss", ["order"] = "1", ["entry"] = "before-run", ["completion"] = "chapter-clear" },
                ["unit:map-route-trace"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "lord-work-path", ["trace"] = "sprout-track", ["connect"] = "adjacent-work-segments", ["enemyResponse"] = "attract-to-connected-region", ["playerChoice"] = "finish-region-or-move-out" },
            });
    }
}
