using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        WeaponDisplayStages displayStages;

        // Whether a wave weapon draws its evolution-only element: the View-only display stage table, by the active runtime
        // evolution of that gear or else by its definition.
        bool WeaponEvolved(string source)
        {
            if (waveDefinition == null || source == null || !waveDefinition.Gear.TryGetValue(source, out var gear)) return false;
            displayStages ??= WeaponDisplayStages.Load();
            return displayStages.Evolved(gear, MusterFormationRule.ActiveWaveEvolution(wave?.Evolutions, waveDefinition.Evolutions, source));
        }
    }
}
