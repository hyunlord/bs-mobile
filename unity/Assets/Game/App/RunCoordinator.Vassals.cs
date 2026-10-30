using System;
using Game.App.Generated;
using SowSiege.Core;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        bool VassalCombatActive
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return IsWave && preferences.VassalCombat;
#else
                return false;
#endif
            }
        }

        Action<bool> VassalCombatSetting
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!IsWave) return null;
                return value => { preferences.VassalCombat = value; preferences.Save(); };
#else
                return null;
#endif
            }
        }

        VassalRunOptions DepartureVassals(bool useMeta) => useMeta && Progression != null && VassalCombatActive
            ? VassalRun.Create(Progression.Catalog, Progression.State, CanonicalContent.CreateVassalRunRules()) : null;
    }
}
