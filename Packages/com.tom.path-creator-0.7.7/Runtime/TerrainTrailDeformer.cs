using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.PathCreator
{
    /// <summary>
    /// Cuts and fills Unity Terrain heightmaps beneath a trail.
    ///
    /// The centre follows the original ground height along the path, removing
    /// sideways bumps and cross-slope while preserving the broad uphill and
    /// downhill shape of the route. The deformation blends back into the
    /// untouched terrain over the shoulder width.
    ///
    /// Every active Terrain chunk touched by the trail is handled automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainTrailDeformer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        private PathCreator path;

        [Header("Trail Bed")]
        [Tooltip("Half-width of the flattened trail bed.")]
        [SerializeField, Min(0.05f)]
        private float bedHalfWidth = 1f;

        [Tooltip(
            "Additional distance used to blend the modified trail bed into " +
            "the untouched terrain.")]
        [SerializeField, Min(0.05f)]
        private float shoulderWidth = 1.5f;

        [Range(0f, 1f)]
        [SerializeField]
        private float strength = 1f;

        [Tooltip(
            "Raises or lowers the finished trail bed relative to the sampled " +
            "terrain centreline.")]
        [SerializeField]
        private float bedHeightOffset = -0.02f;

        [Header("Safety Limits")]
        [Tooltip("Maximum amount of terrain that may be removed at one point.")]
        [SerializeField, Min(0f)]
        private float maximumCutDepth = 1.5f;

        [Tooltip("Maximum amount of terrain that may be added at one point.")]
        [SerializeField, Min(0f)]
        private float maximumFillHeight = 1.5f;

        [Header("Sampling")]
        [SerializeField, Min(0.1f)]
        private float pathSampleSpacing = 0.5f;

        [Tooltip(
            "Extra spline evaluation quality. Higher values improve tight " +
            "corners but increase establishment cost.")]
        [SerializeField, Min(0.25f)]
        private float pathResolution = 2f;

        [Header("Runtime")]
        [SerializeField]
        private bool allowRuntimeDeformation = true;

        [Tooltip(
            "Captures the original heightmap before the first runtime change " +
            "and restores it automatically when Play Mode ends.")]
        [SerializeField]
        private bool restoreTerrainAfterPlayMode = true;

        [SerializeField]
        private bool syncTerrainImmediately = true;

        public PathCreator Path => path;
        public float BedHalfWidth => bedHalfWidth;
        public float ShoulderWidth => shoulderWidth;
        public float TotalRadius => bedHalfWidth + shoulderWidth;
        public bool AllowRuntimeDeformation => allowRuntimeDeformation;
        public bool RestoreTerrainAfterPlayMode =>
            restoreTerrainAfterPlayMode;

        private struct CentreSample
        {
            public Vector3 position;
            public float targetWorldHeight;
        }

        private struct CentreSegment
        {
            public Vector2 a;
            public Vector2 b;
            public float heightA;
            public float heightB;
            public float sqrLength;
        }

        private void Reset()
        {
            path = GetComponent<PathCreator>();
        }

        private void OnValidate()
        {
            bedHalfWidth = Mathf.Max(0.05f, bedHalfWidth);
            shoulderWidth = Mathf.Max(0.05f, shoulderWidth);
            maximumCutDepth = Mathf.Max(0f, maximumCutDepth);
            maximumFillHeight = Mathf.Max(0f, maximumFillHeight);
            pathSampleSpacing = Mathf.Max(0.1f, pathSampleSpacing);
            pathResolution = Mathf.Max(0.25f, pathResolution);

            if (path == null)
            {
                path = GetComponent<PathCreator>();
            }
        }

        public void SetPath(PathCreator newPath)
        {
            path = newPath;
        }

        public void SetBedWidth(float fullWidth)
        {
            bedHalfWidth = Mathf.Max(0.05f, fullWidth * 0.5f);
        }

        [ContextMenu("Deform Terrain Along Trail")]
        public void ApplyDeformation()
        {
            if (Application.isPlaying && !allowRuntimeDeformation)
            {
                Debug.LogWarning(
                    "Runtime terrain deformation is disabled on this component.",
                    this);
                return;
            }

            if (path == null || path.SegmentCount == 0)
            {
                Debug.LogWarning(
                    "Terrain deformation requires a valid trail path.",
                    this);
                return;
            }

            Vector3[] rawSamples =
                path.GetEvenlySpacedPoints(
                    pathSampleSpacing,
                    pathResolution);

            if (rawSamples == null || rawSamples.Length < 2)
            {
                return;
            }

            CentreSample[] centreSamples =
                BuildCentreSamples(rawSamples);

            List<Terrain> terrains =
                TrailTerrainUtility.FindTerrainsForPath(
                    rawSamples,
                    TotalRadius);

            if (terrains.Count == 0)
            {
                Debug.LogWarning(
                    "No active Terrain chunks overlap this trail.",
                    this);
                return;
            }

            for (int i = 0; i < terrains.Count; i++)
            {
                DeformTerrain(terrains[i], centreSamples);
            }

            if (syncTerrainImmediately)
            {
                for (int i = 0; i < terrains.Count; i++)
                {
                    Terrain terrain = terrains[i];
                    if (terrain == null || terrain.terrainData == null)
                    {
                        continue;
                    }

                    terrain.terrainData.SyncHeightmap();
                    terrain.Flush();
                }
            }
        }

        [ContextMenu("Restore Runtime Terrain Backups")]
        private void RestoreRuntimeTerrainBackups()
        {
            TerrainRuntimeBackupRegistry.RestoreAll();
        }

        private CentreSample[] BuildCentreSamples(
            IReadOnlyList<Vector3> rawSamples)
        {
            var samples = new CentreSample[rawSamples.Count];

            for (int i = 0; i < rawSamples.Count; i++)
            {
                Vector3 point = rawSamples[i];
                Terrain terrain =
                    TrailTerrainUtility.FindTerrainAt(point);

                float height = point.y;

                if (terrain != null && terrain.terrainData != null)
                {
                    height =
                        terrain.SampleHeight(point) +
                        terrain.transform.position.y;
                }

                samples[i] = new CentreSample
                {
                    position = point,
                    targetWorldHeight = height + bedHeightOffset
                };
            }

            return samples;
        }

        private void DeformTerrain(
            Terrain terrain,
            IReadOnlyList<CentreSample> centreSamples)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return;
            }

            List<CentreSegment> segments =
                BuildSegmentsForTerrain(
                    terrain,
                    centreSamples,
                    TotalRadius);

            if (segments.Count == 0)
            {
                return;
            }

            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            Vector3 terrainOrigin = terrain.transform.position;
            Vector3 terrainSize = data.size;

            CalculateHeightmapBounds(
                terrain,
                centreSamples,
                TotalRadius,
                out int minX,
                out int minZ,
                out int maxX,
                out int maxZ);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (Application.isPlaying &&
                restoreTerrainAfterPlayMode)
            {
                TerrainRuntimeBackupRegistry.CapturePatch(
                    terrain,
                    minX,
                    minZ,
                    width,
                    height);
            }

            float[,] heights =
                data.GetHeights(minX, minZ, width, height);

            float metresPerPixelX =
                terrainSize.x / Mathf.Max(1, resolution - 1);
            float metresPerPixelZ =
                terrainSize.z / Mathf.Max(1, resolution - 1);

            for (int localZ = 0; localZ < height; localZ++)
            {
                int heightmapZ = minZ + localZ;
                float worldZ =
                    terrainOrigin.z +
                    heightmapZ * metresPerPixelZ;

                for (int localX = 0; localX < width; localX++)
                {
                    int heightmapX = minX + localX;
                    float worldX =
                        terrainOrigin.x +
                        heightmapX * metresPerPixelX;

                    if (!TryGetClosestCentreline(
                        new Vector2(worldX, worldZ),
                        segments,
                        out float distance,
                        out float targetWorldHeight))
                    {
                        continue;
                    }

                    if (distance > TotalRadius)
                    {
                        continue;
                    }

                    float blend = CalculateBlend(distance) * strength;
                    if (blend <= 0f)
                    {
                        continue;
                    }

                    float originalWorldHeight =
                        terrainOrigin.y +
                        heights[localZ, localX] * terrainSize.y;

                    float limitedTarget = Mathf.Clamp(
                        targetWorldHeight,
                        originalWorldHeight - maximumCutDepth,
                        originalWorldHeight + maximumFillHeight);

                    float resultWorldHeight = Mathf.Lerp(
                        originalWorldHeight,
                        limitedTarget,
                        blend);

                    heights[localZ, localX] = Mathf.Clamp01(
                        (resultWorldHeight - terrainOrigin.y) /
                        terrainSize.y);
                }
            }

            data.SetHeightsDelayLOD(minX, minZ, heights);
        }

        private List<CentreSegment> BuildSegmentsForTerrain(
            Terrain terrain,
            IReadOnlyList<CentreSample> samples,
            float padding)
        {
            var segments = new List<CentreSegment>(
                Mathf.Max(0, samples.Count - 1));

            for (int i = 0; i < samples.Count - 1; i++)
            {
                CentreSample first = samples[i];
                CentreSample second = samples[i + 1];

                Vector3 midpoint =
                    Vector3.Lerp(
                        first.position,
                        second.position,
                        0.5f);

                bool overlaps =
                    TrailTerrainUtility.ContainsXZ(
                        terrain,
                        first.position,
                        padding) ||
                    TrailTerrainUtility.ContainsXZ(
                        terrain,
                        second.position,
                        padding) ||
                    TrailTerrainUtility.ContainsXZ(
                        terrain,
                        midpoint,
                        padding);

                if (!overlaps)
                {
                    continue;
                }

                Vector2 a =
                    new Vector2(
                        first.position.x,
                        first.position.z);

                Vector2 b =
                    new Vector2(
                        second.position.x,
                        second.position.z);

                segments.Add(new CentreSegment
                {
                    a = a,
                    b = b,
                    heightA = first.targetWorldHeight,
                    heightB = second.targetWorldHeight,
                    sqrLength = (b - a).sqrMagnitude
                });
            }

            return segments;
        }

        private void CalculateHeightmapBounds(
            Terrain terrain,
            IReadOnlyList<CentreSample> samples,
            float radius,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int resolution = data.heightmapResolution;

            float worldMinX = float.PositiveInfinity;
            float worldMinZ = float.PositiveInfinity;
            float worldMaxX = float.NegativeInfinity;
            float worldMaxZ = float.NegativeInfinity;

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 point = samples[i].position;

                if (!TrailTerrainUtility.ContainsXZ(
                    terrain,
                    point,
                    radius))
                {
                    continue;
                }

                worldMinX = Mathf.Min(worldMinX, point.x - radius);
                worldMinZ = Mathf.Min(worldMinZ, point.z - radius);
                worldMaxX = Mathf.Max(worldMaxX, point.x + radius);
                worldMaxZ = Mathf.Max(worldMaxZ, point.z + radius);
            }

            if (float.IsInfinity(worldMinX))
            {
                minX = minZ = 0;
                maxX = maxZ = -1;
                return;
            }

            float normalizedMinX =
                Mathf.Clamp01((worldMinX - origin.x) / size.x);
            float normalizedMinZ =
                Mathf.Clamp01((worldMinZ - origin.z) / size.z);
            float normalizedMaxX =
                Mathf.Clamp01((worldMaxX - origin.x) / size.x);
            float normalizedMaxZ =
                Mathf.Clamp01((worldMaxZ - origin.z) / size.z);

            minX = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedMinX * (resolution - 1)),
                0,
                resolution - 1);

            minZ = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedMinZ * (resolution - 1)),
                0,
                resolution - 1);

            maxX = Mathf.Clamp(
                Mathf.CeilToInt(
                    normalizedMaxX * (resolution - 1)),
                0,
                resolution - 1);

            maxZ = Mathf.Clamp(
                Mathf.CeilToInt(
                    normalizedMaxZ * (resolution - 1)),
                0,
                resolution - 1);
        }

        private bool TryGetClosestCentreline(
            Vector2 point,
            IReadOnlyList<CentreSegment> segments,
            out float distance,
            out float targetWorldHeight)
        {
            float bestSqrDistance = float.PositiveInfinity;
            float bestHeight = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                CentreSegment segment = segments[i];
                Vector2 delta = segment.b - segment.a;

                float t = segment.sqrLength > 0.000001f
                    ? Mathf.Clamp01(
                        Vector2.Dot(
                            point - segment.a,
                            delta) /
                        segment.sqrLength)
                    : 0f;

                Vector2 closest =
                    segment.a + delta * t;

                float sqrDistance =
                    (point - closest).sqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                bestHeight = Mathf.Lerp(
                    segment.heightA,
                    segment.heightB,
                    t);
            }

            if (float.IsInfinity(bestSqrDistance))
            {
                distance = 0f;
                targetWorldHeight = 0f;
                return false;
            }

            distance = Mathf.Sqrt(bestSqrDistance);
            targetWorldHeight = bestHeight;
            return true;
        }

        private float CalculateBlend(float distance)
        {
            if (distance <= bedHalfWidth)
            {
                return 1f;
            }

            float shoulderT = Mathf.InverseLerp(
                bedHalfWidth + shoulderWidth,
                bedHalfWidth,
                distance);

            return shoulderT * shoulderT * (3f - 2f * shoulderT);
        }
    }
}
