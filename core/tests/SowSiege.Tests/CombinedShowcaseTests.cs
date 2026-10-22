using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;
public sealed class CombinedShowcaseTests
{
    private static (ContentCatalog, WaveCombinedShowcaseCase) Load()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var baseline = ContentLoader.Load(data, profileName: "wave-1a");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "review", "m1-weapon-combined-case.json")));
        return (baseline, json.RootElement.GetProperty("combined").Deserialize<WaveCombinedShowcaseCase>(ShowcaseFixtureCli.Json)!);
    }
    [Fact]
    public void CombinedReviewRecordsOfferedPreparationWithoutChangingBaseline()
    {
        var (baseline, review) = Load();
        var oldHash = ReviewShowcaseCatalog.Hash(baseline);
        var catalog = ReviewShowcaseCatalog.CreateCombined(baseline, review);
        Assert.Equal(oldHash, ReviewShowcaseCatalog.Hash(baseline));
        Assert.Equal(7, catalog.Weapons.Count); Assert.Single(catalog.Tools);
        Assert.Equal(review.LordHealth, catalog.Tuning.World.Map.LordHealth);
        var options = new InteractiveOptions(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement,
            ReviewShowcaseCatalog.Hash(catalog), Field: new(FieldOptions.ReviewVersion, review.Arena.SpawnRadius, review.Arena.RecycleRadius), MeleeAim: new());
        var session = new InteractiveSession(catalog, options);
        using var stream = new MemoryStream(); ReplayCodec.WriteHeader(stream, ReplayCodec.Header(options));
        var commands = new List<ReplayCommand>();
        void Apply(ReplayCommand command) { session.Apply(command); commands.Add(command); ReplayCodec.WriteCommand(stream, command); }
        CombinedShowcaseFixtureCli.Equip(session, review, Apply);
        Assert.Equal(6, commands.Count(c => c.Kind == ReplayCommandKind.GrantLevel));
        Assert.Equal(6, commands.Count(c => c.Kind == ReplayCommandKind.ChooseCard));
        Assert.Equal(8, session.View.CaptureFrame().Equipment.Count);
        Assert.DoesNotContain(commands, c => c.Kind == ReplayCommandKind.SetInvulnerable);
        for (var tick = 0; tick < 60; tick++) { Apply(new(session.NextSequence, tick, ReplayCommandKind.Advance, ShowcaseFixtureCli.WalkingInput(tick))); }
        ReplayCodec.WriteEnd(stream, new(session.NextSequence, 60, ReplayEndKind.Quit, session.ComputeStateHash()));
        stream.Position = 0;
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, ReplayCodec.Read(stream)).StateHash);
    }
    [Fact]
    public void CombinedReviewRejectsDuplicateWrongStageAndUnboundWindow()
    {
        var (baseline, review) = Load();
        var duplicate = review.Cases.ToArray(); duplicate[1] = duplicate[0];
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { Cases = duplicate }));
        var wrongStage = review.Cases.ToArray(); wrongStage[0] = wrongStage[0] with { Stage = "max" };
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { Cases = wrongStage }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { CaptureStartTick = -1 }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { WeaponSlots = 5 }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { LordHealth = 0 }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { SecondaryEnemyHealth = 0 }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.CreateCombined(baseline, review with { Arena = review.Arena with { EnemyBodyWidth = 0 } }));
    }
}
