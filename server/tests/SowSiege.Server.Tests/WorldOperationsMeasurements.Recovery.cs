using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class WorldOperationsMeasurements
{
    [WorldMeasurement, Trait("Category", "Postgres")]
    public async Task Recovery()
    {
        int count = Count("WORLD_MEASURE_MARCHES", 1000);
        string raw = Environment.GetEnvironmentVariable("WORLD_MEASURE_BATCH_SIZES") ?? throw new InvalidOperationException("WORLD_MEASURE_BATCH_SIZES is required.");
        int[] budgets = raw.Split(',').Select(int.Parse).ToArray();
        Assert.NotEmpty(budgets);
        Assert.Equal(budgets.Length, budgets.Distinct().Count());
        Assert.All(budgets, value => Assert.InRange(value, 1, 4096));
        Output();
        string? reference = null;
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (int budget in budgets)
        {
            var setup = Stopwatch.StartNew();
            var samples = new List<Sample>();
            await using var db = await WorldTestDatabase.CreateAsync(4);
            var clock = new Clock { Now = startTime };
            using var factory = new Factory(db.ConnectionString, clock, budget);
            using var client = factory.CreateClient();
            var accounts = factory.Services.GetRequiredService<AccountStore>();
            var marches = factory.Services.GetRequiredService<WorldMarchStore>();
            var map = factory.Services.GetRequiredService<WorldMapContent>();
            var training = factory.Services.GetRequiredService<TrainingHandler>();
            var trainingContent = factory.Services.GetRequiredService<TrainingContent>();
            var policy = factory.Services.GetRequiredService<WorldMarchContent>();
            var gathering = factory.Services.GetRequiredService<GatheringContent>();
            var estate = EstateContent.Load(ContentEstateTests.Root());
            int soldiers = checked((gathering.Policy.Stock + gathering.Policy.CapacityPerSoldier - 1) / gathering.Policy.CapacityPerSoldier);
            var level = trainingContent.Rules.Levels.OrderBy(l => l.Level).First(l => l.Capacity >= soldiers && policy.Capacity.GetValueOrDefault(l.Level) >= soldiers);
            string unit = trainingContent.Rules.Units.First(u => u.RequiredBarracksLevel <= level.Level).UnitId;
            Assert.True(policy.Slots >= 1);
            Assert.InRange(soldiers, 1, level.Capacity);
            Assert.InRange(soldiers, 1, policy.Capacity[level.Level]);
            var target = map.InitialTargets.First(t => t.Kind == "resource");
            var accountIds = new List<Guid>();
            for (int i = 0; i < count; i++)
            {
                var created = await accounts.CreateAsync(None);
                Guid id = (await accounts.AuthenticateAsync(created.Token, None))!.Value;
                accountIds.Add(id);
                await using (var read = db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1"))
                {
                    read.Parameters.AddWithValue(id);
                    var state = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
                    state = state with
                    {
                        ActiveVassalIds = [],
                        Estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b => b.Kind == trainingContent.Rules.BarracksKind ? b with { Level = level.Level } : b).ToArray() },
                        Wallet = estate.Config.WalletCaps.ToDictionary(p => p.Key, p => p.Value)
                    };
                    await using var write = db.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
                    write.Parameters.AddWithValue(id);
                    write.Parameters.AddWithValue(MetaSaveCodec.Encode(state));
                    await write.ExecuteNonQueryAsync();
                }
                await accounts.ExecuteAccountAsync(id, null, None);
                var troops = training.InitialState.Deserialize<TrainingState>(AccountStore.Json)!;
                troops = troops with { Units = troops.Units.Select(u => u with { Ready = u.UnitId == unit ? soldiers : 0 }).ToArray() };
                await using (var write = db.Source.CreateCommand("UPDATE account_domains SET state_json=$2 WHERE account_id=$1 AND domain='training'"))
                {
                    write.Parameters.AddWithValue(id);
                    write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(troops, AccountStore.Json));
                    await write.ExecuteNonQueryAsync();
                }
                var view = SlgFixture.Parse(await accounts.ExecuteAccountAsync(id, null, None));
                var request = new WorldMarchCommand(Guid.NewGuid(), view.Estate.Version, "gather", target.TargetId, target.Generation,
                    policy.BasicHero, [new(unit, soldiers)]);
                var reply = JsonSerializer.Deserialize<WorldMarchReply>(await marches.ExecuteAsync(id, request, None), AccountStore.Json)!;
                Assert.True(reply.Done, reply.Reason);
                Assert.Equal("outbound", reply.March!.State);
            }
            setup.Stop();
            clock.Now = checked(startTime + (long)TimeSpan.FromDays(30).TotalMilliseconds);
            var drain = Stopwatch.StartNew();
            int selected = 0, committed = 0, combatAttempts = 0, pass = 0;
            WorldRecoveryBatchResult result;
            do
            {
                var timer = Stopwatch.StartNew();
                result = await marches.RecoverBatchAsync(None);
                timer.Stop();
                Assert.InRange(result.ProcessedEvents, 0, budget);
                Assert.True(!result.HasMore || result.OldestDueAtMs <= clock.Now);
                selected += result.ProcessedEvents;
                committed += result.CommittedEvents;
                combatAttempts += result.CombatRecoveryAttempts;
                samples.Add(new("recovery-batch", "budget-" + budget, pass++, timer.Elapsed.TotalMilliseconds, 0,
                    result.CommittedEvents, result.ProcessedEvents, result.CombatRecoveryAttempts,
                    result.OldestDueAtMs is { } due ? clock.Now - due : null));
                if (pass > count * 10 + 100) { throw new InvalidOperationException("Recovery did not converge."); }
            }
            while (result.HasMore);
            drain.Stop();
            Assert.Null(result.OldestDueAtMs);
            Assert.Equal(0, combatAttempts);
            Assert.True(selected >= count * 2);
            samples.Add(new("recovery-total", "budget-" + budget, 0, drain.Elapsed.TotalMilliseconds, 0, committed, selected, combatAttempts));
            await using (var verify = db.Source.CreateCommand("""
                SELECT (SELECT count(*) FROM world_marches WHERE state='returned'),
                  (SELECT count(*) FROM world_marches WHERE state<>'returned'),
                  (SELECT coalesce(sum(cargo),0)::bigint FROM world_gathering_arrivals WHERE status='settled'),
                  (SELECT count(*) FROM world_gathering_generations),
                  (SELECT generation FROM world_targets WHERE target_id=$1)
                """))
            {
                verify.Parameters.AddWithValue(target.TargetId);
                await using var rows = await verify.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                Assert.Equal((long)count, rows.GetInt64(0));
                Assert.Equal(0L, rows.GetInt64(1));
                Assert.Equal((long)gathering.Policy.Stock, rows.GetInt64(2));
                Assert.Equal(1L, rows.GetInt64(3));
                Assert.Equal((long)target.Generation + 1, rows.GetInt64(4));
            }
            await using (var held = db.Source.CreateCommand("SELECT coalesce(sum(amount),0)::bigint FROM account_held_materials"))
            {
                Assert.Equal((long)gathering.Policy.Stock, await held.ExecuteScalarAsync());
            }
            var canonical = new List<string>();
            foreach (Guid id in accountIds)
            {
                var view = SlgFixture.Parse(await accounts.ExecuteAccountAsync(id, null, None));
                var state = view.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!;
                var troops = state.Units.Single(u => u.UnitId == unit);
                Assert.Equal(soldiers, troops.Ready);
                Assert.Equal(0, troops.Deployed);
                // Strip random account/command identifiers; compare authoritative troop and wallet/credit outcomes in allocation order.
                canonical.Add(JsonSerializer.Serialize(new { state.Units, view.Estate.Wallet, Credits = view.Held }, AccountStore.Json));
            }
            string final = string.Join("\n", canonical);
            if (reference is null) { reference = final; }
            else { Assert.Equal(reference, final); }
            await Save("recovery-" + budget, samples, db.Source, map, count, setup.Elapsed.TotalMilliseconds);
        }
    }
}
