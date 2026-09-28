using UnityEngine;

namespace GetLost.Trails
{
    [CreateAssetMenu(
        fileName = "TrailSystemSettings",
        menuName = "Get Lost/Trails/Trail System Settings")]
    public class TrailSystemSettings : ScriptableObject
    {
        [Header("Recording")]
        [Min(0.1f)] public float sampleSpacing = 1.5f;
        [Min(0.02f)] public float sampleInterval = 0.15f;
        [Min(2)] public int minimumSamplesToFinish = 8;

        [Header("Trail Width")]
        [Min(0.1f)] public float trailWidth = 2.0f;
        [Min(0f)] public float textureBlendWidth = 1.0f;

        [Header("Terrain Deformation")]
        public bool deformTerrain = true;

        [Tooltip("How far below the sampled centreline terrain height the finished trail surface sits.")]
        [Min(0f)]
        public float lowerByMetres = 0.08f;

        [Tooltip("How strongly the trail surface is flattened toward the centreline height.")]
        [Range(0f, 1f)]
        public float flattenStrength = 1f;

        [Tooltip("Maximum amount the system is allowed to raise terrain when creating a flatter trail bench.")]
        [Min(0f)]
        public float maximumFillMetres = 0.25f;

        [Tooltip("Maximum amount the system is allowed to cut terrain downward when creating the trail bench.")]
        [Min(0f)]
        public float maximumCutMetres = 0.75f;

        [Tooltip("Distance outside the trail edge used to blend the benched trail back into the original terrain.")]
        [Min(0f)]
        public float deformationFalloffWidth = 0.8f;

        [Header("Path Texture")]
        public bool paintPathTexture = true;
        [Tooltip("Zero-based Terrain Layer index. Example: the 5th visible layer is index 4.")]
        [Min(0)] public int pathTerrainLayerIndex = 1;
        [Range(0f, 1f)] public float pathTextureStrength = 1f;

        [Header("Foliage Clearance")]
        public bool clearFoliage = true;
        [Min(0f)] public float foliageClearancePadding = 0.75f;
        [Min(0f)] public float terrainDetailClearancePadding = 1.5f;
        public bool clearTerrainTrees = true;
        public bool clearTerrainDetails = true;
        [Tooltip("Keep mesh detail prototypes, which may represent rocks or large plants.")]
        public bool preserveMeshTerrainDetails;
        public bool clearSpawnedGameObjects = true;

        [Header("Build Performance")]
        [Tooltip("Approximate length of each local trail section.")]
        [Min(2f)]
        public float buildSectionLength = 15f;

        [Tooltip("Maximum approximate CPU time the Trail Builder may use per frame, in milliseconds.")]
        [Range(0.5f, 10f)]
        public float buildTimeBudgetMs = 3f;

        [Tooltip("Adds one overlapping recorded point on each side of a section.")]
        public bool overlapSectionEdges = true;

        [Tooltip("Use Unity's delayed height LOD update while building, then sync once after deformation work finishes.")]
        public bool useDelayedHeightmapLOD = true;

        [Header("Editor Testing")]
        public bool restoreTerrainAfterPlayMode = true;
        public bool clearTrailSaveOnPlayModeStart = true;

        [Header("Save")]
        public string saveFileName = "getlost_trails.json";

        [Header("Debug")]
        public bool drawRecordedPath = true;
        public bool logBuildProgress = false;
    }
}
