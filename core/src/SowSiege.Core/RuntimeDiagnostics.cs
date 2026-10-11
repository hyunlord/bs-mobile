namespace SowSiege.Core
{
    public enum RuntimeStage { Enemy, Separation, Residual, Work, Attack, Movement, Collect, Harvest, Spawn, Clear }

    public static class RuntimeDiagnosticsCapabilities
    {
#if SOWSIEGE_FINE_DIAGNOSTICS
        public const bool FineStagesEnabled = true;
#else
        public const bool FineStagesEnabled = false;
#endif
    }

    // Optional host-owned measurements. No time source, allocations, or state codec entries live here.
    public interface IRuntimeDiagnostics
    {
        void Begin(RuntimeStage stage);
        void End(RuntimeStage stage);
    }
}
