using System;
using System.Linq;

namespace SowSiege.Core
{
    internal sealed class WaveGrowthPrimitives
    {
        private readonly WorldState world;
        private readonly WavePrimitiveModules modules;
        internal WaveGrowthPrimitives(WorldState world, WavePrimitiveModules modules) { this.world = world; this.modules = modules; }
        internal bool AdvanceTime(WaveWork work, WaveGearDefinition gear)
        {
            var program = modules[work.Source];
            if (!program.Is("unit:growth-cycle", "drivers", "time")) { return false; }
            var gate = program.Value("unit:growth-cycle", "workGate");
            if (gate == "within-work-radius" && !WaveRuntimeSystem.Within(world.Lord, work.Position, gear.WorkRadius)) { return false; }
            if (gate == "linked-building-complete" && !world.WaveRuntime!.Work.Any(w => w.Id == work.ParentId && w.Complete && w.Health > 0)) { return false; }
            return ++work.Progress >= work.Required;
        }
        internal static bool Eligible(WavePrimitiveProgram program, WaveRuntimeState state)
        {
            return program.Value("unit:event-gate", "condition") switch
            {
                "attack-kill-near-growth" => state.BladePlotKill,
                "repair-completed" => state.RepairCompleted,
                "harvest-near-building" => state.HarvestNearBuilding,
                _ => throw new ArgumentException("Unsupported evolution history gate.")
            };
        }
    }
    internal sealed class WaveCompletionLedger
    {
        private readonly WorldState world;
        private readonly WavePrimitiveModules modules;
        internal WaveCompletionLedger(WorldState world, WavePrimitiveModules modules) { this.world = world; this.modules = modules; }
        internal bool Claim(string source, int instance, int cycle, string kind, Position at, int experience)
        {
            var program = modules[source];
            var gate = program.Value("unit:completion-ledger", "claimGate");
            var expected = kind switch { "growth" => "ripe-contact", "irrigation" => "work-complete", "shipment" => "cargo-arrival", "mission" => "mission-return", _ => "" };
            if (expected.Length == 0 || gate != expected || !program.Is("unit:completion-ledger", "channels", "xp")) { return false; }
            var state = world.WaveRuntime!;
            var key = kind + ":" + source + ":" + instance + ":" + cycle;
            if (!state.Completed.Add(key)) { return false; }
            state.Rewards.Add(new() { Id = world.AllocateId(), Source = source, CompletionKey = key, Experience = experience, Position = at });
            state.Emit(world.Tick, "reward-created", source, instance, at, at, experience);
            return true;
        }
    }
}
