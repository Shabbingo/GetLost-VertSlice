using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    public readonly struct TerrainSample
    {
        private readonly float[] layerWeights;

        public TerrainSample(
            Vector3 worldPosition,
            Vector3 worldNormal,
            float[] layerWeights)
        {
            WorldPosition = worldPosition;
            WorldNormal = worldNormal;
            this.layerWeights = layerWeights;
        }

        public Vector3 WorldPosition { get; }
        public Vector3 WorldNormal { get; }
        public int LayerCount => layerWeights != null ? layerWeights.Length : 0;

        public float GetLayerWeight(int layerIndex)
        {
            if (layerWeights == null ||
                layerIndex < 0 ||
                layerIndex >= layerWeights.Length)
            {
                return 0f;
            }

            return layerWeights[layerIndex];
        }

        public float[] CopyLayerWeights()
        {
            if (layerWeights == null)
            {
                return System.Array.Empty<float>();
            }

            float[] copy = new float[layerWeights.Length];
            System.Array.Copy(layerWeights, copy, layerWeights.Length);
            return copy;
        }
    }
}
