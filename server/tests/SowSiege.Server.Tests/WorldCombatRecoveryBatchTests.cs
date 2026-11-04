using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class WorldCombatStoreTests
{
    [Fact, Trait("Category", "Postgres")]
    public async Task DirectNoInputReplayConsumesOneRecoverySlotAndLeavesLaterEventsDue()
    {
        await using var s = await CreateAsync();
        await PrepareArmy(s, s.Account, 10);
        var marches = await RealMarches(s, recoveryBatchSize: 1);
        var march = await Depart(s, marches, "pve", "slg:test_grain");
        s.Clock.Now = march.DueAtMs;
        Assert.Equal(1, (await marches.RecoverBatchAsync(None)).ProcessedEvents);
        var issued = await s.Store.ChooseAsync(marches, s.Account, march.MarchId, "direct", None);
        s.Clock.Now += 30L * 86_400_000;

        var batch = await marches.RecoverBatchAsync(None);

        Assert.Equal(1, batch.ProcessedEvents);
        Assert.Equal(1, batch.CombatRecoveryAttempts);
        Assert.Equal(1, batch.CommittedEvents);
        Assert.True(batch.HasMore);
        var recovered = (await s.Store.GetAsync(s.Account, march.MarchId, None))!;
        Assert.True(recovered.Frontier.ConfirmedTick > issued.Frontier.ConfirmedTick);
        Assert.NotEmpty(recovered.Commands);
        Assert.Equal(issued.Frontier.LastReceivedAtMs, recovered.Frontier.LastReceivedAtMs);
        Assert.Equal(recovered.DueAtMs, batch.OldestDueAtMs);
        Assert.True(batch.OldestDueAtMs <= s.Clock.Now);
        Assert.NotEqual("returned", recovered.Status);
        Assert.Equal(5, (await Army(s, s.Account)).Units.Single(u => u.UnitId == "slg:levy").Deployed);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CityAutomaticBattleConsumesOneRecoverySlotBeforeSeparateReturnBatch()
    {
        await using var s = await CreateAsync();
        await PrepareArmy(s, s.Account, 10);
        var created = await s.Accounts.CreateAsync(None);
        var defender = (await s.Accounts.AuthenticateAsync(created.Token, None))!.Value;
        await PrepareArmy(s, defender, 2, true);
        var marches = await RealMarches(s, recoveryBatchSize: 1);
        s.Clock.Now += s.Security.NewShieldMilliseconds + 1;
        var march = await Depart(s, marches, "attack", "city:" + defender.ToString("D"));
        s.Clock.Now = march.DueAtMs;
        Assert.Equal(1, (await marches.RecoverBatchAsync(None)).ProcessedEvents);
        var pending = (await s.Store.GetAsync(s.Account, march.MarchId, None))!;
        Assert.Equal("active", pending.Status);
        s.Clock.Now += 30L * 86_400_000;

        var battleBatch = await marches.RecoverBatchAsync(None);

        Assert.Equal(1, battleBatch.ProcessedEvents);
        Assert.Equal(1, battleBatch.CombatRecoveryAttempts);
        Assert.Equal(1, battleBatch.CommittedEvents);
        Assert.True(battleBatch.HasMore);
        var battle = (await s.Store.GetAsync(s.Account, march.MarchId, None))!;
        Assert.Equal("committed", battle.Status);
        Assert.NotNull(battle.Result);
        Assert.Equal(battle.DueAtMs, battleBatch.OldestDueAtMs);
        Assert.Equal(5, (await Army(s, s.Account)).Units.Single(u => u.UnitId == "slg:levy").Deployed);

        var returnBatch = await marches.RecoverBatchAsync(None);

        Assert.Equal(new WorldRecoveryBatchResult(1, 1, 0, false, null), returnBatch);
        Assert.Equal("returned", (await s.Store.GetAsync(s.Account, march.MarchId, None))!.Status);
        Assert.Equal(0, (await Army(s, s.Account)).Units.Sum(u => u.Deployed));
        Assert.Equal(new WorldRecoveryBatchResult(0, 0, 0, false, null), await marches.RecoverBatchAsync(None));
    }
}
