using System;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    public enum TerrainFoliageOutputMode
    {
        GameObject,
        TerrainDetail,
        TerrainTree,
        InstancedGrass
    }

    public enum TerrainGameObjectStorageMode
    {
        SceneObjects,
        RuntimeStreamed
    }

    public enum TerrainDetailConcentration
    {
        Normal,
        Dense,
        VeryDense,
        Custom
    }

    [Serializable]
    public sealed class TerrainFoliagePrefabEntry
    {
        [Tooltip("Prefab used for GameObject output or matched against an existing Terrain prototype.")]
        public GameObject prefab;

        [Min(0f)] public float weight = 1f;
        public TerrainFoliageOutputMode outputMode = TerrainFoliageOutputMode.GameObject;

        [Header("GameObject Placement")]
        [Tooltip("Scene Objects creates one serialized GameObject per placement. Runtime Streamed stores compact placement data and spawns only nearby instances during play.")]
        public TerrainGameObjectStorageMode gameObjectStorageMode = TerrainGameObjectStorageMode.SceneObjects;
        [Tooltip("Uses this GameObject entry as a large structural panel when its rule builds continuous cliff faces.")]
        public bool useForCliffFaces;
        public bool alignToTerrainNormal = true;
        public bool randomYRotation = true;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
        public Vector3 scaleMultiplier = Vector3.one;

        [Header("Existing Terrain Detail")]
        [Range(1,16)] public int detailDensity = 1;
        public TerrainDetailConcentration detailConcentration =
            TerrainDetailConcentration.Normal;

        [Tooltip("Radius in detail-map cells used by Custom concentration.")]
        [Range(0, 8)] public int customDetailRadius = 2;

        [Tooltip("How quickly density fades toward the edge of a Custom patch. 0 keeps the patch even; 1 fades strongly.")]
        [Range(0f, 1f)] public float customDetailFalloff = 0.35f;

        [Tooltip("Allows this Terrain Detail entry to expand into broad patches when its rule enables Gentle Ground Meadows.")]
        public bool allowMeadowExpansion = true;

        [Header("Existing Terrain Tree")]
        public bool treeRandomRotation = true;
        [Min(0.01f)] public float treeScaleMultiplier = 1f;
        public Color treeColor = Color.white;
        public Color treeLightmapColor = Color.white;

        [Header("Instanced Grass")]
        [Tooltip("Exact mesh instances generated around each accepted foliage sample.")]
        [Range(1, 16)] public int grassInstancesPerSample = 4;

        [Tooltip("World-space radius used to spread grass around its accepted sample. Final positions are individually terrain- and path-tested.")]
        [Min(0f)] public float grassSpread = 2.25f;

        [Min(0.01f)] public float grassMinimumWidthScale = 0.8f;
        [Min(0.01f)] public float grassMaximumWidthScale = 1.25f;
        [Min(0.01f)] public float grassMinimumHeightScale = 0.85f;
        [Min(0.01f)] public float grassMaximumHeightScale = 1.4f;
        public bool grassAlignToTerrainNormal = true;
        public bool grassRandomYRotation = true;
        public float grassGroundOffset = 0f;

        [SerializeField, HideInInspector] private float detailMinWidth = 0.8f;
        [SerializeField, HideInInspector] private float detailMaxWidth = 1.2f;
        [SerializeField, HideInInspector] private float detailMinHeight = 0.8f;
        [SerializeField, HideInInspector] private float detailMaxHeight = 1.2f;
        [SerializeField, HideInInspector] private float detailNoiseSpread = 0.2f;
        [SerializeField, HideInInspector] private Color detailHealthyColor = Color.white;
        [SerializeField, HideInInspector] private Color detailDryColor = Color.white;

        public void Validate()
        {
            weight = Mathf.Max(0f, weight);
            scaleMultiplier.x = Mathf.Max(0.01f, scaleMultiplier.x);
            scaleMultiplier.y = Mathf.Max(0.01f, scaleMultiplier.y);
            scaleMultiplier.z = Mathf.Max(0.01f, scaleMultiplier.z);
            detailDensity = Mathf.Clamp(detailDensity, 1, 16);
            customDetailRadius = Mathf.Clamp(customDetailRadius, 0, 8);
            customDetailFalloff = Mathf.Clamp01(customDetailFalloff);
            treeScaleMultiplier = Mathf.Max(0.01f, treeScaleMultiplier);
            grassInstancesPerSample = Mathf.Clamp(grassInstancesPerSample, 1, 16);
            grassSpread = Mathf.Max(0f, grassSpread);
            grassMinimumWidthScale = Mathf.Max(0.01f, grassMinimumWidthScale);
            grassMaximumWidthScale = Mathf.Max(grassMinimumWidthScale, grassMaximumWidthScale);
            grassMinimumHeightScale = Mathf.Max(0.01f, grassMinimumHeightScale);
            grassMaximumHeightScale = Mathf.Max(grassMinimumHeightScale, grassMaximumHeightScale);
        }
    }
}
