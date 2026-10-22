using System;
using System.Collections.Generic;
using System.Text;
namespace SowSiege.Core
{
    // ADR0064: opt-in dark estate. Only runs that select a loadout through EstateRunOptions own this state.
    public enum WaveEstateWake { Channel, Spend, Both }
    public enum WaveEstateResource { Food, Timber }
    public enum WaveEstatePhase { Asleep, Waking, Active }
    public enum WaveEstateGround { Dark, Fallow, Restored }
    public sealed record WaveEstateBuildingDefinition(string Id, string DesignRef, WaveEstateWake Wake, int WakeRadius, int LitRadius, int ChannelTicks, WaveEstateResource Resource, int Cost, int ConfirmTicks);
    public sealed record WaveEstateSlot(string BuildingId, int MinRadius, int MaxRadius);
    public sealed record WaveEstateLightDefinition(int Version, string HomeId, int HomeLitRadius, int SightRadius, int DarkDamagePermille, int DarkFinishPermillePerSleeping, int DarkFinishCapPermille,
        IReadOnlyDictionary<string, WaveEstateBuildingDefinition> Buildings, IReadOnlyDictionary<string, WaveEstateSlot[]> Loadouts);
    public sealed record EstateRunOptions(int Version, string LoadoutId, string LoadoutCatalogHash);
    public sealed record WaveEstateBuildingView(int Id, string DefinitionId, WorldPoint Position, WaveEstatePhase Phase, int Progress, int Required, int Dwell, int ConfirmTicks,
        bool Paid, WaveEstateResource Resource, int Cost, int WakeRadius, int LitRadius);
    public sealed record WaveEstateFrame(int Version, string LoadoutId, int SightRadius, IReadOnlyList<WaveEstateBuildingView> Buildings, int SleepingBuildings, int DarkFinishBonusPermille);

    public static class WaveEstateLight
    {
        public const int Version = 1;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        // Lit zones are rules (Core); the hero's sight only lifts dark to fallow grey for presentation.
        public static WaveEstateGround Ground(WaveEstateFrame frame, WorldPoint lord, WorldPoint point)
        {
            if (frame is null) { throw new ArgumentNullException(nameof(frame)); }
            foreach (var building in frame.Buildings)
            {
                if (building.Phase == WaveEstatePhase.Active && Within(building.Position, point, building.LitRadius)) { return WaveEstateGround.Restored; }
            }
            return Within(lord, point, frame.SightRadius) ? WaveEstateGround.Fallow : WaveEstateGround.Dark;
        }
        private static bool Within(WorldPoint center, WorldPoint point, int radius)
        {
            var dx = (long)point.X - center.X; var dy = (long)point.Y - center.Y;
            return dx * dx + dy * dy <= (long)radius * radius;
        }

        public static void Validate(WaveEstateLightDefinition definition)
        {
            if (definition is null) { throw new ArgumentNullException(nameof(definition)); }
            if (definition.Version != Version) { throw new ArgumentException("Unsupported estate light version."); }
            if (string.IsNullOrEmpty(definition.HomeId) || definition.HomeLitRadius <= 0 || definition.SightRadius < 0 || definition.DarkDamagePermille < 0 ||
                definition.DarkFinishPermillePerSleeping < 0 || definition.DarkFinishCapPermille < 0 || definition.Buildings is null || definition.Loadouts is null || definition.Loadouts.Count == 0)
            { throw new ArgumentException("Invalid estate light definition."); }
            foreach (var pair in definition.Buildings)
            {
                var b = pair.Value;
                var channel = b?.Wake is WaveEstateWake.Channel or WaveEstateWake.Both;
                var spend = b?.Wake is WaveEstateWake.Spend or WaveEstateWake.Both;
                if (b is null || b.Id != pair.Key || b.Id == definition.HomeId || string.IsNullOrEmpty(b.DesignRef) || !Enum.IsDefined(typeof(WaveEstateWake), b.Wake) || !Enum.IsDefined(typeof(WaveEstateResource), b.Resource) ||
                    b.WakeRadius <= 0 || b.LitRadius <= 0 || (channel ? b.ChannelTicks <= 0 : b.ChannelTicks != 0) || (spend ? b.Cost <= 0 || b.ConfirmTicks < 0 : b.Cost != 0 || b.ConfirmTicks != 0))
                { throw new ArgumentException("Invalid estate building " + pair.Key + "."); }
            }
            foreach (var pair in definition.Loadouts)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value is null || pair.Value.Length == 0) { throw new ArgumentException("Estate loadouts need at least one slot."); }
                foreach (var slot in pair.Value)
                {
                    if (slot is null || !definition.Buildings.ContainsKey(slot.BuildingId) || slot.MinRadius <= 0 || slot.MaxRadius < slot.MinRadius) { throw new ArgumentException("Invalid estate slot in " + pair.Key + "."); }
                }
            }
        }

        public static void Validate(EstateRunOptions options)
        {
            if (options is null) { throw new ArgumentNullException(nameof(options)); }
            if (options.Version != Version || string.IsNullOrEmpty(options.LoadoutId) || Utf8.GetByteCount(options.LoadoutId) > ReplayCodec.MaximumStringBytes) { throw new ArgumentException("Invalid estate run options."); }
            ReplayCodec.ValidateHash(options.LoadoutCatalogHash);
        }
    }
}
