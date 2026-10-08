using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class ContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ContentCatalog Load(string directory, bool includeTest = false, string profileName = "s2-baseline")
    {
        var profile = LoadProfile(directory, profileName);
        var baseline = Read<Tuning>(ConfigurationPath(directory, profile.TuningFile ?? "tuning.json", profile.FirstPlayable is null ? @"\A(?:tuning\.json|experiments/tuning-s2-baseline\.json)\z" : @"\Afirst-playable-tuning\.json\z"));
        var experiment = LoadExperiment(directory, profile, baseline);
        var tuning = experiment?.Tuning ?? baseline;
        var selection = profile.Select(includeTest);
        var allowS4 = profile.Runtime is not null;
        var roots = includeTest ? new[] { directory, Path.Combine(directory, "test") } : new[] { directory };
        var tools = LoadSelected<ToolContent, ToolDefinition>(roots, "tools", selection.Tools, tool => tool.ToCore(), allowS4);
        var heroes = LoadSelected<HeroContent, HeroDefinition>(roots, "heroes", selection.Heroes, hero => hero.ToCore(), allowS4);
        var estates = LoadSelected<EstateContent, EstateDefinition>(roots, "estates", selection.Estates, estate => estate.ToCore(), allowS4);
        var weapons = LoadSelected<WeaponContent, WeaponDefinition>(roots, "weapons", selection.Weapons, weapon => weapon.ToCore(), allowS4);
        var enemies = LoadSelected<EnemyContent, EnemyDefinition>(roots, "enemies", selection.Enemies, enemy => enemy.ToCore(), allowS4);
        if (experiment is not null)
        {
            foreach (var change in experiment.EnemyOverrides)
            {
                var enemy = enemies[change.Id]; enemies[change.Id] = enemy with { Speed = change.Speed, Damage = change.Damage, Health = change.Health, AttackCooldownTicks = change.AttackCooldownTicks };
            }
        }
        var allIds = tools.Keys.Concat(heroes.Keys).Concat(estates.Keys).Concat(weapons.Keys).Concat(enemies.Keys).ToArray();
        Require(allIds.All(value => NamespaceId().IsMatch(value)), "Invalid namespace ID.");
        Require(allIds.Distinct(StringComparer.Ordinal).Count() == allIds.Length, "Duplicate content ID across kinds.");
        foreach (var hero in heroes.Values)
        {
            Require(tools.ContainsKey(hero.StartingTool), $"Unknown starting tool: {hero.StartingTool}");
            Require(hero.DamageMultiplier > 0, $"Invalid hero multiplier: {hero.Id}");
        }
        foreach (var estate in estates.Values)
        {
            Require(estate.GrowthMultiplier > 0, $"Invalid estate multiplier: {estate.Id}");
            if (estate.RemainsLoop is { } loop) { Require(new[] { loop.Capacity, loop.LifetimeTicks, loop.AbsorptionRadius }.All(value => value > 0 && value <= 1000000), "Invalid remains loop."); }
        }
        foreach (var tool in tools.Values)
        {
            ValidateActivation(tool.Id, tool.Activation);
            Require(tool.Growth.Yield > 0 && tool.Growth.Target is "land" or "building" or "people", $"Invalid growth: {tool.Id}");
            Require(tool.Tags.Length > 0 && tool.Tags.All(tag => !string.IsNullOrWhiteSpace(tag)) && !string.IsNullOrWhiteSpace(tool.FloorRationale), $"Tool description missing: {tool.Id}");
            foreach (var reference in tool.AntiSynergy) { Require(tools.ContainsKey(reference) || profile.FirstPlayable is not null && CanonicalToolExists(directory, reference), $"Unknown anti-synergy: {reference}"); }
        }
        foreach (var weapon in weapons.Values) { ValidateActivation(weapon.Id, weapon.Activation); }
        foreach (var enemy in enemies.Values)
        {
            Require(enemy.Target is "lord" or "seed" or "ripe" or "building" || profile.FirstPlayable is not null && enemy.Target == "people", $"Invalid enemy target: {enemy.Id}");
            Require(new[] { enemy.Health, enemy.Speed, enemy.Damage, enemy.Range, enemy.AttackCooldownTicks, enemy.Experience }.All(value => value > 0), $"Invalid enemy values: {enemy.Id}");
        }
        Require(tuning.TickRate == 30 && tuning.DurationTicks > 0 && tuning.DamageRollMax > 0, "Invalid simulation clock or damage roll.");
        Require(heroes.ContainsKey(tuning.DefaultHero) && estates.ContainsKey(tuning.DefaultEstate), "Unknown default hero or estate.");
        ValidateWorld(tuning, weapons);
        var runtime = profile.Runtime is null ? null : LoadRuntime(roots, profile.Runtime, selection, tools, weapons, profile.FirstPlayable, profile.RuntimeOverrides);
        Require(profile.FirstPlayable is not null || profile.RuntimeOverrides is null, "Runtime overrides require first playable.");
        ValidateFirstPlayable(profile, tuning, weapons, tools, enemies, runtime);
        return new(tuning, tools, heroes, estates, weapons, enemies, runtime, experiment?.Experiment, LoadWeaponCombat(directory, profile, selection.Weapons), profile.FirstPlayable);
    }

    public static RuntimeProfile LoadProfile(string directory, string profileName = "s2-baseline")
    {
        var profile = Read<RuntimeProfile>(ProfilePath(directory, profileName));
        Require(NamespaceId().IsMatch(profile.Id), "Invalid profile ID.");
        Require(new[] { profile.Selection.Tools, profile.Selection.Weapons, profile.Selection.Enemies,
            profile.Selection.Heroes, profile.Selection.Estates }.All(ids => ids.Length > 0), "Runtime profile must select every required kind.");
        return profile;
    }

    public static string ProfileHash(string directory, string profileName = "s2-baseline") =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ProfilePath(directory, profileName))));

    private static string ProfilePath(string directory, string profileName)
    {
        Require(ProfileName().IsMatch(profileName), "Profile name must be a safe filename stem.");
        return Path.Combine(directory, "profiles", profileName + ".json");
    }

    public static string Hash(string directory, bool includeTest)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!includeTest && relative.StartsWith("test/", StringComparison.Ordinal)) { continue; }
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ValidateWorld(Tuning tuning, IReadOnlyDictionary<string, WeaponDefinition> weapons)
    {
        var world = tuning.World;
        Require(world.DefaultPeopleRule is "A" or "B" or "C", "People rule must be A, B, or C.");
        Require(world.Seasons.Length == 4 && world.Seasons.Sum(season => (long)season.DurationTicks) == tuning.DurationTicks, "Four season durations must sum to the full configured duration.");
        Require(world.Seasons.All(season => season.DurationTicks > 0 && season.GrowthMultiplier > 0 && season.SpawnMultiplier > 0), "Invalid season values.");
        Require(world.Seasons.Select(season => season.Name).Distinct(StringComparer.Ordinal).Count() == world.Seasons.Length, "Duplicate season names.");
        Require(world.Farms.StageTicks.Length == 4 && world.Farms.StageTicks.All(value => value > 0), "Four positive crop stage durations required.");
        Require(world.Progression.CardCount == 3 && weapons.ContainsKey(world.Progression.StartingWeapon), "Three cards and a valid starting weapon required.");
        Require(world.Progression.Rarities.Length > 0 && world.Progression.Rarities.All(rarity => rarity.Weight > 0 && rarity.UpgradeAmount > 0), "Invalid rarity weights or upgrade amounts.");
        Require(world.Progression.Rarities.Select(rarity => rarity.Name).Distinct(StringComparer.Ordinal).Count() == world.Progression.Rarities.Length, "Duplicate rarity names.");
        Require(world.People.InitialFood <= world.People.FoodCapacity && world.People.InitialPeasants < world.People.MaxPeople, "Initial people or food exceed capacity.");
        Require(world.People.SquadSize <= world.People.MaxPeople, "Squad size exceeds people capacity.");
        Require(world.Load.Enemies <= world.Threat.EnemyCap && world.Load.Farms <= world.Farms.Capacity && world.Load.Buildings <= world.Buildings.SiteCount && world.Load.People <= world.People.MaxPeople, "Load fixture exceeds world capacity.");
        Require(world.Threat.SpawnInset * 2L < Math.Min(world.Map.Width, world.Map.Height) && world.Map.CellSize <= Math.Min(world.Map.Width, world.Map.Height), "Map bounds are invalid for spawn inset or spatial cell.");
        var policyNames = new[] { "weapon", "land", "building", "people", "mixed", "random" };
        Require(policyNames.Order(StringComparer.Ordinal).SequenceEqual(tuning.Policies.Keys.Order(StringComparer.Ordinal)), "Six named policies required.");
        foreach (var policy in tuning.Policies.Values)
        {
            Require(new[] { "weapon", "land", "building", "people" }.Order(StringComparer.Ordinal).SequenceEqual(policy.CardWeights.Keys.Order(StringComparer.Ordinal)) && policy.CardWeights.Values.All(weight => weight > 0), "Policy requires four positive card weights.");
        }
    }

    private static void ValidateActivation(string id, Activation activation)
    {
        Require(activation.Damage > 0 && activation.Range > 0 && activation.CooldownTicks > 0 && activation.Knockback >= 0, $"Invalid activation values: {id}");
        Require(activation.Shape is "melee" or "projectile" or "orbit" or "wave", $"Invalid activation shape: {id}");
    }

    private static Dictionary<string, TDefinition> LoadSelected<TContent, TDefinition>(IEnumerable<string> roots,
        string kind, string[] selectedIds, Func<TContent, TDefinition> project, bool allowS4 = false) where TContent : ContentRecord
    {
        Require(selectedIds.All(id => NamespaceId().IsMatch(id)), $"Invalid selected namespace ID in {kind}.");
        Require(selectedIds.Distinct(StringComparer.Ordinal).Count() == selectedIds.Length, $"Duplicate selection in {kind}.");
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in roots.Select(root => Path.Combine(root, kind)).Where(Directory.Exists)
                     .SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)).Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var id = document.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException($"Missing ID: {file}");
            Require(index.TryAdd(id, file), $"Duplicate {kind} ID: {id}");
        }
        foreach (var id in selectedIds) { Require(index.ContainsKey(id), $"Profile references missing {kind} ID: {id}"); }
        var result = new Dictionary<string, TDefinition>(StringComparer.Ordinal);
        foreach (var id in selectedIds.OrderBy(id => index[id], StringComparer.Ordinal))
        {
            var record = Read<TContent>(index[id]);
            Require(record.DesignStatus == "s2-runtime" || (allowS4 && record.DesignStatus == "s4-runtime"), $"Profile cannot activate unimplemented candidate: {record.Id}");
            result.Add(record.Id, project(record));
        }
        return result;
    }

    private static T Read<T>(string path)
    {
        Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, $"Content symlink is not allowed: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        UniqueProperties(document.RootElement);
        ValidateRequiredShape(document.RootElement, typeof(T), path);
        var result = document.RootElement.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException($"Null content: {path}");
        if (result is WeaponContent weapon) { ValidateWeaponSource(weapon); }
        return result;
    }

    private static void ValidateRequiredShape(JsonElement element, Type type, string location)
    {
        Require(element.ValueKind != JsonValueKind.Null, $"Null content at {location}.");
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(int))
        {
            var zeroAllowed = new[] { ".initialPeasants", ".initialFood", ".knockback", ".rerolls", ".bans", ".locks", ".radius", ".durationTicks", ".foodCost", ".minimum", ".linear", ".pierce", ".beamHalfWidth", ".speed", ".chainRange", ".firstSpawnTick", ".repeatTicks", ".weight", ".rewardCount", ".healAmount", ".experience", ".health", ".spreadPermille", ".burstIntervalTicks" };
            var minimum = (location.EndsWith(".amount", StringComparison.Ordinal) || location.EndsWith(".x", StringComparison.Ordinal) || location.EndsWith(".y", StringComparison.Ordinal)) ? -1000000 : zeroAllowed.Any(suffix => location.EndsWith(suffix, StringComparison.Ordinal)) ? 0 : 1;
            Require(element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number >= minimum && number <= 1000000, $"Integer out of content bounds at {location}.");
            return;
        }
        if (type == typeof(string)) { Require(element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()), $"Empty content string at {location}."); return; }
        if (type == typeof(decimal))
        {
            Require(element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number) && number > 0 && number <= 10,
                $"Invalid design coefficient at {location}.");
            return;
        }
        if (type.IsArray)
        {
            Require(element.ValueKind == JsonValueKind.Array, $"Expected array at {location}.");
            foreach (var item in element.EnumerateArray()) { ValidateRequiredShape(item, type.GetElementType()!, location + "[]"); }
            return;
        }
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        {
            Require(element.ValueKind == JsonValueKind.Object, $"Expected dictionary at {location}.");
            foreach (var property in element.EnumerateObject()) { ValidateRequiredShape(property.Value, type.GenericTypeArguments[1], location + "." + property.Name); }
            return;
        }
        Require(element.ValueKind == JsonValueKind.Object, $"Expected object at {location}.");
        foreach (var property in type.GetProperties())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!element.TryGetProperty(name, out var member))
            {
                if (name is "runtime" or "runtimeProjection" || name == "remainsLoop" && type == typeof(EstateContent)
                    || name is "experiment" or "weaponCombat" or "tuningFile" or "gameplay" or "firstPlayable" or "runtimeOverrides" && type == typeof(RuntimeProfile)
                    || name == "evolutionGrowthRequirements" && type == typeof(FirstPlayableDefinition)
                    || name == "growth" && type == typeof(WeaponContent)
                    || name is "damage" or "range" or "cooldownTicks" or "knockback" && type == typeof(WeaponActivationContent)
                    || name == "definitionsFile" && type == typeof(WeaponCombatProfileExtension))
                {
                    continue;
                }

                throw new InvalidDataException($"Missing required property {location}.{name}.");
            }
            Require(!(type == typeof(EstateContent) && name == "remainsLoop" && member.ValueKind == JsonValueKind.Null), "Remains loop must be omitted or a complete object.");
            Require(!(type == typeof(RuntimeProfile) && name is "experiment" or "weaponCombat" or "tuningFile" or "gameplay" or "firstPlayable" or "runtimeOverrides" && member.ValueKind == JsonValueKind.Null), "Profile extensions must be omitted or complete.");
            Require(!(type == typeof(WeaponContent) && name == "growth" && member.ValueKind == JsonValueKind.Null), "Weapon growth must be omitted or complete.");
            Require(!(type == typeof(WeaponActivationContent) && member.ValueKind == JsonValueKind.Null), "Activation members cannot be null.");
            Require(!(type == typeof(WeaponCombatProfileExtension) && member.ValueKind == JsonValueKind.Null), "Weapon combat members cannot be null.");
            if (member.ValueKind == JsonValueKind.Null && new System.Reflection.NullabilityInfoContext().Create(property).ReadState == System.Reflection.NullabilityState.Nullable)
            {
                continue;
            }

            ValidateRequiredShape(member, property.PropertyType, location + "." + name);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceId();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileName();
}
