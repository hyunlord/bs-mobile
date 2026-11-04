using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatStoreTests
{
    private static readonly CancellationToken None=CancellationToken.None;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private sealed class Clock : TimeProvider
    {
        public long Now {get;set;}=1_793_520_000_000;
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
    private sealed record Setup(WorldTestDatabase Db,WorldMapContent Map,WorldCombatContent Content,WorldCombatStore Store,
        AccountStore Accounts,Guid Account,WorldCombatContractFactory Factory,WorldCombatExecutor Executor,TrainingHandler Training,
        TrainingContent TrainingContent,CitySecurityPolicy Security,Clock Clock,WorldStore World,WorldRecoveryGate Gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
        public WorldCombatStore Restart()=>new(Db.Source,Map,Content,Factory,Executor,Accounts,Training,TrainingContent,Security);
    }
    private static async Task<Setup> CreateAsync()
    {
        var db=await WorldTestDatabase.CreateAsync(maximumPoolSize:2,applicationName:"world-combat-tests");
        try
        {
            var map=WorldMapContent.Parse(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(WorldStoreTests.MapBytes())
                .Replace("\"resource\"","\"monster\"",StringComparison.Ordinal)
                .Replace("meta:grain","slg:encounter_field_mites",StringComparison.Ordinal)));
            string root=ContentEstateTests.Root();
            var estate=EstateContent.Load(root);var world=new WorldStore(db.Source,map);var clock=new Clock();
            var trainingContent=TrainingContent.Load(root,estate);var training=new TrainingHandler(estate,trainingContent);
            var research=ResearchContent.Load(root);
            var registry=new DomainRegistry([training,new RecruitDomain(RecruitPool.Load(root),0),
                new ResearchDomain(research,new ExpeditionRules(3_600_000,3,estate.Config.WalletCaps))],new byte[32]);
            var gate=new WorldRecoveryGate(db.Source,clock);
            var accounts=new AccountStore(db.Source,new EstateApplication(estate),clock,registry,world,recoveryGate:gate);
            await accounts.InitializeAsync(None);
            await new AllianceStore(db.Source,AllianceContent.Load(root),TimeProvider.System).InitializeAsync(None);
            await new AuthStore(db.Source,accounts,new AuthOptions(TimeSpan.FromMinutes(15),TimeSpan.FromDays(30),TimeSpan.FromMinutes(1),AuthTests.ShippedLimits(),true),TimeProvider.System).InitializeAsync(None);
            await world.InitializeAsync(None);
            await using(var q=db.Source.CreateCommand("CREATE SEQUENCE world_gathering_sequence"))
            {
                await q.ExecuteNonQueryAsync();
            }

            var created=await accounts.CreateAsync(None);var account=(await accounts.AuthenticateAsync(created.Token,None))!.Value;
            var content=WorldCombatContent.Load(root);
            var executor=new WorldCombatExecutor(new(Path.Combine(root,"data"),"wave-1a",4*1024*1024,TimeSpan.FromSeconds(30),1));
            var factory=new WorldCombatContractFactory(content,executor.Catalog,executor.DataHash,"wave-1a");
            var security=CitySecurityPolicy.Load(root);
            var store=new WorldCombatStore(db.Source,map,content,factory,executor,accounts,training,trainingContent,security);
            await store.InitializeAsync(None);
            return new(db,map,content,store,accounts,account,factory,executor,training,trainingContent,security,clock,world,gate);
        }
        catch{await db.DisposeAsync();throw;}
    }
    private static WorldMarch March(Setup s)=>new(Guid.NewGuid(),s.Account,null,"slg:test_grain",1,"pve","core:frontier_knight",
        [new("slg:levy",5)],[],0,1000,1000,1,"outbound","",JsonSerializer.SerializeToElement(new
        {combatAccount=new {activeVassalIds=Array.Empty<string>()},researchHash=s.Content.Research.Hash,domains=new {research=new {levels=new Dictionary<string,int>()}}}));
    private static async Task<T> Locked<T>(Setup s,Func<NpgsqlConnection,NpgsqlTransaction,Task<T>> action)
    {
        await using var c=await s.Db.Source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await using(var q=new NpgsqlCommand("SELECT 1 FROM world_instances WHERE world_id=$1 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(s.Map.WorldId);await q.ExecuteScalarAsync();}
        await using(var q=new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE",c,t))
        {q.Parameters.AddWithValue(s.Account);await q.ExecuteScalarAsync();}
        var value=await action(c,t);await t.CommitAsync();return value;
    }

    [Fact,Trait("Category","Postgres")]
    public async Task ConcurrentArrivalsHaveOneDurableLeaseWithSmallConnectionPool()
    {
        await using var s=await CreateAsync();var marches=Enumerable.Range(0,12).Select(_=>March(s)).ToArray();
        var result=await Task.WhenAll(marches.Select(m=>Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,m,1000,None)))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(result,x=>x.State=="battle-choice");Assert.Equal(11,result.Count(x=>x.Reason=="target-busy"));
        int winner=Array.FindIndex(result,x=>x.State=="battle-choice");
        var persisted=await s.Store.GetAsync(s.Account,marches[winner].MarchId,None);Assert.NotNull(persisted);
        var duplicate=await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,marches[winner],2000,None));
        Assert.Equal(result[winner],duplicate);
        Assert.Null(await s.Store.GetAsync(Guid.NewGuid(),marches[winner].MarchId,None));
    }

    [Fact,Trait("Category","Postgres")]
    public async Task RestartPreservesChoiceDeadlineAndExpiryReleasesTargetWithoutLosses()
    {
        await using var s=await CreateAsync();var march=March(s);
        var arrival=await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,march,1000,None));
        var restarted=s.Restart();await restarted.InitializeAsync(None);
        var stored=(await restarted.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Equal(1000+s.Content.Rules.ChoiceTimeoutMilliseconds,stored.DueAtMs);
        var expired=await Locked(s,(c,t)=>restarted.AdvanceAsync(c,t,march,arrival.DueAtMs,false,None));
        Assert.Equal("returning",expired.State);Assert.Equal(arrival.DueAtMs+march.TravelMilliseconds,expired.DueAtMs);
        Assert.Null(await Locked(s,(c,t)=>restarted.PrepareReturnAsync(c,t,march,expired.DueAtMs,None)));
        Assert.Equal("battle-choice",(await Locked(s,(c,t)=>restarted.ArriveAsync(c,t,March(s),expired.DueAtMs,None))).State);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task RollbackDoesNotConsumeLeaseOrLeaveOpenTransaction()
    {
        await using var s=await CreateAsync();var march=March(s);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Locked<bool>(s,async(c,t)=>
        {
            await s.Store.ArriveAsync(c,t,march,1000,None);throw new InvalidOperationException("Injected before commit");
        }));
        Assert.Null(await s.Store.GetAsync(s.Account,march.MarchId,None));
        Assert.Equal("battle-choice",(await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,march,1000,None))).State);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task DurablePrefixRestoresSignedMovementAndDoesNotRenewGrace()
    {
        await using var s=await CreateAsync();var march=March(s);
        await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,march,1000,None));
        var original=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        var contract=s.Factory.Issue(march,"slg:encounter_field_mites",original.RunId,1000,WorldCombatMode.DirectPve);
        var run=original with {Contract=contract,Status="active",Commands=[new(0,0,ReplayCommandKind.Advance,new(-1000,500))],Frontier=new(1200,1100,1,1),Revision=2};
        await Locked(s,async(c,t)=>
        {
            await using var q=new NpgsqlCommand("UPDATE world_combat_runs SET payload=$2::jsonb,revision=$3 WHERE run_id=$1",c,t);
            q.Parameters.AddWithValue(run.RunId);q.Parameters.AddWithValue(JsonSerializer.Serialize(run,Json));q.Parameters.AddWithValue(run.Revision);
            return await q.ExecuteNonQueryAsync();
        });
        var restored=(await s.Restart().GetAsync(s.Account,march.MarchId,None))!;
        Assert.Equal((short)-1000,Assert.Single(restored.Commands).Input.X);Assert.Equal((short)500,restored.Commands[0].Input.Y);
        Assert.Equal(run.Frontier,restored.Frontier);Assert.Equal(run.Contract.ContractHash,restored.Contract!.ContractHash);
        await Assert.ThrowsAsync<CommandConflictException>(()=>Locked(s,(c,t)=>s.Store.AdvanceAsync(c,t,march,1300,true,None)));
        var required=await Assert.ThrowsAsync<WorldCombatRecoveryRequiredException>(()=>Locked(s,(c,t)=>s.Store.AdvanceAsync(c,t,march,1300,false,None)));
        Assert.Equal(run.RunId,required.RunId);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task RespawnBoundaryAndDuplicateRecoveryIncrementGenerationOnce()
    {
        await using var s=await CreateAsync();var march=March(s);
        await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,march,1000,None));
        await Locked(s,async(c,t)=>
        {
            await using var q=new NpgsqlCommand("UPDATE world_combat_targets SET respawn_at=2000",c,t);
            return await q.ExecuteNonQueryAsync();
        });
        Assert.Null(await Locked(s,(c,t)=>s.Store.NextEventAsync(c,t,1999,None)));
        var due=(await Locked(s,(c,t)=>s.Store.NextEventAsync(c,t,2000,None)))!;
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Locked(s,async(c,t)=>{await s.Store.RecoverTargetAsync(c,t,due,None);return true;}))).WaitAsync(TimeSpan.FromSeconds(20));
        await using var q=s.Db.Source.CreateCommand("SELECT generation FROM world_targets WHERE target_id='slg:test_grain'");
        Assert.Equal(2L,await q.ExecuteScalarAsync());Assert.Null(await Locked(s,(c,t)=>s.Store.NextEventAsync(c,t,long.MaxValue,None)));
    }
    private static async Task PrepareArmy(Setup s,Guid account,int ready,bool rich=false)
    {
        await using(var read=s.Db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1"))
        {
            read.Parameters.AddWithValue(account);
            var state=MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
            state=state with {ActiveVassalIds=[],Estate=state.Estate! with {Buildings=state.Estate!.Buildings.Select(b=>b.Kind==s.TrainingContent.Rules.BarracksKind?b with {Level=2}:b).ToArray()},
                Wallet=rich?EstateContent.Load(ContentEstateTests.Root()).Config.WalletCaps.ToDictionary(p=>p.Key,p=>p.Value):state.Wallet};
            await using var write=s.Db.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
            write.Parameters.AddWithValue(account);write.Parameters.AddWithValue(MetaSaveCodec.Encode(state));await write.ExecuteNonQueryAsync();
        }
        await s.Accounts.ExecuteAccountAsync(account,null,None);
        var troops=s.Training.InitialState.Deserialize<TrainingState>(AccountStore.Json)!;
        troops=troops with {Units=troops.Units.Select(u=>u with {Ready=u.UnitId=="slg:levy"?ready:0}).ToArray()};
        await using var q=s.Db.Source.CreateCommand("UPDATE account_domains SET state_json=$2::jsonb WHERE account_id=$1 AND domain='training'");
        q.Parameters.AddWithValue(account);q.Parameters.AddWithValue(JsonSerializer.Serialize(troops,Json));await q.ExecuteNonQueryAsync();
    }
    private static async Task<TrainingState> Army(Setup s,Guid id)
    {
        await using var q=s.Db.Source.CreateCommand("SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='training'");
        q.Parameters.AddWithValue(id);return JsonSerializer.Deserialize<TrainingState>((string)(await q.ExecuteScalarAsync())!,Json)!;
    }
    private static async Task<WorldMarchStore> RealMarches(Setup s)
    {
        var security=new CitySecurityStore(s.Db.Source,s.Security,s.Clock,s.Gate);await security.InitializeAsync(None);
        var marches=new WorldMarchStore(s.Db.Source,s.World,s.Map,s.Accounts,WorldMarchContent.Load(ContentEstateTests.Root()),s.Training,
            s.TrainingContent,s.Content.Research,s.Clock,[new WorldCombatActivity(s.Store,security)],s.Gate,combat:s.Store,combatContent:s.Content);
        await marches.InitializeAsync(None);return marches;
    }
    private static async Task<WorldMarch> Depart(Setup s,WorldMarchStore marches,string operation,string target,int count=5)
    {
        var view=SlgFixture.Parse(await s.Accounts.ExecuteAccountAsync(s.Account,null,None));
        var command=new WorldMarchCommand(Guid.NewGuid(),view.Estate.Version,operation,target,1,"core:frontier_knight",[new("slg:levy",count)]);
        await marches.ExecuteAsync(s.Account,command,None);
        return (await marches.GetAsync(s.Account,None)).Single(m=>m.MarchId==command.CommandId);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task RealAutomaticPveCommitsCoreResultBeforeReturningArmyAndCreditsOnce()
    {
        await using var s=await CreateAsync();await PrepareArmy(s,s.Account,10,true);var marches=await RealMarches(s);
        var march=await Depart(s,marches,"pve","slg:test_grain");s.Clock.Now=march.DueAtMs;
        await marches.RecoverAsync(None);
        var run=await s.Store.ChooseAsync(marches,s.Account,march.MarchId,"auto",None);
        Assert.Equal("committed",run.Status);Assert.NotNull(run.Result);
        var expected=await s.Executor.ResolveAutoAsync(run.Contract!.Auto!,None);
        Assert.Equal(expected.Combat.StateHash,run.Result!.StateHash);
        Assert.Equal(5,(await Army(s,s.Account)).Units.Single(u=>u.UnitId=="slg:levy").Deployed);
        s.Clock.Now=run.DueAtMs;
        await Task.WhenAll(marches.RecoverAsync(None),marches.RecoverAsync(None)).WaitAsync(TimeSpan.FromSeconds(30));
        var returned=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Equal("returned",returned.Status);
        var unit=(await Army(s,s.Account)).Units.Single(u=>u.UnitId=="slg:levy");
        Assert.Equal(0,unit.Deployed);Assert.Equal(10-expected.Combat.Troops.Single(t=>t.Id=="slg:levy").K,unit.Ready);
        await using var credits=s.Db.Source.CreateCommand("SELECT coalesce(sum(amount),0) FROM account_held_materials WHERE account_id=$1");
        credits.Parameters.AddWithValue(s.Account);
        Assert.Equal((long)returned.Credits.Sum(x=>x.Amount),Convert.ToInt64(await credits.ExecuteScalarAsync()));
        Assert.Equal(0,await marches.RecoverAsync(None));
    }

    [Theory,InlineData(0),InlineData(2),Trait("Category","Postgres")]
    public async Task RealCityCombatDebitsLootAndSettlesDefenderOnceWhileAttackerTravels(int defenders)
    {
        await using var s=await CreateAsync();await PrepareArmy(s,s.Account,10);
        var created=await s.Accounts.CreateAsync(None);var defender=(await s.Accounts.AuthenticateAsync(created.Token,None))!.Value;
        await PrepareArmy(s,defender,defenders,true);var marches=await RealMarches(s);
        s.Clock.Now+=s.Security.NewShieldMilliseconds+1;
        var march=await Depart(s,marches,"attack","city:"+defender.ToString("D"));s.Clock.Now=march.DueAtMs;
        await marches.RecoverAsync(None);
        var pending=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Equal("active",pending.Status);s.Clock.Now=pending.DueAtMs;
        await marches.RecoverAsync(None);
        var battle=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;Assert.Equal("committed",battle.Status);
        var expected=await s.Executor.ResolveAutoAsync(battle.Contract!.Auto!,None);
        Assert.Equal(expected.Combat.StateHash,battle.Result!.StateHash);
        Assert.Equal(defenders-expected.DefenderTroops.Sum(t=>t.K),(await Army(s,defender)).Units.Single(u=>u.UnitId=="slg:levy").Ready);
        Assert.Equal(0,(await Army(s,defender)).Units.Sum(u=>u.Deployed));
        Assert.Equal(5,(await Army(s,s.Account)).Units.Single(u=>u.UnitId=="slg:levy").Deployed);
        await using(var q=s.Db.Source.CreateCommand("SELECT -coalesce(sum(delta),0) FROM resource_events WHERE account_id=$1 AND delta<0"))
        {q.Parameters.AddWithValue(defender);Assert.Equal((long)battle.Credits.Sum(x=>x.Amount),Convert.ToInt64(await q.ExecuteScalarAsync()));}
        s.Clock.Now=battle.DueAtMs;await Task.WhenAll(marches.RecoverAsync(None),marches.RecoverAsync(None)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("returned",(await s.Store.GetAsync(s.Account,march.MarchId,None))!.Status);
        Assert.Equal(0,(await Army(s,s.Account)).Units.Sum(u=>u.Deployed));
        Assert.Equal(0,await marches.RecoverAsync(None));
    }

    [Fact,Trait("Category","Postgres")]
    public async Task ChangedDefenderVersionRefreshesFrozenContractBeforeAnyDebitOrCasualty()
    {
        await using var s=await CreateAsync();await PrepareArmy(s,s.Account,10);
        var created=await s.Accounts.CreateAsync(None);var defender=(await s.Accounts.AuthenticateAsync(created.Token,None))!.Value;
        await PrepareArmy(s,defender,2,true);var marches=await RealMarches(s);s.Clock.Now+=s.Security.NewShieldMilliseconds+1;
        var march=await Depart(s,marches,"attack","city:"+defender.ToString("D"));s.Clock.Now=march.DueAtMs;await marches.RecoverAsync(None);
        var before=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        await s.Accounts.ExecuteAccountAsync(defender,null,None);
        s.Clock.Now=before.DueAtMs;await marches.RecoverAsync(None);
        var refreshed=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Equal("active",refreshed.Status);Assert.Null(refreshed.Result);Assert.True(refreshed.Revision>before.Revision);
        Assert.Equal(2,(await Army(s,defender)).Units.Single(u=>u.UnitId=="slg:levy").Ready);
        s.Clock.Now=refreshed.DueAtMs;await marches.RecoverAsync(None);
        Assert.Equal("committed",(await s.Store.GetAsync(s.Account,march.MarchId,None))!.Status);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task InvalidPrefixReleasesVerificationLeaseWithoutRenewingInputGrace()
    {
        await using var s=await CreateAsync();await PrepareArmy(s,s.Account,10);var marches=await RealMarches(s);
        var march=await Depart(s,marches,"pve","slg:test_grain");s.Clock.Now=march.DueAtMs;await marches.RecoverAsync(None);
        var issued=await s.Store.ChooseAsync(marches,s.Account,march.MarchId,"direct",None);
        s.Clock.Now+=issued.Contract!.Direct!.Input.InputGraceMilliseconds;
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Store.PrefixAsync(marches,s.Account,march.MarchId,
            [new(0,0,ReplayCommandKind.SetInvulnerable,Value:1)],None));
        var rejected=(await s.Store.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Null(rejected.VerificationId);Assert.Equal(issued.Frontier.LastReceivedAtMs,rejected.Frontier.LastReceivedAtMs);
        var accepted=await s.Store.PrefixAsync(marches,s.Account,march.MarchId,[new(0,0,ReplayCommandKind.Advance,new(1000,0))],None);
        Assert.Equal(s.Clock.Now,accepted.Frontier.LastReceivedAtMs);Assert.Equal(1,accepted.Frontier.ConfirmedTick);
        Assert.Null(accepted.VerificationId);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task CrashedVerificationLeaseRestartsWithoutExtendingGraceAndRecoversAtExpiry()
    {
        await using var s=await CreateAsync();await PrepareArmy(s,s.Account,10);var marches=await RealMarches(s);
        var march=await Depart(s,marches,"pve","slg:test_grain");s.Clock.Now=march.DueAtMs;await marches.RecoverAsync(None);
        var issued=await s.Store.ChooseAsync(marches,s.Account,march.MarchId,"direct",None);
        long expiry=s.Clock.Now+s.Executor.VerificationTimeoutMilliseconds+1;
        var reserved=issued with {VerificationId=Guid.NewGuid(),VerificationUntilMs=expiry,BeforeVerificationDueAtMs=issued.DueAtMs,DueAtMs=expiry,Revision=issued.Revision+1};
        await marches.ExecuteLockedAsync(async(c,t,now,ct)=>
        {
            var locked=(await marches.FindLockedAsync(c,t,s.Account,march.MarchId,ct))!;
            await using var q=new NpgsqlCommand("UPDATE world_combat_runs SET revision=$2,payload=$3::jsonb WHERE run_id=$1",c,t);
            q.Parameters.AddWithValue(reserved.RunId);q.Parameters.AddWithValue(reserved.Revision);q.Parameters.AddWithValue(JsonSerializer.Serialize(reserved,Json));
            await q.ExecuteNonQueryAsync(ct);await marches.RescheduleAsync(c,t,locked,"battle-active",expiry,"verification-pending",now,ct);return true;
        },None);
        var restart=s.Restart();await restart.InitializeAsync(None);
        Assert.Equal(reserved.VerificationId,(await restart.GetAsync(s.Account,march.MarchId,None))!.VerificationId);
        s.Clock.Now=expiry-1;Assert.Equal(0,await marches.RecoverAsync(None));
        s.Clock.Now=expiry;await marches.RecoverAsync(None);
        var recovered=(await restart.GetAsync(s.Account,march.MarchId,None))!;
        Assert.Null(recovered.VerificationId);Assert.Equal(issued.Frontier.LastReceivedAtMs,recovered.Frontier.LastReceivedAtMs);
        Assert.True(recovered.Frontier.ConfirmedTick>0);Assert.NotEmpty(recovered.Commands);
    }

    [Fact,Trait("Category","Postgres")]
    public async Task AccountDeletionReleasesUnfinishedPveLeaseThroughForeignKey()
    {
        await using var s=await CreateAsync();var march=March(s);
        await Locked(s,(c,t)=>s.Store.ArriveAsync(c,t,march,1000,None));
        var auth = new AuthStore(s.Db.Source, s.Accounts,
            new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1),
                AuthTests.ShippedLimits(), true, PseudonymKey: new byte[32]), s.Clock);
        var purgeAt = await auth.DeleteAccountAsync(s.Account, None);
        s.Clock.Now = purgeAt.ToUnixTimeMilliseconds();
        Assert.Equal(1, await auth.PurgeExpiredAsync(1, None));
        await using var lease=s.Db.Source.CreateCommand("SELECT run_id FROM world_combat_targets WHERE target_id='slg:test_grain'");
        Assert.IsType<DBNull>(await lease.ExecuteScalarAsync());
    }

}
