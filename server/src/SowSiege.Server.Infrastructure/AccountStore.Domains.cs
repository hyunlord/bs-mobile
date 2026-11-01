using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Infrastructure;

public sealed partial class AccountStore
{
    private sealed record DomainAdvance(StoredAccount Account, bool Done, string Reason, Dictionary<string, JsonElement> Views, DomainResource[] Resources, ResourceAmount[]? Held = null);
    private sealed record HeldMaterial(long Seq, long OccurredUtc, Guid CommandId, string Domain, string Material, int Amount);
    private const string HeldDomain = "@held";
    private sealed record StoredDomain(int Version, string Hash, JsonElement State);

    private async Task<DomainAdvance> ApplyDomainsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        StoredAccount account, AccountCommand? command, EstateCommand? legacyCommand, long now, CancellationToken cancellation)
    {
        var registry = domains ?? new DomainRegistry([]);
        if (command is not null)
        {
            registry.Resolve(command.Domain);
        }
        var stored = new Dictionary<string, StoredDomain>(StringComparer.Ordinal);
        await using (var query = new NpgsqlCommand("SELECT domain,state_version,content_hash,state_json::text FROM account_domains WHERE account_id=$1", connection, transaction))
        {
            query.Parameters.AddWithValue(account.Id);
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                string domain = reader.GetString(0);
                registry.Resolve(domain); // Never silently omit persisted state when a plugin is missing.
                using var document = JsonDocument.Parse(reader.GetString(3));
                stored.Add(domain, new(reader.GetInt32(1), reader.GetString(2), document.RootElement.Clone()));
            }
        }
        var views = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (registry.Handlers.Count == 0)
        {
            return new(account, true, "", views, []);
        }
        long utc = Math.Max(now, account.Clock.HighWatermarkMilliseconds);
        await using (var query = new NpgsqlCommand("SELECT utc_highwater FROM account_job_clocks WHERE account_id=$1", connection, transaction))
        {
            query.Parameters.AddWithValue(account.Id);
            if (await query.ExecuteScalarAsync(cancellation) is long previous)
            {
                utc = Math.Max(utc, previous);
            }
        }
        // Account-wide held materials, credited oldest first (occurred UTC, commandId, seq) regardless of the source domain.
        var held = new List<HeldMaterial>();
        await using (var query = new NpgsqlCommand("SELECT seq,occurred_utc,command_id,source_domain,material,amount FROM account_held_materials WHERE account_id=$1", connection, transaction))
        {
            query.Parameters.AddWithValue(account.Id);
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                held.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
            }
        }
        long nextSeq = held.Count == 0 ? 1 : held.Max(h => h.Seq) + 1;
        var heldEvents = new List<DomainEvent>();
        Guid eventCommand = command?.CommandId ?? legacyCommand?.CommandId ?? Guid.Empty;
        void Enqueue(string domain, DomainCredit[]? credits)
        {
            foreach (var credit in credits ?? [])
            {
                if (credit.Amount <= 0 || application.WalletCap(credit.Material) <= 0)
                {
                    throw new InvalidDataException("Domain credit must be a positive amount of a wallet material.");
                }
                held.Add(new(nextSeq++, utc, eventCommand, domain, credit.Material, credit.Amount));
            }
        }
        void Flush()
        {
            var wallet = new Dictionary<string, int>(account.State.Wallet);
            bool changed = false;
            foreach (var item in held.OrderBy(h => h.OccurredUtc).ThenBy(h => h.CommandId).ThenBy(h => h.Seq).ToArray())
            {
                int amount = Math.Min(item.Amount, Math.Max(0, application.WalletCap(item.Material) - wallet.GetValueOrDefault(item.Material)));
                if (amount == 0)
                {
                    continue;
                }
                wallet[item.Material] = checked(wallet.GetValueOrDefault(item.Material) + amount);
                held.Remove(item);
                if (item.Amount > amount)
                {
                    held.Add(item with { Amount = item.Amount - amount });
                }
                heldEvents.Add(new("held-credited", JsonSerializer.SerializeToElement(new { item.Seq, item.Domain, materialId = item.Material, amount, remaining = item.Amount - amount }, Json)));
                changed = true;
            }
            if (changed)
            {
                account = account with { State = account.State with { Wallet = wallet } };
                application.Validate(account.State);
            }
        }
        bool done = true;
        string reason = "";
        var results = new Dictionary<string, DomainResult>(StringComparer.Ordinal);
        var changes = new Dictionary<string, List<DomainEvent>>(StringComparer.Ordinal);
        foreach (var handler in registry.Handlers)
        {
            JsonElement state = handler.InitialState;
            if (stored.TryGetValue(handler.Domain, out var saved))
            {
                if (saved.Version != handler.StateVersion || saved.Hash != handler.ContentHash)
                {
                    throw new CommandConflictException("domain-content-version-mismatch");
                }
                state = saved.State;
            }
            var events = new List<DomainEvent>();
            var result = handler.Advance(new(account, state, utc));
            events.AddRange(result.Events);
            account = account with { State = result.AccountState };
            application.Validate(account.State);
            Enqueue(handler.Domain, result.Credits);
            results.Add(handler.Domain, result);
            changes.Add(handler.Domain, events);
        }
        // Held materials enter the wallet before any estate production claim in this request.
        Flush();
        if (legacyCommand is not null)
        {
            var legacy = application.Apply(account with { Version = account.Version - 1 }, legacyCommand, now);
            account = legacy.Account;
            done = legacy.Reply.Done;
            reason = legacy.Reply.Reason;
        }
        foreach (var handler in registry.Handlers)
        {
            var result = results[handler.Domain];
            var events = changes[handler.Domain];
            if (command?.Domain == handler.Domain)
            {
                var ownHeld = held.Where(h => h.Domain == handler.Domain).GroupBy(h => h.Material, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(h => h.Amount), StringComparer.Ordinal);
                var context = new DomainContext(account, result.State, utc, command.CommandId, null, ownHeld);
                string? material = handler.SeedMaterial(context, command.Operation, command.Payload);
                if (material is not null)
                {
                    context = context with { Seed = DomainSeed.Compute(registry.SeedKey ?? throw new InvalidOperationException("Domains:SeedKey required."), account.Id, material) };
                }
                result = handler.Execute(context, command.Operation, command.Payload);
                events.AddRange(result.Events);
                account = account with { State = result.AccountState };
                application.Validate(account.State);
                Enqueue(handler.Domain, result.Credits);
                done = result.Done;
                reason = result.Reason;
            }
            results[handler.Domain] = result;
        }
        // Right after spending, before each domain's after-command step.
        Flush();
        if (command is not null || legacyCommand is not null)
        {
            foreach (var handler in registry.Handlers)
            {
                var settled = handler.AfterCommand(new(account, results[handler.Domain].State, utc, command?.CommandId ?? legacyCommand?.CommandId));
                account = account with { State = settled.AccountState };
                application.Validate(account.State);
                results[handler.Domain] = settled;
                changes[handler.Domain].AddRange(settled.Events);
                Enqueue(handler.Domain, settled.Credits);
            }
        }
        Flush();
        await using (var clear = new NpgsqlCommand("DELETE FROM account_held_materials WHERE account_id=$1", connection, transaction))
        {
            clear.Parameters.AddWithValue(account.Id);
            await clear.ExecuteNonQueryAsync(cancellation);
        }
        foreach (var item in held)
        {
            await using var keep = new NpgsqlCommand("INSERT INTO account_held_materials VALUES($1,$2,$3,$4,$5,$6,$7)", connection, transaction);
            keep.Parameters.AddWithValue(account.Id); keep.Parameters.AddWithValue(item.Seq); keep.Parameters.AddWithValue(item.OccurredUtc);
            keep.Parameters.AddWithValue(item.CommandId); keep.Parameters.AddWithValue(item.Domain); keep.Parameters.AddWithValue(item.Material);
            keep.Parameters.AddWithValue(item.Amount);
            await keep.ExecuteNonQueryAsync(cancellation);
        }
        for (int index = 0; index < heldEvents.Count; index++)
        {
            await using var entry = new NpgsqlCommand("INSERT INTO domain_events(account_id,version,domain,ordinal,kind,data) VALUES($1,$2,$3,$4,$5,$6)", connection, transaction);
            entry.Parameters.AddWithValue(account.Id); entry.Parameters.AddWithValue(account.Version);
            entry.Parameters.AddWithValue(HeldDomain); entry.Parameters.AddWithValue(index);
            entry.Parameters.AddWithValue(heldEvents[index].Kind); entry.Parameters.AddWithValue(NpgsqlDbType.Jsonb, heldEvents[index].Data.GetRawText());
            await entry.ExecuteNonQueryAsync(cancellation);
        }
        foreach (var handler in registry.Handlers)
        {
            var result = results[handler.Domain];
            var events = changes[handler.Domain];
            if (result.State.ValueKind != JsonValueKind.Object || result.View.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Domain state and view must be objects.");
            }
            results[handler.Domain] = result;
            await using var write = new NpgsqlCommand("INSERT INTO account_domains(account_id,domain,state_version,content_hash,state_json) VALUES($1,$2,$3,$4,$5) ON CONFLICT(account_id,domain) DO UPDATE SET state_version=$3,content_hash=$4,state_json=$5", connection, transaction);
            write.Parameters.AddWithValue(account.Id); write.Parameters.AddWithValue(handler.Domain);
            write.Parameters.AddWithValue(handler.StateVersion); write.Parameters.AddWithValue(handler.ContentHash);
            write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, result.State.GetRawText());
            await write.ExecuteNonQueryAsync(cancellation);
            for (int index = 0; index < events.Count; index++)
            {
                await using var entry = new NpgsqlCommand("INSERT INTO domain_events(account_id,version,domain,ordinal,kind,data) VALUES($1,$2,$3,$4,$5,$6)", connection, transaction);
                entry.Parameters.AddWithValue(account.Id); entry.Parameters.AddWithValue(account.Version);
                entry.Parameters.AddWithValue(handler.Domain); entry.Parameters.AddWithValue(index);
                entry.Parameters.AddWithValue(events[index].Kind); entry.Parameters.AddWithValue(NpgsqlDbType.Jsonb, events[index].Data.GetRawText());
                await entry.ExecuteNonQueryAsync(cancellation);
            }
        }
        foreach (var handler in registry.Handlers)
        {
            var view = handler.View(new(account, results[handler.Domain].State, utc));
            if (view.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Domain view must be an object.");
            }
            views.Add(handler.Domain, view.Clone());
        }
        await using var clock = new NpgsqlCommand("INSERT INTO account_job_clocks(account_id,utc_highwater) VALUES($1,$2) ON CONFLICT(account_id) DO UPDATE SET utc_highwater=$2", connection, transaction);
        clock.Parameters.AddWithValue(account.Id); clock.Parameters.AddWithValue(utc);
        await clock.ExecuteNonQueryAsync(cancellation);
        var resources = registry.Handlers.SelectMany(h => h.Resources(results[h.Domain].State)).ToArray();
        var totals = held.GroupBy(h => h.Material, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ResourceAmount(g.Key, g.Sum(h => h.Amount))).ToArray();
        return new(account, done, reason, views, resources, totals);
    }
}
