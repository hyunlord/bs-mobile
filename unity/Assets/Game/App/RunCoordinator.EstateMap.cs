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
                estateConfig ??= CanonicalContent.CreateAccountEstateConfig();
                if (estateConfig == null) { ShowAccountEstate(); return; }
                // Without a saved estate the prototype keeps its own new estate for the session (#370). An account that does
                // carry an estate is copied again whenever it changes; Progression.State is a fresh copy on every read, so it is
                // compared by its save bytes. Either way the view keeps its zoom and position.
                var account = Progression.State;
                var bytes = account.Estate != null ? MetaSaveCodec.Encode(account) : null;
                if (estateMap == null || estateMap.FromSave != (bytes != null) || bytes != null && !bytes.SequenceEqual(estateMapSource))
                {
                    estateMap = new EstateMapScreen(Progression.Catalog, estateConfig, account, estateMap?.Map, () => UnityEngine.Time.realtimeSinceStartupAsDouble);
                    estateMapSource = bytes;
                }
                estateMap.Advance();
                MetaPage(UiScreen.EstateMap, () => PortraitMetaPanels.EstateMap(Ui, estateMap, account, new EstateMapActions
                {
                    Redraw = ShowEstateMap, Depart = ShowPortraitDeparture, Settings = () => ShowSettings(false), Navigate = NavigatePortraitMeta,
                }));
            }
            catch (Exception e) { Fail(e); }
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
