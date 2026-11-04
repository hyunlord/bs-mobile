using System.Security.Cryptography;
using System.Text.Json;

namespace SowSiege.Server.Application;

public sealed record ScoutMaterial(string MaterialId, int ProtectedAmount);
public sealed record CitySecurityPolicy(long NewShieldMilliseconds, long FreeShieldMilliseconds, long FreePeriodMilliseconds,
    long HostileReturnCooldownMilliseconds, long RepeatAttackCooldownMilliseconds, ScoutMaterial[] Materials, int LootPermille, string Hash)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static CitySecurityPolicy Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        byte[] bytes = File.ReadAllBytes(Path.Combine(data, "opt-in/slg/city-security-v1.json"));
        var policy = JsonSerializer.Deserialize<CitySecurityPolicy>(bytes, Json) ?? throw new InvalidDataException("Missing city security policy.");
        if (policy.NewShieldMilliseconds <= 0 || policy.FreeShieldMilliseconds <= 0 ||
            policy.FreePeriodMilliseconds < policy.FreeShieldMilliseconds || policy.HostileReturnCooldownMilliseconds < 0 ||
            policy.RepeatAttackCooldownMilliseconds < 0 || policy.LootPermille is < 0 or > 1000 ||
            policy.Materials is not { Length: > 0 } || policy.Materials.Any(m => string.IsNullOrWhiteSpace(m.MaterialId) || m.ProtectedAmount < 0) ||
            policy.Materials.Select(m => m.MaterialId).Distinct(StringComparer.Ordinal).Count() != policy.Materials.Length)
        {
            throw new InvalidDataException("Invalid city security policy.");
        }
        return policy with { Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }
}

public sealed record CityShieldState(long UtcHighWatermark, long ShieldUntilMs, long NextFreeAtMs, long HostileUntilMs);
public sealed record CityShieldReply(bool Done, string Reason, CityShieldState State);
public sealed record ScoutTroops(string UnitId, int Ready);
public sealed record ScoutLoot(string MaterialId, int MaximumLoot);
public sealed record CityScoutReport(long ObservedAtMs, bool ShieldActive, ScoutTroops[] Troops, ScoutLoot[] Loot);
public sealed record CitySecurityDecision(bool Allowed, string Reason, CityScoutReport? Scout = null);

public static class CitySecurityRules
{
    public static CityShieldState Create(long now, CitySecurityPolicy policy, bool newAccount) =>
        new(now, newAccount ? checked(now + policy.NewShieldMilliseconds) : 0, checked(now + policy.FreePeriodMilliseconds), 0);

    public static bool Protected(CityShieldState state, long now) =>
        Math.Max(now, state.UtcHighWatermark) < state.ShieldUntilMs;

    public static CityShieldReply Activate(CityShieldState state, long now, bool hostileActive, CitySecurityPolicy policy)
    {
        now = Math.Max(now, state.UtcHighWatermark);
        state = state with { UtcHighWatermark = now };
        if (Protected(state, now)) { return new(false, "shield-active", state); }
        if (hostileActive) { return new(false, "hostile-march-active", state); }
        if (now < state.HostileUntilMs) { return new(false, "hostile-cooldown", state); }
        if (now < state.NextFreeAtMs) { return new(false, "shield-cooldown", state); }
        return new(true, "shield-activated", state with
        {
            ShieldUntilMs = checked(now + policy.FreeShieldMilliseconds),
            NextFreeAtMs = checked(now + policy.FreePeriodMilliseconds),
        });
    }

    public static int LootLimit(int wallet, int protectedAmount, int permille) =>
        checked((int)(Math.Max(0L, (long)wallet - protectedAmount) * permille / 1000));
}
