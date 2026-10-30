namespace SowSiege.Server.Application;

public sealed record TrainingTroops(string UnitId, int Ready, int Deployed, int Wounded, int Reserved);
public sealed record TrainingJob(Guid JobId, string UnitId, int Count, Dictionary<string, int> ChargedCost,
    long BaseDurationMilliseconds, long? StartedAtMilliseconds, long? CompletesAtMilliseconds, int? BonusPermille);
public sealed record TrainingState(TrainingTroops[] Units, TrainingJob? Active, TrainingJob? Waiting)
{
    public Dictionary<string, int> PendingRefunds { get; init; } = new(StringComparer.Ordinal);
}
