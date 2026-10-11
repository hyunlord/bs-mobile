using System;

namespace SowSiege.Core
{
    public enum HostRevivePreservation { PreserveWorldAndCards }
    public sealed record HostReviveRules(int RestoreHealth, int InvulnerabilityTicks, HostRevivePreservation Preservation);
    public sealed record HostRewardRunOptions(bool AllowRerollGrant, HostReviveRules? Revive)
    {
        internal void Validate()
        {
            if (!AllowRerollGrant && Revive is null) { throw new ArgumentException("Host reward options must enable a command."); }
            if (Revive is { } revive && (revive.RestoreHealth <= 0 || revive.InvulnerabilityTicks < 0 || revive.Preservation != HostRevivePreservation.PreserveWorldAndCards))
            { throw new ArgumentException("Invalid host revive rules."); }
        }
    }
    internal sealed class HostRewardRunState
    {
        internal bool Revived;
        internal int RerollGrants;
        internal int InvulnerableUntil;
    }
}
