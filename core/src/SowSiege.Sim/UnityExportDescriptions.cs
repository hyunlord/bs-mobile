using System.Globalization;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class UnityExport
{
    private static IEnumerable<(string Id, string Label, string Unit, int Level, int Value, bool PerLevel)> UpgradeStats(ContentCatalog catalog)
    {
        if (catalog.WeaponCombat is not null)
        {
            foreach (var pair in catalog.WeaponCombat.Weapons)
            {
                foreach (var level in pair.Value.Levels)
                {
                    yield return (pair.Key, "피해", "", level.Level, level.Damage, false);
                    yield return (pair.Key, "범위", "", level.Level, level.Range, false);
                    yield return (pair.Key, "수", "", level.Level, level.Count, false);
                    yield return (pair.Key, "간격", "틱", level.Level, level.CooldownTicks, false);
                    yield return (pair.Key, "관통", "", level.Level, level.Pierce, false);
                }
            }
        }
        foreach (var pair in catalog.Tools) { yield return (pair.Key, "피해", "", 1, pair.Value.Activation.Damage, true); }
        if (catalog.Runtime is not null)
        {
            foreach (var pair in catalog.Runtime.Charters)
            {
                foreach (var effect in pair.Value.Effects.Where(effect => effect.Operation == "stat-add")) { yield return (pair.Key, StatName(effect.Subject), effect.Subject is "attack-cooldown" or "draft-min-rest" ? "틱" : "", 1, effect.Amount, true); }
            }
        }
    }

    private static string StatName(string subject) => subject switch { "attack-damage" => "공격 피해", "attack-knockback" => "넉백", "attack-cooldown" => "공격 간격", "repair-amount" => "수리량", "draft-min-rest" => "귀환 뒤 휴식", "draft-speed" => "징집 이동", "worker-speed" => "일꾼 이동", "worker-incoming-damage" => "일꾼 받는 피해", "building-front-damage" => "건물 정면 피해", "building-rear-damage" => "건물 후면 피해", _ => subject };

    private static string DisplayDescription(ContentCatalog catalog, string id, IReadOnlyDictionary<string, string> names)
    {
        if (catalog.Weapons.TryGetValue(id, out var weapon))
        {
            var form = catalog.FirstPlayable?.Weapons.GetValueOrDefault(id)?.Form ?? weapon.Activation.Shape;
            return FormName(form) + " · 피해 " + weapon.Activation.Damage.ToString(CultureInfo.InvariantCulture);
        }
        if (catalog.Tools.TryGetValue(id, out var tool)) { return FormName(tool.Activation.Shape) + " · 피해 " + tool.Activation.Damage.ToString(CultureInfo.InvariantCulture) + " · " + GrowthDescription(catalog, id, names); }
        var effects = catalog.Runtime?.Charters.GetValueOrDefault(id)?.Effects ?? catalog.Runtime?.Items.GetValueOrDefault(id)?.Effects ?? catalog.Runtime?.Evolutions.GetValueOrDefault(id)?.Effects;
        if (effects is not null) { return string.Join(" · ", effects.Select(effect => DescribeEffect(effect, names))); }
        if (catalog.Enemies.TryGetValue(id, out var enemy)) { return TargetName(enemy.Target) + " 추격 · 체력 " + enemy.Health.ToString(CultureInfo.InvariantCulture); }
        return "";
    }

    private static string GrowthDescription(ContentCatalog catalog, string id, IReadOnlyDictionary<string, string> names)
    {
        if (catalog.Tools.TryGetValue(id, out var tool))
        {
            var text = tool.Growth.Target switch { "land" => "이동한 땅에 씨앗 파종", "building" => "건물 건설·수리", "people" => "백성을 징집", _ => "" };
            var effects = catalog.Runtime?.Equipment.GetValueOrDefault(id)?.Effects;
            return effects is { Length: > 0 } ? text + " · " + string.Join(" · ", effects.Select(effect => DescribeEffect(effect, names))) : text;
        }
        if (catalog.WeaponCombat?.Weapons.TryGetValue(id, out var weapon) == true)
        {
            var first = weapon.Levels[0]; var last = weapon.Levels[^1];
            return FormattableString.Invariant($"12단계: 피해 {first.Damage}→{last.Damage}, 범위 {first.Range}→{last.Range}, 수 {first.Count}→{last.Count}, 간격 {first.CooldownTicks}→{last.CooldownTicks}틱, 관통 {first.Pierce}→{last.Pierce}");
        }
        return "";
    }

    private static string EvolutionHint(string directory, ContentCatalog catalog, string id)
    {
        if (catalog.FirstPlayable is null || catalog.Runtime is null) { return ""; }
        var names = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "test" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(file => JsonDocument.Parse(File.ReadAllBytes(file))).ToArray();
        try
        {
            string Name(string value) => names.Select(document => document.RootElement).FirstOrDefault(element => element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var key) && key.GetString() == value) is var found && found.ValueKind == JsonValueKind.Object && found.TryGetProperty("name", out var name) ? name.GetString()! : value;
            return string.Join(" / ", catalog.Runtime.Evolutions.Values.Where(evolution => evolution.Id == id || evolution.InputIds.Contains(id, StringComparer.Ordinal)).Select(evolution =>
            {
                var growth = catalog.FirstPlayable.EvolutionGrowthRequirements?.GetValueOrDefault(evolution.Id);
                return Name(evolution.Id) + ": " + string.Join(" + ", catalog.FirstPlayable.EvolutionRequirements[evolution.Id].Select(requirement => Name(requirement.EquipmentId) + " Lv." + requirement.MinimumLevel.ToString(CultureInfo.InvariantCulture))) + (growth is null ? "" : " + 익은 밭 " + growth.Minimum.ToString(CultureInfo.InvariantCulture));
            }));
        }
        finally { foreach (var document in names) { document.Dispose(); } }
    }

    private static string FormName(string form) => form switch { "sector90" => "전방 좁은 베기", "sector180" => "전방 넓은 베기", "projectile" => "추적 방향 투사체", "volley" => "부채꼴 일제 사격", "orbit" => "주위를 도는 수호물", "field" => "지속 피해 장판", "piercing" => "일직선 관통", "chain" => "가까운 적으로 연쇄", "boomerang" => "왕복 원반", "nova" => "퍼져 나가는 충격파", "melee" => "근접 타격", "wave" => "넓은 파동", _ => form };
    private static string TargetName(string target) => target switch { "lord" => "영주", "seed" => "어린 밭", "ripe" => "익은 밭", "building" => "건물", "people" => "백성", _ => target };
    public static string DescribeEffect(RuntimeEffectDefinition effect, IReadOnlyDictionary<string, string> names)
    {
        string Name(string id) => names.TryGetValue(id, out var name) ? name : id;
        var conditions = string.Join("·", effect.Conditions.Select(condition => condition.Kind switch
        {
            "near-seed" => "어린 밭 근처",
            "near-ripe" => "익은 밭 근처",
            "near-building" => "건물 근처",
            "equipment-kind" => condition.Value == "weapon" ? "무기" : "도구",
            "owned-tag" => TagName(condition.Value!) + " 장비 보유",
            "equipment-tag" => TagName(condition.Value!) + " 공격",
            "equipment-owned" => Name(condition.Value!) + " 보유",
            "equipment-id" => Name(condition.Value!) + " 발동",
            "growth-target" => TagName(condition.Value!) + " 성장 도구",
            "enemy-target" => TargetName(condition.Value!) + "을 노리는 적",
            "estate-inside" => "영지 안",
            "estate-outside" => "영지 밖",
            "building-ruined" => "폐허",
            "building-new" => "새 건물",
            "shield-consumed" => "보호막 소모",
            "person-role" => PersonRole(condition.Value!),
            "count" => CountName(condition.Value!) + " " + condition.Minimum.ToString(CultureInfo.InvariantCulture) + " 이상",
            "season" => new[] { "봄", "여름", "가을", "겨울" }[condition.Minimum],
            _ => throw new InvalidDataException("Unsupported display condition: " + condition.Kind)
        }));
        var trigger = effect.Trigger switch
        {
            "modifier" => "",
            "attack" => "공격할 때 ",
            "harvest" => "수확할 때 ",
            "repair" => "수리할 때 ",
            "plant" => "파종할 때 ",
            "farm-hit" => "밭이 피격될 때 ",
            "draft" => "징집할 때 ",
            "return-start" => "귀환 시작 때 ",
            "return-arrival" => "귀환 도착 때 ",
            "estate-cross" => "영지 경계를 지날 때 ",
            "kill" => "적 처치 때 ",
            _ => throw new InvalidDataException("Unsupported display trigger: " + effect.Trigger)
        };
        var duration = effect.DurationTicks > 0 ? " (" + effect.DurationTicks.ToString(CultureInfo.InvariantCulture) + "틱)" : "";
        var cost = effect.FoodCost > 0 ? " · 식량 " + effect.FoodCost.ToString(CultureInfo.InvariantCulture) + " 소모" : "";
        return (conditions.Length == 0 ? "" : conditions + ": ") + trigger + EffectAction(effect) + duration + cost;
    }

    private static string PersonRole(string role) => role switch { "peasant" => "일꾼", "militia" => "징집병", "returning" => "귀환 중인 백성", "guard" => "수비대", "vassal" => "가신", _ => throw new InvalidDataException("Unsupported person role: " + role) };
    private static string CountName(string value) => value switch { "farms" => "밭 수", "buildings" => "완성 건물 수", "people" => "인구", "harvests" => "수확 횟수", _ => throw new InvalidDataException("Unsupported count: " + value) };
    private static string TagName(string tag) => tag switch { "weapon" => "무기", "melee" => "근접", "ranged" => "원거리", "orbit" => "궤도", "land" => "땅", "harvest" => "수확", "building" => "건물", "repair" => "수리", "people" => "사람", "muster" => "징집", "control" => "제어", "food" => "식량", "fertility" => "비옥", "defense" => "방어", "economy" => "경제", "growth" => "성장", _ => tag };

    private static string EffectAction(RuntimeEffectDefinition effect)
    {
        if (effect.Operation == "stat-add") { return StatName(effect.Subject) + " " + (effect.Amount > 0 ? "+" : "") + effect.Amount.ToString(CultureInfo.InvariantCulture); }
        return effect.Operation switch
        {
            "shield-farms" => "밭 보호막",
            "harvest-near" => "주변 밭 수확",
            "worker-buff" => "일꾼 강화",
            "repair-nearest" => "가까운 건물 수리",
            "extend-duty" => "징집 지속 연장",
            "plant-path" => "발동 경로에 파종",
            "guard-return" => "귀환 아군 보호",
            "rally-returners" => "복귀 병력 재집결",
            "return-via-building" => "건물을 경유해 귀환",
            "planting-bias" when effect.Subject == "estate-inward" => "영지 중심 쪽으로 파종 위치 이동",
            "planting-bias" when effect.Subject == "existing-edge" => "가까운 기존 밭 쪽으로 파종 위치 이동",
            "pause-neighbor-growth" => "주변 밭 성장 잠시 중단",
            "damage-young-plots" => "어린 밭 소모",
            "damage-pulse" => "전방 추가 타격",
            _ => throw new InvalidDataException("Unsupported display operation/subject: " + effect.Operation + "/" + effect.Subject)
        };
    }
}
