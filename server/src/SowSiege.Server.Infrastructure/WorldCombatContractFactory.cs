using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

/// <summary>Only server march snapshots and locked defender state enter this boundary.</summary>
public sealed class WorldCombatContractFactory(WorldCombatContent content, ContentCatalog catalog, string dataHash, string profileId)
{
    public WorldCombatIssuedContract Issue(WorldMarch march, string encounterId, Guid runId, long issuedAtMs,
        WorldCombatMode mode, TrainingState? defender = null, JsonElement? defenderDomains = null, JsonElement? defenderAccount = null)
    {
        if (runId == Guid.Empty || march.AccountId == Guid.Empty || march.MarchId == Guid.Empty || issuedAtMs < 0 ||
            !Enum.IsDefined(mode) || march.Troops.Any(t => t.Count < 0) || march.Troops.Select(t => t.UnitId).Distinct(StringComparer.Ordinal).Count() != march.Troops.Length)
        { throw new CommandConflictException("invalid-combat-contract"); }
        if (march.HeroId != content.Research.Fallback.HeroId || !catalog.Heroes.ContainsKey(march.HeroId))
        { throw new CommandConflictException("world-hero-mapping-unsupported"); }
        if (!march.Snapshot.TryGetProperty("combatAccount", out var account))
        { throw new CommandConflictException("world-account-snapshot-required"); }
        ValidateAccountEffects(account);
        if (!march.Snapshot.TryGetProperty("researchHash", out var researchHash) ||
            !string.Equals(researchHash.GetString(), content.Research.Hash, StringComparison.OrdinalIgnoreCase))
        { throw new CommandConflictException("world-research-content-changed"); }
        var domains = march.Snapshot.GetProperty("domains");
        var effects = Effects(domains, mode);
        var rules = content.Rules;
        if (rules.TickRate != catalog.Tuning.TickRate) { throw new CommandConflictException("world-profile-tick-rate-mismatch"); }
        int count = checked(march.Troops.Sum(t => t.Count));
        if (count > rules.MaximumSlots) { throw new CommandConflictException("world-army-too-large"); }
        WorldCombatEncounter? encounter = mode == WorldCombatMode.CityPvp ? null : content.Encounters.SingleOrDefault(e => e.Id == encounterId)
            ?? throw new CommandConflictException("unknown-world-encounter");
        if (encounter is not null && count < encounter.MinimumTroops) { throw new CommandConflictException("world-minimum-troops"); }
        if (mode == WorldCombatMode.CityPvp && (march.TargetAccountId is null || defender is null || defenderDomains is null || defenderAccount is null))
        { throw new CommandConflictException("world-defender-snapshot-required"); }
        if (defenderAccount is { } da) { ValidateAccountEffects(da); }
        if (defenderDomains is { } dd) { _ = Effects(dd, mode); }
        int seed = BinaryPrimitives.ReadInt32LittleEndian(SHA256.HashData(runId.ToByteArray()));
        var award = new WorldCombatAward(CapAward(encounter?.Credits ?? [], count), encounter?.ZoneEligibilityId,
            encounter?.ProofIds.ToArray() ?? [], mode == WorldCombatMode.CityPvp ? rules.PvpInjuryPercent : rules.PveInjuryPercent,
            encounter?.RespawnMilliseconds ?? 0);
        string emptyHash = Convert.ToHexString(new byte[SHA256.HashSizeInBytes]);
        WorldCombatSnapshot? direct = null; ArmyAutoInput? auto = null;
        if (mode == WorldCombatMode.DirectPve)
        {
            var replayHeader = ReplayCodec.Header(new(new RunOptions(seed, march.HeroId, catalog.Tuning.DefaultEstate, "manual", ManualCards: true),
                AimMode.Movement, dataHash));
            var e = encounter!; var map = catalog.Tuning.World.Map; int x = map.Width / 2, y = map.Height / 2;
            int limit = checked(e.LimitSeconds * catalog.Tuning.TickRate);
            if (limit > catalog.Tuning.DurationTicks || !catalog.Enemies.ContainsKey(e.EnemyId) || x + rules.EnemyOffset > map.Width)
            { throw new CommandConflictException("world-profile-incompatible"); }
            var troops = march.Troops.OrderBy(t => t.UnitId, StringComparer.Ordinal).Select(t => new WorldTroopType(t.UnitId, t.Count, Unit(t.UnitId),
                Enumerable.Range(0, t.Count).Select(i => new WorldTroopSlot(i, x, y)).ToArray())).ToArray();
            direct = new(emptyHash, mode, new(profileId, ReviewShowcaseCatalog.Hash(catalog), "world-combat-v1", 1, content.Hash,
                dataHash, 1, 1, 1, catalog.Tuning.TickRate, seed, replayHeader.RngAlgorithm), new(0, 0, map.Width, map.Height),
                new(march.HeroId, dataHash, []), new(rules.InputGraceMilliseconds, rules.MaximumChunkCommands, rules.MaximumChunkBytes),
                [new("slg:defeat", WorldObjectiveKind.DefeatDesignated, "slg:target", e.Count, 0, 0),
                 new("slg:hold", WorldObjectiveKind.HoldMarker, "slg:marker", 0, checked(e.HoldSeconds * catalog.Tuning.TickRate), 0),
                 new("slg:limit", WorldObjectiveKind.TimeLimit, "", 0, 0, limit)], troops,
                [new("slg:target", e.EnemyId, 0, e.Count, x + rules.EnemyOffset, y, rules.EnemyStats)],
                [new("slg:marker", x, y, rules.MarkerRadius, rules.MarkerHealth, true)], effects);
            WorldCombatCodec.Validate(new(direct));
        }
        else
        {
            var attack = march.Troops.OrderBy(t => t.UnitId, StringComparer.Ordinal).Select(t => AutoTroop(t.UnitId, t.Count, Unit(t.UnitId))).ToArray();
            var defense = mode == WorldCombatMode.CityPvp
                ? defender!.Units.OrderBy(t => t.UnitId, StringComparer.Ordinal).Select(t => AutoTroop(t.UnitId, t.Ready, Unit(t.UnitId))).ToArray()
                : new[] { AutoTroop("slg:target", encounter!.Count, rules.EnemyStats) };
            if (defense.Sum(t => t.Ordinals.Count) > rules.MaximumSlots) { throw new CommandConflictException("world-army-too-large"); }
            var ids = attack.Concat(defense).Select(t => t.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var autoRules = new ArmyAutoRules("army-auto-v1", rules.MaximumAutoRounds, rules.MaximumSlots, rules.MaximumStat,
                rules.MinimumDamage, 0, 0, 0, [new(WorldCombatMode.AutoPve, true, true), new(WorldCombatMode.CityPvp, true, true)],
                ids.SelectMany(a => ids.Select(d => new ArmyAutoMatchup(a, d, rules.NeutralMatchupPermille))).ToArray());
            auto = new(emptyHash, mode, dataHash, autoRules, new(true, [], attack), new(false, [], defense), 0, 0);
        }
        // Fixed property order and sorted nested lists form the server canonical envelope; no client JSON is hashed.
        byte[] unsigned = JsonSerializer.SerializeToUtf8Bytes(new { runId, march.AccountId, march.MarchId, march.TargetId, march.Generation,
            march.TargetAccountId, encounterId, mode, issuedAtMs, policyHash = content.Hash, profileId, profileHash = ReviewShowcaseCatalog.Hash(catalog), seed, marchSnapshot = Canonical(march.Snapshot),
            defenderSnapshot = defenderDomains is { } value ? Canonical(value) : null,
            defenderAccountSnapshot = defenderAccount is { } state ? Canonical(state) : null, direct, auto, award }, WorldCombatContent.Json);
        string hash = Convert.ToHexString(SHA256.HashData(unsigned));
        if (direct is not null) { direct = WorldCombatCodec.Freeze(new(direct with { ContractHash = hash })).Snapshot; }
        if (auto is not null) { auto = auto with { ContractHash = hash }; }
        return new(runId, march.AccountId, march.MarchId, march.TargetId, march.Generation, encounterId, mode, issuedAtMs, hash, direct, auto, award);
    }

    private DomainCredit[] CapAward(DomainCredit[] awards, int count)
    {
        if (awards.Any(a => a.Amount < 0 || !content.Rules.CargoMaterialOrder.Contains(a.Material, StringComparer.Ordinal)) ||
            awards.Select(a => a.Material).Distinct(StringComparer.Ordinal).Count() != awards.Length)
        { throw new InvalidDataException("Unsupported or duplicate reward material."); }
        long remaining = checked((long)count * content.Rules.CargoPerSurvivor);
        var credits = new List<DomainCredit>();
        foreach (var material in content.Rules.CargoMaterialOrder)
        {
            int amount = (int)Math.Min(remaining, awards.SingleOrDefault(a => a.Material == material)?.Amount ?? 0);
            if (amount > 0) { credits.Add(new(material, amount)); }
            remaining -= amount;
        }
        return credits.ToArray();
    }

    private static void ValidateAccountEffects(JsonElement account)
    {
        if (!account.TryGetProperty("activeVassalIds", out var active) || active.ValueKind != JsonValueKind.Array)
        { throw new CommandConflictException("world-account-snapshot-required"); }
        if (active.GetArrayLength() != 0) { throw new CommandConflictException("world-vassal-mapping-unsupported"); }
    }

    private WorldCombatStats Unit(string id) => content.Rules.Units.SingleOrDefault(u => u.Id == id)?.Stats
        ?? throw new CommandConflictException("world-unit-mapping-unsupported");
    private static ArmyAutoTroop AutoTroop(string id, int count, WorldCombatStats s) =>
        new(id, s.Health, s.Attack, s.Defense, 0, Enumerable.Range(0, count).ToArray());

    private WorldResolvedEffect[] Effects(JsonElement domains, WorldCombatMode mode)
    {
        if (!domains.TryGetProperty("research", out var research) || !research.TryGetProperty("levels", out var levels))
        { throw new CommandConflictException("world-effects-snapshot-required"); }
        var groups = new Dictionary<MetaAbility, List<(ResearchNode Node, bool Hero)>>();
        foreach (var level in levels.EnumerateObject().Where(p => p.Value.GetInt32() > 0))
        {
            var node = content.Research.Nodes.SingleOrDefault(n => n.Id == level.Name);
            if (node is null || level.Value.GetInt32() != node.Level) { throw new CommandConflictException("world-research-mapping-unsupported"); }
            if (!node.Domain.StartsWith("run.", StringComparison.Ordinal)) { continue; }
            if (mode != WorldCombatMode.DirectPve) { throw new CommandConflictException("world-auto-percentage-effect-unsupported"); }
            (MetaAbility ability, bool hero) = node.Domain switch
            {
                "run.heroDamagePercent" => (MetaAbility.Attack, true),
                "run.allyDamagePercent" => (MetaAbility.Attack, false),
                "run.allyMaxHealthPercent" => (MetaAbility.Allies, false),
                _ => throw new CommandConflictException("world-research-mapping-unsupported")
            };
            if (node.Unit != "percent") { throw new CommandConflictException("world-effect-unit-unsupported"); }
            if (!groups.TryGetValue(ability, out var group)) { groups[ability] = group = []; }
            group.Add((node, hero));
        }
        return Enum.GetValues<MetaAbility>().Select(ability =>
        {
            var group = groups.GetValueOrDefault(ability) ?? [];
            if (group.Select(g => g.Hero).Distinct().Count() > 1) { throw new CommandConflictException("world-effect-scope-conflict"); }
            var sources = group.OrderBy(g => g.Node.Id, StringComparer.Ordinal).Select(g => new WorldRawEffect(WorldEffectSourceKind.Research,
                g.Node.Id, content.Research.Hash.ToUpperInvariant(), checked(g.Node.Value * 10))).ToArray();
            int raw = sources.Sum(s => s.Raw);
            int cap = group.Count == 0 ? content.Rules.EffectCapPermille : checked(content.Research.Caps[group[0].Node.Domain] * 10);
            return new WorldResolvedEffect(ability, raw, cap, Math.Min(raw, cap), group.Any(g => g.Hero), group.Any(g => !g.Hero), false, sources);
        }).ToArray();
    }
    private static object? Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => Canonical(p.Value), StringComparer.Ordinal),
        JsonValueKind.Array => value.EnumerateArray().Select(Canonical).ToArray(),
        JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetInt64(),
        JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
        _ => throw new InvalidDataException("Unsupported server snapshot JSON.")
    };
}
