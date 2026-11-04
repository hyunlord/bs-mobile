using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;

internal sealed record MixedTrainingPlan(IDomainHandler Handler, string Barracks, int Level, string Unit,
    int ActiveCount, long DurationMilliseconds, IReadOnlyDictionary<string, int> Cost);

internal static class MixedTraining
{
    internal static void RequireAvailable()
    {
#if !TRAINING_LOAD_AVAILABLE
        Console.Error.WriteLine("MIXED_DEPENDENCY_MISSING: server-only training/help overlay required; database was not opened.");
        throw new InvalidOperationException("Mixed workload requires the explicit server-only training/help measurement overlay.");
#endif
    }

    internal static MixedTrainingPlan Create(string root, EstateContent estate, AllianceContent alliance)
    {
        RequireAvailable();
#if TRAINING_LOAD_AVAILABLE
        var content = TrainingContent.Load(root, estate);
        foreach (var level in content.Rules.Levels.OrderByDescending(l => l.Level))
        {
            foreach (var unit in content.Rules.Units.Where(u => u.RequiredBarracksLevel <= level.Level))
            {
                int count = Math.Min(level.BatchLimit, level.Capacity - 1);
                long duration = checked((long)count * unit.SecondsPerUnit * 1000);
                if (count > 0 && AllianceRules.HelpShortening(alliance.Help, duration, duration, 0) > 0)
                {
                    return new(new TrainingHandler(estate, content), content.Rules.BarracksKind, level.Level,
                        unit.UnitId, count, duration, unit.CostPerUnit);
                }
            }
        }
        throw new InvalidDataException("Content has no training batch that permits both a queued unit and positive help.");
#else
        throw new InvalidOperationException("Training overlay unavailable.");
#endif
    }

    internal static AllianceStore Alliances(NpgsqlDataSource source, AllianceContent content, TimeProvider clock, AccountStore accounts)
    {
        RequireAvailable();
#if TRAINING_LOAD_AVAILABLE
        return new(source, content, clock, accounts);
#else
        throw new InvalidOperationException("Help writer overlay unavailable.");
#endif
    }
}
