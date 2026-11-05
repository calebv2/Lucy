using System;
using System.Linq;
using Alta;
using Alta.Inventory;
using Alta.Networking;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucyMouldMod
{
    // Save compatibility only. Never add these items to loot, command spawning,
    // smelter/press dock settings, or a usable mould recipe.
    internal static class LegacyItems
    {
        internal static void Register(Core mod, GameObject staging, GameObject bladeGraphics)
        {
            var nativeMould = PrefabManager.AllPrefabs.FirstOrDefault(prefab => prefab != null
                && prefab.name == "Mould" && prefab.GetComponent<Mould>() != null);
            if (nativeMould == null || nativeMould.GetComponent<Pickup>()?.Item == null)
                throw new InvalidOperationException("Native template required to load retired Lucy items is missing.");
            var donor = HashedGeneralValue<MouldDefinition>.Get(LucyRules.DefinitionDonorHash);
            if (donor == null) throw new InvalidOperationException("Legacy Lucy definition template is missing.");
            Native.CheckValueAvailable<MouldDefinition>(LucyRules.DefinitionHash);

            var blade = LucyWeapon.Register(mod, staging, bladeGraphics);
            Native.Set(blade, "canBeSpawnedThroughCommand", false);
            var item = blade.GetComponent<Pickup>().Item;
            if (item == null || item.GetComponent<MouldItemComponent>() != null)
                throw new InvalidOperationException("Retired Lucy blade must not carry a mould-press recipe.");

            // Keep a nonnull, unique product for the native definition lookup.
            // This private EMPTY material set rejects even previously saved
            // moulds with positive usage before ingredients can be consumed.
            var materials = ScriptableObject.CreateInstance<ItemSet>();
            materials.name = "Retired Lucy - no casting materials";
            var definition = Object.Instantiate(donor);
            definition.name = "Retired Lucy mould - save compatibility only";
            Native.Set(definition, "product", item);
            Native.Set(definition, "cost", LucyRules.IngotCost);
            Native.Set(definition, "quantityProduced", 0);
            Native.Set(definition, "isFindable", false);
            Native.Set(definition, "allowedMaterials", materials);
            Native.Set(definition, "graphic", null);
            Native.AddValue(definition, LucyRules.DefinitionHash);

            var root = Object.Instantiate(nativeMould.gameObject, staging.transform, false);
            root.name = LucyRules.MouldPrefab;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.SetActive(true);
            var mould = root.GetComponent<Mould>();
            Native.Set(mould, "definition", definition);
            Native.Set(mould, "isGenerating", false);
            Native.Set(mould, "usage", 0);
            var prefab = Native.RegisterClone(mod, root, LucyRules.MouldPrefab);
            Native.Set(prefab, "canBeSpawnedThroughCommand", false);
            var mouldItem = Object.Instantiate(nativeMould.GetComponent<Pickup>().Item);
            mouldItem.name = LucyRules.MouldPrefab;
            mouldItem.Prefab = prefab;
            Native.AddValue(mouldItem, prefab.Hash);
            root.GetComponent<Pickup>().Item = mouldItem;

            Native.ResetMouldCaches();
            if (definition.IsFindable || definition.AllowedMaterials == donor.AllowedMaterials
                || definition.AllowedMaterials.Items.Count != 0 || definition.QuantityProduced != 0
                || MouldDefinition.GetDefinition(item) != definition)
                throw new InvalidOperationException("Retired Lucy acquisition was not disabled safely.");
            MelonLogger.Msg("LUCY_LEGACY_RETIRED blade=" + blade.Hash + " mould=" + prefab.Hash
                + " save-load-only=true loot=false casting=false imprint=false command-spawn=false");
        }
    }
}
