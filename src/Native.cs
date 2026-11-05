using System;
using System.Collections.Generic;
using System.Reflection;
using Alta;
using Alta.Networking;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucyMouldMod
{
    internal static class Native
    {
        static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        internal static FieldInfo Field(Type type, string name, bool isStatic = false)
        {
            string key = type.FullName + "." + name + "/" + isStatic;
            if (fields.TryGetValue(key, out var field)) return field;
            for (var current = type; current != null; current = current.BaseType)
            {
                field = current.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Public
                    | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
                if (field == null) continue;
                fields.Add(key, field);
                return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        internal static T Get<T>(object target, string name)
        {
            return (T)Field(target.GetType(), name).GetValue(target);
        }

        internal static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        internal static void CheckValueAvailable<T>(uint hash) where T : HashedGeneralValue<T>
        {
            HashedGeneralValue<T>.CheckItems();
            var map = (Dictionary<uint, T>)Field(typeof(HashedGeneralValue<T>), "items", true).GetValue(null);
            if (hash == 0 || hash > int.MaxValue || map.ContainsKey(hash))
                throw new InvalidOperationException("Registry collision: " + typeof(T).Name + " " + hash);
        }

        internal static void AddValue<T>(T value, uint hash) where T : HashedGeneralValue<T>
        {
            CheckValueAvailable<T>(hash);
            var type = typeof(HashedGeneralValue<T>);
            var map = (Dictionary<uint, T>)Field(type, "items", true).GetValue(null);
            Set(value, "hash", checked((int)hash));
            map.Add(hash, value);
            Field(type, "itemsByName", true).SetValue(null, null);
        }

        internal static NetworkPrefab RegisterClone(Core mod, GameObject root, string identifier)
        {
            if (root.activeInHierarchy) throw new InvalidOperationException("Registration requires inactive staging.");
            var nativeBody = root.GetComponent<NetworkRigidbody>();
            var prefab = mod.RegisterCustomPrefab(root, identifier);
            if (prefab == null) throw new InvalidOperationException("SyncLib rejected " + identifier);
            // Unity has already remapped serialized native references. Refresh only
            // the root cache after removing the extra rigidbody added by SyncLib.
            if (nativeBody != null)
                foreach (var body in root.GetComponents<NetworkRigidbody>())
                    if (body != nativeBody) Object.DestroyImmediate(body);
            NetworkPrefabRegistry.ExecuteAllGetComponentAtts(root.GetComponent<NetworkEntity>());
            Set(prefab, "canBeSpawnedThroughCommand", true);
            return prefab;
        }

        internal static void ResetMouldCaches()
        {
            Field(typeof(MouldDefinition), "definitions", true).SetValue(null, null);
            Field(typeof(MouldDefinition), "findable", true).SetValue(null, null);
        }
    }
}
