namespace SowSiege.Server.Application;

public sealed record MarchTroops(string UnitId, int Count);

public sealed partial class TrainingHandler
{
    // Trusted server mutation only. Public training Execute intentionally has no deploy/return operation.
    public DomainResult Deploy(DomainContext context, MarchTroops[] troops, int capacity) => Move(context, troops, capacity, false);
    public DomainResult ReturnUnharmed(DomainContext context, MarchTroops[] troops) => Move(context, troops, int.MaxValue, true);

    private DomainResult Move(DomainContext context, MarchTroops[] troops, int capacity, bool returning)
    {
        var state = Read(context);
        DomainResult Reject(string reason) => Result(context, state, context.Account.State, false, reason);
        if (troops is not { Length: > 0 } || troops.Any(t => t is null || t.Count <= 0 || string.IsNullOrWhiteSpace(t.UnitId)) ||
            troops.Select(t => t.UnitId).Distinct(StringComparer.Ordinal).Count() != troops.Length ||
            troops.Sum(t => (long)t.Count) > capacity) { return Reject("invalid-deployment"); }
        foreach (var selected in troops)
        {
            var unit = state.Units.SingleOrDefault(u => u.UnitId == selected.UnitId);
            if (unit is null) { return Reject("unknown-unit"); }
            if ((returning ? unit.Deployed : unit.Ready) < selected.Count) { return Reject(returning ? "insufficient-deployed" : "insufficient-ready"); }
        }
        var next = state with { Units = state.Units.Select(u =>
        {
            int count = troops.SingleOrDefault(t => t.UnitId == u.UnitId)?.Count ?? 0;
            return u with { Ready = checked(u.Ready + (returning ? count : -count)), Deployed = checked(u.Deployed + (returning ? -count : count)) };
        }).ToArray() };
        return Result(context, next, context.Account.State, true, returning ? "troops-returned" : "troops-deployed",
            [new(returning ? "troops-returned" : "troops-deployed", Encode(new { context.CommandId, troops }))]);
    }
}
