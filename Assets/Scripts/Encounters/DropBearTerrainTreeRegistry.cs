using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Encounters
{
    /// <summary>
    /// Spatial index of the actual tree instances painted onto active terrains.
    /// Built once per scene so escape queries do not scan every tree during combat.
    /// </summary>
    internal static class DropBearTerrainTreeRegistry
    {
        internal readonly struct TreeSite
        {
            internal readonly Vector3 Base;
            internal readonly Vector3 Perch;

            internal TreeSite(Vector3 treeBase, Vector3 perch)
            {
                Base = treeBase;
                Perch = perch;
            }
        }

        private const float CellSize = 32f;
        private static readonly Dictionary<Vector2Int, List<TreeSite>> Cells = new();
        private static readonly List<TreeSite> AllSites = new();
        private static bool built;
        private static int lastTreeSignature;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Cells.Clear();
            AllSites.Clear();
            built = false;
            lastTreeSignature = 0;
        }

        internal static bool TryFindNearest(Vector3 position, float minimumDistance,
            float maximumDistance, out TreeSite result)
        {
            EnsureBuilt();
            result = default;
            float bestSqrDistance = maximumDistance * maximumDistance;
            bool found = false;
            int minX = Mathf.FloorToInt((position.x - maximumDistance) / CellSize);
            int maxX = Mathf.FloorToInt((position.x + maximumDistance) / CellSize);
            int minZ = Mathf.FloorToInt((position.z - maximumDistance) / CellSize);
            int maxZ = Mathf.FloorToInt((position.z + maximumDistance) / CellSize);
            float minimumSqrDistance = minimumDistance * minimumDistance;

            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!Cells.TryGetValue(new Vector2Int(x, z), out List<TreeSite> sites))
                    continue;
                foreach (TreeSite site in sites)
                {
                    Vector2 offset = new(site.Base.x - position.x, site.Base.z - position.z);
                    float sqrDistance = offset.sqrMagnitude;
                    if (sqrDistance < minimumSqrDistance || sqrDistance >= bestSqrDistance)
                        continue;
                    result = site;
                    bestSqrDistance = sqrDistance;
                    found = true;
                }
            }
            return found;
        }

        internal static IReadOnlyList<TreeSite> GetAllSites()
        {
            EnsureBuilt();
            return AllSites;
        }

        private static void EnsureBuilt()
        {
            int signature = CalculateTreeSignature();
            if (built && signature == lastTreeSignature)
                return;
            built = true;
            lastTreeSignature = signature;
            Cells.Clear();
            AllSites.Clear();

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;
                TerrainData data = terrain.terrainData;
                TreePrototype[] prototypes = data.treePrototypes;
                if (prototypes == null || prototypes.Length == 0)
                    continue;

                float[] prototypeHeights = new float[prototypes.Length];
                for (int i = 0; i < prototypes.Length; i++)
                    prototypeHeights[i] = EstimatePrototypeHeight(prototypes[i]);

                Vector3 terrainSize = data.size;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypeHeights.Length)
                        continue;
                    float height = prototypeHeights[tree.prototypeIndex] * tree.heightScale;
                    // Excludes grass, shrubs and tiny decorative instances.
                    if (height < 3.2f)
                        continue;
                    Vector3 local = Vector3.Scale(tree.position, terrainSize);
                    Vector3 treeBase = terrain.transform.TransformPoint(local);
                    Vector3 perch = treeBase + Vector3.up * Mathf.Clamp(height * 0.62f, 2.5f, 11f);
                    var site = new TreeSite(treeBase, perch);
                    AllSites.Add(site);
                    Vector2Int cell = new(
                        Mathf.FloorToInt(treeBase.x / CellSize),
                        Mathf.FloorToInt(treeBase.z / CellSize));
                    if (!Cells.TryGetValue(cell, out List<TreeSite> sites))
                    {
                        sites = new List<TreeSite>();
                        Cells.Add(cell, sites);
                    }
                    sites.Add(site);
                }
            }
        }

        private static int CalculateTreeSignature()
        {
            unchecked
            {
                int signature = 17;
                Terrain[] terrains = Terrain.activeTerrains;
                signature = signature * 31 + terrains.Length;
                foreach (Terrain terrain in terrains)
                {
                    int count = terrain != null && terrain.terrainData != null
                        ? terrain.terrainData.treeInstanceCount
                        : 0;
                    signature = signature * 31 + count;
                }
                return signature;
            }
        }

        private static float EstimatePrototypeHeight(TreePrototype prototype)
        {
            if (prototype.prefab == null)
                return 5f;
            Renderer[] renderers = prototype.prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return 5f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return Mathf.Clamp(bounds.size.y, 1f, 24f);
        }
    }
}
