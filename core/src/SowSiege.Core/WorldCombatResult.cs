using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum WorldTerminalReason { ObjectivesCompleted, HeroDefeated, RequiredMarkerDestroyed, Timeout, Quit, AutoVictory, AutoDefeat, SimultaneousElimination, RoundLimit }
    public sealed record WorldObjectiveResult(string Id, int Progress, bool Completed);
    public sealed record WorldTroopResult(string Id, int N, int K);
    // This is a result claim until the dedicated world runner has reproduced it from the authoritative input.
    public sealed record WorldCombatResult(string ContractHash, WorldCombatMode Mode, string RulesHash, string ContentHash,
        WorldTerminalReason Terminal, int TickOrRound, IReadOnlyList<WorldObjectiveResult> Objectives,
        IReadOnlyList<WorldTroopResult> Troops, string StateHash);
    public sealed record WorldReplayEnd(long AppliedCommands, WorldCombatResult Result);
    public sealed record WorldReplayDocument(ReplayHeader Header, IReadOnlyList<ReplayCommand> Commands,
        IReadOnlyList<ReplayCheckpoint> Checkpoints, WorldReplayEnd End);
}
