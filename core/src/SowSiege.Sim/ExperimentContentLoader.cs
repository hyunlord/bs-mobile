using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record ExperimentProfileExtension(int ContractVersion, string TuningFile);
public sealed record EnemyOverride(string Id, int Speed, int Damage, int Health, int AttackCooldownTicks);
public sealed record ExperimentTuningFile(Tuning Tuning, EnemyOverride[] EnemyOverrides, ExperimentDefinition Experiment);
public static partial class ContentLoader
{
    private static ExperimentTuningFile? LoadExperiment(string directory, RuntimeProfile profile, Tuning baseline)
    {
        Require(profile.Gameplay is null || profile.Experiment is null && profile.TuningFile is null, "Production gameplay cannot also select experimental tuning.");
        if (profile.Gameplay is { } gameplay)
        {
            var production = new ExperimentTuningFile(baseline, gameplay.EnemyOverrides, gameplay.Experiment);
            ValidateExperiment(production, profile);
            return production;
        }
        if (profile.Experiment is null) { return null; }
        Require(profile.Experiment.ContractVersion == 1, "Unsupported experiment version.");
        var result = Read<ExperimentTuningFile>(ConfigurationPath(directory, profile.Experiment.TuningFile, @"\A(?:experiments/)?tuning-s4b-[a-zA-Z0-9_-]+\.json\z"));
        var original = JsonSerializer.SerializeToNode(baseline, JsonOptions)!;
        var candidate = JsonSerializer.SerializeToNode(result.Tuning, JsonOptions)!;
        foreach (var value in new[] { original, candidate })
        {
            value["world"]!["map"]!.AsObject().Remove("lordHealth");
            value["world"]!.AsObject().Remove("threat");
        }
        Require(JsonNode.DeepEquals(original, candidate), "Experiment tuning may change only lord health and global threat values.");
        ValidateExperiment(result, profile);
        return result;
    }

    private static void ValidateExperiment(ExperimentTuningFile result, RuntimeProfile profile)
    {
        Require(result.EnemyOverrides.Length is >= 1 and <= 1000, "Invalid enemy override count.");
        Require(result.EnemyOverrides.Select(enemy => enemy.Id).Distinct(StringComparer.Ordinal).Count() == result.EnemyOverrides.Length && result.EnemyOverrides.Select(enemy => enemy.Id).ToHashSet(StringComparer.Ordinal).SetEquals(profile.Selection.Enemies), "Experiment overrides must cover selected enemies exactly.");
        foreach (var enemy in result.EnemyOverrides) { Require(new[] { enemy.Speed, enemy.Damage, enemy.Health, enemy.AttackCooldownTicks }.All(value => value is > 0 and <= 1000000), "Invalid enemy override value."); }
        var curve = result.Experiment.Experience;
        Require(curve.Base is > 0 and <= 1000000 && curve.Linear is >= 0 and <= 1000000 && curve.Quadratic is > 0 and <= 1000000, "Invalid nonlinear experience curve.");
        var categories = result.Experiment.MixedCategoryOrder;
        Require(categories.Length == 4 && categories.ToHashSet(StringComparer.Ordinal).SetEquals(["weapon", "land", "building", "people"]), "Mixed category order must contain each family exactly once.");
        var movement = result.Experiment.Movement; var map = result.Tuning.World.Map;
        Require(movement.DecisionPeriodTicks > 0 && movement.DecisionPeriodTicks <= Math.Min(1000000, result.Tuning.DurationTicks) && movement.EvadeRange is > 0 and <= 1000000 && movement.EvadeStep is > 0 and <= 1000000, "Invalid movement tuning.");
        Require(movement.CircuitOffsets.Length is >= 2 and <= 10000 && movement.CircuitOffsets.All(point => Math.Abs((long)point.X) <= map.Width && Math.Abs((long)point.Y) <= map.Height), "Invalid circuit offset.");
        Require(movement.CircuitOffsets.Select(point => (Math.Clamp((map.Width >> 1) + point.X, 0, map.Width), Math.Clamp((map.Height >> 1) + point.Y, 0, map.Height))).Distinct().Count() >= 2, "Circuit must have distinct clamped destinations.");
    }

    private static string ConfigurationPath(string directory, string relative, string pattern)
    {
        Require(Regex.IsMatch(relative, pattern, RegexOptions.CultureInvariant), "Unsafe configuration filename.");
        var current = directory;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Configuration symlink is forbidden.");
        }
        return current;
    }
}
