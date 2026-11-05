using MelonLoader;
using UnityEngine;
using Alta.Networking;

namespace LucyMouldMod
{
    internal static class Registration
    {
        internal static NetworkPrefab WholePrefab;

        internal static void Register(Core mod)
        {
            var staging = new GameObject("Lucy native templates");
            staging.SetActive(false);
            Object.DontDestroyOnLoad(staging);
            // Reserve the old prefab identities before the complete sword.
            // These templates load old saves, with all acquisition disabled.
            LegacyItems.Register(mod, staging, Core.Graphics);
            var whole = LucyWholeSword.Register(mod, staging, Core.WholeGraphics);
            LucySpawn.PrepareTemplate(whole);
            WholePrefab = whole;
            MelonLogger.Msg("LUCY_WEEK_READY version=" + LucyRules.Version
                + " prefab=LucySword hash=" + whole.Hash + " schedule=Monday-06:00-America/New_York layers=0..100 scope=server acquisition=mines-only");
        }
    }
}
