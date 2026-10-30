using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Application;

public sealed partial class TrainingHandler(EstateContent estate, TrainingContent content) : IDomainHandler
{
    public string Domain => "training";
    public int StateVersion => 1;
    public string ContentHash => content.Hash;
    public JsonElement InitialState => Encode(new TrainingState(content.Rules.Units.Select(u =>
        new TrainingTroops(u.UnitId, 0, 0, 0, 0)).ToArray(), null, null));

    public JsonElement View(DomainContext context) => Result(context, Read(context), context.Account.State, true, "view").View;

    public DomainResult Advance(DomainContext context)
    {
        var state = Read(context);
        var events = new List<DomainEvent>();
        var account = context.Account.State;
        (state, account) = FlushRefunds(state, account, events);
        while (state.Active is { } active && active.CompletesAtMilliseconds <= context.UtcMilliseconds)
        {
            state = state with
            {
                Units = state.Units.Select(u => u.UnitId == active.UnitId ? u with
                {
                    Reserved = checked(u.Reserved - active.Count), Ready = checked(u.Ready + active.Count)
                } : u).ToArray(),
                Active = state.Waiting is { } waiting ? Activate(waiting, active.CompletesAtMilliseconds!.Value) : null,
                Waiting = null
            };
            events.Add(new("training-completed", Encode(new { active.JobId, active.UnitId, active.Count,
                completedAtMilliseconds = active.CompletesAtMilliseconds })));
        }
        return Result(context, state, account, true, "advanced", events.ToArray());
    }

    public DomainResult Execute(DomainContext context, string operation, JsonElement payload)
    {
        var state = Read(context);
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != payload.EnumerateObject().Count())
        {
            return Result(context, state, context.Account.State, false, "invalid-payload");
        }
        try
        {
            return operation switch
            {
                "start" => Start(context, state, payload.Deserialize<TrainingStart>(TrainingContent.Json)!),
                "cancel-waiting" => Cancel(context, state, payload.Deserialize<TrainingCancel>(TrainingContent.Json)!),
                _ => Result(context, state, context.Account.State, false, "unsupported-operation")
            };
        }
        catch (JsonException)
        {
            return Result(context, state, context.Account.State, false, "invalid-payload");
        }
    }

    private DomainResult Start(DomainContext context, TrainingState state, TrainingStart request)
    {
        DomainResult Reject(string reason) => Result(context, state, context.Account.State, false, reason);
        var level = Level(context.Account.State);
        if (level.Level == 0) { return Reject("barracks-required"); }
        var unit = content.Rules.Units.SingleOrDefault(u => u.UnitId == request.UnitId);
        if (unit is null) { return Reject("unknown-unit"); }
        if (unit.RequiredBarracksLevel > level.Level) { return Reject("unit-locked"); }
        if (request.Count <= 0) { return Reject("invalid-count"); }
        if (request.Count > level.BatchLimit) { return Reject("batch-limit"); }
        if (state.Waiting is not null) { return Reject("queue-full"); }
        if ((long)Occupied(state) + request.Count > level.Capacity) { return Reject("capacity"); }
        var cost = unit.CostPerUnit.ToDictionary(c => c.Key, c => checked(c.Value * request.Count), StringComparer.Ordinal);
        if (cost.Any(c => context.Account.State.Wallet[c.Key] < c.Value)) { return Reject("insufficient-resources"); }
        var reserve = AccountEstate.ProtectedReserve(estate.Config, context.Account.State);
        if (reserve.Any(r => context.Account.State.Wallet[r.Key] - cost.GetValueOrDefault(r.Key) < r.Value))
        {
            return Reject("protected-reserve");
        }
        Guid jobId = context.CommandId ?? throw new InvalidOperationException("Training requires a command identity.");
        if (jobId == Guid.Empty || state.Active?.JobId == jobId || state.Waiting?.JobId == jobId)
        {
            return Reject("invalid-job-identity");
        }
        var account = context.Account.State with { Wallet = new(context.Account.State.Wallet) };
        foreach (var material in cost) { account.Wallet[material.Key] -= material.Value; }
        var job = new TrainingJob(jobId, unit.UnitId, request.Count, cost,
            checked((long)unit.SecondsPerUnit * request.Count * 1000), null, null, null);
        var next = state with
        {
            Units = state.Units.Select(u => u.UnitId == unit.UnitId ? u with { Reserved = checked(u.Reserved + request.Count) } : u).ToArray(),
            Active = state.Active ?? Activate(job, context.UtcMilliseconds),
            Waiting = state.Active is null ? null : job
        };
        var events = new List<DomainEvent> { new("training-reserved", Encode(new { job.JobId, job.UnitId, job.Count, chargedCost = cost })) };
        (next, account) = FlushRefunds(next, account, events);
        return Result(context, next, account, true, state.Active is null ? "training-started" : "training-queued", events.ToArray());
    }

    private DomainResult Cancel(DomainContext context, TrainingState state, TrainingCancel request)
    {
        if (state.Waiting is not { } job || job.JobId != request.JobId)
        {
            return Result(context, state, context.Account.State, false, "unknown-waiting-job");
        }
        var pending = new Dictionary<string, int>(state.PendingRefunds, StringComparer.Ordinal);
        foreach (var material in job.ChargedCost) { pending[material.Key] = checked(pending.GetValueOrDefault(material.Key) + material.Value); }
        var next = state with
        {
            Waiting = null, PendingRefunds = pending,
            Units = state.Units.Select(u => u.UnitId == job.UnitId ? u with { Reserved = u.Reserved - job.Count } : u).ToArray()
        };
        var events = new List<DomainEvent> { new("training-cancelled", Encode(new { job.JobId, job.UnitId, job.Count, refundedCost = job.ChargedCost })) };
        var account = context.Account.State;
        (next, account) = FlushRefunds(next, account, events);
        ValidatePending(next, account);
        return Result(context, next, account, true, "training-cancelled", events.ToArray());
    }

    private TrainingJob Activate(TrainingJob job, long atMilliseconds)
    {
        // No approved server hero/research bonus source is connected yet. Clients cannot provide one.
        int bonus = 0;
        long duration = Duration(job.BaseDurationMilliseconds, bonus, content.Rules.MaximumBonusPermille);
        return job with { StartedAtMilliseconds = atMilliseconds, CompletesAtMilliseconds = checked(atMilliseconds + duration), BonusPermille = bonus };
    }

    public static long Duration(long baseMilliseconds, int bonusPermille, int maximumBonusPermille)
    {
        if (baseMilliseconds <= 0 || bonusPermille < 0 || maximumBonusPermille < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseMilliseconds));
        }
        long divisor = checked(PlayerInput.Scale + Math.Min(bonusPermille, maximumBonusPermille));
        // GDD15 defines duration in whole seconds; round up once before converting the deadline to milliseconds.
        long seconds = baseMilliseconds / divisor + (baseMilliseconds % divisor == 0 ? 0 : 1);
        return checked(seconds * 1000);
    }

    private TrainingLevel Level(MetaState account)
    {
        int level = account.Estate?.Buildings.Single(b => b.Kind == content.Rules.BarracksKind).Level
            ?? throw new InvalidDataException("Training requires the account estate.");
        return content.Rules.Levels.SingleOrDefault(l => l.Level == level)
            ?? throw new InvalidDataException("Unknown completed barracks level.");
    }

    private TrainingState Read(DomainContext context)
    {
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json) ?? throw new InvalidDataException("Missing training state.");
        var jobs = new[] { state.Active, state.Waiting }.OfType<TrainingJob>().ToArray();
        bool valid = state.Units.Select(u => u.UnitId).Order(StringComparer.Ordinal)
            .SequenceEqual(content.Rules.Units.Select(u => u.UnitId).Order(StringComparer.Ordinal)) &&
            state.Units.All(u => u.Ready >= 0 && u.Deployed >= 0 && u.Wounded >= 0 && u.Reserved >= 0 &&
                (long)u.Reserved == jobs.Where(j => j.UnitId == u.UnitId).Sum(j => (long)j.Count)) &&
            (state.Waiting is null || state.Active is not null) &&
            jobs.Select(j => j.JobId).Distinct().Count() == jobs.Length &&
            jobs.All(j => j.JobId != Guid.Empty && j.Count > 0 && j.BaseDurationMilliseconds > 0 &&
                content.Rules.Units.Any(u => u.UnitId == j.UnitId) && j.ChargedCost.Count > 0 &&
                ValidJob(j)) &&
            (state.Active is null || (state.Active.StartedAtMilliseconds.HasValue && state.Active.BonusPermille.HasValue &&
                state.Active.BonusPermille >= 0 && state.Active.BonusPermille <= content.Rules.MaximumBonusPermille &&
                state.Active.CompletesAtMilliseconds == checked(state.Active.StartedAtMilliseconds +
                    Duration(state.Active.BaseDurationMilliseconds, state.Active.BonusPermille.Value, content.Rules.MaximumBonusPermille)))) &&
            (state.Waiting is null || (state.Waiting.StartedAtMilliseconds is null && state.Waiting.CompletesAtMilliseconds is null && state.Waiting.BonusPermille is null));
        if (!valid) { throw new InvalidDataException("Invalid training reservation or job state."); }
        if (Occupied(state) > Level(context.Account.State).Capacity)
        {
            throw new InvalidDataException("Stored troop occupancy exceeds the completed barracks capacity.");
        }
        ValidatePending(state, context.Account.State);
        return state;
    }

    private bool ValidJob(TrainingJob job)
    {
        var unit = content.Rules.Units.Single(u => u.UnitId == job.UnitId);
        return job.Count <= content.Rules.Levels.Max(l => l.BatchLimit) &&
            job.BaseDurationMilliseconds == checked((long)unit.SecondsPerUnit * job.Count * 1000) &&
            job.ChargedCost.Count == unit.CostPerUnit.Count &&
            unit.CostPerUnit.All(c => job.ChargedCost.TryGetValue(c.Key, out int paid) && paid == checked(c.Value * job.Count));
    }

    private DomainResult Result(DomainContext context, TrainingState state, MetaState account, bool done, string reason,
        DomainEvent[]? events = null)
    {
        var level = Level(account);
        var view = new TrainingView(level.Level, level.Capacity, level.BatchLimit, Occupied(state), context.UtcMilliseconds,
            content.Rules.Units.Select(unit =>
            {
                var troops = state.Units.Single(u => u.UnitId == unit.UnitId);
                return new TrainingUnitView(unit.UnitId, unit.Name, unit.RequiredBarracksLevel, unit.RequiredBarracksLevel <= level.Level,
                    troops.Ready, troops.Deployed, troops.Wounded, troops.Reserved, Materials(unit.CostPerUnit), unit.SecondsPerUnit);
            }).ToArray(), JobView(state.Active), JobView(state.Waiting), Materials(state.PendingRefunds), RefundPreview(state, account));
        return new(account, Encode(state), Encode(view), done, reason, events ?? []);
    }

    private static int Occupied(TrainingState state) => checked((int)state.Units.Sum(u => (long)u.Ready + u.Deployed + u.Wounded + u.Reserved));
    private static TrainingMaterialView[] Materials(Dictionary<string, int> materials) => materials.OrderBy(m => m.Key, StringComparer.Ordinal)
        .Select(m => new TrainingMaterialView(m.Key, m.Value)).ToArray();
    private static TrainingJobView? JobView(TrainingJob? job) => job is null ? null : new(job.JobId, job.UnitId, job.Count,
        job.StartedAtMilliseconds, job.CompletesAtMilliseconds,
        job.CompletesAtMilliseconds - job.StartedAtMilliseconds ?? job.BaseDurationMilliseconds, job.BonusPermille, Materials(job.ChargedCost));
    private static JsonElement Encode<T>(T value) => JsonSerializer.SerializeToElement(value, TrainingContent.Json);
}
