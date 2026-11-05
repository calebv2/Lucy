using System;
using System.Linq;
using Alta.Blacksmithing;
using Alta.Crafting;
using Alta.Impact;
using Alta.Inventory;
using Alta.Networking;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LucyMouldMod
{
    // The mould casts a blade. Native large-sword sockets, forging, material,
    // durability, and handle assembly retain their original implementations.
    internal static class LucyWeapon
    {
        internal const uint DonorPrefabHash = 63886;
        // Baked client donor bounds work identically on stripped server donors.
        internal static readonly Bounds NativeBladeBounds = new Bounds(
            new Vector3(0, .607524872f, 0), new Vector3(.206318721f, 1.21504974f, .0771481842f));
        internal static readonly Bounds LucyBladeBounds = new Bounds(
            new Vector3(-.001143584f, 1.012565881f, 0), new Vector3(.108213957f, 2.025131762f, .028386692f));

        internal static NetworkPrefab Register(Core mod, GameObject staging, GameObject graphics)
        {
            if (mod == null || staging == null || staging.activeInHierarchy)
                throw new ArgumentException("Lucy blade requires inactive registration staging.");
            bool headless = ApplicationManager.IsHeadless;
            if (!headless && (graphics == null || graphics.GetComponentsInChildren<MeshFilter>(true).Length != 1
                || graphics.GetComponentsInChildren<Collider>(true).Length != 0
                || graphics.GetComponentsInChildren<MonoBehaviour>(true).Length != 0))
                throw new InvalidOperationException("LucyGraphics must contain recovered blade geometry only.");
            var donor = PrefabManager.AllPrefabs.SingleOrDefault(p => p != null && p.Hash == DonorPrefabHash);
            var donorPart = donor == null ? null : donor.GetComponent<CraftingPart>();
            if (donorPart == null || donorPart.Insert == null || donor.GetComponent<Pickup>()?.Item == null
                || donor.GetComponent<PhysicalMaterialPart>() == null || donor.GetComponent<MeltingModelPart>() == null
                || donor.GetComponentsInChildren<ForgedPoint>(true).Length != 7)
                throw new InvalidOperationException("Native Great Sword Blade forging/insert contract changed.");
            var donorItem = donor.GetComponent<Pickup>().Item;
            var root = Object.Instantiate(donor.gameObject, staging.transform, false);
            root.name = LucyRules.SwordPrefab;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(true); // Still inactive under staging; Awake has not run.
            var part = root.GetComponent<CraftingPart>();
            var insertPosition = part.Insert.transform.localPosition;
            var insertRotation = part.Insert.transform.localRotation;
            var insertType = part.Insert.Type;
            // Identical baked geometry on both peers. The Linux headless server
            // never needs the Windows client graphics bundle to fit colliders.
            var bounds = LucyBladeBounds;
            if (!headless)
            {
                var hidden = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.GetComponentInParent<JointPoint>() == null).ToArray();
                foreach (var renderer in hidden) renderer.forceRenderingOff = true;
                var visual = Object.Instantiate(graphics, root.transform, false);
                visual.name = "LucyBladeVisual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                var actual = LocalBounds(root.transform, visual.GetComponentInChildren<MeshFilter>(true));
                if ((actual.center - bounds.center).sqrMagnitude > .00000001f
                    || (actual.size - bounds.size).sqrMagnitude > .00000001f)
                    throw new InvalidOperationException("Lucy blade art differs from shared server collision bounds.");
                var keeper = root.AddComponent<LucyBladeVisual>();
                keeper.Hidden = hidden;
                keeper.Visible = visual.GetComponentsInChildren<Renderer>(true);
                keeper.ForgePoints = root.GetComponentsInChildren<ForgedPoint>(true);
                foreach (var renderer in keeper.Visible)
                {
                    renderer.gameObject.layer = root.layer;
                    renderer.forceRenderingOff = false;
                }
            }
            FitBlade(root, bounds);
            Native.Set(part, "length", bounds.size.y);
            Native.Set(root.GetComponent<MeltingModelPart>(), "ingotValue", (float)LucyRules.IngotCost);
            var item = Object.Instantiate(donorItem);
            item.name = LucyRules.SwordPrefab;
            // Avoid editing the native Item's shared component list or using its mould.
            Native.Set(item, "components", donorItem.Components.Where(c => !(c is MouldItemComponent)).ToList());
            root.GetComponent<Pickup>().Item = item;
            var prefab = Native.RegisterClone(mod, root, LucyRules.SwordPrefab);
            item.Prefab = prefab;
            Native.AddValue(item, prefab.Hash);
            if (part.Insert.Type != insertType || part.Insert.transform.localPosition != insertPosition
                || part.Insert.transform.localRotation != insertRotation
                || root.GetComponents<NetworkRigidbody>().Length != 1
                || root.GetComponentsInChildren<ForgedPoint>(true).Length != 7
                || root.GetComponent<DurabilityModule>() == null)
                throw new InvalidOperationException("Lucy blade native assembly validation failed.");
            var slots = Resources.FindObjectsOfTypeAll<JointSlotType>().Where(s => s.AllowedInserts.Contains(insertType)).ToArray();
            if (slots.Length == 0) throw new InvalidOperationException("No native handle socket accepts Lucy's large-sword insert.");
            MelonLogger.Msg("LUCY_BLADE_REGISTERED prefab=" + LucyRules.SwordPrefab + " hash=" + prefab.Hash
                + " donor=63886 blade-length=" + bounds.size.y.ToString("F4") + " forge-points=7 handle-socket-types=" + slots.Length
                + " native-forging/material/durability=retained blade-only=PASS");
            return prefab;
        }

        internal static Vector3 MapPoint(Vector3 point, Bounds from, Bounds to)
        {
            return to.center + Vector3.Scale(point - from.center,
                new Vector3(to.size.x / from.size.x, to.size.y / from.size.y, to.size.z / from.size.z));
        }

        static Bounds LocalBounds(Transform root, MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Lucy blade mesh is missing.");
            var bounds = filter.sharedMesh.bounds;
            var result = new Bounds(root.InverseTransformPoint(filter.transform.TransformPoint(bounds.center)), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(root.InverseTransformPoint(filter.transform.TransformPoint(bounds.center
                + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
            return result;
        }

        static void FitBlade(GameObject root, Bounds target)
        {
            var source = NativeBladeBounds;
            var scale = new Vector3(target.size.x / source.size.x, target.size.y / source.size.y, target.size.z / source.size.z);
            Func<Vector3, Vector3> map = point => MapPoint(point, source, target);
            var colliders = root.GetComponentsInChildren<Collider>(true);
            var matrices = colliders.ToDictionary(c => c, c => c.transform.localToWorldMatrix);
            var points = root.GetComponentsInChildren<Impactor>(true).Select(i => i.transform)
                .Concat(root.GetComponentsInChildren<MetalHeatPoint>(true).Select(i => i.transform)).Distinct().ToArray();
            var positions = points.Select(t => map(root.transform.InverseTransformPoint(t.position))).ToArray();
            // Preserve pre-move collider coordinates: heat-point capsules share
            // transforms with those points and must not be translated twice.
            for (int i = 0; i < points.Length; i++) points[i].position = root.transform.TransformPoint(positions[i]);
            foreach (var impactor in root.GetComponentsInChildren<Impactor>(true))
            {
                var values = Object.Instantiate(impactor.ImpactorValues);
                if (values.EdgeLength > 0) Native.Set(values, "edgeLength", values.EdgeLength * scale.y);
                Native.Set(impactor, "impactorValues", values);
                // Damage/follow parameters stay native. Only edge geometry is fitted.
            }
            foreach (var collider in colliders)
            {
                // Leave the connection and the native loose-blade grasp intact.
                if (collider.GetComponentInParent<JointPoint>() != null || collider.name == "Tight Grip"
                    || collider.name == "Handle Collision") continue;
                var matrix = matrices[collider];
                Func<Vector3, Vector3> center = p => collider.transform.InverseTransformPoint(root.transform.TransformPoint(
                    map(root.transform.InverseTransformPoint(matrix.MultiplyPoint3x4(p)))));
                Func<Vector3, float> factor = axis =>
                {
                    var vector = root.transform.InverseTransformVector(matrix.MultiplyVector(axis));
                    return Vector3.Scale(vector, scale).magnitude / Mathf.Max(vector.magnitude, .000001f);
                };
                if (collider is BoxCollider box)
                {
                    box.center = center(box.center);
                    box.size = Vector3.Scale(box.size, new Vector3(factor(Vector3.right), factor(Vector3.up), factor(Vector3.forward)));
                }
                else if (collider is CapsuleCollider capsule)
                {
                    capsule.center = center(capsule.center);
                    var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    var radialA = capsule.direction == 0 ? Vector3.up : Vector3.right;
                    var radialB = capsule.direction == 2 ? Vector3.up : Vector3.forward;
                    capsule.height *= factor(axis);
                    capsule.radius *= Mathf.Max(factor(radialA), factor(radialB));
                }
                else if (collider is SphereCollider sphere)
                {
                    sphere.center = center(sphere.center);
                    sphere.radius *= Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                }
            }
            // Keep serialized collider/impactor pairs; rebuild only their lazy cache.
            foreach (var mapComponent in root.GetComponentsInChildren<ImpactorMap>(true)) Native.Set(mapComponent, "map", null);
        }
    }

    public sealed class LucyBladeVisual : MonoBehaviour
    {
        public Renderer[] Hidden;
        public Renderer[] Visible;
        public ForgedPoint[] ForgePoints;
        MaterialPropertyBlock block;
        static readonly int Heat = Shader.PropertyToID("_HeatGlow");
        void LateUpdate()
        {
            if (Hidden != null) foreach (var renderer in Hidden) if (renderer != null) renderer.forceRenderingOff = true;
            if (Visible == null || ForgePoints == null || ApplicationManager.IsHeadless) return;
            float glow = 0;
            foreach (var point in ForgePoints) if (point != null && point.HeatPoint != null) glow = Mathf.Max(glow, point.Glow);
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var renderer in Visible)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetFloat(Heat, Mathf.Clamp01(glow));
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
