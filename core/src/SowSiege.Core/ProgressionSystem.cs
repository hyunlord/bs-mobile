namespace SowSiege.Core;

internal sealed class ProgressionSystem(ContentCatalog catalog, RunOptions options, WorldState world, TrackedRandom random, RuntimeSystem? runtime = null, ExperimentSystem? experiment = null)
{
    public void Tick()
    {
        var tuning = catalog.Tuning.World.Progression;
        while (world.Experience >= RequiredExperience())
        {
            world.Experience -= RequiredExperience();
            world.Level++;
            Deal();
            if (world.PendingCards.Length == 0) { continue; }
            if (options.ManualCards) { return; }
            Select(Choose(world.PendingCards));
        }
    }

    private void Deal()
    {
        var pool = catalog.Weapons.Keys.Concat(catalog.Tools.Keys).Concat(catalog.Runtime?.Charters.Keys ?? []).Order(StringComparer.Ordinal)
            .Where(id => CanOffer(id) && !world.BannedCards.Contains(id)).ToList();
        var offer = new List<string>();
        if (world.LockedCard is not null && pool.Remove(world.LockedCard)) { offer.Add(world.LockedCard); }
        while (offer.Count < catalog.Tuning.World.Progression.CardCount && pool.Count > 0)
        {
            var index = runtime is null ? random.Next(pool.Count) : WeightedOffer(pool);
            offer.Add(pool[index]); pool.RemoveAt(index);
        }
        world.PendingCards = offer.ToArray();
    }

    public void Select(string id)
    {
        RequireOffered(id);
        var rarity = RollRarity();
        if (catalog.Runtime?.Charters.ContainsKey(id) == true)
        {
            world.Runtime!.Charters[id] = checked(world.Runtime.Charters.GetValueOrDefault(id) + rarity.UpgradeAmount);
        }
        else
        {
            var owned = world.Equipment.FirstOrDefault(equipment => equipment.Id == id);
            if (owned is null) { world.Equipment.Add(new() { Id = id, Level = rarity.UpgradeAmount }); }
            else { owned.Level = checked(owned.Level + rarity.UpgradeAmount); }
        }
        runtime?.UnlockEvolutions();
        world.Cards.Add(new(world.Tick, world.PendingCards.ToArray(), id, rarity.Name, world.Level));
        world.PendingCards = [];
        world.LockedCard = null;
    }

    public void Reroll()
    {
        if (world.PendingCards.Length == 0 || world.Rerolls <= 0) { throw new InvalidOperationException("No pending offer or reroll budget."); }
        world.Rerolls--;
        Deal();
    }

    public void Ban(string id)
    {
        RequireOffered(id);
        if (world.Bans <= 0) { throw new InvalidOperationException("No ban budget."); }
        world.Bans--;
        world.BannedCards.Add(id);
        if (world.LockedCard == id) { world.LockedCard = null; }
        Deal();
    }

    public void Lock(string id)
    {
        RequireOffered(id);
        if (world.Locks <= 0) { throw new InvalidOperationException("No lock budget."); }
        world.Locks--;
        world.LockedCard = id;
    }

    private void RequireOffered(string id)
    {
        if (!world.PendingCards.Contains(id, StringComparer.Ordinal)) { throw new ArgumentException("Card is not in the pending offer.", nameof(id)); }
    }

    private long RequiredExperience() => experiment?.RequiredExperience(world.Level) ?? catalog.Tuning.World.Progression.BaseExperience + (long)(world.Level - 1) * catalog.Tuning.World.Progression.ExperiencePerLevel;
    private bool CanOffer(string id)
    {
        if (catalog.Runtime?.Charters.ContainsKey(id) == true) { return world.Runtime!.Charters.ContainsKey(id) || world.Runtime.Charters.Count < catalog.Runtime.Tuning.CharterSlots; }
        if (world.Equipment.Any(equipment => equipment.Id == id)) { return true; }
        var tool = catalog.Tools.ContainsKey(id);
        var count = world.Equipment.Count(equipment => catalog.Tools.ContainsKey(equipment.Id) == tool);
        return count < (tool ? catalog.Runtime?.Tuning.ToolSlots ?? catalog.Tuning.World.Progression.ToolSlots : catalog.Runtime?.Tuning.WeaponSlots ?? catalog.Tuning.World.Progression.WeaponSlots);
    }
    private string Choose(IReadOnlyList<string> offer)
    {
        if (options.Policy == "random") { return offer[random.Next(offer.Count)]; }
        if (experiment is not null && options.Policy == "mixed")
        {
            var owned = world.Equipment.Select(equipment => equipment.Id).Concat(world.Runtime?.Charters.Keys.AsEnumerable() ?? []).ToArray();
            var category = catalog.Experiment!.MixedCategoryOrder.Where(category => offer.Any(id => Category(id) == category))
                .OrderBy(category => owned.Count(id => Category(id) == category)).First();
            offer = offer.Where(id => Category(id) == category).ToArray();
        }
        var policy = catalog.Tuning.Policies[options.Policy];
        var weights = offer.Select(id => policy.CardWeights[Category(id)]).ToArray();
        var roll = random.Next(weights.Sum());
        for (var index = 0; index < offer.Count; index++)
        {
            roll -= weights[index];
            if (roll < 0) { return offer[index]; }
        }
        throw new InvalidOperationException("Card weights must be positive.");
    }
    private string Category(string id) => catalog.Runtime?.Charters.TryGetValue(id, out var charter) == true ? charter.PolicyCategory : catalog.Tools.TryGetValue(id, out var tool) ? tool.Growth.Target : "weapon";
    private int WeightedOffer(IReadOnlyList<string> pool)
    {
        var weights = pool.Select(id => runtime!.OfferWeight(Category(id))).ToArray();
        var roll = random.Next(weights.Sum());
        for (var index = 0; index < weights.Length; index++) { roll -= weights[index]; if (roll < 0) { return index; } }
        throw new InvalidOperationException("Positive offer weights required.");
    }
    private RarityDefinition RollRarity()
    {
        var rarities = catalog.Tuning.World.Progression.Rarities;
        var roll = random.Next(rarities.Sum(rarity => rarity.Weight));
        foreach (var rarity in rarities)
        {
            roll -= rarity.Weight;
            if (roll < 0) { return rarity; }
        }
        throw new InvalidOperationException("Rarity weights must be positive.");
    }
}
