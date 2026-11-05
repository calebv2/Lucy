using System;
using System.Globalization;
using Alta.Chunks;
using Alta.Networking;
using Alta.Networking.Servers;
using ATT.Saving;
using UnityEngine;

namespace LucyMouldMod
{
    // Uses the ordinary native pickup/chunk save path. No parent changes, loot
    // population, or repeating container timer is involved in a weekly spawn.
    internal static class LucySpawn
    {
        internal static event Action<string> TokenRestored;

        internal static void PrepareTemplate(NetworkPrefab prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            LucyTokenSaving.Register();
            if (prefab.GetComponent<LucyToken>() == null) prefab.gameObject.AddComponent<LucyToken>();
        }

        internal static string WorldKey()
        {
            var handler = ServerHandler.Current;
            if (!NetworkSceneManager.IsServer || handler == null || handler.SaveUtility == null)
                return null;
            return "server-" + handler.ServerInfo.Identifier.ToString(CultureInfo.InvariantCulture);
        }

        internal static string WorldSavePath()
        {
            var handler = ServerHandler.Current;
            if (!NetworkSceneManager.IsServer || handler == null || handler.SaveUtility == null)
                return null;
            return handler.SaveUtility.SaveFolder.Path;
        }

        internal static NetworkEntity Spawn(NetworkPrefab prefab, LocationChunk chunk,
            Vector3 position, Quaternion rotation, string token)
        {
            if (!NetworkSceneManager.IsServer)
                throw new InvalidOperationException("Only the server may spawn the weekly Lucy.");
            if (prefab == null || prefab.GetComponent<LucyToken>() == null)
                throw new InvalidOperationException("Lucy token template was not prepared.");
            if (chunk == null || !chunk.IsFullyLoaded)
                throw new InvalidOperationException("Lucy's selected cave chunk is not fully loaded.");
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Weekly token required.", nameof(token));
            LucyToken.Validate(token);
            LucyTokenSaving.Register();
            // The callback overload runs before InitializeWithChildrenAsServer.
            // Set both the persisted identity and transform before native code
            // initializes the pickup. SpawnHelper assigns its native Chunk last.
            var entity = SpawnHelper.Spawn(prefab, SpawnData.DontGenerate, chunk, spawned =>
            {
                spawned.transform.SetPositionAndRotation(position, rotation);
                spawned.Prefab.SaveMode = SaveMode.Base;
                spawned.GetComponent<LucyToken>().Set(token);
                var body = spawned.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            });
            if (entity == null || entity.Prefab == null || entity.Chunk != chunk
                || entity.Prefab.SaveMode != SaveMode.Base
                || entity.GetComponent<LucyToken>()?.Token != token)
                throw new InvalidOperationException("Native Lucy spawn did not complete with its saved token and chunk.");
            return entity;
        }

        internal static void Restored(string token)
        {
            if (!NetworkSceneManager.IsServer || string.IsNullOrEmpty(token)) return;
            var callbacks = TokenRestored;
            if (callbacks == null) return;
            foreach (Action<string> callback in callbacks.GetInvocationList())
            {
                try { callback(token); }
                catch (Exception error)
                {
                    // Do not turn a ledger I/O error into a failed native item
                    // load. The coordinator keeps uncertain weeks closed.
                    MelonLoader.MelonLogger.Error("LUCY_TOKEN_RECONCILE_FAILED " + error);
                }
            }
        }
    }
}
