using SowSiege.Core;

namespace SowSiege.Sim;

public static class SimulationFactory
{
    public static Simulation Create(ContentCatalog catalog, RunOptions options, DiagnosticObserver? diagnostics = null) => new(catalog, options, new CanonicalStateHasher(), diagnostics);
}
