using System;
using System.Collections.Generic;
using Alta.Chunks;
using Alta.Networking;
using Alta.Saving;
using HarmonyLib;
using UnityEngine;

namespace LucyMouldMod
{
    // This component is save metadata only. It introduces no native network
    // messages, interactions, colliders, or ownership/parenting behaviour.
    [SavingInfo("caleb.LucyMould.WeeklyToken", false)]
    public sealed class LucyToken : MonoBehaviour, ISavable<LucyTokenData>
    {
        [SerializeField] string token = "";
        public string Token => token;

        internal static void Validate(string value)
        {
            if (value == null || value.Length > 200)
                throw new ArgumentException("Invalid Lucy weekly save token.");
            foreach (char character in value)
                if (character < 32 || character > 126)
                    throw new ArgumentException("Invalid Lucy weekly save token; native saves use ASCII.");
        }

        internal void Set(string value)
        {
            Validate(value);
            token = value;
        }

        public LucyTokenData Save(ISerializeChildren children)
        {
            return new LucyTokenData { token = token };
        }

        public void Load(LucyTokenData data, ISerializeChildren children)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Set(data.token ?? "");
            LucySpawn.Restored(token);
        }
    }

    [SaveStructure(typeof(LucyToken), 1u)]
    public sealed class LucyTokenData : NewSaveData<LucyToken, LucyTokenData>
    {
        public string token = "";
    }

    internal static class LucyTokenSaving
    {
        // SavingSystem scans its own assembly, so mod save types must be added
        // explicitly. Preserve every native setup and use the native serializer.
        internal static void Register()
        {
            var byType = (Dictionary<Type, SavingSystem.SavingSetup>)
                Native.Field(typeof(SavingSystem), "savingSetups", true).GetValue(null);
            var byHash = (Dictionary<uint, SavingSystem.SavingSetup>)
                Native.Field(typeof(SavingSystem), "savingTypes", true).GetValue(null);
            var latest = SavingSystem.LatestChunkVersioning;
            if (byType == null || byHash == null || latest == null)
                throw new InvalidOperationException("Native saving registry is not ready.");

            var setup = new SavingSystem.SavingSetup(typeof(LucyToken));
            SavingSystem.SavingSetup existing;
            if (byHash.TryGetValue(setup.Hash, out existing) && existing.Topic != typeof(LucyToken))
                throw new InvalidOperationException("Lucy weekly save hash collision: " + setup.Hash);
            if (byType.TryGetValue(typeof(LucyToken), out existing))
            {
                if (!byHash.ContainsKey(existing.Hash) || byHash[existing.Hash] != existing
                    || existing.LatestVersion != 1u || existing.LatestSetup.Type != typeof(LucyTokenData))
                    throw new InvalidOperationException("Inconsistent Lucy weekly saving setup.");
                EnsureVersion(latest, existing.Hash);
                return;
            }
            if (byHash.ContainsKey(setup.Hash))
                throw new InvalidOperationException("Orphan Lucy weekly saving setup.");
            setup.Add(1u, typeof(LucyTokenData));
            setup.FinishInitialization(byType);
            if (setup.Save == null)
                throw new InvalidOperationException("Lucy weekly native save delegate is unavailable.");
            // Check the final version before mutating either registry.
            uint version;
            if (latest.HashVersions.TryGetValue(setup.Hash, out version) && version != 1u)
                throw new InvalidOperationException("Lucy weekly save version collision.");
            byType.Add(typeof(LucyToken), setup);
            byHash.Add(setup.Hash, setup);
            EnsureVersion(latest, setup.Hash);
        }

        static void EnsureVersion(ChunkVersioning versioning, uint hash)
        {
            uint version;
            if (versioning.HashVersions.TryGetValue(hash, out version))
            {
                if (version != 1u) throw new InvalidOperationException("Unsupported Lucy weekly save version.");
                return;
            }
            versioning.Add(hash, 1u);
        }
    }

    [HarmonyPatch(typeof(SavingSystem), nameof(SavingSystem.Reinitialize))]
    internal static class LucySavingReinitializePatch
    {
        static void Postfix()
        {
            try { LucyTokenSaving.Register(); }
            catch (Exception error)
            {
                // A conflicting mod/save schema must disable Lucy spawning,
                // not poison the native SavingSystem static constructor.
                // PrepareTemplate/Spawn retry validation and fail closed.
                MelonLoader.MelonLogger.Error("LUCY_TOKEN_SAVE_SETUP_FAILED " + error);
            }
        }
    }
}
