using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Chunks;
using Alta.Populations;
using Pathfinding;
using UnityEngine;

namespace LucyMouldMod
{
    internal static class LucyPlacement
    {
        // The recovered full sword is 2.483 m long. Lay it flat with support
        // beneath both ends and room around the blade, guard and pickup grip.
        internal static bool TryFind(LocationChunk chunk, System.Random random, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (chunk == null || !chunk.IsFullyLoaded || chunk.Bounds.size.sqrMagnitude < 1f) return false;
            var ecosystem = chunk.GetComponent<Ecosystem>();
            var maps = ecosystem == null ? new PopulationSpawnInformation[0]
                : Native.Get<Dictionary<uint, PopulationSpawnInformation>>(ecosystem, "spawnInformationMap")
                    .Values.Where(info => info is CavePopulationSpawnInformation).Distinct().ToArray();
            var areas = maps.Length == 0 ? UnityEngine.Object.FindObjectsOfType<PopulationSpawnArea>()
                .Where(area => area.Entity != null && area.Entity.Chunk == chunk).ToArray() : new PopulationSpawnArea[0];
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 sample;
                if (!TrySample(chunk, attempt < 8 ? maps : new PopulationSpawnInformation[0], areas, random, out sample)) continue;
                if (!Finite(sample) || !chunk.Bounds.Contains(sample)) continue;
                RaycastHit floor;
                if (!Ground(sample + Vector3.up * 1.5f, 4f, out floor) || floor.normal.y < .85f) continue;
                var point = floor.point;
                if (!chunk.Bounds.Contains(point) || NearPortal(chunk, point)) continue;

                var yaw = Quaternion.Euler(0f, (float)(random.NextDouble() * 360), 0f);
                var forward = yaw * Vector3.forward;
                // Native Lucy's long axis is local Y. Its broad blade face is
                // local XY, so a quarter turn puts it on the floor.
                var pose = yaw * Quaternion.Euler(90f, 0f, 0f);
                bool supported = true;
                foreach (float distance in new[] { -.9f, 0f, .7f, 1.45f })
                {
                    RaycastHit support;
                    if (!Ground(point + forward * distance + Vector3.up * .5f, .8f, out support)
                        || support.normal.y < .85f || Math.Abs(support.point.y - point.y) > .12f
                        || !chunk.Bounds.Contains(support.point))
                    { supported = false; break; }
                }
                if (!supported) continue;
                var root = point + Vector3.up * .20f;
                var center = root + forward * .2777f;
                if (Physics.CheckBox(center, new Vector3(.23f, .13f, 1.35f), yaw, ~0, QueryTriggerInteraction.Ignore)) continue;
                position = root;
                rotation = pose;
                return true;
            }
            return false;
        }

        static bool TrySample(LocationChunk chunk, PopulationSpawnInformation[] maps, PopulationSpawnArea[] areas,
            System.Random random, out Vector3 point)
        {
            point = Vector3.zero;
            if (maps.Length != 0)
            {
                var spawn = maps[random.Next(maps.Length)].GetRandom(null);
                if (spawn != null) { point = spawn.Position; return true; }
            }
            if (areas.Length != 0)
            {
                var area = areas[random.Next(areas.Length)];
                var info = area.PopulationDefinition == null ? null : ((ISpawnSystem)area)[area.PopulationDefinition.Hash];
                var spawn = info == null ? null : info.GetRandom(area.PopulationDefinition);
                if (spawn != null) { point = spawn.Position; return true; }
            }
            if (chunk.Positions != null && chunk.Positions.PositionDefinitions.Count != 0)
            {
                var surface = chunk.Positions.GetPosition(2, .85f);
                if (surface.HasValue) { point = surface.Value.point; return true; }
            }
            // Special, hand-built floors do not necessarily have cave maps.
            // Use the game's walkable navigation surface, never the top of an
            // arbitrary cave ceiling or a guessed world coordinate.
            if (AstarPath.active == null) return false;
            var bounds = chunk.Bounds;
            var guess = new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble()),
                Mathf.Lerp(bounds.min.y, bounds.max.y, (float)random.NextDouble()),
                Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble()));
            var nearest = AstarPath.active.GetNearest(guess, NNConstraint.Default);
            if (nearest.node == null || !nearest.node.Walkable || !bounds.Contains(nearest.position)) return false;
            point = nearest.position;
            return true;
        }

        static bool NearPortal(LocationChunk chunk, Vector3 point)
        {
            if (chunk.Connections != null && chunk.Connections.Portals.Any(portal => portal != null
                && Vector3.Distance(point, portal.transform.position) < 4f)) return true;
            var streamed = chunk.GetComponent<CaveLayerStreamer>();
            var fixedLayer = chunk.GetComponent<StaticCaveChunk>();
            Transform portal = streamed != null ? streamed.PortalTransform : fixedLayer != null ? fixedLayer.PortalTransform : null;
            return portal != null && Vector3.Distance(point, portal.position) < 4f;
        }

        static bool Ground(Vector3 from, float distance, out RaycastHit hit)
        {
            foreach (var candidate in Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(value => value.distance))
            {
                // A sword must not be placed on another pickup or a creature.
                if (candidate.collider == null || candidate.collider.attachedRigidbody != null
                    || candidate.collider.GetComponentInParent<Pickup>() != null) continue;
                hit = candidate;
                return true;
            }
            hit = default(RaycastHit);
            return false;
        }

        static bool Finite(Vector3 point)
        {
            return !(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
                || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z));
        }
    }
}
