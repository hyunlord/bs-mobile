using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed class GatheringMarchActivity(GatheringStore gathering) : IWorldMarchActivity
{
    public bool Supports(string operation) => operation == "gather";
    public Task DispatchAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, CancellationToken ct) => Task.CompletedTask;

    public async Task<MarchActivityResult> ArriveAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, CancellationToken ct)
    {
        var visit = await gathering.ArriveAsync(c, t, new(march.MarchId, march.AccountId, march.TargetId, march.Generation,
            checked((int)march.Troops.Sum(x => (long)x.Count)), march.TravelMilliseconds, true), at, ct);
        return Next(visit);
    }

    public async Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, bool cancel, CancellationToken ct) =>
        Next(await gathering.ProcessActivityAsync(c, t, march.MarchId, at, cancel, ct));

    public async Task<DomainCredit[]> ReturnAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, CancellationToken ct)
    {
        // A recalled outbound march or a disappeared target never acquired a gathering arrival receipt.
        await using var exists = new NpgsqlCommand("SELECT 1 FROM world_gathering_arrivals WHERE march_id=$1", c, t);
        exists.Parameters.AddWithValue(march.MarchId);
        if (await exists.ExecuteScalarAsync(ct) is null) { return []; }
        return await gathering.ReturnCreditsAsync(c, t, march.MarchId, march.AccountId, at, ct);
    }

    private static MarchActivityResult Next(GatheringVisit visit) => new(
        visit.Status == "collecting" ? "gathering" : "returning",
        visit.DueAtMs ?? throw new InvalidDataException("Gathering visit requires a deadline."), visit.Reason);
}
