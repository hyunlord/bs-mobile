using SowSiege.Core;

namespace SowSiege.Server.Application;

/// <summary>
/// ADR0079 mapping from capped effect domains (content-draft sharedCaps, summed and clamped by the source domain)
/// to the Core estate inputs. Core never sees research or caps; it only receives these values.
/// </summary>
public static class EstateEffects
{
    private static readonly IReadOnlyDictionary<string, string> Production = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["estate.grainProductionPercent"] = "meta:grain",
        ["estate.timberProductionPercent"] = "meta:timber",
        ["estate.ironProductionPercent"] = "meta:iron",
    };
    public const string Construction = "construction.speedPercent";
    public const string Storage = "estate.storageSeconds";

    /// <summary>Percent domains become permille (5 percent = 50); storage seconds pass through.</summary>
    public static AccountEstateBonuses ToBonuses(IReadOnlyDictionary<string, int> values)
    {
        var production = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (domain, material) in Production)
        {
            if (values.TryGetValue(domain, out int percent) && percent != 0)
            {
                production[material] = checked(percent * 10);
            }
        }
        return new(production, checked(values.GetValueOrDefault(Construction) * 10), values.GetValueOrDefault(Storage));
    }

    /// <summary>Merges domain timelines into one ordered list of (fromUtc, bonuses); later snapshots of a domain replace earlier ones.</summary>
    public static (long FromUtc, AccountEstateBonuses Bonuses)[] Timeline(IEnumerable<EstateEffectSnapshot[]> perDomain)
    {
        var lists = perDomain.Where(l => l.Length != 0).ToArray();
        var times = lists.SelectMany(l => l.Select(s => s.FromUtc)).Distinct().Order().ToArray();
        return times.Select(t =>
        {
            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var list in lists)
            {
                var current = list.LastOrDefault(s => s.FromUtc <= t);
                foreach (var pair in current?.Values ?? new Dictionary<string, int>())
                {
                    merged[pair.Key] = pair.Value;
                }
            }
            return (t, ToBonuses(merged));
        }).ToArray();
    }
}
