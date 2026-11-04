using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed partial class TrainingHandler
{
    // Called only by the world writer: attacker on return, defender on battle commit.
    public DomainResult ApplyCombatLosses(DomainContext context, IReadOnlyList<WorldTroopResult> troops,
        int injuryPercent, int bedCapacity, bool deployed)
    {
        var state = Read(context);
        DomainResult Reject(string reason) => Result(context, state, context.Account.State, false, reason);
        if (bedCapacity < 0 || injuryPercent is < 0 or > 100 ||
            troops.Any(t => t is null || t.N < 0 || t.K < 0 || t.K > t.N || string.IsNullOrWhiteSpace(t.Id)) ||
            troops.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != troops.Count)
        {
            return Reject("invalid-combat-losses");
        }

        foreach (var troop in troops)
        {
            var unit = state.Units.SingleOrDefault(u => u.UnitId == troop.Id);
            if (unit is null || (deployed ? unit.Deployed : unit.Ready) < troop.N)
            {
                return Reject("insufficient-combat-troops");
            }
        }
        int beds = (int)Math.Max(0L, bedCapacity - state.Units.Sum(u => (long)u.Wounded));
        var settlement = WorldCombatInput.SettleLosses(troops, injuryPercent, beds);
        var next = state with { Units = state.Units.Select(unit =>
        {
            var loss = settlement.SingleOrDefault(t => t.Id == unit.UnitId);
            return loss is null ? unit : unit with
            {
                Ready = checked(unit.Ready + (deployed ? loss.Survivors : -loss.K)),
                Deployed = checked(unit.Deployed - (deployed ? loss.N : 0)),
                Wounded = checked(unit.Wounded + loss.Admitted)
            };
        }).ToArray() };
        return Result(context, next, context.Account.State, true, "combat-losses-settled",
            [new("combat-losses-settled", Encode(new { context.CommandId, deployed, settlement }))]);
    }
}
