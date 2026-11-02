using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record WorldCombatUnitPolicy(string Id, WorldCombatStats Stats);
public sealed record WorldCombatServerRules(string Id, string Kind, int SchemaVersion, string Status, long ChoiceTimeoutMilliseconds,
    long PrefixIntervalMilliseconds, int InputGraceMilliseconds, int MaximumChunkCommands, int MaximumChunkBytes,
    int PveInjuryPercent, int PvpInjuryPercent, int MaximumAutoRounds, int MaximumSlots, int MaximumStat,
    int MinimumDamage, int NeutralMatchupPermille, int MarkerRadius, int MarkerHealth, int EnemyOffset,
    int EffectCapPermille, int TickRate, int CargoPerSurvivor, string[] CargoMaterialOrder, WorldCombatStats EnemyStats, WorldCombatUnitPolicy[] Units);
public sealed record WorldCombatEncounter(string Id, string EnemyId, int MinimumTroops, int Count, int HoldSeconds,
    int LimitSeconds, DomainCredit[] Credits, string? ZoneEligibilityId, string[] ProofIds, long RespawnMilliseconds);
public sealed record WorldCombatAward(DomainCredit[] Credits, string? ZoneEligibilityId, string[] ProofIds,
    int InjuryPercent, long RespawnMilliseconds);
public sealed record WorldCombatIssuedContract(Guid RunId, Guid AccountId, Guid MarchId, string TargetId, long Generation,
    string EncounterId, WorldCombatMode Mode, long IssuedAtMs, string ContractHash, WorldCombatSnapshot? Direct,
    ArmyAutoInput? Auto, WorldCombatAward Award);

public sealed record WorldCombatContent(WorldCombatServerRules Rules, WorldCombatEncounter[] Encounters,
    ResearchContent Research, string Hash, IReadOnlyDictionary<int, int> Beds)
{
    public int BedCapacity(MetaState account, string barracksKind) => Beds.GetValueOrDefault(
        (account.Estate?.Buildings ?? []).Where(b => b.Kind == barracksKind).Select(b => b.Level).DefaultIfEmpty(0).Max());
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };
    public static WorldCombatContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        byte[] policy = File.ReadAllBytes(Path.Combine(data, "opt-in/slg/world-combat-server-v1.json"));
        byte[] draft = File.ReadAllBytes(Path.Combine(data, RecruitPool.DraftPath));
        return Parse(policy, draft);
    }
    public static WorldCombatContent Parse(byte[] policy, byte[] draft)
    {
        var rules = JsonSerializer.Deserialize<WorldCombatServerRules>(policy, Json) ?? throw new InvalidDataException("Missing combat policy.");
        if (rules.Id != "slg:world_combat_server_v1" || rules.Kind != "slg-world-combat-server" || rules.SchemaVersion != 1 || rules.Status != "hypothesis" ||
            rules.ChoiceTimeoutMilliseconds <= 0 || rules.PrefixIntervalMilliseconds <= 0 || rules.InputGraceMilliseconds <= 0 ||
            rules.MaximumChunkCommands <= 0 ||
            rules.MaximumChunkBytes <= 0 || rules.MaximumChunkBytes > WorldCombatCodec.MaximumBytes ||
            rules.PveInjuryPercent is < 0 or > 100 || rules.PvpInjuryPercent is < 0 or > 100 ||
            rules.MaximumAutoRounds <= 0 || rules.MaximumSlots <= 0 || rules.MaximumSlots > WorldCombatCodec.MaximumEntries ||
            rules.MaximumStat <= 0 || rules.MinimumDamage <= 0 || rules.NeutralMatchupPermille <= 0 ||
            rules.TickRate <= 0 || rules.CargoPerSurvivor <= 0 || rules.CargoMaterialOrder is not { Length: > 0 } ||
            rules.CargoMaterialOrder.Any(string.IsNullOrWhiteSpace) || rules.CargoMaterialOrder.Distinct(StringComparer.Ordinal).Count() != rules.CargoMaterialOrder.Length || rules.MarkerRadius < 0 || rules.MarkerHealth <= 0 || rules.EnemyOffset < 0 || rules.EffectCapPermille < 0 ||
            rules.Units is null || rules.Units.Length == 0 || rules.Units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != rules.Units.Length)
            throw new InvalidDataException("Invalid world combat policy.");
        foreach (var stats in rules.Units.Select(u => u.Stats).Append(rules.EnemyStats))
            if (stats is null || stats.Health <= 0 || stats.Attack < 0 || stats.Defense < 0 || stats.Speed < 0 || stats.Range < 0 || stats.CooldownTicks <= 0)
                throw new InvalidDataException("Invalid world combat stats.");
        using var document = JsonDocument.Parse(draft);
        var encounters = document.RootElement.GetProperty("worldEncounters").EnumerateArray().Select(e =>
        {
            var objective = e.GetProperty("objective"); var outcomes = e.GetProperty("outcomes");
            return new WorldCombatEncounter(e.GetProperty("id").GetString()!, e.GetProperty("enemyReferences")[0].GetString()!,
                e.GetProperty("minimumTroops").GetInt32(), objective.GetProperty("count").GetInt32(), objective.GetProperty("holdSeconds").GetInt32(),
                e.GetProperty("timeLimitSeconds").GetInt32(), e.GetProperty("materialReward").EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
                    .Where(x => x.Value.GetInt32() > 0).Select(x => new DomainCredit("meta:" + x.Name, x.Value.GetInt32())).ToArray(),
                outcomes.GetProperty("zoneEligibilityId").GetString(), outcomes.GetProperty("researchProofIds").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal).ToArray(),
                checked(e.GetProperty("respawnSeconds").GetInt64() * 1000));
        }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (encounters.Any(e => e.MinimumTroops < 0 || e.Count <= 0 || e.HoldSeconds <= 0 || e.LimitSeconds < e.HoldSeconds || e.RespawnMilliseconds <= 0))
            throw new InvalidDataException("Invalid world encounter.");
        return new(rules, encounters, ResearchContent.Parse(draft), Convert.ToHexString(SHA256.HashData([.. policy, .. draft])), document.RootElement.GetProperty("contracts")
            .GetProperty("barracksLevels").EnumerateArray().ToDictionary(b => b.GetProperty("level").GetInt32(), b => b.GetProperty("beds").GetInt32()));
    }
}
