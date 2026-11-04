using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

internal sealed class HelpRejectedException(string reason) : Exception(reason);

public sealed partial class AccountStore
{
    // Caller already holds the account row. Membership writers must take that row before changing membership.
    // This plain read never acquires an alliance lock after an account/world lock.
    private static async Task<DomainMembership?> ReadHelpMembershipAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CancellationToken ct)
    {
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('alliance_members') IS NOT NULL", c, t))
        {
            if (await exists.ExecuteScalarAsync(ct) is not true) { return null; }
        }
        await using var query = new NpgsqlCommand("SELECT alliance_id,joined_ms FROM alliance_members WHERE account_id=$1", c, t);
        query.Parameters.AddWithValue(account);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetInt64(1)) : null;
    }

    internal async Task<IReadOnlyList<HelpJob>> HelpJobsAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CancellationToken ct)
    {
        var jobs = new List<HelpJob>();
        await using var query = new NpgsqlCommand("SELECT domain,state_version,content_hash,state_json::text FROM account_domains WHERE account_id=$1 AND domain=ANY($2)", c, t);
        query.Parameters.AddWithValue(account); query.Parameters.AddWithValue(new[] { "research", "training" });
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var handler = (domains ?? throw new InvalidOperationException("Help requires registered domains.")).Resolve(reader.GetString(0));
            if (handler.StateVersion != reader.GetInt32(1) || handler.ContentHash != reader.GetString(2))
            {
                throw new CommandConflictException("domain-content-version-mismatch");
            }
            using var json = JsonDocument.Parse(reader.GetString(3));
            jobs.AddRange(handler.HelpJobs(json.RootElement));
        }
        return jobs;
    }

    // AllianceStore holds alliance -> giver/requester accounts in UUID order and owns the outer transaction/receipt.
    internal Task<string> ExecuteAllianceHelpAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid requester, Guid operation,
        Guid alliance, string key, long now, Func<HelpJob, long, CancellationToken, Task<long>> grant, CancellationToken ct) =>
        ExecuteInTransactionAsync(c, t, requester, null, null, true, ct, new(operation, now, async (context, token) =>
        {
            string domain = key.StartsWith("research:", StringComparison.Ordinal) ? "research"
                : key.StartsWith("training:", StringComparison.Ordinal) ? "training" : throw new HelpRejectedException("invalid-job-key");
            var registry = domains ?? throw new InvalidOperationException("Help requires registered domains.");
            var handler = registry.Resolve(domain);
            var job = handler.HelpJobs(context.States[domain]).SingleOrDefault(j => j.Key == key);
            var membership = await ReadHelpMembershipAsync(c, t, requester, token);
            if (job is null || job.CompletesAt <= context.UtcMilliseconds) { throw new HelpRejectedException("job-not-active"); }
            if (membership is null || job.StartAllianceId != alliance || membership.AllianceId != alliance || job.StartedAt < membership.JoinedAtMilliseconds)
            {
                throw new HelpRejectedException("request-closed");
            }
            long shortened = await grant(job, context.UtcMilliseconds, token);
            if (shortened <= 0 || shortened > job.CompletesAt - context.UtcMilliseconds) { throw new InvalidDataException("Invalid help duration."); }
            var changed = handler.ShortenHelpJob(context.States[domain], key, shortened);
            var states = context.States.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            states[domain] = changed;
            var effects = Array.AsReadOnly(registry.Handlers.SelectMany(h => h.Effects(states[h.Domain])).OrderBy(e => e.FromUtcMilliseconds).ToArray());
            var result = handler.Advance(new(context.Account, changed, context.UtcMilliseconds, operation, Effects: effects, Membership: membership));
            var events = new[] { new DomainEvent("alliance-help-applied", JsonSerializer.SerializeToElement(new { jobKey = key, shortenedMilliseconds = shortened }, Json)) }
                .Concat(result.Events).ToArray();
            return new("alliance", [new(domain, result.State, events)], result.Credits ?? []);
        }));
}
