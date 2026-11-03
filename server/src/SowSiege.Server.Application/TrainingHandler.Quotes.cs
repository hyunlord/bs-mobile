using SowSiege.Core;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Application;

public sealed partial class TrainingHandler
{
    private string StartRejection(TrainingState state, MetaState account, string unitId, int count)
    {
        var level = Level(account);
        if (level.Level == 0) { return "barracks-required"; }
        var unit = content.Rules.Units.SingleOrDefault(u => u.UnitId == unitId);
        if (unit is null) { return "unknown-unit"; }
        if (unit.RequiredBarracksLevel > level.Level) { return "unit-locked"; }
        if (count <= 0) { return "invalid-count"; }
        if (count > level.BatchLimit) { return "batch-limit"; }
        if (state.Waiting is not null) { return "queue-full"; }
        if ((long)Occupied(state) + count > level.Capacity) { return "capacity"; }
        var cost = BatchCost(unit, count);
        if (cost.Any(c => account.Wallet[c.Key] < c.Value)) { return "insufficient-resources"; }
        var reserve = AccountEstate.ProtectedReserve(estate.Config, account);
        if (reserve.Any(r => account.Wallet[r.Key] - cost.GetValueOrDefault(r.Key) < r.Value))
        {
            return "protected-reserve";
        }
        return "";
    }

    private static Dictionary<string, int> BatchCost(TrainingUnit unit, int count) =>
        unit.CostPerUnit.ToDictionary(c => c.Key, c => checked(c.Value * count), StringComparer.Ordinal);

    private TrainingQuoteView[] Quotes(DomainContext context, TrainingState state, MetaState account, TrainingUnit unit)
    {
        var level = Level(account);
        return Enumerable.Range(1, level.BatchLimit).Select(count =>
        {
            string reason = StartRejection(state, account, unit.UnitId, count);
            long duration = Duration(checked((long)unit.SecondsPerUnit * count * 1000), 0, content.Rules.MaximumBonusPermille);
            long startsAt = Math.Max(context.UtcMilliseconds, state.Active?.CompletesAtMilliseconds ?? context.UtcMilliseconds);
            return new TrainingQuoteView(count, Materials(BatchCost(unit, count)), duration,
                reason.Length == 0 ? checked(startsAt + duration) : null, checked(Occupied(state) + count),
                level.Capacity, reason.Length == 0, reason);
        }).ToArray();
    }
}
