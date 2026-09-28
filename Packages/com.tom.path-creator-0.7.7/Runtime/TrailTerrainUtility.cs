using System.Collections.Generic;
using UnityEngine;

namespace Tom.PathCreator
{
    /// <summary>
    /// Shared helpers for locating Unity Terrain chunks from world positions.
    /// Supports trails that cross any number of adjacent terrain tiles.
    /// </summary>
    public static class TrailTerrainUtility
    {
        public static Terrain FindTerrainAt(Vector3 worldPosition)
        {
            Terrain[] terrains = Terrain.activeTerrains;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (ContainsXZ(terrain, worldPosition))
                {
                    return terrain;
                }
            }

            return null;
        }

        public static bool ContainsXZ(
            Terrain terrain,
            Vector3 worldPosition,
            float padding = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            return worldPosition.x >= origin.x - padding &&
                   worldPosition.z >= origin.z - padding &&
                   worldPosition.x <= origin.x + size.x + padding &&
                   worldPosition.z <= origin.z + size.z + padding;
        }

        public static List<Terrain> FindTerrainsForPath(
            IReadOnlyList<Vector3> samples,
            float padding)
        {
            var results = new List<Terrain>();
            Terrain[] terrains = Terrain.activeTerrains;

            if (samples == null || samples.Count == 0)
            {
                return results;
            }

            for (int terrainIndex = 0;
                terrainIndex < terrains.Length;
                terrainIndex++)
            {
                Terrain terrain = terrains[terrainIndex];

                if (terrain == null || terrain.terrainData == null)
                {
                    continue;
                }

                for (int sampleIndex = 0;
                    sampleIndex < samples.Count;
                    sampleIndex++)
                {
                    if (!ContainsXZ(
                        terrain,
                        samples[sampleIndex],
                        padding))
                    {
                        continue;
                    }

                    results.Add(terrain);
                    break;
                }
            }

            return results;
        }

        public static Vector3 ProjectToTerrain(
            Vector3 worldPosition,
            Terrain fallbackTerrain = null)
        {
            Terrain terrain =
                FindTerrainAt(worldPosition) ?? fallbackTerrain;

            if (terrain == null || terrain.terrainData == null)
            {
                return worldPosition;
            }

            if (!ContainsXZ(terrain, worldPosition))
            {
                return worldPosition;
            }

            worldPosition.y =
                terrain.SampleHeight(worldPosition) +
                terrain.transform.position.y;

            return worldPosition;
        }
    }
}
