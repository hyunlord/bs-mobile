using System.Text.Json.Serialization;

namespace SowSiege.Server.Contracts;

public sealed record TrainingStart([property: JsonRequired] string UnitId, [property: JsonRequired] int Count);
public sealed record TrainingCancel([property: JsonRequired] Guid JobId);
public sealed record TrainingMaterialView(string MaterialId, int Amount);
public sealed record TrainingQuoteView(int Count, TrainingMaterialView[] TotalCost, long DurationMilliseconds,
    long? EstimatedCompletesAtMilliseconds, int OccupiedAfter, int Capacity, bool CanStart, string Reason);
public sealed record TrainingUnitView(string UnitId, string Name, int RequiredBarracksLevel, bool Unlocked,
    int Ready, int Deployed, int Wounded, int Reserved, TrainingMaterialView[] CostPerUnit, int SecondsPerUnit, TrainingQuoteView[] Quotes);
public sealed record TrainingJobView(Guid JobId, string UnitId, int Count, long? StartedAtMilliseconds,
    long? CompletesAtMilliseconds, long DurationMilliseconds, int? BonusPermille, TrainingMaterialView[] ChargedCost);
public sealed record TrainingRefundPreview(string MaterialId, int ImmediateAmount, int PendingAmount);
public sealed record TrainingView(int BarracksLevel, int Capacity, int BatchLimit, int Occupied, long ServerUtcMilliseconds,
    TrainingUnitView[] Units, TrainingJobView? Active, TrainingJobView? Waiting, TrainingRefundPreview[] WaitingCancellationRefunds);
