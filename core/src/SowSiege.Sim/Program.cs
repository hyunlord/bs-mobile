using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;

try
{
    Run(args);
    return 0;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException or OverflowException)
{
    Console.Error.WriteLine($"Simulation failed: {error.Message}");
    return 2;
}

static void Run(string[] args)
{
    var valueOptions = new HashSet<string>(StringComparer.Ordinal)
{
    "--data", "--seed", "--iterations", "--hero", "--estate", "--policy", "--output", "--metrics",
        "--people-rule", "--scenario", "--duration-ticks", "--warmup-ticks", "--timings", "--profile", "--movement"
};
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index++)
    {
        var key = args[index];
        if (key == "--include-test") { options.Add(key, "true"); }
        else if (valueOptions.Contains(key) && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) { options.Add(key, args[++index]); }
        else { throw new ArgumentException($"Unknown or incomplete option: {key}"); }
    }
    var data = options.GetValueOrDefault("--data", "data");
    var includeTest = options.ContainsKey("--include-test");
    var profileName = options.GetValueOrDefault("--profile", "s2-baseline");
    var profile = ContentLoader.LoadProfile(data, profileName);
    var profileHash = ContentLoader.ProfileHash(data, profileName);
    var selection = profile.Select(includeTest);
    var catalog = ContentLoader.Load(data, includeTest, profileName);
    var configuredDurationTicks = catalog.Tuning.DurationTicks;
    var seed = ParseInteger("--seed", "42");
    var iterations = ParseInteger("--iterations", "3");
    var warmupTicks = ParseInteger("--warmup-ticks", "300");
    if (iterations <= 0 || warmupTicks < 0) { throw new ArgumentException("iterations must be positive; warmup-ticks must be nonnegative."); }
    if (options.ContainsKey("--duration-ticks"))
    {
        var duration = ParseInteger("--duration-ticks", "0");
        if (duration < 1 || duration > configuredDurationTicks) { throw new ArgumentException("duration-ticks must be positive and no greater than configured full duration."); }
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = duration } };
    }
    var shortened = catalog.Tuning.DurationTicks < configuredDurationTicks;
    var peopleRule = options.GetValueOrDefault("--people-rule", catalog.Tuning.World.DefaultPeopleRule);
    var scenario = options.GetValueOrDefault("--scenario", "normal");
    var policy = options.GetValueOrDefault("--policy", "mixed");
    var heroId = options.GetValueOrDefault("--hero", catalog.Tuning.DefaultHero);
    var estateId = options.GetValueOrDefault("--estate", catalog.Tuning.DefaultEstate);
    if (peopleRule is not ("A" or "B" or "C") || scenario is not ("normal" or "load")) { throw new ArgumentException("people-rule must be A/B/C; scenario must be normal/load."); }
    if (!catalog.Tuning.Policies.ContainsKey(policy) || !catalog.Heroes.ContainsKey(heroId) || !catalog.Estates.ContainsKey(estateId)) { throw new ArgumentException("Unknown policy, hero, or estate."); }
    var movement = options.TryGetValue("--movement", out var requestedMovement) ? requestedMovement : catalog.Experiment is null ? null : "circuit";
    var run = new RunOptions(seed, heroId, estateId, policy, peopleRule, scenario, Movement: movement);
    var samples = new List<double>();
    var results = new List<SimulationResult>();
    var loadExpected = catalog.Tuning.World.Load;
    var beforeMinimum = new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
    var beforeMaximum = new int[5];
    var afterMinimum = new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
    var afterMaximum = new int[5];
    var timingCsv = options.ContainsKey("--timings") ? new StringBuilder("iteration,tick,elapsed_ms,before_enemies,before_farms,before_buildings,before_people,after_enemies,after_farms,after_buildings,after_people,before_population_members,after_population_members\n") : null;
    var warmup = new Simulation(catalog, run);
    var actualWarmupTicks = 0;
    while (!warmup.IsComplete && actualWarmupTicks < warmupTicks)
    {
        if (scenario == "load") { warmup.PrepareLoadTick(); }
        warmup.Tick();
        actualWarmupTicks++;
    }
    for (var repeat = 0; repeat < iterations; repeat++)
    {
        var simulation = new Simulation(catalog, run);
        while (!simulation.IsComplete)
        {
            if (scenario == "load") { simulation.PrepareLoadTick(); }
            var before = simulation.Snapshot;
            if (scenario == "load") { VerifyLoad(before); }
            var start = Stopwatch.GetTimestamp();
            simulation.Tick();
            var elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            var after = simulation.Snapshot;
            if (scenario == "load") { VerifyLoad(after); }
            Observe(before, beforeMinimum, beforeMaximum);
            Observe(after, afterMinimum, afterMaximum);
            samples.Add(elapsed);
            timingCsv?.Append(CultureInfo.InvariantCulture, $"{repeat},{after.Tick},{elapsed:R},{before.ActiveEnemies},{before.Farms},{before.Buildings},{before.People},{after.ActiveEnemies},{after.Farms},{after.Buildings},{after.People},{before.PopulationMembers},{after.PopulationMembers}\n");
        }
        results.Add(simulation.Result());
    }
    if (samples.Count == 0) { throw new InvalidOperationException("No simulation ticks measured."); }
    if (results.Select(result => result.Hash).Distinct(StringComparer.Ordinal).Count() != 1)
    {
        throw new InvalidOperationException("Determinism failure: identical input produced different hashes.");
    }
    samples.Sort();
    var contentHash = ContentLoader.Hash(data, includeTest);
    var gitRoot = Git("rev-parse", "--show-toplevel");
    var gitHead = Git("rev-parse", "HEAD");
    var gitStatus = Git("status", "--porcelain");
    var sourceHash = SourceHash(gitRoot);
    var stage = catalog.Experiment is not null ? "S4b" : catalog.Runtime is null ? "S2" : "S4";
    var scope = scenario == "load" ? "S2 exact-load mechanics fixture; maintenance included; not natural gameplay" : shortened ? "S2 truncated headless mechanics fixture; not a full game" : "S2 full-duration headless gameplay simulation; balance not approved";
    scope = scope.Replace("S2", stage, StringComparison.Ordinal);
    var metadata = new
    {
        profileId = profile.Id,
        profileSha256 = profileHash,
        experimentContractVersion = profile.Experiment?.ContractVersion,
        movementMode = movement,
        tuningFile = profile.Experiment?.TuningFile,
        tuningSha256 = profile.Experiment is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data, profile.Experiment.TuningFile)))),
        experimentDefinition = catalog.Experiment,
        experienceCurve = catalog.Experiment?.Experience,
        mixedCategoryOrder = catalog.Experiment?.MixedCategoryOrder,
        worldUnit = catalog.Experiment is null ? null : "world-unit",
        threatTuning = catalog.Experiment is null ? null : catalog.Tuning.World.Threat,
        enemyTuning = catalog.Experiment is null ? null : catalog.Enemies.Values.OrderBy(enemy => enemy.Id, StringComparer.Ordinal).ToArray(),
        selectedIds = new
        {
            selection.Weapons,
            selection.Tools,
            selection.Enemies,
            selection.Heroes,
            selection.Estates,
            charters = profile.Runtime?.Charters ?? [],
            items = profile.Runtime?.Items ?? [],
            evolutions = profile.Runtime?.Evolutions ?? []
        },
        runtimeContractVersion = profile.Runtime?.ContractVersion,
        cardCatalog = ContentLoader.CardCatalog(data, profile, includeTest, catalog),
        effectCatalog = ContentLoader.EffectCatalog(data, profile, includeTest, catalog),
        acquisitionCatalog = catalog.Runtime?.Tuning,
        timelineSampleIntervalTicks = catalog.Tuning.World.TelemetryPeriodTicks,
        charterSlots = catalog.Runtime?.Tuning.CharterSlots ?? 0,
        designMetadataPolicy = catalog.Runtime is null ? "Candidate effects and design damage coefficients are not applied to unchanged S2 runtime tuning" : "Only explicit runtimeProjection executes; candidate prose and design damage coefficients are not executable rules",
        contentSha256 = contentHash,
        commit = gitHead ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unavailable",
        gitDirty = gitStatus is null ? (bool?)null : gitStatus.Length != 0,
        gitStatusAvailable = gitStatus is not null,
        sourceTreeSha256 = sourceHash,
        simulationAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ContentLoader).Assembly.Location))),
        coreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Simulation).Assembly.Location))),
        metaSnapshot = "neutral-no-meta",
        advertisementUses = 0,
        tuningStatus = "provisional-unbalanced",
        includeTest,
        shortenedSimulation = shortened,
        configuredDurationTicks,
        requestedDurationTicks = catalog.Tuning.DurationTicks,
        seasonDurationTicks = catalog.Tuning.World.Seasons.Select(season => season.DurationTicks).ToArray(),
        seasonNames = catalog.Tuning.World.Seasons.Select(season => season.Name).ToArray(),
        catalog.Tuning.TickRate,
        nominalDurationSeconds = (double)configuredDurationTicks / catalog.Tuning.TickRate,
        peopleRule,
        scenario,
        heroId,
        estateId,
        policy,
        seed
    };
    var metrics = new
    {
        schemaVersion = 1,
        model = catalog.Experiment is not null ? "headless-s4b-controlled-league" : catalog.Runtime is null ? "headless-gameplay" : "headless-s4-league",
        stage,
        scope,
        gameplayBalanceClaim = false,
        commit = metadata.commit,
        timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        runtime = Environment.Version.ToString(),
        seed,
        policy,
        iterations,
        ticks = results[0].Ticks,
        tickP95Ms = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1],
        tickSampleCount = samples.Count,
        stopwatchFrequency = Stopwatch.Frequency,
        requestedWarmupTicks = warmupTicks,
        actualWarmupTicks,
        warmupExcluded = true,
        tickTimingScope = "Simulation.Tick only; snapshots and PrepareLoadTick excluded; end-tick fixture restoration included",
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        damage = results[0].Damage,
        growth = results[0].Growth,
        hash = results[0].Hash,
        balanceDispersion = (double?)null,
        config = new
        {
            profileId = profile.Id,
            profileSha256 = profileHash,
            experimentContractVersion = profile.Experiment?.ContractVersion,
            movementMode = movement,
            tuningFile = profile.Experiment?.TuningFile,
            tuningSha256 = profile.Experiment is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data, profile.Experiment.TuningFile)))),
            experimentDefinition = catalog.Experiment,
            experienceCurve = catalog.Experiment?.Experience,
            mixedCategoryOrder = catalog.Experiment?.MixedCategoryOrder,
            worldUnit = catalog.Experiment is null ? null : "world-unit",
            threatTuning = catalog.Experiment is null ? null : catalog.Tuning.World.Threat,
            enemyTuning = catalog.Experiment is null ? null : catalog.Enemies.Values.OrderBy(enemy => enemy.Id, StringComparer.Ordinal).ToArray(),
            heroId,
            estateId,
            peopleRule,
            scenario,
            policy,
            seed,
            includeTest,
            shortenedSimulation = shortened,
            configuredDurationTicks,
            requestedDurationTicks = catalog.Tuning.DurationTicks,
            catalog.Tuning.TickRate,
            contentSha256 = contentHash,
            metaSnapshot = "neutral-no-meta",
            advertisementUses = 0
        },
        sourceMetadata = metadata,
        load = new
        {
            enabled = scenario == "load",
            peopleRepresentation = "Active simulation entities; a militia or returning squad is one entity with multiple population members",
            squadSize = catalog.Tuning.World.People.SquadSize,
            expected = Counts(new[] { loadExpected.Enemies, loadExpected.Farms, loadExpected.Buildings, loadExpected.People }),
            beforeMinimum = Counts(beforeMinimum),
            beforeMaximum = Counts(beforeMaximum),
            afterMinimum = Counts(afterMinimum),
            afterMaximum = Counts(afterMaximum),
            populationMembers = new { beforeMinimum = beforeMinimum[4], beforeMaximum = beforeMaximum[4], afterMinimum = afterMinimum[4], afterMaximum = afterMaximum[4] },
            verifiedSamples = scenario == "load" ? samples.Count : 0,
            allSamplesExact = scenario == "load"
        }
    };
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var artifacts = new JsonArray();
    foreach (var result in results)
    {
        var artifact = JsonSerializer.SerializeToNode(result, jsonOptions)!.AsObject();
        if (result.Experiment is not null)
        {
            var trace = result.Experiment.MovementTrace;
            var bytes = new byte[checked(trace.Length * sizeof(int))];
            for (var index = 0; index < trace.Length; index++) { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)), trace[index]); }
            var experiment = artifact["experiment"]!.AsObject();
            experiment.Remove("movementTrace");
            experiment["movementTraceEncoding"] = "int32le-xy-v1";
            experiment["movementTraceCount"] = trace.Length / 2;
            experiment["movementTraceBase64"] = Convert.ToBase64String(bytes);
        }
        artifact["artifactVersion"] = 2;
        artifact["scope"] = scope;
        artifact["runMetadata"] = JsonSerializer.SerializeToNode(metadata, jsonOptions);
        if (shortened && result.EndReason == "duration")
        {
            artifact["endReason"] = "fixture-duration";
            artifact["fullRunCompleted"] = false;
        }
        else { artifact["fullRunCompleted"] = !shortened && result.Ticks == configuredDurationTicks; }
        artifacts.Add(artifact);
    }
    var resultJson = artifacts.ToJsonString(jsonOptions);
    if (options.TryGetValue("--output", out var output)) { Write(output, resultJson); }
    if (options.TryGetValue("--metrics", out var metricsOutput)) { Write(metricsOutput, JsonSerializer.Serialize(metrics, jsonOptions)); }
    if (options.TryGetValue("--timings", out var timingsOutput)) { Write(timingsOutput, timingCsv!.ToString()); }
    Console.WriteLine(resultJson);

    int ParseInteger(string key, string fallback) => int.Parse(options.GetValueOrDefault(key, fallback), CultureInfo.InvariantCulture);

    void VerifyLoad(WorldSnapshot snapshot)
    {
        if (snapshot.ActiveEnemies != loadExpected.Enemies || snapshot.Farms != loadExpected.Farms || snapshot.Buildings != loadExpected.Buildings || snapshot.People != loadExpected.People)
        {
            throw new InvalidOperationException($"Load count drift at tick {snapshot.Tick}: {snapshot.ActiveEnemies}/{snapshot.Farms}/{snapshot.Buildings}/{snapshot.People}");
        }
    }

    static void Observe(WorldSnapshot snapshot, int[] minimum, int[] maximum)
    {
        var values = new[] { snapshot.ActiveEnemies, snapshot.Farms, snapshot.Buildings, snapshot.People, snapshot.PopulationMembers };
        for (var index = 0; index < values.Length; index++)
        {
            minimum[index] = Math.Min(minimum[index], values[index]);
            maximum[index] = Math.Max(maximum[index], values[index]);
        }
    }

    static object Counts(int[] values) => new { enemies = values[0], farms = values[1], buildings = values[2], people = values[3] };

    static string? Git(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments) { startInfo.ArgumentList.Add(argument); }
            using var process = Process.Start(startInfo);
            if (process is null) { return null; }
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    static string? SourceHash(string? root)
    {
        if (root is null || !Directory.Exists(Path.Combine(root, "core"))) { return null; }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var paths = Directory.EnumerateFiles(Path.Combine(root, "core"), "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" or ".props" or ".targets")
            .Concat(new[] { "global.json", "Directory.Build.props", "Directory.Build.targets" }.Select(file => Path.Combine(root, file)).Where(File.Exists))
            .Order(StringComparer.Ordinal);
        foreach (var file in paths)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + "\0"));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content.TrimEnd('\r', '\n') + Environment.NewLine);
    }
}
