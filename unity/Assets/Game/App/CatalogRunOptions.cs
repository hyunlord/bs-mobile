using SowSiege.Core;

namespace Game.App
{
    public static class CatalogRunOptions
    {
        public static MeleeAimOptions MeleeAim(ContentCatalog catalog)
        {
            if (catalog.WaveRuntime == null) return null;
            foreach (var gear in catalog.WaveRuntime.Gear.Values)
                if (gear.Aim == "nearest-enemy") return new MeleeAimOptions();
            return null;
        }
    }
}
