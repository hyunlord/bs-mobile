using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    /// <summary>
    /// Opt-in account estate inputs (ADR0079). Core only applies the given values; the caller sums sources and applies
    /// caps from data. Absent or all-zero inputs take the historical arithmetic unchanged.
    /// ProductionPermille: per resource, the extra share of the site's current rate (50 = +5%).
    /// ConstructionSpeedPermille: speed bonus for newly started construction, duration = ceil(seconds * 1000 / (1000 + bonus)).
    /// StorageSeconds: extra production storage seconds before the configured storage maximum.
    /// </summary>
    public sealed record AccountEstateBonuses(IReadOnlyDictionary<string, int> ProductionPermille, int ConstructionSpeedPermille, long StorageSeconds)
    {
        public static readonly AccountEstateBonuses None = new AccountEstateBonuses(new Dictionary<string, int>(), 0, 0);

        public bool IsNone => ConstructionSpeedPermille == 0 && StorageSeconds == 0 && ProductionPermille.Values.All(x => x == 0);

        internal void Validate(AccountEstateConfig config)
        {
            if (ProductionPermille == null || ConstructionSpeedPermille < 0 || StorageSeconds < 0 ||
                ProductionPermille.Any(x => x.Value < 0 || !config.Storage.BaseStock.ContainsKey(x.Key)))
            {
                throw new ArgumentException("Invalid account estate bonuses.");
            }
        }
    }
}
