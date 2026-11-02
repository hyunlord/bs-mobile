using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed partial class WorldCombatStore
{
    // A received candidate reserves its immutable input frontier while bounded verification runs outside locks.
    // Invalid, duplicate and crashed requests never change LastReceivedAtMs. The lease survives restart.
    private Task<(WorldCombatRun Run,long Now)> ReserveVerification(WorldMarchStore marches,Guid account,Guid marchId,CancellationToken ct)=>
        marches.ExecuteAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
            var run=await Find(c,t,marchId,token)??throw new KeyNotFoundException();
            if(run.AccountId!=account)throw new KeyNotFoundException();
            if(run.Status is "committed" or "returned")return(run,now);
            if(run.Status!="active" || run.Contract?.Direct is null || run.VerificationId is not null)throw new CommandConflictException();
            if(DueTick(run,now)>run.Frontier.ConfirmedTick)throw new CommandConflictException("input-frontier-expired");
            long until=checked(now+executor.VerificationTimeoutMilliseconds+1);
            run=run with {VerificationId=Guid.NewGuid(),VerificationUntilMs=until,BeforeVerificationDueAtMs=run.DueAtMs,
                DueAtMs=until,Revision=checked(run.Revision+1)};
            await Save(c,t,run,token);
            await marches.RescheduleAsync(c,t,march,"battle-active",until,"verification-pending",now,token);
            return(run,now);
        },ct);

    private static WorldCombatRun ClearVerification(WorldCombatRun run)=>run with
    {VerificationId=null,VerificationUntilMs=null,DueAtMs=run.BeforeVerificationDueAtMs??run.DueAtMs,BeforeVerificationDueAtMs=null};

    private Task<bool> ReleaseVerification(WorldMarchStore marches,WorldCombatRun reserved,CancellationToken ct)=>
        marches.ExecuteLockedAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,reserved.AccountId,reserved.MarchId,token);
            if(march is null)return false;
            var current=await Find(c,t,reserved.MarchId,token);
            if(current is null || current.VerificationId!=reserved.VerificationId || current.Revision!=reserved.Revision)return false;
            current=ClearVerification(current) with {Revision=checked(current.Revision+1)};
            await Save(c,t,current,token);
            // Restore the original event time, even after its deadline; the global scheduler orders its recovery.
            await marches.RescheduleAsync(c,t,march,"battle-active",current.DueAtMs,"verification-rejected",Math.Min(now,current.DueAtMs-1),token);
            return true;
        },ct);
}
