using System;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // #483: terrain presentation follows the approved r8 proposal, never Core occupancy or unlock rules.
    [Serializable]
    public sealed class EstateR8Layout
    {
        public const string Version = "m3a-prototype-r8";
        public string version;
        public Cell[] riverCells = Array.Empty<Cell>();
        public Bridge[] bridges = Array.Empty<Bridge>();
        public EstateDecorRules.Placed[] placed = Array.Empty<EstateDecorRules.Placed>();
        [Serializable] public sealed class Cell { public int x, y; }
        [Serializable] public sealed class Bridge { public string id, zone; public int x, y, width, height; }
        static EstateR8Layout loaded;
        public static EstateR8Layout Load() => loaded ?? (loaded = JsonUtility.FromJson<EstateR8Layout>(Resources.Load<TextAsset>("EstateR8Layout").text));
        public static bool UsesPlazaRendering(AccountEstateConfig config) => config.Version == "m3a-prototype-r7" || config.Version == Version;
        public static EstateDecorRules.Placed[] Pieces(EstateDecorRules rules, AccountEstateConfig config) => config.Version == Version ? Load().placed : rules.placed;
        public static EstateDecorRules.Placed RoadBridge(EstateDecorRules.Placed[] pieces, AccountEstateConfig config) =>
            config.Version == Version ? pieces.First(p => p.id == "bridge-nw-se-base" && p.zone == "zone:inner-fallow") : pieces.FirstOrDefault(p => p.id == "bridge-nw-se-base");
    }
}
