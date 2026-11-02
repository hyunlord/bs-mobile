using System.Security.Cryptography;
using System.Text.Json;

namespace SowSiege.Server.Application;

public sealed record WorldMarchContent(int Slots, long EdgeMilliseconds, int MaximumSpeedPercent, string BasicHero,
    IReadOnlyDictionary<int, int> Capacity, string Hash)
{
    public static WorldMarchContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        byte[] bytes = File.ReadAllBytes(Path.Combine(data, "opt-in/slg/world-march-v1.json"));
        byte[] draftBytes = File.ReadAllBytes(Path.Combine(data, RecruitPool.DraftPath));
        using var policy = JsonDocument.Parse(bytes);
        using var draft = JsonDocument.Parse(draftBytes);
        var p = policy.RootElement;
        var contracts = draft.RootElement.GetProperty("contracts");
        int slots = p.GetProperty("slots").GetInt32();
        long edge = p.GetProperty("edgeMilliseconds").GetInt64();
        int cap = draft.RootElement.GetProperty("sharedCaps").GetProperty("march.speedPercent").GetInt32();
        var capacity = contracts.GetProperty("barracksLevels").EnumerateArray()
            .ToDictionary(x => x.GetProperty("level").GetInt32(), x => x.GetProperty("marchCapacity").GetInt32());
        string hero = contracts.GetProperty("personalFallback").GetProperty("heroId").GetString()!;
        if (p.GetProperty("kind").GetString() != "slg-world-march" || p.GetProperty("schemaVersion").GetInt32() != 1 ||
            p.GetProperty("status").GetString() != "hypothesis" || slots <= 0 || edge <= 0 || edge % 1000 != 0 ||
            cap < 0 || cap > 100 || string.IsNullOrWhiteSpace(hero) || capacity.Count == 0 || capacity.Values.Any(v => v < 0))
        {
            throw new InvalidDataException("Invalid world march policy.");
        }
        return new(slots, edge, cap, hero, capacity, Convert.ToHexString(SHA256.HashData([.. bytes, .. draftBytes])).ToLowerInvariant());
    }
}

public static class WorldMarchRules
{
    public static long TravelMilliseconds(int edges, long edgeMilliseconds, int speedPercent, int capPercent)
    {
        if (edges < 0 || edgeMilliseconds <= 0 || speedPercent < 0 || capPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edges));
        }
        long numerator = checked(edgeMilliseconds * edges * 100);
        long divisor = checked((100L + Math.Min(speedPercent, capPercent)) * 1000);
        return checked((numerator / divisor + (numerator % divisor == 0 ? 0 : 1)) * 1000);
    }

    // Reversing the same immutable path and departure speed makes elapsed travel exactly the return duration,
    // including a partially traversed edge. The client never supplies coordinates or elapsed time.
    public static long ReturnTravelMilliseconds(long startedAt, long arrivalAt, long now) =>
        Math.Clamp(now - startedAt, 0, checked(arrivalAt - startedAt));
}
