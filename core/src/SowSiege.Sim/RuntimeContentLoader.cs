using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class ContentLoader
{
    private static RuntimeCatalog LoadRuntime(string[] roots, RuntimeProfileExtension profile, ContentSelection selection,
        IReadOnlyDictionary<string, ToolDefinition> tools, IReadOnlyDictionary<string, WeaponDefinition> weapons, FirstPlayableDefinition? firstPlayable = null, RuntimeProjectionOverrides? overrides = null)
    {
        Require(profile.ContractVersion == 1, "Unsupported runtime contract version.");
        var equipment = new Dictionary<string, EquipmentRuntimeDefinition>(StringComparer.Ordinal);
        var toolRecords = LoadSelected<ToolContent, ToolContent>(roots, "tools", selection.Tools, record => record, true);
        var weaponRecords = LoadSelected<WeaponContent, WeaponContent>(roots, "weapons", selection.Weapons, record => record, true);
        foreach (var record in toolRecords.Values)
        {
            AddEquipment(record, overrides?.Equipment.GetValueOrDefault(record.Id) ?? record.RuntimeProjection, false);
        }

        foreach (var record in weaponRecords.Values)
        {
            AddEquipment(record, overrides?.Equipment.GetValueOrDefault(record.Id) ?? record.RuntimeProjection, true);
        }

        var charterRecords = LoadSelected<CharterContent, CharterContent>(roots, "charters", profile.Charters, record => record, true);
        var itemRecords = LoadSelected<ItemContent, ItemContent>(roots, "items", profile.Items, record => record, true);
        var evolutionRecords = LoadSelected<EvolutionContent, EvolutionContent>(roots, "evolutions", profile.Evolutions, record => record, true);
        var charters = new Dictionary<string, CharterDefinition>(StringComparer.Ordinal);
        var items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        var evolutions = new Dictionary<string, EvolutionDefinition>(StringComparer.Ordinal);
        var allTags = tools.Values.SelectMany(t => t.Tags).Concat(weapons.Values.SelectMany(w => w.Tags)).ToHashSet(StringComparer.Ordinal);
        foreach (var record in charterRecords.Values)
        {
            var projection = overrides?.Charters.GetValueOrDefault(record.Id) ?? record.RuntimeProjection ?? throw new InvalidDataException($"Missing charter runtime projection: {record.Id}");
            Require(projection.PolicyCategory is "weapon" or "land" or "building" or "people", "Invalid charter policy category.");
            Require(projection.Effects.Length > 0, "Charter requires executable effects.");
            charters.Add(record.Id, new(record.Id, projection.PolicyCategory, projection.Effects));
        }
        foreach (var record in itemRecords.Values)
        {
            var projection = overrides?.Items.GetValueOrDefault(record.Id) ?? record.RuntimeProjection ?? throw new InvalidDataException($"Missing item runtime projection: {record.Id}");
            Require(projection.RequiredTags.All(allTags.Contains), $"Unknown selected equipment tag: {record.Id}");
            Require(projection.Effects.Length > 0, "Item requires executable effects.");
            items.Add(record.Id, new(record.Id, projection.RequiredTags, projection.Effects));
        }
        foreach (var record in evolutionRecords.Values)
        {
            var projection = overrides?.Evolutions.GetValueOrDefault(record.Id) ?? record.RuntimeProjection ?? throw new InvalidDataException($"Missing evolution runtime projection: {record.Id}");
            Require(record.InputIds.Distinct(StringComparer.Ordinal).Count() == record.InputIds.Length, "Duplicate evolution input.");
            var evolutionKind = record.EvolutionKind ?? record.Kind ?? throw new InvalidDataException("Missing evolution subtype.");
            var valid = evolutionKind switch
            {
                "weapon-tool" => record.InputIds.Length == 2 && weapons.ContainsKey(record.InputIds[0]) && tools.ContainsKey(record.InputIds[1]),
                "tool-tool" => record.InputIds.Length == 2 && record.InputIds.All(tools.ContainsKey),
                "tool-growth" => firstPlayable is not null && record.InputIds.Length == 1 && record.InputIds.All(tools.ContainsKey) && record.GrowthCondition is { Target: "land", State: "ripe" } growth && firstPlayable.EvolutionGrowthRequirements?.TryGetValue(record.Id, out var required) == true && required.Target == "ripe" && required.Minimum == growth.Minimum,
                _ => false
            };
            Require(valid && (record.GrowthCondition is null || evolutionKind == "tool-growth" && firstPlayable is not null), "S4 runtime supports only selected cross-equipment evolutions.");
            Require(record.InputIds.Contains(record.Result.BaseId, StringComparer.Ordinal) && projection.Effects.Length > 0, "Invalid evolution base or effects.");
            evolutions.Add(record.Id, new(record.Id, evolutionKind, record.InputIds, record.Result.BaseId, projection.Effects));
        }
        var effects = equipment.Values.SelectMany(e => e.Effects).Concat(charters.Values.SelectMany(e => e.Effects))
            .Concat(items.Values.SelectMany(e => e.Effects)).Concat(evolutions.Values.SelectMany(e => e.Effects)).ToArray();
        Require(effects.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() == effects.Length, "Duplicate runtime effect ID.");
        foreach (var effect in effects)
        {
            ValidateEffect(effect, tools, weapons, allTags);
        }

        Require(profile.Tuning.LootSources.Length > 0 && profile.Tuning.LootSources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == profile.Tuning.LootSources.Length, "Loot channels must be nonempty and unique.");
        foreach (var source in profile.Tuning.LootSources)
        {
            Require(NamespaceId().IsMatch(source.Id) && source.Kind is "chest" or "cart" or "market", "Invalid loot channel.");
        }

        if (overrides is not null)
        {
            Require(overrides.Equipment.Keys.All(id => tools.ContainsKey(id) || weapons.ContainsKey(id)) && overrides.Charters.Keys.All(charters.ContainsKey) && overrides.Items.Keys.All(items.ContainsKey) && overrides.Evolutions.Keys.All(evolutions.ContainsKey), "Runtime override must reference selected content.");
        }
        return new(profile.Tuning, equipment, charters, items, evolutions);

        void AddEquipment(ContentRecord record, EquipmentProjection? projection, bool weapon)
        {
            Require(record.DesignStatus != "s4-runtime" || projection is not null, $"S4 equipment requires runtime projection: {record.Id}");
            if (projection is null)
            {
                return;
            }

            Require(projection.Effects.Length > 0 || projection.GrowthActions.Length > 0, "Empty equipment runtime projection.");
            Require(!weapon || projection.GrowthActions.Length == 0, "Weapon cannot replace tool growth.");
            Require(projection.GrowthActions.Select(g => g.Target).Distinct(StringComparer.Ordinal).Count() == projection.GrowthActions.Length, "Duplicate mixed growth target.");
            foreach (var growth in projection.GrowthActions)
            {
                Require((growth.Target == "building" && growth.Operation == "construct" && growth.DurationTicks == 0) ||
                    (growth.Target == "people" && growth.Operation == "garrison" && growth.DurationTicks > 0), "Unsupported growth action.");
            }

            equipment.Add(record.Id, new(record.Id, projection.GrowthActions, projection.Effects));
        }
    }

    private static void ValidateEffect(RuntimeEffectDefinition effect, IReadOnlyDictionary<string, ToolDefinition> tools,
        IReadOnlyDictionary<string, WeaponDefinition> weapons, HashSet<string> tags)
    {
        Require(NamespaceId().IsMatch(effect.Id), "Invalid effect namespace ID.");
        Require(effect.Trigger is "attack" or "harvest" or "repair" or "plant" or "farm-hit" or "draft" or "return-start" or "return-arrival" or "estate-cross" or "kill" or "modifier", "Invalid effect trigger.");
        var modifier = effect.Operation is "stat-add" or "planting-bias";
        Require(modifier == (effect.Trigger == "modifier"), "Modifier operation/trigger mismatch.");
        Require(!modifier || effect.FoodCost == 0, "Modifier food cost is unsupported.");
        var validSubject = effect.Operation switch
        {
            "stat-add" => effect.Subject is "attack-damage" or "attack-knockback" or "attack-cooldown" or "repair-amount" or "draft-min-rest" or "draft-speed" or "worker-speed" or "worker-incoming-damage" or "building-front-damage" or "building-rear-damage",
            "planting-bias" => effect.Subject is "existing-edge" or "estate-inward",
            "damage-pulse" => effect.Subject == "weapon-front",
            "repair-nearest" or "return-via-building" => effect.Subject == "building",
            "worker-buff" or "rally-returners" or "extend-duty" or "guard-return" => effect.Subject == "people",
            "shield-farms" or "damage-young-plots" or "pause-neighbor-growth" => effect.Subject == "land",
            "plant-path" => tools.ContainsKey(effect.Subject),
            "harvest-near" => tools.ContainsKey(effect.Subject) || weapons.ContainsKey(effect.Subject),
            _ => false
        };
        Require(validSubject && effect.Amount != 0 && (effect.Operation == "stat-add" || effect.Amount > 0), $"Invalid effect operation/subject/amount: {effect.Id}");
        if (effect.Operation is "worker-buff" or "rally-returners" or "shield-farms" or "extend-duty" or "guard-return" or "pause-neighbor-growth")
        {
            Require(effect.DurationTicks > 0, "Timed effect requires positive duration.");
        }

        foreach (var condition in effect.Conditions)
        {
            var valid = condition.Kind switch
            {
                "equipment-owned" or "equipment-id" => condition.Value is not null && (tools.ContainsKey(condition.Value) || weapons.ContainsKey(condition.Value)),
                "owned-tag" or "equipment-tag" => condition.Value is not null && tags.Contains(condition.Value),
                "equipment-kind" => condition.Value is "weapon" or "tool",
                "growth-target" => condition.Value is "land" or "building" or "people",
                "person-role" => condition.Value is "peasant" or "militia" or "returning" or "guard" or "vassal",
                "enemy-target" => condition.Value is "lord" or "seed" or "ripe" or "building",
                "count" => condition.Value is "farms" or "buildings" or "people" or "harvests",
                "season" => condition.Value is null && condition.Minimum <= 3,
                "near-seed" or "near-ripe" or "near-building" or "estate-inside" or "estate-outside" or "building-ruined" or "building-new" or "shield-consumed" => condition.Value is null && condition.Minimum == 0,
                _ => false
            };
            Require(condition.Kind is "count" or "season" || condition.Minimum == 0, "Unused condition minimum must be zero.");
            Require(valid, $"Invalid runtime condition: {effect.Id}/{condition.Kind}");
        }
    }
}
