using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SowSiege.Core;

public sealed record RunOptions(int Seed, string HeroId, string EstateId, string Policy);
public sealed record SimulationResult(string Scope, int Seed, string HeroId, string EstateId, string Policy, string ToolId,
    int Ticks, long Damage, long Growth, string Hash);

/// <summary>S0 synthetic CI workload. It does not implement S2 combat or demonstrate game balance.</summary>
public sealed class Simulation
{
    private readonly ContentCatalog catalog;
    private readonly RunOptions options;
    private readonly Random random;
    private readonly HeroDefinition hero;
    private readonly EstateDefinition estate;
    private readonly ToolDefinition tool;
    private readonly PolicyDefinition policy;
    private int ticks;
    private long damage;
    private long growth;

    public Simulation(ContentCatalog catalog, RunOptions options)
    {
        this.catalog = catalog;
        this.options = options;
        random = new Random(options.Seed);
        hero = catalog.Heroes[options.HeroId];
        estate = catalog.Estates[options.EstateId];
        tool = catalog.Tools[hero.StartingTool];
        policy = catalog.Tuning.Policies[options.Policy];
    }

    public bool IsComplete => ticks >= catalog.Tuning.DurationTicks;

    public void Tick()
    {
        if (IsComplete) { throw new InvalidOperationException("Run is already complete."); }
        damage = checked(damage + (long)(tool.Activation.Damage + random.Next(catalog.Tuning.DamageRollMax)) * hero.DamageMultiplier * policy.DamageMultiplier);
        growth = checked(growth + (long)tool.Growth.Yield * estate.GrowthMultiplier * policy.GrowthMultiplier);
ticks++;
    }

    public SimulationResult Result()
    {
        var state = JsonSerializer.Serialize(new { ticks, damage, growth });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        return new("S0 synthetic scaffold; not gameplay balance", options.Seed, options.HeroId, options.EstateId,
            options.Policy, tool.Id, ticks, damage, growth, hash);
    }
}
