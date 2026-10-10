using System;
using System.IO;
using Game.View;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        FallowExperienceTrace fallowExperienceTrace;
        partial void ConfigureNormalTrace(NormalPlayTrace trace)
        {
            if (trace == null) return;
            fallowExperienceTrace = new FallowExperienceTrace();
            trace.SampleDiagnostics = SampleFallowDiagnostics;
            trace.WriteDiagnostics = fallowExperienceTrace.Write;
        }
        void SampleFallowDiagnostics()
        {
            if (Frame == null || Wave == null || world == null) return;
            if (fallowExperienceTrace.LastTick == Frame.Tick) return;
            fallowExperienceTrace.Sample(Frame, Wave, world.ReadFallowDiagnostics());
        }
    }

    sealed class FallowExperienceTrace
    {
        readonly Row[] rows = new Row[2048];
        int count, dropped, missedTicks;
        long eventId = -1, missingEvents, killXp, growthXp, absorbedXp, absorptionCount, harvestCount;
        public int LastTick { get; private set; } = -1;
        struct Row
        {
            public int Tick, Level, PendingRewards, MissedTicks;
            public long Experience, Required, KillXp, GrowthXp, AbsorbedXp, Absorptions, Harvests, MissingEvents;
            public FallowDiagnosticSnapshot Fallow;
        }
        public void Sample(SowSiege.Core.RunFrame frame, SowSiege.Core.WaveRuntimeFrame wave, FallowDiagnosticSnapshot fallow)
        {
            if (LastTick >= 0) missedTicks += Math.Max(0, frame.Tick - LastTick - 1);
            LastTick = frame.Tick;
            for (var i = 0; i < wave.Events.Count; i++)
            {
                var value = wave.Events[i];
                if (value.Id <= eventId) continue;
                if (eventId >= 0) missingEvents += Math.Max(0, value.Id - eventId - 1);
                eventId = value.Id;
                if (value.Kind == "enemy-killed") killXp += value.Amount;
                else if (value.Kind == "reward-created") growthXp += value.Amount;
                else if (value.Kind == "reward-collected") { absorbedXp += value.Amount; absorptionCount++; }
                else if (value.Kind == "harvest-complete") harvestCount++;
            }
            if (count == rows.Length) { dropped++; return; }
            rows[count++] = new Row { Tick = frame.Tick, Level = frame.Level, Experience = frame.Experience, Required = frame.RequiredExperience,
                PendingRewards = wave.Rewards.Count, KillXp = killXp, GrowthXp = growthXp, AbsorbedXp = absorbedXp, Absorptions = absorptionCount,
                Harvests = harvestCount, MissedTicks = missedTicks, MissingEvents = missingEvents, Fallow = fallow };
        }
        public void Write(string folder)
        {
            using (var file = new StreamWriter(Path.Combine(folder, "normal-fallow-xp.csv")))
            {
                file.WriteLine("tick,level,currentXp,requiredXp,killXpObserved,growthXpCreatedObserved,growthXpAbsorbedObserved,absorptionCountObserved,harvestCountObserved,pendingRewards,missedTicks,missingEventIds,fallowActive,chapterEnabled,artProfile,chapter,shaderSupported,maskBound,maskPixels,nonzeroPixels,greenPixels,goldPixels,buildingPixels,totalWork,seed,sprout,ripe,building,built,otherWork,deadWork");
                for (var i = 0; i < count; i++)
                {
                    var r = rows[i]; var f = r.Fallow;
                    file.WriteLine(FormattableString.Invariant($"{r.Tick},{r.Level},{r.Experience},{r.Required},{r.KillXp},{r.GrowthXp},{r.AbsorbedXp},{r.Absorptions},{r.Harvests},{r.PendingRewards},{r.MissedTicks},{r.MissingEvents},{f.Active},{f.ChapterEnabled},{f.ArtProfile},{f.Chapter},{f.ShaderSupported},{f.MaskBound},{f.Pixels},{f.NonzeroPixels},{f.GreenPixels},{f.GoldPixels},{f.BuildingPixels},{f.TotalWork},{f.Seed},{f.Sprout},{f.Ripe},{f.Building},{f.Built},{f.OtherWork},{f.DeadWork}"));
                }
            }
            File.WriteAllText(Path.Combine(folder, "normal-fallow-xp-boundary.txt"), $"NormalPlayTrace opt-in read-only diagnostics; source/profile/input provenance is normal-trace.txt.\nrows={count}\ndroppedRows={dropped}\nMask counts are whole-map CPU mask coverage, not visible-screen color coverage or a GPU shader result.\nXP amounts are observed event sums; missedTicks/missingEventIds expose skipped snapshots. Kill XP is granted directly on enemy-killed; growth XP is created on reward-created and granted on reward-collected. No kill pickup is implied.\nSeed=grain progress zero; sprout=unfinished grain progress positive; ripe=complete grain; dead grain/buildings counted separately.\n");
        }
    }
}
