using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace LucyMouldMod
{
    public sealed class WeeklyConfig
    {
        public int schemaVersion = 1;
        public bool enabled = true;
        // Engine layer IDs, inclusive. The game's progression counter adds one.
        public int minLayer = 0;
        public int maxLayer = 100;

        public void Validate()
        {
            if (schemaVersion != 1) throw new InvalidDataException("Unsupported Lucy settings schema.");
            if (minLayer < 0 || maxLayer > 100 || maxLayer < minLayer)
                throw new InvalidDataException("Use an inclusive mine layer range within 0..100.");
        }
    }

    public static class WeeklyClock
    {
        public static TimeZoneInfo Zone
        {
            get
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
                catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            }
        }

        public static DateTime Start(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Expected a UTC clock value.");
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
            var start = local.Date.AddDays(-((7 + (int)local.DayOfWeek - (int)DayOfWeek.Monday) % 7)).AddHours(6);
            return local < start ? start.AddDays(-7) : start;
        }

        public static string Key(DateTime utc) { return Start(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static DateTime EndUtc(DateTime utc)
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(Start(utc).AddDays(7), DateTimeKind.Unspecified), Zone);
        }
    }

    public sealed class WeeklyEntry
    {
        public string week;
        public string token;
        public int layer;
        public int caveSeed;
        public string state = "planned";
        public string chunkId;
        public float[] position;
        public float[] rotation;
        public string reservedUtc;
        public string spawnedUtc;
        public string note;
    }

    public sealed class WeeklyLedger
    {
        public int schemaVersion = 1;
        public string worldKey;
        public List<WeeklyEntry> weeks = new List<WeeklyEntry>();

        public void Validate(string expectedWorld)
        {
            if (schemaVersion != 1 || worldKey != expectedWorld || weeks == null)
                throw new InvalidDataException("Lucy ledger does not match this world or schema. Do not delete it to repair a load error.");
            if (weeks.Any(e => !Valid(e)) || weeks.Select(e => e.week).Distinct().Count() != weeks.Count
                || weeks.Select(e => e.token).Distinct().Count() != weeks.Count)
                throw new InvalidDataException("Invalid or duplicate Lucy weekly records. Restore a matching native world backup.");
        }

        static bool Valid(WeeklyEntry entry)
        {
            DateTime week;
            Guid token;
            if (entry == null || !DateTime.TryParseExact(entry.week, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out week) || week.DayOfWeek != DayOfWeek.Monday
                || !Guid.TryParseExact(entry.token, "N", out token) || entry.layer < 0 || entry.layer > 100 || entry.caveSeed < 0)
                return false;
            if (entry.state == "planned") return true;
            if (entry.state != "reserved" && entry.state != "spawned" && entry.state != "review") return false;
            return !string.IsNullOrEmpty(entry.chunkId) && Finite(entry.position, 3) && Finite(entry.rotation, 4);
        }

        static bool Finite(float[] values, int count)
        {
            return values != null && values.Length == count && values.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
        }

        public WeeklyEntry Plan(string week, int seed, WeeklyConfig config, Random random)
        {
            config.Validate();
            var current = weeks.SingleOrDefault(e => e.week == week);
            if (current != null) return current;
            current = new WeeklyEntry { week = week, token = Guid.NewGuid().ToString("N"),
                layer = random.Next(config.minLayer, config.maxLayer + 1), caveSeed = seed };
            weeks.Add(current);
            return current;
        }

        public static void Reserve(WeeklyEntry entry, string chunk, float[] position, float[] rotation, DateTime utc)
        {
            if (entry.state != "planned" || string.IsNullOrEmpty(chunk) || !Finite(position, 3) || !Finite(rotation, 4))
                throw new InvalidOperationException("Lucy spawn must be reserved once from a valid planned location.");
            entry.chunkId = chunk;
            entry.position = (float[])position.Clone();
            entry.rotation = (float[])rotation.Clone();
            entry.reservedUtc = utc.ToString("o", CultureInfo.InvariantCulture);
            entry.state = "reserved";
        }

        public static void Confirm(WeeklyEntry entry, DateTime utc)
        {
            if (entry.state != "reserved" && entry.state != "review")
                throw new InvalidOperationException("Cannot confirm an unreserved weekly Lucy.");
            entry.state = "spawned";
            entry.spawnedUtc = utc.ToString("o", CultureInfo.InvariantCulture);
            entry.note = null;
        }

        public bool RecoverInterrupted()
        {
            bool changed = false;
            foreach (var entry in weeks.Where(e => e.state == "reserved"))
            {
                entry.state = "review";
                entry.note = "Spawn was interrupted. No automatic replacement; the saved sword may still exist. A restored matching token will confirm it.";
                changed = true;
            }
            return changed;
        }
    }

    public static class AtomicJson
    {
        public static T Load<T>(string path)
        {
            return Parse<T>(File.ReadAllText(path));
        }

        public static T Parse<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings {
                TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 32
            });
        }

        public static void Save(string path, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
