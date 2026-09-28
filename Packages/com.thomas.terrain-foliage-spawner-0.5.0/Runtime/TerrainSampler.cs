using System;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    public sealed class TerrainSampler
    {
        private readonly Terrain terrain;
        private readonly TerrainData terrainData;
        private readonly Vector3 terrainPosition;

        public TerrainSampler(Terrain terrain)
        {
            this.terrain = terrain != null
                ? terrain
                : throw new ArgumentNullException(nameof(terrain));

            terrainData = terrain.terrainData != null
                ? terrain.terrainData
                : throw new InvalidOperationException(
                    "The assigned Terrain does not have TerrainData.");

            terrainPosition = terrain.transform.position;
        }

        public TerrainData TerrainData => terrainData;

        public bool ContainsWorldXZ(float worldX, float worldZ)
        {
            float localX = worldX - terrainPosition.x;
            float localZ = worldZ - terrainPosition.z;

            return localX >= 0f &&
                   localZ >= 0f &&
                   localX <= terrainData.size.x &&
                   localZ <= terrainData.size.z;
        }

        public float SampleWorldHeight(float worldX, float worldZ)
        {
            float normalizedX = Mathf.InverseLerp(
                terrainPosition.x,
                terrainPosition.x + terrainData.size.x,
                worldX);

            float normalizedZ = Mathf.InverseLerp(
                terrainPosition.z,
                terrainPosition.z + terrainData.size.z,
                worldZ);

            return terrainPosition.y + terrainData.GetInterpolatedHeight(
                Mathf.Clamp01(normalizedX),
                Mathf.Clamp01(normalizedZ));
        }

        public TerrainSample Sample(float worldX, float worldZ)
        {
            float normalizedX = Mathf.InverseLerp(
                terrainPosition.x,
                terrainPosition.x + terrainData.size.x,
                worldX);

            float normalizedZ = Mathf.InverseLerp(
                terrainPosition.z,
                terrainPosition.z + terrainData.size.z,
                worldZ);

            normalizedX = Mathf.Clamp01(normalizedX);
            normalizedZ = Mathf.Clamp01(normalizedZ);

            int alphamapX = Mathf.Clamp(
                Mathf.RoundToInt(normalizedX * (terrainData.alphamapWidth - 1)),
                0,
                terrainData.alphamapWidth - 1);

            int alphamapY = Mathf.Clamp(
                Mathf.RoundToInt(normalizedZ * (terrainData.alphamapHeight - 1)),
                0,
                terrainData.alphamapHeight - 1);

            // Unity returns [height, width, layer]. For a 1x1 query,
            // the requested layer weights are always at [0, 0, layer].
            float[,,] alphamap = terrainData.GetAlphamaps(
                alphamapX,
                alphamapY,
                1,
                1);

            int layerCount = alphamap.GetLength(2);
            float[] layerWeights = new float[layerCount];

            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                layerWeights[layerIndex] = alphamap[0, 0, layerIndex];
            }

            float localHeight = terrainData.GetInterpolatedHeight(
                normalizedX,
                normalizedZ);

            Vector3 worldPosition = new Vector3(
                worldX,
                terrainPosition.y + localHeight,
                worldZ);

            Vector3 worldNormal = terrainData
                .GetInterpolatedNormal(normalizedX, normalizedZ)
                .normalized;

            return new TerrainSample(
                worldPosition,
                worldNormal,
                layerWeights);
        }

        public int FindLayerIndex(TerrainLayer targetLayer)
        {
            if (targetLayer == null)
            {
                return -1;
            }

            TerrainLayer[] terrainLayers = terrainData.terrainLayers;

            for (int index = 0; index < terrainLayers.Length; index++)
            {
                if (terrainLayers[index] == targetLayer)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
