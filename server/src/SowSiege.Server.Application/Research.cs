using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record ResearchEffect(string Domain, int Raw, int Cap, int Applied);

public sealed record ResearchNode(string Id, string Name, string Category, int Level, (string Kind, int Level)[] Buildings,
    (string Id, int Level)[] Nodes, string ProofId, IReadOnlyDictionary<string, int> Cost, long Seconds, string Domain, int Value, string Unit);

/// <summary>Level-1 research nodes and queue limits from the [가설] content draft (data/opt-in/slg/content-draft-v1.json).</summary>
/// <summary>Draft contracts.personalFallback: the free knight run that can prove any listed objective (caps are [가설] data).</summary>
public sealed record PersonalFallback(string HeroId, string[] ProofIds, int MaterialRewardCap);

public sealed record ResearchContent(ResearchNode[] Nodes, int ActiveSlots, IReadOnlyDictionary<string, int> Caps, string Hash, PersonalFallback Fallback)
{
    public static ResearchContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Parse(File.ReadAllBytes(Path.Combine(data, RecruitPool.DraftPath)));
    }

    public static ResearchContent Parse(byte[] draftBytes)
    {
        using var document = JsonDocument.Parse(draftBytes);
        var root = document.RootElement;
        var nodes = root.GetProperty("researchNodes").EnumerateArray().Select(n =>
        {
            var pre = n.GetProperty("prerequisites");
            var effect = n.GetProperty("effect");
            return new ResearchNode(n.GetProperty("id").GetString()!, n.GetProperty("name").GetString()!, n.GetProperty("category").GetString()!,
                n.GetProperty("level").GetInt32(),
                pre.GetProperty("buildings").EnumerateArray().Select(b => (b.GetProperty("id").GetString()!, b.GetProperty("level").GetInt32())).ToArray(),
                pre.GetProperty("nodes").EnumerateArray().Select(b => (b.GetProperty("id").GetString()!, b.GetProperty("level").GetInt32())).ToArray(),
                pre.GetProperty("proofId").GetString()!,
                // Draft costs use bare grain/timber/iron; the account wallet uses meta: material IDs.
                n.GetProperty("cost").EnumerateObject().ToDictionary(c => "meta:" + c.Name, c => c.Value.GetInt32(), StringComparer.Ordinal),
                n.GetProperty("seconds").GetInt64(), effect.GetProperty("domain").GetString()!, effect.GetProperty("value").GetInt32(),
                effect.GetProperty("unit").GetString()!);
        }).ToArray();
        var caps = root.GetProperty("sharedCaps").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetInt32(), StringComparer.Ordinal);
        int active = root.GetProperty("contracts").GetProperty("queues").GetProperty("activeResearch").GetInt32();
        var fallbackJson = root.GetProperty("contracts").GetProperty("personalFallback");
        var fallback = new PersonalFallback(fallbackJson.GetProperty("heroId").GetString()!,
            fallbackJson.GetProperty("proofIds").EnumerateArray().Select(p => p.GetString()!).ToArray(), fallbackJson.GetProperty("materialRewardCap").GetInt32());
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Check(ids.Count == nodes.Length && nodes.Length > 0, "unique nodes");
        Check(nodes.All(n => n.Level == 1 && n.Seconds > 0 && n.Cost.Values.All(v => v >= 0)), "level-1 cost and time");
        Check(nodes.All(n => n.Nodes.All(p => ids.Contains(p.Id) && p.Id != n.Id)), "node prerequisites exist");
        Check(nodes.All(n => caps.ContainsKey(n.Domain)), "effect domain has a shared cap");
        Check(active >= 1, "active research slots");
        Check(Acyclic(nodes), "prerequisite graph is acyclic");
        Check(nodes.All(n => fallback.ProofIds.Contains(n.ProofId)), "every node proof has a personal fallback run");
        Check(fallback.MaterialRewardCap >= 0, "fallback reward cap");
        return new(nodes, active, caps, Convert.ToHexString(SHA256.HashData(draftBytes)).ToLowerInvariant(), fallback);
    }

    private static bool Acyclic(ResearchNode[] nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (state.TryGetValue(id, out int s))
            {
                return s == 2;
            }
            state[id] = 1;
            bool ok = byId[id].Nodes.All(p => Visit(p.Id));
            state[id] = 2;
            return ok;
        }
        return nodes.All(n => Visit(n.Id));
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidDataException("Research content invalid: " + what);
        }
    }
}

/// <summary>Pure research rules: no I/O and no clock reads; the caller passes server time.</summary>
public static class ResearchRules
{
    public static string[] LockReasons(ResearchNode node, MetaState state, IReadOnlyDictionary<string, int> levels, IReadOnlySet<string> proofs)
    {
        var reasons = new List<string>();
        var buildings = state.Estate?.Buildings ?? [];
        foreach (var (kind, level) in node.Buildings)
        {
            if (buildings.Where(b => b.Kind == kind).Select(b => b.Level).DefaultIfEmpty(0).Max() < level)
            {
                reasons.Add("building:" + kind + ":" + level);
            }
        }
        foreach (var (id, level) in node.Nodes)
        {
            if (levels.GetValueOrDefault(id) < level)
            {
                reasons.Add("node:" + id + ":" + level);
            }
        }
        if (!proofs.Contains(node.ProofId))
        {
            reasons.Add("proof:" + node.ProofId);
        }
        return reasons.ToArray();
    }

    public static bool Affordable(ResearchNode node, MetaState state) => node.Cost.All(c => state.Wallet.GetValueOrDefault(c.Key) >= c.Value);

    public static MetaState Charge(ResearchNode node, MetaState state)
    {
        var wallet = new Dictionary<string, int>(state.Wallet);
        foreach (var (material, amount) in node.Cost)
        {
            wallet[material] = checked(wallet.GetValueOrDefault(material) - amount);
        }
        return state with { Wallet = wallet };
    }

    /// <summary>Same-domain values are summed then clamped once by the shared cap.</summary>
    public static ResearchEffect[] Effects(ResearchContent content, IReadOnlyDictionary<string, int> levels) =>
        content.Nodes.Where(n => levels.GetValueOrDefault(n.Id) >= n.Level).GroupBy(n => n.Domain, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => { int raw = g.Sum(n => n.Value); int cap = content.Caps[g.Key]; return new ResearchEffect(g.Key, raw, cap, Math.Min(raw, cap)); })
            .ToArray();
}
