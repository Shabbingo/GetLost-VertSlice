using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Resolves the current surface from either a TerrainSurface component or
    /// the dominant painted TerrainLayer underneath the player.
    ///
    /// Terrain alphamaps are intentionally not sampled every frame. The last
    /// terrain result is cached until the player has moved far enough, enough
    /// time has elapsed, or the underlying Terrain/collider changes.
    /// </summary>
    public sealed class TerrainSurfaceResolver : MonoBehaviour
    {
        [SerializeField] private TerrainLayerSurfaceMap terrainLayerMap;
        [SerializeField] private TerrainSurfaceProfile defaultProfile;

        [Header("Terrain Sampling")]
        [Tooltip("Minimum horizontal distance the player must move before the painted TerrainLayer is sampled again.")]
        [SerializeField, Min(0.05f)] private float terrainResampleDistance = 0.5f;

        [Tooltip("Maximum time a cached Terrain surface may be reused while grounded. This catches texture changes even when the player barely moves.")]
        [SerializeField, Min(0.05f)] private float maximumTerrainSampleAge = 0.5f;

        public TerrainSurfaceProfile DefaultProfile => defaultProfile;

        private Collider cachedCollider;
        private TerrainSurface cachedExplicitSurface;

        private Terrain cachedTerrain;
        private TerrainData cachedTerrainData;
        private Vector3 lastTerrainSamplePosition;
        private float lastTerrainSampleTime = -999f;
        private TerrainSurfaceProfile cachedTerrainProfile;

        public TerrainSurfaceProfile Resolve(RaycastHit hit)
        {
            if (hit.collider == null)
                return defaultProfile;

            // Only search the hierarchy again when the hit collider changes.
            if (hit.collider != cachedCollider)
            {
                cachedCollider = hit.collider;
                cachedExplicitSurface =
                    hit.collider.GetComponentInParent<TerrainSurface>();
            }

            if (cachedExplicitSurface != null &&
                cachedExplicitSurface.Profile != null)
            {
                return cachedExplicitSurface.Profile;
            }

            Terrain terrain = hit.collider.GetComponent<Terrain>();

            if (terrain == null || terrain.terrainData == null)
            {
                cachedTerrain = null;
                cachedTerrainData = null;
                cachedTerrainProfile = null;
                return defaultProfile;
            }

            bool terrainChanged =
                terrain != cachedTerrain ||
                terrain.terrainData != cachedTerrainData;

            Vector3 delta = hit.point - lastTerrainSamplePosition;
            float horizontalSqr =
                delta.x * delta.x +
                delta.z * delta.z;

            float resampleDistanceSqr =
                terrainResampleDistance * terrainResampleDistance;

            bool movedEnough =
                horizontalSqr >= resampleDistanceSqr;

            bool sampleTooOld =
                Time.unscaledTime - lastTerrainSampleTime >=
                maximumTerrainSampleAge;

            if (terrainChanged ||
                cachedTerrainProfile == null ||
                movedEnough ||
                sampleTooOld)
            {
                cachedTerrain = terrain;
                cachedTerrainData = terrain.terrainData;
                lastTerrainSamplePosition = hit.point;
                lastTerrainSampleTime = Time.unscaledTime;

                TerrainLayer layer =
                    GetDominantLayer(terrain, hit.point);

                TerrainSurfaceProfile mapped =
                    terrainLayerMap != null
                        ? terrainLayerMap.FindProfile(layer)
                        : null;

                cachedTerrainProfile =
                    mapped != null
                        ? mapped
                        : defaultProfile;
            }

            return cachedTerrainProfile != null
                ? cachedTerrainProfile
                : defaultProfile;
        }

        public void InvalidateCache()
        {
            cachedCollider = null;
            cachedExplicitSurface = null;
            cachedTerrain = null;
            cachedTerrainData = null;
            cachedTerrainProfile = null;
            lastTerrainSampleTime = -999f;
        }

        private static TerrainLayer GetDominantLayer(
            Terrain terrain,
            Vector3 worldPoint)
        {
            TerrainData data = terrain.terrainData;
            Vector3 local =
                worldPoint - terrain.transform.position;

            float normalizedX =
                Mathf.Clamp01(local.x / data.size.x);

            float normalizedZ =
                Mathf.Clamp01(local.z / data.size.z);

            int mapX =
                Mathf.Clamp(
                    Mathf.RoundToInt(
                        normalizedX *
                        (data.alphamapWidth - 1)),
                    0,
                    data.alphamapWidth - 1);

            int mapZ =
                Mathf.Clamp(
                    Mathf.RoundToInt(
                        normalizedZ *
                        (data.alphamapHeight - 1)),
                    0,
                    data.alphamapHeight - 1);

            float[,,] weights =
                data.GetAlphamaps(
                    mapX,
                    mapZ,
                    1,
                    1);

            int dominantIndex = 0;
            float dominantWeight =
                weights[0, 0, 0];

            int layerCount =
                weights.GetLength(2);

            for (int i = 1; i < layerCount; i++)
            {
                float weight =
                    weights[0, 0, i];

                if (weight > dominantWeight)
                {
                    dominantWeight = weight;
                    dominantIndex = i;
                }
            }

            TerrainLayer[] layers =
                data.terrainLayers;

            return dominantIndex >= 0 &&
                   dominantIndex < layers.Length
                ? layers[dominantIndex]
                : null;
        }

        private void OnValidate()
        {
            terrainResampleDistance =
                Mathf.Max(
                    0.05f,
                    terrainResampleDistance);

            maximumTerrainSampleAge =
                Mathf.Max(
                    0.05f,
                    maximumTerrainSampleAge);
        }
    }
}