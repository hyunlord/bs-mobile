using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed partial class WorldCombatStore
{
    public async Task<MarchActivityResult> ArriveCityAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        var existing=await Find(c,t,march.MarchId,ct);
        if(existing is not null)return Next(existing,march,at);
        var run=new WorldCombatRun(Guid.NewGuid(),march.MarchId,march.AccountId,march.TargetId,march.Generation,"account:city","active",0,
            checked(at+1),null,[],new(at,at,0,0),null,[]);
        try
        {
            await accounts.ExecuteWorldAsync(c,t,march.TargetAccountId!.Value,run.RunId,at,(context,_)=>
            {
                run=RefreshCity(run,march,context,at);
                return Task.FromResult(new WorldAccountChanges("world-city-snapshot",[],[]));
            },ct);
        }
        catch(CommandConflictException conflict)
        {
            // Unsupported snapshots never start a battle or consume defense troops.
            return new("returning",checked(at+march.TravelMilliseconds),conflict.Message);
        }
        await Insert(c,t,run,ct);
        return Next(run,march,at);
    }

    private WorldCombatRun RefreshCity(WorldCombatRun run,WorldMarch march,WorldAccountContext context,long at)
    {
        var domains=JsonSerializer.SerializeToElement(new SortedDictionary<string,JsonElement>(context.States.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal),AccountStore.Json);
        var trainingState=JsonSerializer.Deserialize<TrainingState>(context.States["training"],AccountStore.Json)
            ??throw new InvalidDataException("Missing city training snapshot.");
        var contract=factory.Issue(march,"account:city",run.RunId,at,WorldCombatMode.CityPvp,trainingState,domains,
            JsonSerializer.SerializeToElement(context.Account.State,AccountStore.Json));
        return run with {Contract=contract,DefenderVersion=context.Account.Version,DefenderDomains=domains.GetRawText(),
            DueAtMs=checked(at+content.Rules.PrefixIntervalMilliseconds),Revision=checked(run.Revision+1)};
    }

    private async Task<(WorldCombatRun Run,bool Committed)> CommitCity(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,
        WorldCombatRun run,ArmyAutoResult result,long at,CancellationToken ct)
    {
        Guid target=march.TargetAccountId??throw new InvalidDataException("Missing defender identity.");
        await using(var eligible=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM auth_deleted_accounts WHERE account_id=ANY($1))
              AND EXISTS(SELECT 1 FROM accounts WHERE id=$2)
              AND NOT EXISTS(SELECT 1 FROM alliance_members a JOIN alliance_members b ON a.alliance_id=b.alliance_id
                WHERE a.account_id=$3 AND b.account_id=$2)
            """,c,t))
        {
            eligible.Parameters.AddWithValue(new[]{march.AccountId,target});eligible.Parameters.AddWithValue(target);eligible.Parameters.AddWithValue(march.AccountId);
            if(await eligible.ExecuteScalarAsync(ct) is not true)
                return(run with {Status="recalled",DueAtMs=checked(at+march.TravelMilliseconds)},false);
        }
        bool committed=false;
        await accounts.ExecuteWorldAsync(c,t,target,run.RunId,at,(context,_)=>
        {
            var domains=JsonSerializer.SerializeToElement(new SortedDictionary<string,JsonElement>(context.States.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal),AccountStore.Json).GetRawText();
            // The common writer increments account version once before its callback and advances all domains.
            // Revalidate both: a training deadline may change R without an intervening player command.
            if(context.Account.Version!=checked(run.DefenderVersion!.Value+1) || domains!=run.DefenderDomains)
            {
                try { run=RefreshCity(run,march,context,at); }
                catch(CommandConflictException)
                {
                    run=run with {Status="recovery-pending",DueAtMs=checked(context.UtcMilliseconds+content.Rules.PrefixIntervalMilliseconds)};
                }
                return Task.FromResult(new WorldAccountChanges("world-city-refresh",[],[]));
            }
            WorldDomainUpdate[] updates=[];
            if(result.DefenderTroops.Count>0)
            {
                var loss=training.ApplyCombatLosses(new(context.Account,context.States["training"],context.UtcMilliseconds,run.RunId),
                    result.DefenderTroops,run.Contract!.Award.InjuryPercent,content.BedCapacity(context.Account.State,trainingContent.Rules.BarracksKind),false);
                if(!loss.Done)throw new InvalidDataException("Defender combat snapshot no longer available: "+loss.Reason);
                updates=[new("training",loss.State,loss.Events)];
            }
            var loot=result.Combat.Terminal==WorldTerminalReason.AutoVictory
                ? Cargo(securityPolicy.Materials.OrderBy(x=>x.MaterialId,StringComparer.Ordinal).Select(m=>new DomainCredit(m.MaterialId,
                    CitySecurityRules.LootLimit(context.Account.State.Wallet.GetValueOrDefault(m.MaterialId),m.ProtectedAmount,securityPolicy.LootPermille)))
                    .Where(x=>x.Amount>0).ToArray(),result.Combat.Troops):[];
            run=run with {Credits=loot,DefenderResult=result.DefenderTroops.ToArray()};
            committed=true;
            return Task.FromResult(new WorldAccountChanges("world-city-combat",updates,[],loot));
        },ct);
        return(run,committed);
    }

    private DomainCredit[] Cargo(DomainCredit[] awards,IReadOnlyList<WorldTroopResult> troops)
    {
        long remaining=checked(troops.Sum(t=>(long)t.N-t.K)*content.Rules.CargoPerSurvivor);
        var credits=new List<DomainCredit>();
        if(awards.Any(a=>!content.Rules.CargoMaterialOrder.Contains(a.Material,StringComparer.Ordinal)))
            throw new InvalidDataException("Combat material has no cargo priority.");
        foreach(var award in awards.OrderBy(a=>Array.IndexOf(content.Rules.CargoMaterialOrder,a.Material)))
        {
            if(award.Amount<0)throw new InvalidDataException("Negative combat award.");
            int amount=(int)Math.Min(remaining,award.Amount);
            if(amount>0)credits.Add(new(award.Material,amount));
            remaining-=amount;
        }
        return credits.ToArray();
    }
}
