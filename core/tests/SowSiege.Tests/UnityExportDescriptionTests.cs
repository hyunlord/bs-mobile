using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class UnityExportDescriptionTests
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["core:iron_blade"] = "철날 검",
        ["core:seed_bag"] = "씨앗 자루"
    };
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "production");

    [Fact]
    public void FarmShieldDescriptionUsesActualRepairTrigger()
    {
        var effect = Assert.Single(Catalog().Runtime!.Evolutions["core:sheltered_sowing"].Effects);
        var text = UnityExport.DescribeEffect(effect, Names);
        Assert.Contains("수리할 때 밭 보호막", text);
        Assert.Contains("건물 근처", text);
        Assert.DoesNotContain("파종", text);
    }

    [Fact]
    public void DutyDescriptionUsesActualDraftTrigger()
    {
        var effect = Catalog().Runtime!.Items["core:levy_bread_wrap"].Effects.Single(effect => effect.Operation == "extend-duty");
        var text = UnityExport.DescribeEffect(effect, Names);
        Assert.Contains("징집할 때 징집 지속 연장", text);
        Assert.DoesNotContain("수확", text);
    }

    [Fact]
    public void PlantingDescriptionDistinguishesEstateInwardFromExistingPlot()
    {
        var effect = Catalog().Runtime!.Items["core:wind_seed_flag"].Effects.Single(effect => effect.Operation == "planting-bias");
        Assert.Equal("estate-inward", effect.Subject);
        var text = UnityExport.DescribeEffect(effect, Names);
        Assert.Contains("영지 중심 쪽", text);
        Assert.DoesNotContain("기존 밭", text);
        var edge = UnityExport.DescribeEffect(effect with { Subject = "existing-edge" }, Names);
        Assert.Contains("가까운 기존 밭 쪽", edge);
        Assert.DoesNotContain("영지 중심", edge);
    }

    [Fact]
    public void DescriptionsRetainEnemyTargetAndEquipmentIdentityConditions()
    {
        var meadow = Catalog().Runtime!.Items["core:meadow_buckle"].Effects.Single(effect => effect.Operation == "stat-add");
        var text = UnityExport.DescribeEffect(meadow, Names);
        Assert.Contains("어린 밭을 노리는 적", text);
        Assert.Contains("영지 안", text);
        Assert.Contains("근접 공격", text);
        var sworddance = Assert.Single(Catalog().Runtime!.Evolutions["core:sowing_sworddance"].Effects);
        Assert.Contains("철날 검 발동", UnityExport.DescribeEffect(sworddance, Names));
    }
}
