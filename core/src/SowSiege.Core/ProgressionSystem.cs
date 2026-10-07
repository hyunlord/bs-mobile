namespace SowSiege.Core;

internal sealed class ProgressionSystem(ContentCatalog catalog, RunOptions options, WorldState world, TrackedRandom random)
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
        var pool = catalog.Weapons.Keys.Concat(catalog.Tools.Keys).Order(StringComparer.Ordinal)
            .Where(id => CanOffer(id) && !world.BannedCards.Contains(id)).ToList();
        var offer = new List<string>();
        if (world.LockedCard is not null && pool.Remove(world.LockedCard)) { offer.Add(world.LockedCard); }
        while (offer.Count < catalog.Tuning.World.Progression.CardCount && pool.Count > 0)
        {
            var index = random.Next(pool.Count);
            offer.Add(pool[index]); pool.RemoveAt(index);
        }
        world.PendingCards = offer.ToArray();
    }

    public void Select(string id)
    {
        RequireOffered(id);
        var rarity = RollRarity();
        var owned = world.Equipment.FirstOrDefault(equipment => equipment.Id == id);
        if (owned is null) { world.Equipment.Add(new() { Id = id, Level = rarity.UpgradeAmount }); }
        else { owned.Level = checked(owned.Level + rarity.UpgradeAmount); }
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

    private long RequiredExperience() => catalog.Tuning.World.Progression.BaseExperience + (long)(world.Level - 1) * catalog.Tuning.World.Progression.ExperiencePerLevel;
    private bool CanOffer(string id)
    {
        if (world.Equipment.Any(equipment => equipment.Id == id)) { return true; }
        var tool = catalog.Tools.ContainsKey(id);
        var count = world.Equipment.Count(equipment => catalog.Tools.ContainsKey(equipment.Id) == tool);
        return count < (tool ? catalog.Tuning.World.Progression.ToolSlots : catalog.Tuning.World.Progression.WeaponSlots);
    }
    private string Choose(IReadOnlyList<string> offer)
    {
        if (options.Policy == "random") { return offer[random.Next(offer.Count)]; }
        var policy = catalog.Tuning.Policies[options.Policy];
        var weights = offer.Select(id => policy.CardWeights[catalog.Tools.TryGetValue(id, out var tool) ? tool.Growth.Target : "weapon"]).ToArray();
        var roll = random.Next(weights.Sum());
        for (var index = 0; index < offer.Count; index++)
        {
            roll -= weights[index];
            if (roll < 0) { return offer[index]; }
        }
        throw new InvalidOperationException("Card weights must be positive.");
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
