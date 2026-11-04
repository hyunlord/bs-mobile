using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed record WorldCombatReturn(WorldTroopResult[] Troops, int InjuryPercent, DomainCredit[] Credits);

// Only the authoritative combat store can supply this result; HTTP has no casualty/reward setter.
public interface IWorldCombatReturnActivity
{
    Task<WorldCombatReturn?> PrepareReturnAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        WorldMarch march, long at, CancellationToken cancellation);
}
