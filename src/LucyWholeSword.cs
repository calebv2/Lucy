using System;
using System.Linq;
using Alta;
using Alta.Crafting;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucyMouldMod
{
    // Recover the original complete Lucy, with current native pickup/combat/save
    // components. A separate Item/prefab leaves existing blade saves intact.
    // All dimensions and base attack values below come from the old
    // Large_Sword prefab, not from resizing a current modular sword blade.
    internal static class LucyWholeSword
    {
        internal const string PrefabName = "LucySword";
        internal const uint DonorPrefabHash = 63156; // Current fixed Rusty Greatsword.
        internal static readonly Bounds OriginalBounds = new Bounds(new Vector3(-.000000014901161f, .277693301f, 0),
            new Vector3(.258874834f, 2.482769668f, .074566178f));

        internal static NetworkPrefab Register(Core mod, GameObject staging, GameObject graphics)
        {
            if (mod == null || staging == null || staging.activeInHierarchy)
                throw new ArgumentException("Lucy requires inactive registration staging.");
            bool headless = ApplicationManager.IsHeadless;
            if ((!headless && graphics == null) || (graphics != null
                && (graphics.GetComponentsInChildren<Renderer>(true).Length == 0
                || graphics.GetComponentsInChildren<Collider>(true).Length != 0
                || graphics.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)))
                throw new InvalidOperationException("LucyWholeGraphics must contain original render geometry only.");
            if (!headless)
            {
                var filters = graphics.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length != 1 || filters[0].sharedMesh == null || filters[0].sharedMesh.vertexCount != 564
                    || (filters[0].sharedMesh.bounds.center - OriginalBounds.center).sqrMagnitude > .00000001f
                    || (filters[0].sharedMesh.bounds.size - OriginalBounds.size).sqrMagnitude > .00000001f
                    || filters[0].transform.localPosition != Vector3.zero || filters[0].transform.localRotation != Quaternion.identity
                    || filters[0].transform.localScale != Vector3.one)
                    throw new InvalidOperationException("LucyWholeGraphics is not the original complete sword at native scale.");
            }

            var donor = PrefabManager.AllPrefabs.SingleOrDefault(p => p != null && p.Hash == DonorPrefabHash);
            if (donor == null || donor.GetComponent<Pickup>() == null || donor.GetComponent<ImpactTool>() == null
                || donor.GetComponent<DurabilityModule>() == null || donor.GetComponent<ImpactorMap>() == null
                || donor.GetComponent<CraftingPart>() != null)
                throw new InvalidOperationException("The fixed native greatsword donor contract changed.");

            var root = Object.Instantiate(donor.gameObject, staging.transform, false);
            root.name = PrefabName;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(true); // Still beneath inactive staging; no Awake/Initialize yet.

            // Rusty loot decay is not part of the original Lucy prefab.
            foreach (var decay in root.GetComponentsInChildren<PickupDecay>(true)) Object.DestroyImmediate(decay);
            var pickup = root.GetComponent<Pickup>();
            var donorItem = donor.GetComponent<Pickup>().Item;
            var item = Object.Instantiate(donorItem);
            item.name = PrefabName;
            Native.Set(item, "components", donorItem.Components.Where(c => !(c is MouldItemComponent)).ToList());
            Native.Set(item, "isStackable", false);
            Native.Set(item, "isStackableWhenCrafted", false);
            Native.Set(item, "isUsingShrunkenVisual", false);
            // The retained native Large Sword Item (#61520) specifies inventory
            // weight 15; this is distinct from its original Rigidbody mass 1.
            Native.Set(item, "weight", 15f);
            pickup.Item = item;
            ConfigurePickup(pickup);
            ConfigureBody(root.GetComponent<Rigidbody>());
            ConfigureCollision(root, pickup);
            ConfigureAttack(root, pickup);

            var hidden = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in hidden) renderer.forceRenderingOff = true;
            var renderers = new Renderer[0];
            if (!headless)
            {
                var visual = Object.Instantiate(graphics, root.transform, false);
                visual.name = "LucyWholeVisual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                renderers = visual.GetComponentsInChildren<Renderer>(true);
            }
            foreach (var renderer in renderers)
            {
                renderer.forceRenderingOff = false;
                renderer.gameObject.layer = root.layer;
            }
            if (!headless)
            {
                var keeper = root.AddComponent<LucyWholeSwordVisual>();
                keeper.Hidden = hidden;
            }
            var group = root.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length == 0) lods = new[] { new LOD(.01f, renderers) };
                for (int i = 0; i < lods.Length; i++) lods[i].renderers = renderers;
                group.SetLODs(lods);
                group.RecalculateBounds();
            }
            ConfigureDurability(root.GetComponent<DurabilityModule>(), renderers);

            var prefab = Native.RegisterClone(mod, root, PrefabName);
            item.Prefab = prefab;
            Native.AddValue(item, prefab.Hash);
            foreach (var settings in Resources.FindObjectsOfTypeAll<PickupDockSettings>())
                if (settings.IncludedItems.Contains(donorItem) && !settings.IncludedItems.Contains(item)) settings.IncludedItems.Add(item);
            if (root.GetComponents<NetworkRigidbody>().Length != 1
                || root.GetComponentsInChildren<NetworkEntity>(true).Length != 1
                || root.GetComponentsInChildren<Collider>(true).Length != 5)
                throw new InvalidOperationException("Lucy native hierarchy validation failed.");
            MelonLogger.Msg("LUCY_WEAPON_REGISTERED prefab=LucySword hash=" + prefab.Hash
                + " item=" + item.Hash + " donor=63156 mass=1 inventory-weight=15 original-collision=5 original-grip=PASS attacks=3 original-unbreakable=true");
            return prefab;
        }

        static void ConfigurePickup(Pickup pickup)
        {
            var grab = new GrabPoint();
            Native.Set(grab, "position", new Vector3(0, -.89f, 0));
            Native.Set(grab, "rotationEuler", new Vector3(-90, 90, 0));
            Native.Set(grab, "linear", new Vector3(0, .275f, 0));
            Native.Set(grab, "rotationMode", (RotationMode)1);
            Native.Set(grab, "poleForward", Vector3.forward);
            Native.Set(grab, "isUsingLocal", true);
            Native.Set(pickup, "grabPoints", new[] { grab });
            Native.Set(pickup, "grabPointsScale", 1f);
            Native.Set(pickup, "isTightGrab", true);
            Native.Set(pickup, "jointType", PickUpJointType.VelocityJoint);
            Native.Set(pickup, "blockLevel", BlockLevel.BlockEverything);
            Native.Set(pickup, "isSwingable", false);
            Native.Set(pickup, "isEnvironmentalJoint", false);
            Native.Set(pickup, "isChunkingParentStatic", false);
        }

        static void ConfigureBody(Rigidbody body)
        {
            if (body == null) throw new InvalidOperationException("Lucy donor has no root Rigidbody.");
            body.mass = 1f;
            body.drag = 0f;
            body.angularDrag = .05f;
            body.useGravity = true;
            body.isKinematic = true; // Native NetworkRigidbody establishes authority on spawn.
            body.constraints = RigidbodyConstraints.None;
        }

        static void ConfigureCollision(GameObject root, Pickup pickup)
        {
            var collision = Required(root.transform, "Collision");
            var handle = Required(collision, "Handle Collision");
            var head = Required(collision, "Head Collision");
            var tight = Required(root.transform, "Tight Grip");
            foreach (var transform in new[] { collision, handle, head })
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
            }
            var grip = tight.GetComponent<CapsuleCollider>();
            var shaft = handle.GetComponent<CapsuleCollider>();
            var guard = handle.GetComponent<BoxCollider>();
            var blade = head.GetComponent<BoxCollider>();
            if (grip == null || shaft == null || guard == null || blade == null)
                throw new InvalidOperationException("The native fixed sword collision layout changed.");

            tight.localPosition = new Vector3(0, -.765f, 0);
            tight.localRotation = Quaternion.identity;
            tight.localScale = Vector3.one;
            grip.center = Vector3.zero; grip.radius = .025f; grip.height = .3f; grip.direction = 1;
            var gripBody = tight.GetComponent<Rigidbody>();
            if (gripBody == null) throw new InvalidOperationException("Native tight grip has no Rigidbody.");
            gripBody.mass = 1; gripBody.isKinematic = true; gripBody.useGravity = false;
            shaft.center = new Vector3(0, -.75f, 0); shaft.radius = .022f; shaft.height = .265f; shaft.direction = 1;
            guard.center = new Vector3(0, -.56f, 0); guard.size = new Vector3(.25887483f, .11f, .07456618f);
            blade.center = new Vector3(0, .52f, 0); blade.size = new Vector3(.1f, 2.03f, .03f);
            var pommel = handle.gameObject.AddComponent<BoxCollider>();
            pommel.center = new Vector3(0, -.925f, 0); pommel.size = new Vector3(.09f, .08f, .05f);
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                collider.enabled = true;
                collider.isTrigger = false;
                collider.sharedMaterial = null;
            }
            Native.Set(pickup, "childrenColliders", colliders);
            // Keep the original blade collider object and map, so all native
            // collider->impactor references remain valid after fitting.
            Native.Set(root.GetComponent<ImpactorMap>(), "map", null);
        }

        static void ConfigureAttack(GameObject root, Pickup pickup)
        {
            var tool = root.GetComponent<ImpactTool>();
            var impactors = root.GetComponentsInChildren<Impactor>(true);
            // The native rusty sword deliberately gives its tip Blunt damage.
            // EdgeLength is the native HasLength geometry test; damage type is
            // not a reliable way to identify its two edges and single point.
            var slash = impactors.Where(i => i.ImpactorValues != null && i.ImpactorValues.EdgeLength > 0f).ToArray();
            var pierce = impactors.Where(i => i.ImpactorValues != null && i.ImpactorValues.EdgeLength <= 0f).ToArray();
            if (slash.Length != 2 || pierce.Length != 1 || impactors.Length != 3
                || impactors.Any(i => i.ImpactorValues == null || float.IsNaN(i.ImpactorValues.EdgeLength)
                    || float.IsInfinity(i.ImpactorValues.EdgeLength)))
                throw new InvalidOperationException("Unsupported native Lucy attack geometry: "
                    + string.Join(", ", impactors.Select(i => i.name + "=" + (i.ImpactorValues == null
                        ? "missing-values" : i.ImpactType + "/length:" + i.ImpactorValues.EdgeLength))));
            Native.Set(tool, "damage", 1f);
            Native.Set(tool, "singleHandedMultiplier", 1f);
            Native.Set(tool, "dualHandedMultiplier", 1f);
            Native.Set(tool, "lengthAxis", Vector3.up);
            Native.Set(tool, "requiredBlockLevel", BlockLevel.BlockPhysicalOnly);
            for (int i = 0; i < slash.Length; i++)
            {
                slash[i].name = "Lucy Slash " + (i + 1);
                slash[i].transform.localPosition = new Vector3(i == 0 ? -.054f : .054f, -.506f, 0);
                slash[i].transform.localRotation = Quaternion.Euler(0, i == 0 ? -90 : 90, 0);
                SetValues(slash[i], ImpactType.Slash, .1f, 1.2f, 1.95f, .7f, 1.5f, .8f, pickup, tool);
            }
            pierce[0].name = "Lucy Pierce";
            pierce[0].transform.localPosition = new Vector3(0, 1.516f, 0);
            pierce[0].transform.localRotation = Quaternion.Euler(-90, 0, 0);
            SetValues(pierce[0], ImpactType.Pierce, .1f, 1f, -1f, .2f, .7f, 8f, pickup, tool);
        }

        static void SetValues(Impactor impactor, ImpactType type, float lerp, float modifier, float edge, float minimum, float maximum,
            float direction, Pickup pickup, ImpactTool tool)
        {
            var values = Object.Instantiate(impactor.ImpactorValues);
            values.name = impactor.name + " original values";
            Native.Set(values, "type", type);
            Native.Set(values, "lerpValue", lerp);
            Native.Set(values, "healthDamageModifier", modifier);
            Native.Set(values, "crippleDamageModifier", modifier);
            Native.Set(values, "edgeLength", edge);
            Native.Set(values, "minimumFollow", minimum);
            Native.Set(values, "maximumFollow", maximum);
            Native.Set(values, "directionPower", direction);
            Native.Set(impactor, "impactorValues", values);
            Native.Set(impactor, "pickup", pickup);
            Native.Set(impactor, "impactTool", tool);
            Native.Set(impactor, "cachedTransform", impactor.transform);
            Native.Set(impactor, "isUsingImprovedDirection", false);
        }

        static void ConfigureDurability(DurabilityModule module, Renderer[] renderers)
        {
            // The original points to the native asset named "unbreakable":
            // chance based, chance 0, conversion 1. Preserve that exact intent.
            var old = Native.Get<DurabilitySettings>(module, "durabilitySettings");
            if (old == null) throw new InvalidOperationException("Native weapon durability settings are missing.");
            var settings = Object.Instantiate(old);
            settings.name = "Lucy original unbreakable";
            Native.Set(settings, "isChanceBasedDurability", true);
            Native.Set(settings, "durability", 0f);
            Native.Set(settings, "damageConversion", 1f);
            Native.Set(module, "durabilitySettings", settings);
            var stage = new DurabilityModule.DurabilityVisual();
            Native.Set(stage, "threshold", 0f);
            Native.Set(stage, "visualRenderers", renderers);
            Native.Set(module, "stageVisuals", new[] { stage });
        }

        static Transform Required(Transform root, string path)
        {
            var transform = root.Find(path);
            if (transform == null) throw new InvalidOperationException("Native Lucy donor path missing: " + path);
            return transform;
        }
    }

    public sealed class LucyWholeSwordVisual : MonoBehaviour
    {
        public Renderer[] Hidden;
        void OnEnable() { Hide(); }
        void LateUpdate() { Hide(); }
        void Hide()
        {
            if (Hidden != null) foreach (var renderer in Hidden) if (renderer != null) renderer.forceRenderingOff = true;
        }
    }
}
