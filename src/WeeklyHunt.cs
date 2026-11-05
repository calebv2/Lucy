using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alta.Caves;
using Alta.Chunks;
using MelonLoader;
using UnityEngine;

namespace LucyMouldMod
{
    internal static class WeeklyHunt
    {
        static readonly HashSet<string> restored = new HashSet<string>(StringComparer.Ordinal);
        static readonly System.Random random = new System.Random();
        static WeeklyConfig config;
        static WeeklyLedger ledger;
        static string worldKey, ledgerPath, configPath, configText, configError;
        static LocationChunk watchedChunk;
        static float nextCheck, nextPlacement, nextNotice;
        static bool fatal;
        static string reportedWeek;

        internal static void ObserveToken(string token)
        {
            if (!string.IsNullOrEmpty(token)) restored.Add(token);
        }

        internal static void Tick()
        {
            if (!NetworkSceneManager.IsServer || Registration.WholePrefab == null || fatal
                || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 3f;
            var manager = CaveLayerManager.Instance;
            if (manager == null || manager.Seed < 0 || manager.LayersSeedHash == null) return;
            try
            {
                if (!EnsureWorld()) return;
                if (!ReloadSettings()) return;
                Reconcile();
                if (!config.enabled) { Watch(null); return; }
                var utc = DateTime.UtcNow;
                var key = WeeklyClock.Key(utc);
                var current = ledger.weeks.SingleOrDefault(e => e.week == key);
                if (current == null)
                {
                    current = ledger.Plan(key, manager.Seed, config, random);
                    Save();
                    MelonLogger.Msg("LUCY_WEEK_PLANNED week=" + key + " layer=" + current.layer
                        + " next-reset-utc=" + WeeklyClock.EndUtc(utc).ToString("o") + " token=" + current.token);
                }
                if (reportedWeek != key)
                {
                    reportedWeek = key;
                    MelonLogger.Msg("LUCY_WEEK_STATUS week=" + key + " state=" + current.state + " layer=" + current.layer
                        + " ledger=" + ledgerPath);
                }
                if (current.state != "planned") { Watch(null); return; }
                if (current.caveSeed != manager.Seed)
                {
                    current.caveSeed = manager.Seed;
                    current.note = "Caves regenerated before placement; same weekly allowance and target layer retained.";
                    Save();
                }
                // Merely polling the existing layer dictionary never generates
                // or force-loads a floor. Deep target floors remain eligible.
                if (!manager.Layers.TryGetValue(current.layer, out var layer)) { Watch(null); return; }
                Watch(layer.Chunk);
                if (watchedChunk == null || !watchedChunk.IsFullyLoaded || Time.realtimeSinceStartup < nextPlacement) return;
                nextPlacement = Time.realtimeSinceStartup + 6f;
                Vector3 position;
                Quaternion rotation;
                if (!LucyPlacement.TryFind(watchedChunk, random, out position, out rotation))
                {
                    if (Time.realtimeSinceStartup >= nextNotice)
                    {
                        nextNotice = Time.realtimeSinceStartup + 300f;
                        MelonLogger.Msg("LUCY_WAITING_FOR_SAFE_FLOOR week=" + key + " layer=" + current.layer
                            + "; no allowance used, retrying while loaded");
                    }
                    return;
                }
                if (WeeklyClock.Key(DateTime.UtcNow) != current.week) return;
                WeeklyLedger.Reserve(current, watchedChunk.ChunkIdentifier,
                    new[] { position.x, position.y, position.z }, new[] { rotation.x, rotation.y, rotation.z, rotation.w }, DateTime.UtcNow);
                // Durable reservation precedes every native side effect. There
                // is no automatic retry after this point, even after a crash.
                Save();
                if (WeeklyClock.Key(DateTime.UtcNow) != current.week)
                {
                    current.state = "review";
                    current.note = "Weekly reset occurred after reservation and before spawn. No sword created by this reservation.";
                    Save();
                    Watch(null);
                    return;
                }
                var entity = LucySpawn.Spawn(Registration.WholePrefab, watchedChunk, position, rotation, current.token);
                if (entity == null) throw new InvalidOperationException("Native spawn returned no Lucy entity.");
                RequestNativeSave(watchedChunk);
                WeeklyLedger.Confirm(current, DateTime.UtcNow);
                Save();
                MelonLogger.Msg("LUCY_WEEK_SPAWNED week=" + key + " layer=" + current.layer + " chunk=" + current.chunkId
                    + " token=" + current.token + " position=" + position.ToString("F2") + " prefab=LucySword");
                Watch(null);
            }
            catch (Exception error)
            {
                fatal = true;
                Watch(null);
                MelonLogger.Error("LUCY_WEEK_DISABLED: " + error + "; no automatic replacement will be created. Keep the ledger with its matching world save.");
            }
        }

        static bool EnsureWorld()
        {
            string nextWorld = LucySpawn.WorldKey();
            string saveRoot = LucySpawn.WorldSavePath();
            if (string.IsNullOrEmpty(nextWorld) || string.IsNullOrEmpty(saveRoot)) return false;
            string nextLedger = Path.Combine(saveRoot, "LucyWeekly", "ledger.json");
            if (ledger != null && worldKey == nextWorld && ledgerPath == nextLedger) return true;
            Watch(null);
            worldKey = nextWorld;
            ledgerPath = nextLedger;
            configPath = Path.Combine(Environment.CurrentDirectory, "UserData", "LucyWeekly", "settings.json");
            config = null;
            configText = null;
            configError = null;
            reportedWeek = null;
            if (File.Exists(ledgerPath))
            {
                ledger = AtomicJson.Load<WeeklyLedger>(ledgerPath);
                if (ledger == null) throw new InvalidDataException("Empty weekly Lucy ledger.");
                ledger.Validate(worldKey);
                if (ledger.RecoverInterrupted()) Save();
            }
            else
            {
                if (File.Exists(ledgerPath + ".bak") || File.Exists(ledgerPath + ".tmp"))
                    throw new InvalidDataException("Lucy ledger missing but recovery files exist. Restore the matching ledger before enabling weekly spawning.");
                ledger = new WeeklyLedger { worldKey = worldKey };
                Save();
            }
            MelonLogger.Msg("LUCY_WEEK_STORAGE world=" + worldKey + " settings=" + configPath + " ledger=" + ledgerPath);
            foreach (var entry in ledger.weeks.Where(e => e.state == "review"))
                MelonLogger.Warning("LUCY_WEEK_REVIEW week=" + entry.week + " token=" + entry.token + "; " + entry.note);
            return true;
        }

        static bool ReloadSettings()
        {
            string text = null;
            try
            {
                if (!File.Exists(configPath))
                {
                    if (config != null) return true; // An editor's temporary rename is not a reset.
                    AtomicJson.Save(configPath, new WeeklyConfig());
                }
                text = File.ReadAllText(configPath);
                if (text == configText) return config != null;
                var next = AtomicJson.Parse<WeeklyConfig>(text);
                if (next == null) throw new InvalidDataException("Empty Lucy settings.");
                next.Validate();
                config = next;
                configText = text;
                configError = null;
                MelonLogger.Msg("LUCY_SETTINGS_ACCEPTED enabled=" + config.enabled + " layers=" + config.minLayer + ".." + config.maxLayer
                    + "; range changes apply to the next planned week");
                return true;
            }
            catch (Exception error)
            {
                if (configError != error.Message || (text != null && text != configText))
                    MelonLogger.Warning("LUCY_SETTINGS_REJECTED; " + (config == null ? "spawning waits for valid settings: " : "keeping last valid settings: ") + error.Message);
                configError = error.Message;
                if (text != null) configText = text;
                return config != null;
            }
        }

        static void Reconcile()
        {
            bool changed = false;
            foreach (var entry in ledger.weeks.Where(e => e.state == "review" && restored.Contains(e.token)))
            {
                WeeklyLedger.Confirm(entry, DateTime.UtcNow);
                changed = true;
                MelonLogger.Msg("LUCY_WEEK_RECONCILED week=" + entry.week + " token=" + entry.token);
            }
            if (changed) Save();
        }

        static void Save() { ledger.Validate(worldKey); AtomicJson.Save(ledgerPath, ledger); }

        static async void RequestNativeSave(LocationChunk chunk)
        {
            try { await chunk.Save(); }
            catch (Exception error) { MelonLogger.Warning("LUCY_NATIVE_SAVE_REQUEST_FAILED: " + error.Message); }
            // Native Save queues the disk write; it is not a transaction with
            // our ledger. A crash can still lose an item, never reopen its week.
        }

        static void Watch(LocationChunk chunk)
        {
            if (watchedChunk == chunk) return;
            if (watchedChunk != null) watchedChunk.FullyLoaded -= OnLoaded;
            watchedChunk = chunk;
            if (watchedChunk != null) watchedChunk.FullyLoaded += OnLoaded;
        }

        static void OnLoaded(Chunk chunk) { nextCheck = 0; nextPlacement = 0; }

        internal static void Dispose()
        {
            Watch(null);
            ledger = null;
            config = null;
            restored.Clear();
            fatal = false;
            nextCheck = nextPlacement = nextNotice = 0;
        }
    }
}
