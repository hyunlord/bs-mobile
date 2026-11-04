using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

internal static class MixedLoad
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class FixtureClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class Actor
    {
        public Guid Account, Giver, Active, Waiting;
        public long Version, GiverVersion, OriginalDeadline, Shortened;
        public int Building;
    }
    // One millisecond upper-bound buckets; overflow is reported rather than hidden in the final bucket.
    internal sealed class Histogram
    {
        private readonly long[] buckets = new long[120001];
        public long Overflow;
        public void Add(double ms)
        {
            long bucket = (long)Math.Ceiling(ms);
            if (bucket >= buckets.Length) { Interlocked.Increment(ref Overflow); }
            else { Interlocked.Increment(ref buckets[Math.Max(0, (int)bucket)]); }
        }
        public Dictionary<int, long> Counts() => buckets.Select((value, index) => (value, index))
            .Where(p => p.value != 0).ToDictionary(p => p.index, p => p.value);
        public double? P95()
        {
            long target = (long)Math.Ceiling((buckets.Sum() + Overflow) * .95), seen = 0;
            if (target == 0) { return null; }
            for (int i = 0; i < buckets.Length; i++) { seen += buckets[i]; if (seen >= target) { return i; } }
            return null;
        }
    }

    internal static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        MixedTraining.RequireAvailable(); // Main must fail before opening a database or creating evidence files.
        int count = int.Parse(options["concurrency"]), poolSize = int.Parse(options["pool"]), seedParallel = int.Parse(options["seed-parallel"]);
        if (count is not (1000 or 10000) || poolSize != 64 || seedParallel is < 1 or > 32)
        { throw new ArgumentException("Mixed mode requires 1000/10000 actors, pool64, seed parallel1..32."); }
        string database = options["database"];
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SOW_LOAD_POSTGRES")
            ?? throw new InvalidOperationException("SOW_LOAD_POSTGRES required."));
        if (!Regex.IsMatch(database, "^sow_load_[a-z0-9_]{1,40}$") || settings.Database != database
            || settings.Host is not ("localhost" or "127.0.0.1" or "::1") || settings.Port != 55441)
        { throw new InvalidOperationException("Dedicated local load database required."); }
        var estate = EstateContent.Load(options["content-root"]);
        var research = ResearchContent.Load(options["content-root"]);
        var recruit = RecruitPool.Load(options["content-root"]);
        var alliance = AllianceContent.Load(options["content-root"]);
        var training = MixedTraining.Create(options["content-root"], estate, alliance);
        var node = research.Nodes.OrderBy(n => n.Nodes.Length).ThenBy(n => n.Id, StringComparer.Ordinal).First();
        var target = estate.Config.Buildings.First(b => !b.StartsComplete && b.Kind != training.Barracks
            && !node.Buildings.Any(r => r.Kind == b.Kind) && !estate.Config.Buildings.Any(k => k.Parent == b.Kind) && b.Levels.Length > 0);
        var app = new EstateApplication(estate);
        var clock = new FixtureClock(DateTimeOffset.UtcNow);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var seed = app.Create(now).State;
        seed = seed with
        {
            Wallet = seed.Wallet.ToDictionary(p => p.Key, p => app.WalletCap(p.Key)),
            Estate = seed.Estate! with
            {
                OpenZones = estate.Config.Zones.Select(z => z.Id).ToArray(),
                Buildings = seed.Estate.Buildings.Select(b => b with { Level = b.Kind == target.Kind ? 0
                    : b.Kind == training.Barracks ? training.Level : estate.Config.Buildings.Single(k => k.Kind == b.Kind).MaxLevel() }).ToArray()
            }
        };
        app.Validate(seed);
        int buildingId = seed.Estate.Buildings.First(b => b.Kind == target.Kind).Id;
        var constructionCheck = app.Apply(app.Create(now) with { State = seed }, new(Guid.NewGuid(), 0, "start-construction", buildingId), now);
        if (!constructionCheck.Reply.Done) { throw new InvalidDataException("Fixture construction is not executable: " + constructionCheck.Reply.Reason); }
        foreach (var material in seed.Wallet.Keys)
        {
            long needed = (long)training.Cost.GetValueOrDefault(material) * (training.ActiveCount + 1)
                + node.Cost.GetValueOrDefault(material) + target.Levels[0].Cost.GetValueOrDefault(material);
            if (needed > seed.Wallet[material]) { throw new InvalidDataException("Fixture costs exceed content wallet cap."); }
        }
        var levels = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Prerequisites(ResearchNode current)
        {
            foreach (var required in current.Nodes)
            {
                var predecessor = research.Nodes.Single(n => n.Id == required.Id);
                Prerequisites(predecessor); levels[required.Id] = required.Level;
            }
        }
        Prerequisites(node);
        var completed = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (string id in levels.Keys) { completed[id] = now; }
        var researchState = new ResearchState(1, [], levels, completed, [node.ProofId]);
        if (ResearchRules.LockReasons(node, seed, levels, new HashSet<string> { node.ProofId }).Length != 0)
        { throw new InvalidDataException("Fixture research prerequisites invalid."); }
        string schema = "mixed_" + Guid.NewGuid().ToString("N");
        settings.SearchPath = schema; settings.ApplicationName = schema; settings.MaxPoolSize = poolSize;
        settings.MinPoolSize = 0; settings.Multiplexing = false; settings.IncludeErrorDetail = false;
        settings.Timeout = 30; settings.CommandTimeout = 60;
        Directory.CreateDirectory(options["output"]);
        await using var manifest = new FileStream(Path.Combine(options["output"], "mixed-manifest.json"), FileMode.CreateNew);
        await using var resultsFile = new FileStream(Path.Combine(options["output"], "mixed-results.jsonl"), FileMode.CreateNew);
        await using var results = new StreamWriter(resultsFile);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(60));
        await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
        await using (var create = source.CreateCommand("CREATE SCHEMA " + schema)) { await create.ExecuteNonQueryAsync(stop.Token); }
        var registry = new DomainRegistry([new ResearchDomain(research), new RecruitDomain(recruit, recruit.CostGems), training.Handler], RandomNumberGenerator.GetBytes(32));
        var accounts = new AccountStore(source, app, clock, registry);
        var alliances = MixedTraining.Alliances(source, alliance, clock, accounts);
        await accounts.InitializeAsync(stop.Token); await alliances.InitializeAsync(stop.Token);
        await JsonSerializer.SerializeAsync(manifest, new
        {
            revision = options["revision"], mode = "finite-mixed-common-writer", count, accountCount = count * 2,
            poolSize, schema, database, fixtureUtc = now, clock = "fixed fixture UTC; not real-time capacity",
            secondsOption = "not used: finite heterogeneous workflow batch, no steady-state TPS claim", estateEffects = "#610 unavailable",
            fixture = "Data-capped wallets, completed prerequisite buildings/research and proof; not guest progression",
            estateHash = estate.Hash, researchHash = research.Hash, recruitHash = recruit.PolicyHash,
            trainingHash = training.Handler.ContentHash, training.Unit, training.ActiveCount, training.DurationMilliseconds,
            researchNode = node.Id, constructionKind = target.Kind, observation = "shared mixed DB waits, activeByKind sampled; no per-kind DB wait attribution",
            workflow = "rotate five independent groups by actor index; training start precedes cancel; help last"
        }, Json, stop.Token);
        await manifest.FlushAsync(stop.Token);
        var actors = Enumerable.Range(0, count).Select(_ => new Actor { Building = buildingId }).ToArray();
        async Task<AccountReply> Domain(Actor actor, string domain, string operation, object payload, Guid? id = null)
        {
            var command = new AccountCommand(id ?? Guid.NewGuid(), actor.Version, domain, operation, JsonSerializer.SerializeToElement(payload, Json));
            var reply = JsonSerializer.Deserialize<AccountReply>(await accounts.ExecuteAccountAsync(actor.Account, command, stop.Token), Json)!;
            actor.Version = reply.Estate.Version;
            if (!reply.Done) { throw new InvalidDataException(domain + "/" + operation + " refused: " + reply.Reason); }
            return reply;
        }
        async Task<AllianceReply> Alliance(Actor actor, bool giver, string operation, object payload)
        {
            var command = new AccountCommand(Guid.NewGuid(), giver ? actor.GiverVersion : actor.Version, "alliance", operation, JsonSerializer.SerializeToElement(payload, Json));
            var reply = JsonSerializer.Deserialize<AllianceReply>(await alliances.ExecuteAsync(giver ? actor.Giver : actor.Account, command, stop.Token), Json)!;
            if (giver) { actor.GiverVersion = reply.AccountVersion; } else { actor.Version = reply.AccountVersion; }
            if (!reply.Done) { throw new InvalidDataException("alliance/" + operation + " refused: " + reply.Reason); }
            return reply;
        }
        await Parallel.ForEachAsync(actors, new ParallelOptions { MaxDegreeOfParallelism = seedParallel, CancellationToken = stop.Token }, async (actor, token) =>
        {
            var created = await accounts.CreateAsync(token); actor.Account = (await accounts.AuthenticateAsync(created.Token, token))!.Value; actor.Version = created.Estate.Version;
            var giver = await accounts.CreateAsync(token); actor.Giver = (await accounts.AuthenticateAsync(giver.Token, token))!.Value; actor.GiverVersion = giver.Estate.Version;
            await using (var payload = source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1"))
            { payload.Parameters.AddWithValue(actor.Account); payload.Parameters.AddWithValue(MetaSaveCodec.Encode(seed)); await payload.ExecuteNonQueryAsync(token); }
            await using (var saved = source.CreateCommand("INSERT INTO account_domains(account_id,domain,state_version,content_hash,state_json) VALUES($1,'research',1,$2,$3)"))
            { saved.Parameters.AddWithValue(actor.Account); saved.Parameters.AddWithValue(research.Hash); saved.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(researchState, Json)); await saved.ExecuteNonQueryAsync(token); }
            var group = await Alliance(actor, true, "create", new { name = "Mixed", mode = "open" });
            await Alliance(actor, false, "apply", new { allianceId = group.CreatedId!.Value });
            actor.Active = Guid.NewGuid();
            var active = await Domain(actor, "training", "start", new { unitId = training.Unit, count = training.ActiveCount }, actor.Active);
            actor.OriginalDeadline = active.Domains["training"].GetProperty("active").GetProperty("completesAtMilliseconds").GetInt64();
            await Alliance(actor, false, "request-help", new { jobKey = "training:" + actor.Active.ToString("D") });
        });
        var observerSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { ApplicationName = schema + "-observer", MaxPoolSize = 1 };
        await using var observerSource = NpgsqlDataSource.Create(observerSettings.ConnectionString);
        await using var observer = await observerSource.OpenConnectionAsync(stop.Token);
        (string Kind, Func<Actor, Task> Command)[] commands =
        [
            ("training-start", async actor => { actor.Waiting = Guid.NewGuid(); await Domain(actor, "training", "start", new { unitId = training.Unit, count = 1 }, actor.Waiting); }),
            ("training-cancel", async actor => { await Domain(actor, "training", "cancel-waiting", new { jobId = actor.Waiting }); }),
            ("construction", async actor =>
            {
                var command = new EstateCommand(Guid.NewGuid(), actor.Version, "start-construction", actor.Building);
                var reply = JsonSerializer.Deserialize<CommandReply>(await accounts.ExecuteAsync(actor.Account, command, stop.Token), Json)!;
                actor.Version = reply.Estate.Version;
                if (!reply.Done) { throw new InvalidDataException("Construction refused: " + reply.Reason); }
            }),
            ("research-start", async actor => { await Domain(actor, "research", "start-research", new { nodeId = node.Id }); }),
            ("recruit", async actor => { await Domain(actor, "recruit", "recruit", new { poolVersion = recruit.PoolVersion }); }),
            ("alliance-chat", async actor => { await Alliance(actor, false, "chat", new { messageId = Guid.NewGuid(), text = "mixed measurement" }); }),
            ("alliance-help", async actor =>
            {
                var reply = await Alliance(actor, true, "give-help", new { requester = actor.Account, jobKey = "training:" + actor.Active.ToString("D") });
                actor.Shortened = reply.ShortenedMs ?? 0;
                if (actor.Shortened <= 0) { throw new InvalidDataException("Help did not shorten active job."); }
            })
        ];
        // Rotate independent groups to mix kinds immediately, while retaining start-before-cancel and help-last.
        int[][] groups = [[0, 1], [2], [3], [4], [5]];
        var byKind = commands.Select(_ => new Load.Stats()).ToArray();
        var latencies = commands.Select(_ => new Histogram()).ToArray();
        var overall = new Load.Stats();
        int maximumObservedKinds = 0;
        object ActiveByKind()
        {
            var active = commands.Select((command, index) => new { command.Kind, Active = Volatile.Read(ref byKind[index].InFlight) })
                .ToDictionary(p => p.Kind, p => p.Active, StringComparer.Ordinal);
            maximumObservedKinds = Math.Max(maximumObservedKinds, active.Values.Count(v => v > 0));
            return new { activeByKind = active, maximumObservedKinds, scope = "mixed aggregate DB waits; no per-kind lock attribution" };
        }
        async Task Execute(Actor actor, int index)
        {
            var stats = byKind[index];
            var elapsed = Stopwatch.StartNew();
            stats.Peak(Interlocked.Increment(ref stats.InFlight));
            overall.Peak(Interlocked.Increment(ref overall.InFlight));
            try
            {
                await commands[index].Command(actor);
                Interlocked.Increment(ref stats.Done); Interlocked.Increment(ref stats.Committed);
                Interlocked.Increment(ref overall.Done); Interlocked.Increment(ref overall.Committed);
            }
            catch
            {
                Interlocked.Increment(ref stats.Errors); Interlocked.Increment(ref overall.Errors); throw;
            }
            finally
            {
                latencies[index].Add(elapsed.Elapsed.TotalMilliseconds);
                Interlocked.Decrement(ref stats.InFlight); Interlocked.Decrement(ref overall.InFlight);
            }
        }
        await using var samplesFile = new FileStream(Path.Combine(options["output"], "mixed-db.jsonl"), FileMode.CreateNew);
        await using var samples = new StreamWriter(samplesFile);
        using var observing = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        long deadlocksBefore = await Load.Deadlocks(observer, stop.Token);
        var observation = Load.Observe(observer, schema, overall, samples, observing.Token, ActiveByKind);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] work = actors.Select(async (actor, actorIndex) =>
        {
            await release.Task.WaitAsync(stop.Token);
            for (int offset = 0; offset < groups.Length; offset++)
            {
                foreach (int index in groups[(actorIndex + offset) % groups.Length]) { await Execute(actor, index); }
            }
            await Execute(actor, commands.Length - 1);
        }).ToArray();
        var watch = Stopwatch.StartNew();
        release.SetResult();
        try { await Task.WhenAll(work); }
        finally
        {
            watch.Stop(); observing.Cancel();
            try { await observation; } catch (OperationCanceledException) when (observing.IsCancellationRequested) { }
            long deadlocks = await Load.Deadlocks(observer, stop.Token) - deadlocksBefore;
            for (int index = 0; index < commands.Length; index++)
            {
                var stats = byKind[index]; var latency = latencies[index];
                await results.WriteLineAsync(JsonSerializer.Serialize(new { kind = commands[index].Kind, planned = count,
                    done = stats.Done, errors = stats.Errors, p95MillisecondsUpperBound = latency.P95(),
                    histogramOverflow = latency.Overflow, histogram1ms = latency.Counts(), maximumInFlight = stats.Maximum,
                    observation = "command latency in heterogeneous mix, including pool wait; DB waits shared" }, Json));
            }
            await results.WriteLineAsync(JsonSerializer.Serialize(new { kind = "mixed-total", planned = count * commands.Length,
                done = overall.Done, errors = overall.Errors, deadlocks, elapsedSeconds = watch.Elapsed.TotalSeconds,
                completedBatchPerSecond = overall.Done / watch.Elapsed.TotalSeconds, maximumInFlight = overall.Maximum,
                maximumObservedKinds, observation = "one heterogeneous batch; aggregate DB waits sampled every250ms" }, Json));
            await results.FlushAsync(stop.Token);
        }
        // Verification queries follow the measured mixed workflow and cannot inflate command latency.
        await Parallel.ForEachAsync(actors, new ParallelOptions { MaxDegreeOfParallelism = seedParallel, CancellationToken = stop.Token }, async (actor, token) =>
        {
            await using var check = source.CreateCommand("""
                SELECT a.version,d.state_json::text,
                  (SELECT count(*) FROM outbox o WHERE o.account_id=a.id AND o.version=a.version),
                  (SELECT count(*) FROM domain_events e WHERE e.account_id=a.id AND e.kind='alliance-help-applied')
                FROM accounts a JOIN account_domains d ON d.account_id=a.id AND d.domain='training' WHERE a.id=$1
                """);
            check.Parameters.AddWithValue(actor.Account);
            await using var reader = await check.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) { throw new InvalidDataException("Missing verification state."); }
            using var state = JsonDocument.Parse(reader.GetString(1));
            var active = state.RootElement.GetProperty("active");
            if (reader.GetInt64(0) != actor.Version + 1 || reader.GetInt64(2) != 1 || reader.GetInt64(3) != 1
                || active.GetProperty("jobId").GetGuid() != actor.Active
                || active.GetProperty("completesAtMilliseconds").GetInt64() != actor.OriginalDeadline - actor.Shortened
                || active.GetProperty("helpMilliseconds").GetInt64() != actor.Shortened
                || state.RootElement.GetProperty("waiting").ValueKind != JsonValueKind.Null)
            { throw new InvalidDataException("Help deadline/version/event/outbox or waiting cancellation verification failed."); }
        });
        await results.WriteLineAsync(JsonSerializer.Serialize(new { verification = "PASS", accountsChecked = count,
            measuredCommands = count * 7, accountCount = count * 2, kindCount = 7,
            capacityClaim = "finite fixed-clock funded fixture, not sustained real-world capacity" }, Json));
        await results.FlushAsync(stop.Token);
        return 0;
    }
}
