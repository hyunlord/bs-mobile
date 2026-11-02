using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed class WorldCombatActivity(WorldCombatStore combat, CitySecurityStore security) : IWorldMarchActivity, IWorldCombatReturnActivity
{
    public bool Supports(string operation) => operation is "pve" or "attack" or "scout";
    public async Task DispatchAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        if(march.Operation=="pve")return;
        var decision=await security.DispatchAsync(c,t,march.MarchId,march.AccountId,march.TargetAccountId??throw new CommandConflictException("city-target-required"),march.Operation,at,ct);
        if(!decision.Allowed)throw new CommandConflictException(decision.Reason);
    }
    public async Task<MarchActivityResult> ArriveAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        if(march.Operation=="pve")return await combat.ArriveAsync(c,t,march,at,ct);
        var decision=await security.ArriveAsync(c,t,march.MarchId,march.AccountId,march.TargetAccountId??throw new InvalidDataException("Missing city target."),march.Operation,at,ct);
        if(!decision.Allowed || march.Operation=="scout")return new("returning",checked(at+march.TravelMilliseconds),decision.Reason);
        return await combat.ArriveCityAsync(c,t,march,at,ct);
    }
    public Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,bool cancel,CancellationToken ct)=>combat.AdvanceAsync(c,t,march,at,cancel,ct);
    public async Task<DomainCredit[]> ReturnAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        if(march.Operation is "attack" or "scout")await security.ReturnAsync(c,t,march.MarchId,march.AccountId,at,ct);
        return [];
    }
    public async Task<WorldCombatReturn?> PrepareReturnAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        var result=await combat.PrepareReturnAsync(c,t,march,at,ct);
        if(result is not null && march.Operation=="attack")await security.ReturnAsync(c,t,march.MarchId,march.AccountId,at,ct);
        return result;
    }
}
