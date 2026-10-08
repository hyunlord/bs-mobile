using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record DiagnosticRequest(string Variant, string Output, string? RawOutput)
{
    public DiagnosticOptions Options => new(Variant switch
    {
        "control" => DiagnosticVariant.Control,
        "offense-off" => DiagnosticVariant.OffenseOff,
        "interception-off" => DiagnosticVariant.InterceptionOff,
        "both-off" => DiagnosticVariant.BothOff,
        _ => throw new ArgumentException("Unknown diagnostic variant.")
    });

    public static DiagnosticRequest? Parse(IReadOnlyDictionary<string, string> arguments, int iterations)
    {
        var present = new[] { "--diagnostic-variant", "--diagnostic-output", "--diagnostic-raw-output" }.Any(arguments.ContainsKey);
        if (!present) { return null; }
        if (!arguments.TryGetValue("--diagnostic-variant", out var variant) || !arguments.TryGetValue("--diagnostic-output", out var output)
            || string.IsNullOrWhiteSpace(output) || iterations != 3 || new[] { "--output", "--metrics", "--timings" }.Any(arguments.ContainsKey))
        {
            throw new ArgumentException("Diagnostics require variant, diagnostic-output and exactly three iterations; use diagnostic-raw-output for selected raw cases.");
        }
        arguments.TryGetValue("--diagnostic-raw-output", out var raw);
        if (raw is not null && (string.IsNullOrWhiteSpace(raw) || Path.GetFullPath(raw) == Path.GetFullPath(output))) { throw new ArgumentException("Diagnostic raw output must be a distinct nonempty path."); }
        var request = new DiagnosticRequest(variant, output, raw);
        _ = request.Options;
        return request;
    }
}

public static class DiagnosticOutput
{
    private static readonly JsonSerializerOptions Json = HostJson.CreateOptions(camelCase: true, indented: true);

    public static string Digest(object value) => new CanonicalStateHasher().Compute(JsonSerializer.SerializeToElement(value, Json));

    public static JsonObject Create(DiagnosticRequest request, string profileName, string? movement, int requestedTicks,
        object metadata, CoreAssemblyMetadata assembly, IReadOnlyList<SimulationResult> results, IReadOnlyList<DiagnosticResult> diagnostics)
    {
        if (results.Count != 3 || diagnostics.Count != 3) { throw new InvalidOperationException("Diagnostics require three complete repeats."); }
        var proofs = results.Select((result, index) => new
        {
            repeatIndex = index,
            gameplayHash = result.Hash,
            gameplayDigest = Digest(result),
            diagnosticDigest = Digest(diagnostics[index])
        }).ToArray();
        if (proofs.Any(proof => proof.gameplayHash != proofs[0].gameplayHash || proof.gameplayDigest != proofs[0].gameplayDigest || proof.diagnosticDigest != proofs[0].diagnosticDigest))
        {
            throw new InvalidOperationException("Diagnostic repeat mismatch in gameplay or deterministic observation.");
        }
        var first = results[0];
        var cardsDigest = Digest(first.Cards);
        var identity = new { caseId = $"{profileName}-{first.Policy}-{first.PeopleRule}-{first.Seed}-{request.Variant}", variant = request.Variant, first.Seed, first.Policy, first.PeopleRule, movement, requestedTicks };
        var source = JsonSerializer.SerializeToNode(metadata, Json)!.AsObject();
        source["coreAssembly"] = JsonSerializer.SerializeToNode(assembly, Json);
        var inputHash = Digest(new { caseIdentity = identity, content = source["contentSha256"], profile = source["profileSha256"], tuning = source["tuningSha256"] });
        source["inputHash"] = inputHash;
        var compact = JsonSerializer.SerializeToNode(diagnostics[0], Json)!.AsObject();
        var trace = diagnostics[0].RngTrace;
        if (trace.Count != first.Ticks + 1 || trace.Where((point, index) => point.Tick != index || point.Draws < 0 || (index > 0 && point.Draws < trace[index - 1].Draws)).Any())
        {
            throw new InvalidOperationException("Diagnostic RNG trace must contain every tick and monotonic draws.");
        }
        var bytes = new byte[checked(trace.Count * sizeof(ulong))];
        for (var index = 0; index < trace.Count; index++) { BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(index * sizeof(ulong), sizeof(ulong)), checked((ulong)trace[index].Draws)); }
        compact["rngTrace"] = Convert.ToBase64String(bytes);
        compact["rngTraceEncoding"] = "uint64le-base64-v1";
        compact["rngTraceCount"] = trace.Count;
        return JsonSerializer.SerializeToNode(new
        {
            contractVersion = 1,
            caseIdentity = identity,
            provenance = source,
            gameplayHash = first.Hash,
            diagnosticIdentity = Digest(new { contractVersion = 1, request.Variant, inputHash, gameplayHash = first.Hash, proofs[0].diagnosticDigest, cardsDigest }),
            proofs[0].gameplayDigest,
            proofs[0].diagnosticDigest,
            cardsDigest,
            cards = first.Cards,
            gameplayDigestRepresentation = "complete-native-SimulationResult-camelCase-no-runMetadata",
            outcome = new
            {
                first.Ticks,
                first.Survived,
                first.DeathCause,
                first.EndReason,
                first.Level,
                first.WeaponDamage,
                toolActivationDamage = first.PerTool.Values.Sum(tool => tool.ActivationDamage),
                toolGrowthDamage = first.PerTool.Values.Sum(tool => tool.GrowthDamage),
                first.AllyDamage,
                first.KillExperience,
                first.HarvestExperience,
                first.TaxExperience,
                first.Food,
                first.Harvests,
                first.RandomDraws
            },
            repeatVerification = new { identical = true, repeats = proofs },
            diagnostics = compact
        }, Json)!.AsObject();
    }
}
