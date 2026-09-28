using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Samples painted Unity Terrain details around the player and reports them
    /// to VegetationInteractionReceiver using prototype-asset mappings.
    ///
    /// Optimized to avoid repeated Terrain API work while the player is standing
    /// still or has barely moved.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VegetationInteractionReceiver))]
    public sealed class TerrainDetailVegetationProvider : MonoBehaviour
    {
        private sealed class EntrySourceKey { }

        private sealed class CachedLayer
        {
            public int layerIndex;
            public TerrainDetailVegetationMapping.Entry entry;
            public object sourceKey;
        }

        [Header("References")]
        [SerializeField] private TerrainDetailVegetationMapping mapping;
        [SerializeField] private Terrain terrainOverride;
        [SerializeField] private VegetationInteractionReceiver receiver;

        [Header("Sampling")]
        [SerializeField, Min(0.05f)] private float sampleRadius = 0.75f;

        [Tooltip("Minimum time between detail samples while moving.")]
        [SerializeField, Min(0.02f)] private float sampleInterval = 0.2f;

        [Tooltip("Minimum horizontal distance the player must move before a new Terrain detail sample is allowed.")]
        [SerializeField, Min(0.01f)] private float minimumMoveDistance = 0.35f;

        [Tooltip("Forces an occasional refresh even if the player has barely moved. Set higher for best performance.")]
        [SerializeField, Min(0.1f)] private float maximumSampleAge = 1.0f;

        [SerializeField, Min(1f)] private float densityForFullEffect = 8f;

        private readonly Dictionary<TerrainDetailVegetationMapping.Entry, object>
            sourceKeys = new();

        private readonly HashSet<object>
            reportedThisSample = new();

        private readonly List<CachedLayer>
            cachedLayers = new();

        private Terrain activeTerrain;
        private TerrainData cachedTerrainData;

        private float nextSampleTime;
        private float lastSampleTime = -999f;
        private Vector3 lastSamplePosition;
        private bool hasSampled;

        private void Reset()
        {
            receiver =
                GetComponent<VegetationInteractionReceiver>();
        }

        private void Awake()
        {
            if (receiver == null)
            {
                receiver =
                    GetComponent<VegetationInteractionReceiver>();
            }
        }

        private void OnDisable()
        {
            ClearAllSources();
            cachedLayers.Clear();
            activeTerrain = null;
            cachedTerrainData = null;
            hasSampled = false;
        }

        private void Update()
        {
            float now =
                Time.unscaledTime;

            if (now < nextSampleTime)
                return;

            Vector3 position =
                transform.position;

            Vector3 delta =
                position - lastSamplePosition;

            float horizontalSqr =
                delta.x * delta.x +
                delta.z * delta.z;

            float minimumMoveDistanceSqr =
                minimumMoveDistance *
                minimumMoveDistance;

            bool movedEnough =
                !hasSampled ||
                horizontalSqr >=
                minimumMoveDistanceSqr;

            bool sampleTooOld =
                !hasSampled ||
                now - lastSampleTime >=
                maximumSampleAge;

            if (!movedEnough &&
                !sampleTooOld)
            {
                nextSampleTime =
                    now + sampleInterval;
                return;
            }

            nextSampleTime =
                now + sampleInterval;

            SampleTerrainDetails(position, now);
        }

        private void SampleTerrainDetails(
            Vector3 worldPosition,
            float now)
        {
            reportedThisSample.Clear();

            if (receiver == null ||
                mapping == null ||
                mapping.entries == null ||
                mapping.entries.Count == 0)
            {
                ClearAllSources();
                RememberSample(worldPosition, now);
                return;
            }

            Terrain terrain =
                ResolveTerrainCached(worldPosition);

            if (terrain == null ||
                terrain.terrainData == null)
            {
                ClearAllSources();
                RememberSample(worldPosition, now);
                return;
            }

            TerrainData data =
                terrain.terrainData;

            if (data != cachedTerrainData)
            {
                cachedTerrainData = data;
                RebuildLayerCache(data);
            }

            Vector3 local =
                worldPosition -
                terrain.transform.position;

            Vector3 size =
                data.size;

            if (local.x < 0f ||
                local.z < 0f ||
                local.x > size.x ||
                local.z > size.z)
            {
                ClearAllSources();
                RememberSample(worldPosition, now);
                return;
            }

            int centreX =
                Mathf.FloorToInt(
                    local.x /
                    size.x *
                    data.detailWidth);

            int centreZ =
                Mathf.FloorToInt(
                    local.z /
                    size.z *
                    data.detailHeight);

            int radiusX =
                Mathf.Max(
                    0,
                    Mathf.CeilToInt(
                        sampleRadius /
                        size.x *
                        data.detailWidth));

            int radiusZ =
                Mathf.Max(
                    0,
                    Mathf.CeilToInt(
                        sampleRadius /
                        size.z *
                        data.detailHeight));

            int xBase =
                Mathf.Clamp(
                    centreX - radiusX,
                    0,
                    data.detailWidth - 1);

            int zBase =
                Mathf.Clamp(
                    centreZ - radiusZ,
                    0,
                    data.detailHeight - 1);

            int width =
                Mathf.Clamp(
                    radiusX * 2 + 1,
                    1,
                    data.detailWidth - xBase);

            int height =
                Mathf.Clamp(
                    radiusZ * 2 + 1,
                    1,
                    data.detailHeight - zBase);

            // Only mapped Terrain detail layers are sampled.
            for (int i = 0; i < cachedLayers.Count; i++)
            {
                CachedLayer cached =
                    cachedLayers[i];

                if (cached.layerIndex < 0 ||
                    cached.layerIndex >=
                    data.detailPrototypes.Length ||
                    cached.entry == null ||
                    cached.entry.vegetationProfile == null)
                {
                    continue;
                }

                int[,] density =
                    data.GetDetailLayer(
                        xBase,
                        zBase,
                        width,
                        height,
                        cached.layerIndex);

                float sum = 0f;

                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        sum +=
                            density[z, x];
                    }
                }

                float averageDensity =
                    sum /
                    Mathf.Max(
                        1,
                        width * height);

                float normalizedDensity =
                    Mathf.Clamp01(
                        averageDensity /
                        densityForFullEffect);

                if (normalizedDensity > 0.001f)
                {
                    receiver.SetTransientSource(
                        cached.sourceKey,
                        cached.entry.vegetationProfile,
                        normalizedDensity);

                    reportedThisSample.Add(
                        cached.sourceKey);
                }
                else
                {
                    receiver.RemoveSource(
                        cached.sourceKey);
                }
            }

            foreach (object sourceKey
                     in sourceKeys.Values)
            {
                if (!reportedThisSample.Contains(
                        sourceKey))
                {
                    receiver.RemoveSource(
                        sourceKey);
                }
            }

            RememberSample(worldPosition, now);
        }

        private Terrain ResolveTerrainCached(
            Vector3 worldPosition)
        {
            if (terrainOverride != null)
            {
                return Contains(
                    terrainOverride,
                    worldPosition)
                    ? terrainOverride
                    : null;
            }

            if (activeTerrain != null &&
                Contains(
                    activeTerrain,
                    worldPosition))
            {
                return activeTerrain;
            }

            activeTerrain = null;

            Terrain[] terrains =
                Terrain.activeTerrains;

            for (int i = 0;
                 i < terrains.Length;
                 i++)
            {
                Terrain candidate =
                    terrains[i];

                if (!Contains(
                        candidate,
                        worldPosition))
                {
                    continue;
                }

                activeTerrain =
                    candidate;

                return activeTerrain;
            }

            return null;
        }

        private void RebuildLayerCache(
            TerrainData data)
        {
            cachedLayers.Clear();

            if (data == null ||
                mapping == null)
            {
                return;
            }

            DetailPrototype[] prototypes =
                data.detailPrototypes;

            for (int layerIndex = 0;
                 layerIndex < prototypes.Length;
                 layerIndex++)
            {
                if (!mapping.TryGetEntry(
                        prototypes[layerIndex],
                        out TerrainDetailVegetationMapping.Entry entry))
                {
                    continue;
                }

                cachedLayers.Add(
                    new CachedLayer
                    {
                        layerIndex = layerIndex,
                        entry = entry,
                        sourceKey = GetSourceKey(entry)
                    });
            }
        }

        private void RememberSample(
            Vector3 position,
            float now)
        {
            lastSamplePosition =
                position;

            lastSampleTime =
                now;

            hasSampled =
                true;
        }

        private object GetSourceKey(
            TerrainDetailVegetationMapping.Entry entry)
        {
            if (!sourceKeys.TryGetValue(
                    entry,
                    out object key))
            {
                key =
                    new EntrySourceKey();

                sourceKeys.Add(
                    entry,
                    key);
            }

            return key;
        }

        private void ClearAllSources()
        {
            if (receiver == null)
                return;

            foreach (object sourceKey
                     in sourceKeys.Values)
            {
                receiver.RemoveSource(
                    sourceKey);
            }
        }

        private static bool Contains(
            Terrain terrain,
            Vector3 worldPosition)
        {
            if (terrain == null ||
                terrain.terrainData == null)
            {
                return false;
            }

            Vector3 origin =
                terrain.transform.position;

            Vector3 size =
                terrain.terrainData.size;

            return worldPosition.x >= origin.x &&
                   worldPosition.z >= origin.z &&
                   worldPosition.x <= origin.x + size.x &&
                   worldPosition.z <= origin.z + size.z;
        }

        private void OnValidate()
        {
            sampleRadius =
                Mathf.Max(
                    0.05f,
                    sampleRadius);

            sampleInterval =
                Mathf.Max(
                    0.02f,
                    sampleInterval);

            minimumMoveDistance =
                Mathf.Max(
                    0.01f,
                    minimumMoveDistance);

            maximumSampleAge =
                Mathf.Max(
                    0.1f,
                    maximumSampleAge);

            densityForFullEffect =
                Mathf.Max(
                    1f,
                    densityForFullEffect);
        }
    }
}