using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SowSiege.Server.Application;

public sealed record RecruitEntry(string HeroId, string Rarity, int Weight);
public sealed record RecruitOddsEntry(string HeroId, string Rarity, int Weight, long Scale, double Probability);

/// <summary>Immutable recruit pool loaded from data/opt-in/slg. Every number in it is a [가설] from GDD16.</summary>
public sealed record RecruitPool(string PoolFamilyId, string PoolVersion, int CostGems, int GuaranteeAfterMisses, string GuaranteedRarity,
    int Scale, RecruitEntry[] Entries, IReadOnlyDictionary<string, int> DuplicateShards, string PolicyHash)
{
    public const string PoolPath = "opt-in/slg/recruit-pool-v1.json";
    public const string DraftPath = "opt-in/slg/content-draft-v1.json";

    public static RecruitPool Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Parse(File.ReadAllBytes(Path.Combine(data, PoolPath)), File.ReadAllBytes(Path.Combine(data, DraftPath)));
    }

    public static RecruitPool Parse(byte[] poolBytes, byte[] draftBytes)
    {
        using var pool = JsonDocument.Parse(poolBytes);
        using var draft = JsonDocument.Parse(draftBytes);
        var p = pool.RootElement;
        Check(p.GetProperty("kind").GetString() == "slg-recruit-pool" && p.GetProperty("schemaVersion").GetInt32() == 1, "kind/schemaVersion");
        var draftRarity = draft.RootElement.GetProperty("heroes").EnumerateArray()
            .ToDictionary(h => h.GetProperty("id").GetString()!, h => h.GetProperty("rarity").GetString()!, StringComparer.Ordinal);
        var entries = p.GetProperty("entries").EnumerateArray()
            .Select(e => new RecruitEntry(e.GetProperty("heroId").GetString()!, e.GetProperty("rarity").GetString()!, e.GetProperty("weight").GetInt32())).ToArray();
        var shards = p.GetProperty("duplicateShards").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt32(), StringComparer.Ordinal);
        int scale = p.GetProperty("probabilityScale").GetInt32();
        string guaranteed = p.GetProperty("guaranteedRarity").GetString()!;
        Check(entries.Length > 0 && entries.Select(e => e.HeroId).Distinct(StringComparer.Ordinal).Count() == entries.Length, "entries unique");
        Check(entries.All(e => e.Weight > 0) && entries.Sum(e => (long)e.Weight) == scale, "weights sum to probabilityScale");
        Check(entries.All(e => draftRarity.TryGetValue(e.HeroId, out string? rarity) && rarity == e.Rarity), "heroes and rarities match content draft");
        Check(entries.All(e => e.HeroId != "core:frontier_knight"), "free default hero excluded");
        Check(entries.Any(e => e.Rarity == guaranteed), "guaranteed rarity present");
        Check(entries.All(e => shards.TryGetValue(e.Rarity, out int amount) && amount > 0), "duplicate shards per rarity");
        var result = new RecruitPool(p.GetProperty("poolFamilyId").GetString()!, p.GetProperty("poolVersion").GetString()!,
            p.GetProperty("costGems").GetInt32(), p.GetProperty("guaranteeAfterMisses").GetInt32(), guaranteed, scale, entries, shards,
            Convert.ToHexString(SHA256.HashData(poolBytes)).ToLowerInvariant());
        Check(result.CostGems > 0 && result.GuaranteeAfterMisses >= 0 && result.PoolVersion.Length > 0, "scalars");
        return result;
    }

    /// <summary>Published per-hero odds for the next draw given the current consecutive-miss counter.</summary>
    public RecruitOddsEntry[] Odds(int pity)
    {
        var eligible = Eligible(pity);
        long total = eligible.Sum(e => (long)e.Weight);
        return Entries.Select(e => eligible.Contains(e)
            ? new RecruitOddsEntry(e.HeroId, e.Rarity, e.Weight, total, (double)e.Weight / total)
            : new RecruitOddsEntry(e.HeroId, e.Rarity, 0, total, 0)).ToArray();
    }

    internal RecruitEntry[] Eligible(int pity) => pity >= GuaranteeAfterMisses ? Entries.Where(e => e.Rarity == GuaranteedRarity).ToArray() : Entries;

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidDataException("Recruit pool invalid: " + what);
        }
    }
}

public sealed record RecruitDrawResult(long DrawIndex, RecruitEntry Entry, bool Guaranteed, long Roll, int PityBefore, int PityAfter, string SeedHex);

/// <summary>Pure, reproducible server draw. The seed is HMAC(serverKey, account, drawIndex, poolVersion); clients never supply it.</summary>
public static class RecruitDraw
{
    /// <summary>Seed material the store feeds into the HMAC; the handler never sees the key.</summary>
    public static string Material(long drawIndex, string poolVersion) =>
        string.Join('\n', "sow-recruit-v1", drawIndex.ToString(CultureInfo.InvariantCulture), poolVersion);

    public static byte[] Seed(byte[] key, Guid account, long drawIndex, string poolVersion) =>
        DomainSeed.Compute(key, account, Material(drawIndex, poolVersion));

    public static RecruitDrawResult Draw(RecruitPool pool, byte[] key, Guid account, long drawIndex, int pity) =>
        Draw(pool, Seed(key, account, drawIndex, pool.PoolVersion), drawIndex, pity);

    public static RecruitDrawResult Draw(RecruitPool pool, byte[] seed, long drawIndex, int pity)
    {
        var eligible = pool.Eligible(pity);
        long total = eligible.Sum(e => (long)e.Weight);
        // High 64 bits of x*total is floor(x*total/2^64): uniform in [0,total) without modulo bias beyond 2^-64.
        long roll = (long)Math.BigMul(BinaryPrimitives.ReadUInt64BigEndian(seed), (ulong)total, out _);
        var entry = Pick(eligible, roll);
        bool guaranteed = pity >= pool.GuaranteeAfterMisses;
        int after = entry.Rarity == pool.GuaranteedRarity ? 0 : checked(pity + 1);
        return new(drawIndex, entry, guaranteed, roll, pity, after, Convert.ToHexString(seed).ToLowerInvariant());
    }

    public static RecruitEntry Pick(RecruitEntry[] eligible, long roll)
    {
        foreach (var entry in eligible)
        {
            if (roll < entry.Weight)
            {
                return entry;
            }
            roll -= entry.Weight;
        }
        throw new InvalidOperationException("Roll outside eligible weights.");
    }
}
