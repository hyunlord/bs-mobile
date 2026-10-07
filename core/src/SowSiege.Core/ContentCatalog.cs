namespace SowSiege.Core;

public sealed record Activation(int Damage);
public sealed record Growth(string Target, int Yield);
public sealed record ToolDefinition(string Id, string[] Tags, Activation Activation, Growth Growth, string FloorRationale, string[] AntiSynergy);
public sealed record HeroDefinition(string Id, string StartingTool, int DamageMultiplier);
public sealed record EstateDefinition(string Id, int GrowthMultiplier);
public sealed record PolicyDefinition(int DamageMultiplier, int GrowthMultiplier);
public sealed record Tuning(int TickRate, int DurationTicks, int DamageRollMax, string DefaultHero, string DefaultEstate, Dictionary<string, PolicyDefinition> Policies);

public sealed record ContentCatalog(Tuning Tuning, IReadOnlyDictionary<string, ToolDefinition> Tools,
    IReadOnlyDictionary<string, HeroDefinition> Heroes, IReadOnlyDictionary<string, EstateDefinition> Estates);
