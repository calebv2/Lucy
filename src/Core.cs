using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;

[assembly: MelonInfo(typeof(LucyMouldMod.Core), "Lucy", LucyMouldMod.LucyRules.Version, "caleb")]
[assembly: MelonGame("Alta", "A Township Tale")]
[assembly: MelonAdditionalDependencies("SyncLib")]

namespace LucyMouldMod
{
    public sealed class Core : MelonMod
    {
        bool attempted;
        internal static GameObject Graphics;
        internal static GameObject WholeGraphics;

        public override void OnInitializeMelon()
        {
            NetworkPrefabRegistry.RegisterPrefabs += RegisterLucy;
            LucySpawn.TokenRestored += WeeklyHunt.ObserveToken;
            LoggerInstance.Msg("LUCY_START version=" + LucyRules.Version);
        }

        void RegisterLucy()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                if (!ApplicationManager.IsHeadless) LoadGraphics();
                Registration.Register(this);
            }
            catch (Exception error)
            {
                LoggerInstance.Error("LUCY_REGISTRATION_FAILED " + error);
            }
        }

        void LoadGraphics()
        {
            AssetBundle bundle;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LucyMould.Graphics"))
            using (var memory = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("Embedded Lucy graphics missing.");
                stream.CopyTo(memory);
                bundle = AssetBundle.LoadFromMemory(memory.ToArray());
            }
            if (bundle == null) throw new InvalidOperationException("Unity rejected the Lucy graphics bundle.");
            try
            {
                var assets = bundle.LoadAllAssets<GameObject>();
                Graphics = assets.SingleOrDefault(asset => asset.name == "LucyGraphics");
                if (Graphics == null) throw new InvalidOperationException("LucyGraphics prefab missing.");
                WholeGraphics = assets.SingleOrDefault(asset => asset.name == "LucyWholeGraphics");
                if (WholeGraphics == null) throw new InvalidOperationException("LucyWholeGraphics prefab missing.");
                foreach (var graphic in new[] { Graphics, WholeGraphics })
                {
                    if (graphic.GetComponentsInChildren<Collider>(true).Length != 0
                        || graphic.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                        throw new InvalidOperationException("Lucy art must contain geometry only; native weapon owns physics.");
                    LucyStereo.Configure(graphic);
                }
            }
            finally { bundle.Unload(false); }
        }

        public override void OnUpdate() { WeeklyHunt.Tick(); }

        public override void OnDeinitializeMelon()
        {
            NetworkPrefabRegistry.RegisterPrefabs -= RegisterLucy;
            LucySpawn.TokenRestored -= WeeklyHunt.ObserveToken;
            WeeklyHunt.Dispose();
            LucyStereo.Dispose();
        }
    }
}
