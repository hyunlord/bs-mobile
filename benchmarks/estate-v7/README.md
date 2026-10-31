# Format 7 estate record from main 75ffc97

`main-75ffc97-l1-30001.ssreplay` was recorded by the Core at main `75ffc97`, which has only the first estate slice and no
effects. It guards format 7 compatibility (Astra review on #323, 2026-10-23): a later Core must still verify this record
byte for byte. The test is `WaveEstateTests.FormatSevenRecordFromMainBeforeEffectsStillVerifies`.

The record:

- Catalog: wave-1a with the effect-less test light `WaveEstateTests.Light()`, loadout L1.
- Catalog hash: `D536D58C8F998C3C8AE3CAACF85AE0A89F33C019A6F00195631AF18FA2374508`.
- Seed 30001 and fixed data hash `A`×64.
- Normal input only: the hero walks toward the nearest sleeping building. There is no teleport and no grant.
- 1800 ticks; every building wakes. It ends with Quit at
  `57E2A2B0EA074EC4A4038B3EC3B9050F6ED09CC962D88CB54BB8DF986F484333`.
- sha256 `7e26eeeef4a856ad69e62857d09903c3633de27315d01538770c962aabeff31f`.

It was recorded by a throwaway console program referencing `core/src/SowSiege.Sim` at `75ffc97`:

```csharp
using SowSiege.Core; using SowSiege.Sim;
const string Tower = "test:estate_tower", Well = "test:estate_well", Granary = "test:estate_granary";
var light = new WaveEstateLightDefinition(WaveEstateLight.Version, "test:estate_home", 2400, 3000, 500, 50, 120,
    new Dictionary<string, WaveEstateBuildingDefinition>
    {
        [Tower] = new(Tower, "test:design/tower", WaveEstateWake.Channel, 600, 2500, 90, WaveEstateResource.Food, 0, 0),
        [Well] = new(Well, "test:design/well", WaveEstateWake.Spend, 600, 2500, 0, WaveEstateResource.Food, 3, 15),
        [Granary] = new(Granary, "test:design/granary", WaveEstateWake.Both, 600, 2500, 60, WaveEstateResource.Timber, 2, 15)
    },
    new Dictionary<string, WaveEstateSlot[]>
    {
        ["L1"] = new[] { new WaveEstateSlot(Tower, 3000, 6000), new WaveEstateSlot(Well, 3000, 6000), new WaveEstateSlot(Granary, 8000, 12000) },
        ["L3"] = new[] { new WaveEstateSlot(Tower, 4000, 5000), new WaveEstateSlot(Granary, 9000, 9000) }
    });
var c = ContentLoader.Load(args[0], profileName: "wave-1a"); c = c with { WaveRuntime = c.WaveRuntime! with { EstateLight = light } };
var dataHash = new string('A', 64);
var options = new InteractiveOptions(new(30001, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, dataHash,
    Field: new FieldOptions(), Estate: new(WaveEstateLight.Version, "L1", ReviewShowcaseCatalog.Hash(c)));
var session = new InteractiveSession(c, options);
using (var output = File.Create(args[1]))
{
    ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
    var tick = 0;
    void Send(ReplayCommandKind kind, PlayerInput input = default, string? card = null) { var command = new ReplayCommand(session.NextSequence, tick, kind, input, card); session.Apply(command); ReplayCodec.WriteCommand(output, command); }
    while (session.View.Status != RunStatus.Completed && tick < 1800)
    {
        if (session.View.Status == RunStatus.AwaitingCard) { var cards = session.View.CaptureCards(); Send(ReplayCommandKind.ChooseCard, card: cards.Cards[tick % cards.Cards.Count]); continue; }
        var lord = session.View.CaptureFrame().Lord.Position; var estate = ((IWaveRunView)session.View).CaptureWaveRuntime()!.Estate!;
        var next = estate.Buildings.Where(b => b.Phase != WaveEstatePhase.Active).OrderBy(b => (long)(b.Position.X - lord.X) * (b.Position.X - lord.X) + (long)(b.Position.Y - lord.Y) * (b.Position.Y - lord.Y)).ThenBy(b => b.Id).FirstOrDefault();
        var input = default(PlayerInput);
        if (next is not null)
        {
            long dx = next.Position.X - lord.X, dy = next.Position.Y - lord.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 200) input = new((short)Math.Round(dx * 1000 / length), (short)Math.Round(dy * 1000 / length));
        }
        Send(ReplayCommandKind.Advance, input); tick++;
        if (tick % 300 == 0) ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, tick, session.ComputeStateHash()));
    }
    var summary = session.GetSummary();
    ReplayCodec.WriteEnd(output, new(session.NextSequence, tick, summary.Survived ? ReplayEndKind.Quit : ReplayEndKind.Death, summary.StateHash));
    var frame = ((IWaveRunView)session.View).CaptureWaveRuntime()!.Estate!;
    Console.WriteLine($"ticks {tick} awake {frame.Buildings.Count(b => b.Phase == WaveEstatePhase.Active)} of {frame.Buildings.Count} catalog {ReviewShowcaseCatalog.Hash(c)}");
}
using var input2 = File.OpenRead(args[1]); var doc = ReplayCodec.Read(input2);
var v = ReplayRunner.Verify(c, dataHash, doc);
Console.WriteLine($"format {doc.Header.FormatVersion} verify {v.Tick} {v.StateHash} {(ReplayEndKind)v.EndKind}");
```
