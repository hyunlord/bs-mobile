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

        // M2 dark estate (#347): the opt-in profile wave-2a-dark carries an estate light; its runs wake loadout L1, the profile's
        // estateLoadout. Catalogs without the light (every default profile) get no estate and keep their replay formats.
        public const string EstateLoadout = "L1";
        public static EstateRunOptions Estate(ContentCatalog catalog) =>
            catalog.WaveRuntime?.EstateLight == null ? null : new EstateRunOptions(WaveEstateLight.Version, EstateLoadout, ReviewShowcaseCatalog.Hash(catalog));
    }
}
