using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record CardCatalogEntry(string Id, string Kind, string[] GrowthTargets, string[] Tags);
public sealed record EffectCatalogEntry(string EffectId, string SourceId, string SourceKind, string Trigger,
    string Operation, string Subject, int Amount, int Radius, int DurationTicks, int FoodCost,
    RuntimeCondition[] Conditions, string Unit, string ImplementationNote);

public static partial class ContentLoader
{
    public static CardCatalogEntry[] CardCatalog(string directory, RuntimeProfile profile, bool includeTest, ContentCatalog catalog)
    {
        var cards = catalog.Weapons.Values.Select(w => new CardCatalogEntry(w.Id, "weapon", [], w.Tags))
            .Concat(catalog.Tools.Values.Select(t => new CardCatalogEntry(t.Id, "tool",
                catalog.Runtime?.Equipment.TryGetValue(t.Id, out var projection) == true && projection.GrowthActions.Length > 0
                    ? projection.GrowthActions.Select(g => g.Target).ToArray() : [t.Growth.Target], t.Tags))).ToList();
        if (profile.Runtime is not null)
        {
            var roots = includeTest ? new[] { directory, Path.Combine(directory, "test") } : [directory];
            cards.AddRange(LoadSelected<CharterContent, CardCatalogEntry>(roots, "charters", profile.Runtime.Charters,
                c => new(c.Id, "charter", [], c.Tags), true).Values);
        }
        return cards.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
    }

    public static EffectCatalogEntry[] EffectCatalog(string directory, RuntimeProfile profile, bool includeTest, ContentCatalog catalog)
    {
        if (catalog.Runtime is null || profile.Runtime is null)
        {
            return [];
        }

        var roots = includeTest ? new[] { directory, Path.Combine(directory, "test") } : [directory];
        var selection = profile.Select(includeTest);
        var records = new List<(ContentRecord Record, string Kind, RuntimeEffectDefinition[] Effects)>();
        records.AddRange(LoadSelected<ToolContent, ToolContent>(roots, "tools", selection.Tools, r => r, true).Values.Select(r => ((ContentRecord)r, "tool", r.RuntimeProjection?.Effects ?? [])));
        records.AddRange(LoadSelected<WeaponContent, WeaponContent>(roots, "weapons", selection.Weapons, r => r, true).Values.Select(r => ((ContentRecord)r, "weapon", r.RuntimeProjection?.Effects ?? [])));
        records.AddRange(LoadSelected<CharterContent, CharterContent>(roots, "charters", profile.Runtime.Charters, r => r, true).Values.Select(r => ((ContentRecord)r, "charter", r.RuntimeProjection?.Effects ?? [])));
        records.AddRange(LoadSelected<ItemContent, ItemContent>(roots, "items", profile.Runtime.Items, r => r, true).Values.Select(r => ((ContentRecord)r, "item", r.RuntimeProjection?.Effects ?? [])));
        records.AddRange(LoadSelected<EvolutionContent, EvolutionContent>(roots, "evolutions", profile.Runtime.Evolutions, r => r, true).Values.Select(r => ((ContentRecord)r, "evolution", r.RuntimeProjection?.Effects ?? [])));
        return records.SelectMany(source => source.Effects.Select(effect => new EffectCatalogEntry(effect.Id, source.Record.Id, source.Kind,
            effect.Trigger, effect.Operation, effect.Subject, effect.Amount, effect.Radius, effect.DurationTicks, effect.FoodCost,
            effect.Conditions, EffectUnit(effect), source.Record.ImplementationNote))).OrderBy(e => e.EffectId, StringComparer.Ordinal).ToArray();
    }

    private static string EffectUnit(RuntimeEffectDefinition effect) => effect.Operation switch
    {
        "damage-pulse" or "repair-nearest" => "health-points",
        "worker-buff" or "rally-returners" or "guard-return" => "ticks",
        "plant-path" or "harvest-near" => "plots",
        "damage-young-plots" => "growth-progress-units",
        "shield-farms" => "shield-charges",
        "extend-duty" or "pause-neighbor-growth" => "ticks",
        "return-via-building" => "rerouted-groups",
        "planting-bias" => "position-units",
        "stat-add" when effect.Subject is "attack-cooldown" or "draft-min-rest" => "ticks",
        "stat-add" when effect.Subject is "draft-speed" or "worker-speed" or "attack-knockback" => "position-units",
        "stat-add" => "health-points",
        _ => throw new InvalidDataException($"Unknown effect unit: {effect.Operation}")
    };
}
