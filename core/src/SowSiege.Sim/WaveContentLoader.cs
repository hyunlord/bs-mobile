using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record WaveDesignReference(string Catalog, string Revision, string Id);
public sealed record WaveDesignBinding(string Id, string Kind, WaveDesignReference DesignRef, string SemanticSha256);
public sealed record WaveParameterProvenance(string Path, string Classification, string Source, string Note);
public sealed record WaveContentFile(int ContractVersion, string DesignRevision, WaveDesignBinding[] Bindings,
    Tuning Tuning, Dictionary<string, EnemyDefinition> EnemyStats, WaveRuntimeDefinition Definition, WaveParameterProvenance[] Provenance);

public static partial class ContentLoader
{
    private const string WaveRevision = "designed-v1.1";
    private static readonly Dictionary<string, (string Kind, string Signature)> WaveSupportedDesign = new(StringComparer.Ordinal)
    {
        { "core:iron_blade", ("weapon", "8c0209e906e43e97d6d5667ad07b62af02657971d3406e585383886a9651612a") },
        { "core:ward_orbit", ("weapon", "c0c1eecf560b7f604de9017a91c447eb1d23a42858e12826db5b9925642f53a8") },
        { "core:storm_fork", ("weapon", "b680d1649fa4d9b9117194380980f985ed9ca71b244ca5ee856cb8600e403818") },
        { "core:ember_wand", ("weapon", "a80f061f339435203251913d5a10f845837185ecf4007cb21226df7fc5c94f96") },
        { "core:harvest_scythe", ("weapon", "ba5b393c8c3522d7a06fc5e590cf87641ee8c97522fac4a91663bf8f03c78379") },
        { "core:seed_bag", ("tool", "27eaff8f097c5418187d40f943c5294844e6ad0066a15417ffd760b3bc3de444") },
        { "core:rain_ladle", ("tool", "b16be61bb7d35c8605f0e68813ab0b1532046a044571cbcc561c425861ed590a") },
        { "core:carpenter_hammer", ("tool", "0c64e01205f15d84bfa12aa3a29866c6729377dd705d5e3180fdbbf9c0d1a9d0") },
        { "core:muster_horn", ("tool", "598c64f4e07c88934585cd2eb4a7a53f0d36ad8c979acc7da14212672abce754") },
        { "core:sowing_sworddance", ("evolution", "86f0e82a5734bb86295475a15c772a83c158fd4b743ecf939b41df38b2aea6cf") },
        { "core:warded_masonry", ("evolution", "43cff35a4525dbd31e23cac074bd51c3475530f5c111489e62b822135f3fb046") },
        { "core:sheltered_sowing", ("evolution", "529223ac02c79f10bf58e48643ecded397933a884b3f9428f76c282535fe2358") },
        { "core:bitter_seed_dust", ("item", "c1a980c7bca64773382d2ad06620465c374de1718c4d4d9233ba540bffb089b1") },
        { "core:clay_water_bead", ("item", "5ff075665aedb34409b13a32aa462b1d5f644daf87016e5247e817d5d19555a8") },
        { "core:crop_guard_signet", ("item", "f99954d0541fc9e6f25caafe7f34d09f71671450a6ca2710051ca59c9167fe85") },
        { "core:joiner_square", ("item", "8f7b9b322bc08fb2234bab0abc9b751284c63d8b9d2c542c3e867131a7fdaa10") },
        { "core:meadow_buckle", ("item", "b9b8028a3fd389a634854e1383a6a82064979d6e83eb058359861db197d43aea") },
        { "core:levy_bread_wrap", ("item", "b991b0fcca0164f76cbd401a2a60770acb6a3e789b26e3000040a10c54cf37a6") },
        { "core:gathering_loop", ("item", "9fd69e6c9837fe1b1442cf97c821137e26eda4e5d8596a36424be41a276833cd") },
        { "core:wayfarer_boots", ("item", "0425043d1ab30a177d1af22cadfc8fb0f61f4d6a2b59430fde9223ee025888b4") },
        { "core:raider", ("enemy", "f59e4a69d0992bbe90029ae23d1860de13e2985f5a21be72912dea4fb393d842") },
        { "core:seed_mite", ("enemy", "94b127632c50293bbc9d49f181f29d197b397800e21a6aae157cfc6fa362d56d") },
        { "core:crop_grazer", ("enemy", "2c622633d4115f071d44ed347a3fe088cca5b72a3ba372356a47651318529c4e") },
        { "core:ram_runner", ("enemy", "7e389eb7005fe3f670f40e6d3c235243cabc9451c7fbefca40e4120b6bc8cd31") },
        { "core:shield_raider", ("enemy", "289bce79527a499e9095280defd291c2c97f23b93446a34b82fe62fc4e0c90b6") },
        { "core:wine_wasp", ("enemy", "b8c5ac8e970b642569ab6cf1da4010cb42757b1ed209f688c6b41136e54f64be") },
        { "core:flood_tusk", ("enemy", "2c1aedd230e339bdedf0c0a5e6813146bbb27ba13b836f758e2f574e9ea1dda2") },
        { "meta:chapter_1", ("chapter", "ac2f24516a0445da4c352e06118e3553f53bfcb139249045b48118d6181c3859") },
    };
    private static readonly JsonSerializerOptions WaveJsonOptions = new(JsonOptions)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static ContentCatalog LoadWaveRuntime(string directory, RuntimeProfile profile, bool includeTest)
    {
        Require(!includeTest, "Wave test extension is not defined; use explicit fixtures without altering production selection.");
        Require(profile.Runtime is null && profile.FirstPlayable is null && profile.RuntimeOverrides is null && profile.Experiment is null
            && profile.Gameplay is null && profile.WeaponCombat is null && profile.TuningFile is null, "Wave runtime cannot mix historical profile extensions.");
        Require(profile.TestSelection.Tools.Concat(profile.TestSelection.Weapons).Concat(profile.TestSelection.Enemies).Concat(profile.TestSelection.Heroes).Concat(profile.TestSelection.Estates).Count() == 0, "Wave test selection must be empty.");
        var file = ReadWaveFile(directory, profile.WaveRuntimeFile!);
        var wave = file.Definition;
        Require(file.ContractVersion == 1 && file.DesignRevision == WaveRevision && wave.Revision == WaveRevision, "Unsupported wave design revision.");
        ValidateWaveBindings(directory, file);
        Require(wave.Gear.Count == 9 && wave.Items.Count == 8 && wave.Evolutions.Count == 3 && wave.Enemies.Count == 7, "Wave runtime exact roster mismatch.");
        Require(profile.Selection.Weapons.Length == 5 && profile.Selection.Tools.Length == 4 && profile.Selection.Enemies.Length == 7
            && profile.Selection.Heroes.Length == 1 && profile.Selection.Estates.Length == 1, "Wave profile scope mismatch.");
        Require(SameIds(profile.Selection.Weapons, file.Bindings.Where(b => b.Kind == "weapon").Select(b => b.Id))
            && SameIds(profile.Selection.Tools, file.Bindings.Where(b => b.Kind == "tool").Select(b => b.Id))
            && SameIds(profile.Selection.Enemies, wave.Enemies.Keys) && SameIds(file.EnemyStats.Keys, wave.Enemies.Keys), "Wave profile selection differs from runtime definitions.");
        ValidateWaveDefinition(file);
        Require(new CanonicalStateHasher().Compute(file.Tuning) == new CanonicalStateHasher().Compute(Read<Tuning>(Path.Combine(directory, "first-playable-tuning.json"))), "Inherited wave tuning differs from declared source; balance remains held.");
        var programs = WavePrimitiveSupport.Resolve(wave);
        var tools = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
        var weapons = new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
        using var design = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "system-design-v1.json")));
        var designs = design.RootElement.GetProperty("content").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        foreach (var entry in wave.Gear)
        {
            var gear = entry.Value;
            var tags = designs[gear.Id].GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();
            var activation = new Activation(gear.Damage, gear.Range, gear.CooldownTicks, programs[gear.Id].Is("unit:attack-shape", "shape", "homing-projectile") ? "projectile" : programs[gear.Id].Is("unit:attack-shape", "shape", "orbit") ? "orbit" : "melee", gear.Knockback);
            if (profile.Selection.Weapons.Contains(gear.Id, StringComparer.Ordinal)) { weapons.Add(gear.Id, new(gear.Id, tags, activation)); }
            else
            {
                var target = programs[gear.Id].Value("unit:remnant-create", "target", "land");
                tools.Add(gear.Id, new(gear.Id, tags, activation, new(target, 1), designs[gear.Id].GetProperty("cardText").GetString()!, []));
            }
        }
        var heroes = LoadSelected<HeroContent, HeroDefinition>([directory], "heroes", profile.Selection.Heroes, hero => hero.ToCore(), true);
        var estates = LoadSelected<EstateContent, EstateDefinition>([directory], "estates", profile.Selection.Estates, estate => estate.ToCore(), true);
        Require(heroes.Values.All(h => tools.ContainsKey(h.StartingTool)) && heroes.ContainsKey(file.Tuning.DefaultHero) && estates.ContainsKey(file.Tuning.DefaultEstate), "Wave infrastructure references unavailable content.");
        ValidateWorld(file.Tuning, weapons);
        return new(file.Tuning, tools, heroes, estates, weapons, file.EnemyStats, WaveRuntime: wave);
    }

    public static WaveContentFile ReadWaveFile(string directory, string relative = "runtime/wave-1a.json")
    {
        Require(relative == "runtime/wave-1a.json", "Unsafe or unsupported wave runtime path.");
        var folder = Path.Combine(directory, "runtime");
        var path = Path.Combine(directory, relative);
        Require((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0 && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Wave runtime symlink forbidden.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        UniqueProperties(document.RootElement);
        ValidateWaveShape(document.RootElement, typeof(WaveContentFile), "wave");
        return document.RootElement.Deserialize<WaveContentFile>(WaveJsonOptions) ?? throw new InvalidDataException("Missing wave runtime file.");
    }

    private static void ValidateWaveShape(JsonElement node, Type type, string location)
    {
        if (type.IsEnum) { Require(node.ValueKind == JsonValueKind.String && Enum.GetNames(type).Contains(node.GetString(), StringComparer.Ordinal), "Unsupported wave enum at " + location); return; }
        if (type == typeof(int)) { Require(node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var number) && number >= 0 && number <= 1000000, "Wave numeric bounds at " + location); return; }
        if (type == typeof(string)) { Require(node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString()), "Missing wave string at " + location); return; }
        if (type.IsArray)
        {
            Require(node.ValueKind == JsonValueKind.Array, "Expected wave array at " + location);
            foreach (var item in node.EnumerateArray()) { ValidateWaveShape(item, type.GetElementType()!, location + "[]"); }
            return;
        }
        Require(node.ValueKind == JsonValueKind.Object, "Expected wave object at " + location);
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        {
            foreach (var property in node.EnumerateObject()) { ValidateWaveShape(property.Value, type.GenericTypeArguments[1], location + "." + property.Name); }
            return;
        }
        foreach (var property in type.GetProperties())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!node.TryGetProperty(name, out var member) && property.IsDefined(typeof(OmitWhenNullAttribute), false)) { continue; }
            Require(node.TryGetProperty(name, out member), "Missing wave property " + location + "." + name);
            ValidateWaveShape(member, property.PropertyType, location + "." + name);
        }
    }

    private static bool SameIds(IEnumerable<string> a, IEnumerable<string> b) => a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));

    private static void ValidateWaveBindings(string directory, WaveContentFile file)
    {
        using var design = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "system-design-v1.json")));
        UniqueProperties(design.RootElement);
        Require(design.RootElement.GetProperty("revision").GetString() == WaveRevision && design.RootElement.GetProperty("approvalStatus").GetString() == "approved", "Wave requires approved design revision.");
        var records = design.RootElement.GetProperty("content").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        Require(SameIds(file.Bindings.Select(b => b.Id), WaveSupportedDesign.Keys), "Wave design binding scope differs from reviewed 28 records.");
        foreach (var binding in file.Bindings)
        {
            var reference = binding.DesignRef;
            Require(reference.Catalog == "data/system-design-v1.json" && reference.Revision == WaveRevision && reference.Id == binding.Id, "Invalid wave designRef: " + binding.Id);
            Require(records.TryGetValue(binding.Id, out var record) && record.GetProperty("kind").GetString() == binding.Kind && WaveSupportedDesign[binding.Id].Kind == binding.Kind, "Wave designRef kind mismatch: " + binding.Id);
            var signature = WaveSemanticSignature(record);
            Require(binding.SemanticSha256 == signature && signature == WaveSupportedDesign[binding.Id].Signature, "Unsupported primitive/parameter/bespoke mapping: " + binding.Id);
        }
        foreach (var item in file.Definition.Items.Values)
        {
            var record = records[item.Id];
            var linked = record.GetProperty("linkedWeaponIds").EnumerateArray().Concat(record.GetProperty("linkedToolIds").EnumerateArray())
                .Select(v => v.GetString()!).Where(file.Definition.Gear.ContainsKey);
            Require(SameIds(item.EquipmentIds, linked), "Item eligibility differs from approved linked equipment.");
        }
        var targets = file.Definition.MaterialTargets;
        Require(targets is not null && SameIds(targets.Keys, new[] { "meta:grain", "meta:timber", "meta:charter" }), "Wave material targets must match available early tools.");
        foreach (var target in targets!)
        {
            Require(file.Definition.Gear.ContainsKey(target.Value) && records[target.Key].GetProperty("guarantee").GetProperty("earlyToolId").GetString() == target.Value,
                "Material guarantee uses an unsupported early tool.");
        }
        var declaredWave = design.RootElement.GetProperty("implementationWaves").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "wave-1a");
        Require(SameIds(declaredWave.GetProperty("contentIds").EnumerateArray().Select(v => v.GetString()!), file.Bindings.Select(b => b.Id)), "Approved wave selection differs from implementation scope.");
    }

    private static string WaveSemanticSignature(JsonElement record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var key in new[] { "bespoke", "params", "primitives" }) { writer.WritePropertyName(key); WriteStable(writer, record.GetProperty(key)); }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteStable(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); WriteStable(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) { WriteStable(writer, item); } writer.WriteEndArray(); }
        else { value.WriteTo(writer); }
    }

    private static void ValidateWaveDefinition(WaveContentFile file)
    {
        var wave = file.Definition;
        string Reference(string id) => "data/system-design-v1.json@" + WaveRevision + "#" + id;
        Require(wave.ChapterId == "meta:chapter_1" && wave.ChapterDesignRef == Reference(wave.ChapterId) && wave.BossId == "core:flood_tusk", "Unsupported wave chapter or boss.");
        Require(wave.BossSpawnTick > 0 && wave.BossSpawnTick < file.Tuning.DurationTicks && wave.GroupCap is > 0 and <= 16
            && wave.InitialWorkers >= wave.GroupCap && wave.InitialTimber > 0 && wave.WaterCapacity > 0 && wave.WaterRefillTicks > 0
            && wave.Behavior is not null && wave.Behavior.WetSpeedDivisor is > 0 and <= 16 && wave.Behavior.DryDamageDivisor is > 0 and <= 16 && wave.Behavior.RangedRangeMultiplier is > 0 and <= 16 && wave.Behavior.PathConnectionMultiplier is > 0 and <= 16
            && wave.DryAfterTicks > 0 && wave.DryAfterTicks < wave.WaterRefillTicks && wave.PickupRadius > 0 && wave.WetTicks > 0 && wave.StopTicks > 0 && wave.PathSpacing > 0 && wave.PathCapacity is > 0 and <= 1024, "Invalid bounded wave work configuration.");
        var kindIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["core:iron_blade"] = "Arc",
            ["core:ward_orbit"] = "Orbit",
            ["core:storm_fork"] = "Chain",
            ["core:ember_wand"] = "Homing",
            ["core:harvest_scythe"] = "HarvestArc",
            ["core:seed_bag"] = "SeedFan",
            ["core:rain_ladle"] = "WaterFan",
            ["core:carpenter_hammer"] = "ConstructionSlam",
            ["core:muster_horn"] = "MusterWave",
            ["core:bitter_seed_dust"] = "SeedDetour",
            ["core:clay_water_bead"] = "CarryWater",
            ["core:crop_guard_signet"] = "HarvestGuard",
            ["core:joiner_square"] = "FrontOrbit",
            ["core:meadow_buckle"] = "RaiderAim",
            ["core:levy_bread_wrap"] = "FieldMeal",
            ["core:gathering_loop"] = "PickupRadius",
            ["core:wayfarer_boots"] = "MoveSpeed",
            ["core:sowing_sworddance"] = "PlantingArc",
            ["core:warded_masonry"] = "RepairOrbit",
            ["core:sheltered_sowing"] = "ShelteredPlot",
            ["core:raider"] = "Pursuer",
            ["core:seed_mite"] = "SeedThief",
            ["core:crop_grazer"] = "RipeGrazer",
            ["core:ram_runner"] = "Charger",
            ["core:shield_raider"] = "Shield",
            ["core:wine_wasp"] = "Ranged",
            ["core:flood_tusk"] = "FloodBoss"
        };
        void Identity(string key, string id, string reference, string kind)
        {
            Require(key == id && reference == Reference(id) && kindIds.TryGetValue(id, out var expected) && expected == kind, "Unsupported wave handler mapping: " + key);
        }
        foreach (var pair in wave.Gear)
        {
            var gear = pair.Value; Identity(pair.Key, gear.Id, gear.DesignRef, gear.Kind.ToString());
            Require(gear.Levels is { Length: 12 }, "Wave gear requires twelve explicit growth levels.");
            foreach (var level in gear.Levels!)
            {
                Require(level.Damage > 0 && level.Range > 0 && level.CooldownTicks > 0 && level.Count is > 0 and <= 16 && level.Speed > 0 && level.LifetimeTicks > 0, "Invalid wave growth level.");
            }
            var first = gear.Levels![0];
            Require(first == new WaveGearLevel(gear.Damage, gear.Range, gear.CooldownTicks, gear.Speed, gear.Count, gear.LifetimeTicks, gear.Knockback), "Base stats must equal growth level one.");
            Require(gear.Levels!.Select(l => l.Range).Distinct().Count() > 1 && gear.Levels.Select(l => l.CooldownTicks).Distinct().Count() > 1, "Wave growth must vary range and cadence, not only damage.");
            Require(gear.Damage > 0 && gear.Range > 0 && gear.CooldownTicks > 0 && gear.Count is > 0 and <= 16 && gear.LifetimeTicks > 0
                && gear.WorkTicks > 0 && gear.WorkRadius > 0 && gear.RewardExperience > 0 && gear.Capacity is > 0 and <= 512
                && (gear.Kind is not (WaveAttackKind.Homing or WaveAttackKind.Orbit) || gear.Speed > 0), "Invalid wave gear parameters: " + gear.Id);
        }
        foreach (var pair in wave.Items)
        {
            var item = pair.Value; Identity(pair.Key, item.Id, item.DesignRef, item.Kind.ToString());
            Require(item.Amount > 0 && item.EquipmentIds.All(wave.Gear.ContainsKey) && item.EquipmentIds.Distinct(StringComparer.Ordinal).Count() == item.EquipmentIds.Length, "Invalid wave item requirements.");
        }
        foreach (var pair in wave.Evolutions)
        {
            var evolution = pair.Value; Identity(pair.Key, evolution.Id, evolution.DesignRef, evolution.Kind.ToString());
            Require(evolution.InputIds.Length == 2 && evolution.InputIds.All(wave.Gear.ContainsKey) && evolution.InputIds.Distinct(StringComparer.Ordinal).Count() == 2, "Invalid wave evolution inputs.");
            var expected = evolution.Kind switch
            {
                WaveEvolutionKind.PlantingArc => new[] { "core:iron_blade", "core:seed_bag" },
                WaveEvolutionKind.RepairOrbit => new[] { "core:ward_orbit", "core:carpenter_hammer" },
                _ => new[] { "core:seed_bag", "core:carpenter_hammer" }
            };
            Require(evolution.InputIds.SequenceEqual(expected), "Wave evolution inputs changed from approved recipe.");
        }
        foreach (var pair in wave.Enemies)
        {
            var enemy = pair.Value; Identity(pair.Key, enemy.Id, enemy.DesignRef, enemy.Kind.ToString());
            Require(enemy.TellTicks > 0 && enemy.ActiveTicks > 0 && enemy.RecoveryTicks > 0 && enemy.ProjectileSpeed > 0 && enemy.FirstSpawnTick < file.Tuning.DurationTicks, "Invalid wave enemy phases.");
            var stats = file.EnemyStats[pair.Key];
            Require(stats.Id == pair.Key && stats.Target is "lord" or "seed" or "ripe" && new[] { stats.Health, stats.Speed, stats.Damage, stats.Range, stats.AttackCooldownTicks, stats.Experience }.All(n => n > 0), "Invalid wave enemy stats.");
        }
        Require(file.Tuning.TickRate == 30 && file.Tuning.DurationTicks > 0 && file.Tuning.DamageRollMax > 0, "Invalid wave clock.");
        Require(file.Provenance.Length == 3 && SameIds(file.Provenance.Select(p => p.Path), new[] { "tuning", "definition", "enemyStats" })
            && file.Provenance.All(p => p.Classification is "inherited-unchanged" or "provisional-executable"), "Wave numeric provenance must cover all numeric sections.");
    }
}
