using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed partial class WorldCombatStore
{
    public async Task<WorldCombatRun> ChooseAsync(WorldMarchStore marches,Guid account,Guid marchId,string choice,CancellationToken ct)
    {
        if (choice is not ("direct" or "auto" or "return")) { throw new ArgumentException("Unknown battle choice."); }
        var selected=await marches.ExecuteAsync(async (c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException("March not found.");
            var run=await Find(c,t,marchId,token)??throw new KeyNotFoundException("Battle not found.");
            if (run.AccountId!=account) { throw new KeyNotFoundException("Battle not found."); }
            if(run.Status!="choice")
            {
                if (choice=="return" && run.Status=="recalled" || choice=="direct" && run.Contract?.Mode==WorldCombatMode.DirectPve || choice=="auto" && run.Contract?.Mode==WorldCombatMode.AutoPve) { return run; }
                throw new CommandConflictException("combat-conflict");
            }
            if(now>=run.DueAtMs) { throw new CommandConflictException("combat-conflict"); }
            if(choice=="return")
            {
                run=run with {Status="recalled",Revision=checked(run.Revision+1),DueAtMs=checked(now+march.TravelMilliseconds)};
                await Release(c,t,run,token);
            }
            else
            {
                var issued=factory.Issue(march,run.EncounterId,run.RunId,now,choice=="direct"?WorldCombatMode.DirectPve:WorldCombatMode.AutoPve);
                run=run with {Status="active",Revision=checked(run.Revision+1),Contract=issued,Frontier=new(now,now,0,0),
                    DueAtMs=choice=="direct"?checked(now+issued.Direct!.Input.InputGraceMilliseconds+1):checked(now+1)};
            }
            await Save(c,t,run,token);
            var next=Next(run,march,now);
            await marches.RescheduleAsync(c,t,march,next.State,next.DueAtMs,next.Reason,now,token);
            return run;
        },ct);
        if(choice=="auto" && selected.Status=="active")
        {
            await RecoverAsync(marches,selected.RunId,ct);
            return await GetAsync(account,marchId,ct)??throw new InvalidDataException("Battle disappeared.");
        }
        return selected;
    }

    public async Task<WorldCombatRun> PrefixAsync(WorldMarchStore marches,Guid account,Guid marchId,ReplayCommand[] completePrefix,CancellationToken ct)
    {
        var captured=await ReserveVerification(marches,account,marchId,ct);
        var run=captured.Run;
        if(run.Status!="active" || run.Contract?.Direct is null) { throw new CommandConflictException("combat-conflict"); }
        WorldPrefixExecution verified;
        try { verified=await executor.ValidatePrefixAsync(executor.ExpectedOptions(run.Contract.Direct),run.Commands,completePrefix,run.Contract.IssuedAtMs,captured.Now,ct); }
        catch { await ReleaseVerification(marches,run,CancellationToken.None); throw; }
        return await marches.ExecuteLockedAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
            var current=await Find(c,t,marchId,token)??throw new KeyNotFoundException();
            Match(run,current);
            // Admission uses the receive highwater. A concurrent recovery wins through the revision comparison.
            if(DueTick(current,captured.Now)>current.Frontier.ConfirmedTick) { throw new CommandConflictException("combat-conflict"); }
            if(verified.NextSequence==current.Frontier.NextSequence)
            {
                current=ClearVerification(current);
                await Save(c,t,current,token);
                await marches.RescheduleAsync(c,t,march,"battle-active",current.DueAtMs,"duplicate-prefix",captured.Now,token);
                return current;
            }
            current=ClearVerification(current);
            current=current with {Revision=checked(current.Revision+1),Commands=verified.Commands,
                Frontier=WorldCombatInput.Accepted(current.Frontier,captured.Now,verified.Tick,verified.NextSequence)};
            if(verified.Result is not null) { return await Commit(c,t,marches,march,current,verified.Result,captured.Now,token); }
            current=current with {DueAtMs=NextFrontier(current)};
            await Save(c,t,current,token);
            await marches.RescheduleAsync(c,t,march,"battle-active",current.DueAtMs,"prefix-accepted",captured.Now,token);
            return current;
        },ct);
    }

    public async Task<WorldCombatRun> SubmitAsync(WorldMarchStore marches,Guid account,Guid marchId,byte[] replay,CancellationToken ct)
    {
        var captured=await ReserveVerification(marches,account,marchId,ct);
        var run=captured.Run;
        if(run.Status is "committed" or "returned") { return run; }
        if(run.Status!="active" || run.Contract?.Direct is null) { throw new CommandConflictException("combat-conflict"); }
        WorldCombatResult result;
        try { result=await executor.VerifyAsync(executor.ExpectedOptions(run.Contract.Direct),run.Commands,replay,run.Contract.IssuedAtMs,captured.Now,ct); }
        catch { await ReleaseVerification(marches,run,CancellationToken.None); throw; }
        return await marches.ExecuteLockedAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
            var current=await Find(c,t,marchId,token)??throw new KeyNotFoundException();
            Match(run,current);
            if(DueTick(current,captured.Now)>current.Frontier.ConfirmedTick) { throw new CommandConflictException("combat-conflict"); }
            return await Commit(c,t,marches,march,ClearVerification(current),result,captured.Now,token);
        },ct);
    }

    public async Task<bool> RecoverAsync(WorldMarchStore marches,Guid runId,CancellationToken ct)
    {
        Guid account;Guid marchId;
        await using(var c=await source.OpenConnectionAsync(ct))
        await using(var q=new NpgsqlCommand("SELECT account_id,march_id FROM world_combat_runs WHERE run_id=$1",c))
        {
            q.Parameters.AddWithValue(runId);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct)) { return false; }
            account=r.GetGuid(0);marchId=r.GetGuid(1);
        }
        var captured=await marches.ExecuteLockedAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
            return await Capture(c,t,marches,account,marchId,Math.Min(now,march.DueAtMs),token);
        },ct);
        var run=captured.Run;
        if(run.Status is not ("active" or "recovery-pending")) { return false; }
        if(run.VerificationUntilMs is { } reservedUntil && captured.Now<reservedUntil) { return false; }
        WorldPrefixExecution? prefix=null;WorldCombatResult? result;ArmyAutoResult? autoResult=null;
        try
        {
            if(run.Contract?.Auto is { } automatic)
            {
                autoResult=await executor.ResolveAutoAsync(automatic,ct);result=autoResult.Combat;
            }
            else if(run.Contract?.Direct is { } direct)
            {
                prefix=await executor.RecoverAsync(executor.ExpectedOptions(direct),run.Commands,DueTick(run,captured.Now),ct);
                result=prefix.Result;
            }
            else { throw new InvalidDataException("Missing frozen battle input."); }
        }
        catch(Exception failure) when(failure is InvalidDataException or ArgumentException or OverflowException or TimeoutException or InvalidOperationException)
        {
            return await marches.ExecuteLockedAsync(async(c,t,now,token)=>
            {
                var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
                var current=await Find(c,t,marchId,token)??throw new KeyNotFoundException();
                if(current.Revision!=run.Revision) { return false; }
                current=current with {Status="recovery-pending",Revision=checked(current.Revision+1),DueAtMs=checked(now+content.Rules.PrefixIntervalMilliseconds)};
                await Save(c,t,current,token);
                await marches.RescheduleAsync(c,t,march,"battle-active",current.DueAtMs,"recovery-pending",now,token);
                return true;
            },ct);
        }
        return await marches.ExecuteLockedAsync(async(c,t,now,token)=>
        {
            var march=await marches.FindLockedAsync(c,t,account,marchId,token)??throw new KeyNotFoundException();
            var current=await Find(c,t,marchId,token)??throw new KeyNotFoundException();
            if(current.Revision!=run.Revision) { return false; }
            current=ClearVerification(current) with {Status="active",Revision=checked(current.Revision+1)};
            if(prefix is not null) { current=current with {Commands=prefix.Commands,Frontier=WorldCombatInput.Observe(current.Frontier,captured.Now) with {ConfirmedTick=prefix.Tick,NextSequence=prefix.NextSequence}}; }
            if(result is not null)
            {
                if(result.Mode==WorldCombatMode.CityPvp)
                {
                    var city=await CommitCity(c,t,march,current,autoResult!,captured.Now,token);
                    current=city.Run;
                    if(!city.Committed)
                    {
                        await Save(c,t,current,token);
                        var next=Next(current,march,captured.Now);
                        await marches.RescheduleAsync(c,t,march,next.State,next.DueAtMs,"defense-refreshed",captured.Now,token);
                        return true;
                    }
                }
                await Commit(c,t,marches,march,current,result,captured.Now,token);
            }
            else
            {
                current=current with {DueAtMs=Math.Min(Math.Max(checked(captured.Now+content.Rules.PrefixIntervalMilliseconds),NextFrontier(current)), checked(AbsoluteDeadline(current)+1))};
                await Save(c,t,current,token);
                await marches.RescheduleAsync(c,t,march,"battle-active",current.DueAtMs,"frontier-confirmed",captured.Now,token);
            }
            return true;
        },ct);
    }

    private Task<(WorldCombatRun Run,long Now)> CaptureAsync(WorldMarchStore marches,Guid account,Guid marchId,CancellationToken ct)=>
        marches.ExecuteAsync((c,t,now,token)=>Capture(c,t,marches,account,marchId,now,token),ct);
    private Task<(WorldCombatRun Run,long Now)> CaptureLockedAsync(WorldMarchStore marches,Guid account,Guid marchId,CancellationToken ct)=>
        marches.ExecuteLockedAsync((c,t,now,token)=>Capture(c,t,marches,account,marchId,now,token),ct);
    private static async Task<(WorldCombatRun Run,long Now)> Capture(NpgsqlConnection c,NpgsqlTransaction t,WorldMarchStore marches,Guid account,Guid marchId,long now,CancellationToken ct)
    {
        _=await marches.FindLockedAsync(c,t,account,marchId,ct)??throw new KeyNotFoundException();
        var run=await Find(c,t,marchId,ct)??throw new KeyNotFoundException();
        if(run.AccountId!=account) { throw new KeyNotFoundException(); }
        return(run,now);
    }
    private static int DueTick(WorldCombatRun run,long now)
    {
        var direct=run.Contract!.Direct!;
        return WorldCombatInput.DueTick(run.Frontier,run.Contract.IssuedAtMs,now,direct.Provenance.TickRate,
            direct.Objectives.Single(x=>x.Kind==WorldObjectiveKind.TimeLimit).LimitTicks,direct.Input.InputGraceMilliseconds);
    }
    private static long AbsoluteDeadline(WorldCombatRun run)
    {
        var direct = run.Contract!.Direct!;
        return WorldCombatInput.AbsoluteDeadline(run.Contract.IssuedAtMs, direct.Provenance.TickRate,
            direct.Objectives.Single(x => x.Kind == WorldObjectiveKind.TimeLimit).LimitTicks, direct.Input.InputGraceMilliseconds);
    }
    private static long NextFrontier(WorldCombatRun run) => checked(Math.Min(
        WorldCombatInput.GraceDeadline(run.Frontier, run.Contract!.Direct!.Input.InputGraceMilliseconds), AbsoluteDeadline(run)) + 1);
    private static void Match(WorldCombatRun expected,WorldCombatRun current)
    {
        if(expected.RunId!=current.RunId || expected.Revision!=current.Revision || current.Status!="active" || expected.Contract?.ContractHash!=current.Contract?.ContractHash) { throw new CommandConflictException("combat-conflict"); }
    }

    private async Task<WorldCombatRun> Commit(NpgsqlConnection c,NpgsqlTransaction t,WorldMarchStore marches,WorldMarch march,WorldCombatRun run,WorldCombatResult result,long at,CancellationToken ct)
    {
        if(run.Contract is null || result.ContractHash!=run.Contract.ContractHash || result.Mode!=run.Contract.Mode || run.Result is not null)
            { throw new InvalidDataException("Battle result does not match its uncommitted contract."); }
        await using var target=new NpgsqlCommand("SELECT 1 FROM world_targets WHERE world_id=$1 AND target_id=$2 AND generation=$3",c,t);
        target.Parameters.AddWithValue(map.WorldId);target.Parameters.AddWithValue(run.TargetId);target.Parameters.AddWithValue(run.Generation);
        if(await target.ExecuteScalarAsync(ct) is null) { throw new InvalidDataException("Battle target generation changed while leased."); }
        bool won=result.Terminal is WorldTerminalReason.ObjectivesCompleted or WorldTerminalReason.AutoVictory;
        var credits=result.Mode==WorldCombatMode.CityPvp?run.Credits:won?Cargo(run.Contract.Award.Credits,result.Troops):[];
        if(won && result.Mode!=WorldCombatMode.CityPvp)
        {
            if(run.Contract.Award.ZoneEligibilityId is { } zone)
                { await Run(c,t,"INSERT INTO world_combat_eligibility VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING",ct,run.AccountId,zone,run.RunId,at); }
            if(run.Contract.Award.ProofIds.Length>0)
            {
                if(run.Contract.Award.ProofIds.Any(p=>!content.Research.Fallback.ProofIds.Contains(p))) { throw new InvalidDataException("Unknown world research proof."); }
                await accounts.ExecuteWorldAsync(c,t,run.AccountId,run.RunId,at,(context,_)=>
                {
                    var state=JsonSerializer.Deserialize<ResearchState>(context.States["research"],AccountStore.Json)??throw new InvalidDataException("Missing research state.");
                    state=state with {Proofs=state.Proofs.Concat(run.Contract.Award.ProofIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()};
                    return Task.FromResult(new WorldAccountChanges("world-combat-proof",
                        [new("research",JsonSerializer.SerializeToElement(state,AccountStore.Json),
                            [new("world-combat-proof",JsonSerializer.SerializeToElement(new {run.RunId,run.Contract.Award.ProofIds},AccountStore.Json))])],[]));
                },ct);
            }
        }

        run=run with {Status="committed",Result=result,Credits=credits,Revision=checked(run.Revision+1),DueAtMs=checked(at+march.TravelMilliseconds)};
        await Save(c,t,run,ct);
        if(won && result.Mode!=WorldCombatMode.CityPvp) { await Run(c,t,"UPDATE world_combat_targets SET respawn_at=$4 WHERE world_id=$1 AND target_id=$2 AND run_id=$3",ct,map.WorldId,run.TargetId,run.RunId,checked(at+run.Contract.Award.RespawnMilliseconds)); }
        else if(result.Mode!=WorldCombatMode.CityPvp)
        {
            await Release(c,t,run,ct);
        }

        await marches.RescheduleAsync(c,t,march,"returning",run.DueAtMs,result.Terminal.ToString(),at,ct);
        return run;
    }
}
