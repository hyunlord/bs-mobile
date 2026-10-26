using System;
using System.Linq;
using Game.App.Generated;
using SowSiege.Core;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        // M3a-2 estate map prototype (#366). A development option switches the estate tab between the manor page and this
        // map; release builds never show the option and always open the manor page. The map edits only an in-memory copy.
        AccountEstateConfig estateConfig;
        EstateMapScreen estateMap;
        byte[] estateMapSource;

        bool EstatePrototypeActive
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return IsWave && (preferences.EstatePrototype || forceEstatePrototype);
#else
                return false;
#endif
            }
        }

        Action<bool> EstatePrototypeSetting
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!IsWave) return null;
                return value => { preferences.EstatePrototype = value; preferences.Save(); };
#else
                return null;
#endif
            }
        }

        public EstateMapScreen EstateMap => estateMap;

        void ShowEstateMap()
        {
            try
            {
                // Without a saved estate the prototype keeps its own new estate for the session (#370). An account that does
                // carry an estate is copied again whenever it changes; Progression.State is a fresh copy on every read, so it is
                // compared by its save bytes. Either way the view keeps its zoom and position.
                var account = Progression.State;
                // A new preview uses r7; a saved prototype keeps its own historical grid and economics.
                if (estateConfig == null || (account.Estate != null
                    ? estateConfig.Version != account.Estate.ConfigVersion
                    : estateMap?.FromSave == true))
                    estateConfig = CanonicalContent.CreateAccountEstateConfig(account.Estate?.ConfigVersion);
                if (estateConfig == null) { ShowAccountEstate(); return; }
                var bytes = account.Estate != null ? MetaSaveCodec.Encode(account) : null;
                if (estateMap == null || estateMap.FromSave != (bytes != null) || bytes != null && !bytes.SequenceEqual(estateMapSource))
                {
                    var first = estateMap == null;
                    estateMap = new EstateMapScreen(Progression.Catalog, estateConfig, account, estateMap?.Map, () => UnityEngine.Time.realtimeSinceStartupAsDouble);
                    if (first) estateMap.SavedView = ParseEstateView(preferences.EstateMapView);
                    estateMapSource = bytes;
                }
                estateMap.Advance();
                MetaPage(UiScreen.EstateMap, () => PortraitMetaPanels.EstateMap(Ui, estateMap, account, new EstateMapActions
                {
                    Redraw = ShowEstateMap, Depart = ShowPortraitDeparture, Settings = () => ShowSettings(false), Navigate = NavigatePortraitMeta,
                    ViewChanged = SaveEstateView,
                }));
            }
            catch (Exception e) { Fail(e); }
        }

        // The map's last view survives restarts (#379): the first visit frames the lit land, later ones open where the player left.
        void SaveEstateView(int zoom, UnityEngine.Vector2 centre)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            var value = zoom.ToString(c) + ";" + centre.x.ToString("0.###", c) + ";" + centre.y.ToString("0.###", c);
            if (value == preferences.EstateMapView) return;
            preferences.EstateMapView = value; preferences.Save();
        }

        static (int, UnityEngine.Vector2)? ParseEstateView(string value)
        {
            var parts = (value ?? "").Split(';'); var c = System.Globalization.CultureInfo.InvariantCulture; var style = System.Globalization.NumberStyles.Float;
            if (parts.Length != 3 || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, c, out var zoom) ||
                !float.TryParse(parts[1], style, c, out var u) || !float.TryParse(parts[2], style, c, out var v)) return null;
            return (zoom, new UnityEngine.Vector2(u, v));
        }

        // Back closes the move preview, then the selection, then asks to quit like the manor page.
        void EstateMapBack()
        {
            if (estateMap?.Moving == true) { estateMap.Cancel(); ShowEstateMap(); }
            else if (estateMap?.Selected != null) { estateMap.Select(null); ShowEstateMap(); }
            else ShowPortraitExit();
        }
    }
}
