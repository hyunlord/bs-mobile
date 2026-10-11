namespace SowSiege.Core
{
    public enum RuntimeStage { Enemy, Separation, Residual, Work, Attack }

    // Optional host-owned measurements. No time source, allocations, or state codec entries live here.
    public interface IRuntimeDiagnostics
    {
        void Begin(RuntimeStage stage);
        void End(RuntimeStage stage);
    }
}
