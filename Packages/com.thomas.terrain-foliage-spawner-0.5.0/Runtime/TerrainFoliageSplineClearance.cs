using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Thomas.TerrainFoliageSpawner
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Terrain Foliage/Spline Clearance")]
    public sealed class TerrainFoliageSplineClearance : MonoBehaviour
    {
        [Header("Spline")]
        [SerializeField] private SplineContainer splineContainer;
        [SerializeField, Min(0.05f)] private float sampleDistance = 0.5f;

        [Header("Clearance")]
        [SerializeField, Min(0f)] private float clearRadius = 1.2f;
        [SerializeField, Min(0f)] private float falloffRadius = 2.5f;
        [SerializeField] private AnimationCurve falloffCurve =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Trail Wear")]
        [SerializeField] private bool useWearNoise = true;
        [SerializeField, Range(0f, 1f)] private float wearNoiseStrength = 0.2f;
        [SerializeField, Min(0.01f)] private float wearNoiseScale = 0.35f;
        [SerializeField] private int wearSeed = 1234;

        [Header("Affected Outputs")]
        [SerializeField] private bool affectGameObjects = true;
        [SerializeField] private bool affectTerrainDetails = true;
        [SerializeField] private bool affectTerrainTrees = true;

        [Header("Runtime Detail Clearing (Beta)")]
        [Tooltip("Terrain modified when a trail section is confirmed. If empty, Terrain.activeTerrain is used.")]
        [SerializeField] private Terrain runtimeTerrain;
        [Tooltip("Duplicates TerrainData on the first runtime edit so the source TerrainData asset is never modified during Play Mode.")]
        [SerializeField] private bool cloneTerrainDataAtRuntime = true;
        [Tooltip("When empty, all Terrain Detail prototypes are cleared. Add prefabs here to limit runtime clearing to selected grass/detail prototypes.")]
        [SerializeField] private List<GameObject> runtimeDetailPrefabs = new List<GameObject>();
        [Tooltip("Safety limit for the number of detail-map cells inspected by one confirmed section, across all affected detail layers.")]
        [SerializeField, Min(1024)] private int maximumCellsPerActivation = 500000;
        [Tooltip("Restores the untouched source TerrainData when this component is destroyed or Play Mode ends.")]
        [SerializeField] private bool restoreSourceTerrainDataOnDestroy = true;
        [SerializeField] private bool logRuntimeOperations = true;

        [Header("Preview")]
        [SerializeField] private bool drawPreview = true;
        [SerializeField, Range(4, 32)] private int previewCircleSegments = 12;

        private readonly List<Vector3> sampledPoints = new List<Vector3>();
        private readonly Dictionary<int, Dictionary<int, int>> originalDetailValues =
            new Dictionary<int, Dictionary<int, int>>();

        private bool cacheDirty = true;
        private TerrainData sourceTerrainData;
        private TerrainData runtimeTerrainData;
        private bool ownsRuntimeTerrainData;

        public SplineContainer SplineContainer => splineContainer;
        public float ClearRadius => clearRadius;
        public float FalloffRadius => falloffRadius;
        public float SampleDistance => sampleDistance;
        public Terrain RuntimeTerrain => runtimeTerrain;
        public bool HasRuntimeChanges => originalDetailValues.Count > 0;
        public bool IsValid => splineContainer != null && splineContainer.Splines != null && splineContainer.Splines.Count > 0;

        private void Reset()
        {
            splineContainer = GetComponent<SplineContainer>();
            runtimeTerrain = GetComponentInParent<Terrain>();
            cacheDirty = true;
        }

        private void OnEnable() => cacheDirty = true;

        private void OnValidate()
        {
            sampleDistance = Mathf.Max(0.05f, sampleDistance);
            clearRadius = Mathf.Max(0f, clearRadius);
            falloffRadius = Mathf.Max(0f, falloffRadius);
            wearNoiseScale = Mathf.Max(0.01f, wearNoiseScale);
            maximumCellsPerActivation = Mathf.Max(1024, maximumCellsPerActivation);
            cacheDirty = true;
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying || !restoreSourceTerrainDataOnDestroy)
                return;

            RestoreSourceTerrainData();
        }

        public bool Affects(TerrainFoliageOutputMode mode)
        {
            return mode == TerrainFoliageOutputMode.GameObject ? affectGameObjects :
                   (mode == TerrainFoliageOutputMode.TerrainDetail ||
                    mode == TerrainFoliageOutputMode.InstancedGrass) ? affectTerrainDetails :
                   affectTerrainTrees;
        }

        public float GetKeepProbability(Vector3 worldPosition)
        {
            if (!isActiveAndEnabled || !IsValid)
                return 1f;

            EnsureCache();
            if (sampledPoints.Count < 2)
                return 1f;

            Vector2 point = new Vector2(worldPosition.x, worldPosition.z);
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < sampledPoints.Count - 1; i++)
            {
                Vector3 a3 = sampledPoints[i];
                Vector3 b3 = sampledPoints[i + 1];
                bestDistance = Mathf.Min(bestDistance, DistanceToSegment(
                    point, new Vector2(a3.x, a3.z), new Vector2(b3.x, b3.z)));
            }

            return EvaluateKeepProbability(worldPosition, bestDistance);
        }

        public void RebuildCache()
        {
            sampledPoints.Clear();
            cacheDirty = false;
            if (!IsValid) return;

            splineContainer.Warmup();
            for (int splineIndex = 0; splineIndex < splineContainer.Splines.Count; splineIndex++)
            {
                float length = Mathf.Max(0.001f, splineContainer.CalculateLength(splineIndex));
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / sampleDistance));
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    sampledPoints.Add((Vector3)splineContainer.EvaluatePosition(splineIndex, t));
                }
            }
        }

        /// <summary>
        /// Clears grass/details around the newest section of a spline. Intended to be called
        /// after the player adds or confirms the newest knot.
        /// </summary>
        public bool ActivateLatestSection(int splineIndex = 0)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Runtime trail activation can only modify Terrain details in Play Mode.", this);
                return false;
            }

            if (!IsValid || splineIndex < 0 || splineIndex >= splineContainer.Splines.Count)
            {
                Debug.LogWarning("No valid spline was available for runtime trail activation.", this);
                return false;
            }

            Spline spline = splineContainer.Splines[splineIndex];
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                Debug.LogWarning("The spline needs at least two knots before a section can be activated.", this);
                return false;
            }

            float denominator = Mathf.Max(1, knotCount - 1);
            float startT = (knotCount - 2) / denominator;
            return ApplyRuntimeClearance(splineIndex, startT, 1f);
        }

        /// <summary>
        /// Clears Terrain details along a normalized portion of a spline. The update is limited
        /// to the rectangular detail-map region touched by this section.
        /// </summary>
        public bool ApplyRuntimeClearance(int splineIndex, float startT, float endT)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Runtime trail activation can only modify Terrain details in Play Mode.", this);
                return false;
            }

            if (!TryPrepareRuntimeTerrain(out Terrain targetTerrain, out TerrainData data))
                return false;

            if (!IsValid || splineIndex < 0 || splineIndex >= splineContainer.Splines.Count)
            {
                Debug.LogWarning("Invalid spline index supplied to ApplyRuntimeClearance.", this);
                return false;
            }

            startT = Mathf.Clamp01(startT);
            endT = Mathf.Clamp01(endT);
            if (endT < startT)
                (startT, endT) = (endT, startT);

            float splineLength = Mathf.Max(0.001f, splineContainer.CalculateLength(splineIndex));
            float sectionLength = Mathf.Max(sampleDistance, splineLength * Mathf.Max(0.0001f, endT - startT));
            int steps = Mathf.Max(1, Mathf.CeilToInt(sectionLength / sampleDistance));
            List<Vector3> sectionPoints = new List<Vector3>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)steps);
                sectionPoints.Add((Vector3)splineContainer.EvaluatePosition(splineIndex, t));
            }

            bool result = ApplyRuntimePolyline(targetTerrain, data, sectionPoints);
            cacheDirty = true;
            return result;
        }

        /// <summary>
        /// Clears Terrain details around one confirmed straight trail section. This is useful
        /// when the player's trail tool already knows the previous and newly confirmed positions.
        /// </summary>
        public bool ApplyRuntimeSegment(Vector3 worldStart, Vector3 worldEnd)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Runtime trail activation can only modify Terrain details in Play Mode.", this);
                return false;
            }

            if (!TryPrepareRuntimeTerrain(out Terrain targetTerrain, out TerrainData data))
                return false;

            float length = Vector3.Distance(worldStart, worldEnd);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / sampleDistance));
            List<Vector3> points = new List<Vector3>(steps + 1);
            for (int i = 0; i <= steps; i++)
                points.Add(Vector3.Lerp(worldStart, worldEnd, i / (float)steps));

            return ApplyRuntimePolyline(targetTerrain, data, points);
        }

        /// <summary>Restores all detail cells changed by confirmed runtime sections.</summary>
        public void RestoreRuntimeClearance()
        {
            Terrain targetTerrain = ResolveRuntimeTerrain();
            TerrainData data = targetTerrain != null ? targetTerrain.terrainData : null;
            if (data == null || originalDetailValues.Count == 0)
                return;

            int resolution = data.detailResolution;
            foreach (KeyValuePair<int, Dictionary<int, int>> layerBackup in originalDetailValues)
            {
                int layer = layerBackup.Key;
                if (layer < 0 || layer >= data.detailPrototypes.Length || layerBackup.Value.Count == 0)
                    continue;

                int minX = resolution - 1;
                int minZ = resolution - 1;
                int maxX = 0;
                int maxZ = 0;

                foreach (int flatIndex in layerBackup.Value.Keys)
                {
                    int z = flatIndex / resolution;
                    int x = flatIndex - z * resolution;
                    minX = Mathf.Min(minX, x);
                    minZ = Mathf.Min(minZ, z);
                    maxX = Mathf.Max(maxX, x);
                    maxZ = Mathf.Max(maxZ, z);
                }

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;
                int[,] map = data.GetDetailLayer(minX, minZ, width, height, layer);

                foreach (KeyValuePair<int, int> value in layerBackup.Value)
                {
                    int z = value.Key / resolution;
                    int x = value.Key - z * resolution;
                    map[z - minZ, x - minX] = value.Value;
                }

                data.SetDetailLayer(minX, minZ, layer, map);
            }

            originalDetailValues.Clear();
            targetTerrain.Flush();

            if (logRuntimeOperations)
                Debug.Log("Restored runtime trail-cleared Terrain details.", this);
        }

        /// <summary>Keeps the current trail edits but discards the undo cache.</summary>
        public void CommitRuntimeClearance()
        {
            originalDetailValues.Clear();
            if (logRuntimeOperations)
                Debug.Log("Committed runtime trail clearance and discarded its restore cache.", this);
        }

        /// <summary>
        /// Returns the Terrain to its original TerrainData asset when runtime cloning is enabled.
        /// This discards every runtime TerrainData modification, not only trail clearance.
        /// </summary>
        public void RestoreSourceTerrainData()
        {
            Terrain targetTerrain = ResolveRuntimeTerrain();
            if (targetTerrain != null && ownsRuntimeTerrainData && sourceTerrainData != null)
                targetTerrain.terrainData = sourceTerrainData;

            if (ownsRuntimeTerrainData && runtimeTerrainData != null)
                Destroy(runtimeTerrainData);

            runtimeTerrainData = null;
            sourceTerrainData = null;
            ownsRuntimeTerrainData = false;
            originalDetailValues.Clear();
        }

        private bool ApplyRuntimePolyline(Terrain targetTerrain, TerrainData data, IReadOnlyList<Vector3> points)
        {
            if (!affectTerrainDetails)
            {
                Debug.LogWarning("Affect Terrain Details is disabled on this spline clearance.", this);
                return false;
            }

            if (points == null || points.Count < 2 || data.detailResolution <= 0)
                return false;

            List<int> layers = FindRuntimeDetailLayers(data);
            if (layers.Count == 0)
            {
                Debug.LogWarning("No matching Terrain Detail prototypes were found for runtime clearing.", this);
                return false;
            }

            float maximumRadius = clearRadius * (useWearNoise ? 1f + wearNoiseStrength : 1f) + falloffRadius;
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++)
                bounds.Encapsulate(points[i]);
            bounds.Expand(new Vector3(maximumRadius * 2f, 0f, maximumRadius * 2f));

            if (!TryWorldBoundsToDetailRect(targetTerrain, data, bounds,
                    out int minX, out int minZ, out int width, out int height))
                return false;

            long cellChecks = (long)width * height * layers.Count;
            if (cellChecks > maximumCellsPerActivation)
            {
                Debug.LogWarning(
                    $"Runtime trail section was skipped because it would inspect {cellChecks:N0} detail cells. " +
                    $"The current safety limit is {maximumCellsPerActivation:N0}. Shorten the confirmed section, " +
                    "reduce the trail radius, or raise Maximum Cells Per Activation.", this);
                return false;
            }

            Vector3 terrainPosition = targetTerrain.transform.position;
            Vector3 terrainSize = data.size;
            int resolution = data.detailResolution;
            int changedCells = 0;

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                int layer = layers[layerIndex];
                int[,] map = data.GetDetailLayer(minX, minZ, width, height, layer);
                Dictionary<int, int> backup = GetOrCreateLayerBackup(layer);
                bool layerChanged = false;

                for (int localZ = 0; localZ < height; localZ++)
                {
                    int detailZ = minZ + localZ;
                    float normalizedZ = (detailZ + 0.5f) / resolution;
                    float worldZ = terrainPosition.z + normalizedZ * terrainSize.z;

                    for (int localX = 0; localX < width; localX++)
                    {
                        int currentDensity = map[localZ, localX];
                        if (currentDensity <= 0)
                            continue;

                        int detailX = minX + localX;
                        float normalizedX = (detailX + 0.5f) / resolution;
                        float worldX = terrainPosition.x + normalizedX * terrainSize.x;
                        Vector3 worldPosition = new Vector3(worldX, 0f, worldZ);

                        float nearest = float.PositiveInfinity;
                        Vector2 point = new Vector2(worldX, worldZ);
                        for (int segment = 0; segment < points.Count - 1; segment++)
                        {
                            Vector3 a = points[segment];
                            Vector3 b = points[segment + 1];
                            nearest = Mathf.Min(nearest, DistanceToSegment(
                                point, new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
                        }

                        float keep = EvaluateKeepProbability(worldPosition, nearest);
                        if (keep >= 0.9999f)
                            continue;

                        int newDensity = Mathf.Clamp(Mathf.FloorToInt(currentDensity * keep), 0, currentDensity);
                        if (newDensity == currentDensity)
                            continue;

                        int flatIndex = detailZ * resolution + detailX;
                        if (!backup.ContainsKey(flatIndex))
                            backup.Add(flatIndex, currentDensity);

                        map[localZ, localX] = newDensity;
                        changedCells++;
                        layerChanged = true;
                    }
                }

                if (layerChanged)
                    data.SetDetailLayer(minX, minZ, layer, map);
            }

            targetTerrain.Flush();

            if (logRuntimeOperations)
            {
                Debug.Log(
                    $"Activated runtime trail section. Updated {changedCells:N0} detail cells " +
                    $"inside a {width:N0} x {height:N0} dirty region across {layers.Count:N0} detail layer(s).",
                    this);
            }

            return true;
        }

        private bool TryPrepareRuntimeTerrain(out Terrain targetTerrain, out TerrainData data)
        {
            targetTerrain = ResolveRuntimeTerrain();
            data = targetTerrain != null ? targetTerrain.terrainData : null;

            if (targetTerrain == null || data == null)
            {
                Debug.LogWarning("Assign a Runtime Terrain or ensure Terrain.activeTerrain exists.", this);
                return false;
            }

            if (cloneTerrainDataAtRuntime && !ownsRuntimeTerrainData)
            {
                sourceTerrainData = data;
                runtimeTerrainData = Instantiate(data);
                runtimeTerrainData.name = data.name + " (Runtime Trail Copy)";
                targetTerrain.terrainData = runtimeTerrainData;
                ownsRuntimeTerrainData = true;
                data = runtimeTerrainData;
            }

            return true;
        }

        private Terrain ResolveRuntimeTerrain()
        {
            if (runtimeTerrain != null)
                return runtimeTerrain;

            Terrain parentTerrain = GetComponentInParent<Terrain>();
            return parentTerrain != null ? parentTerrain : Terrain.activeTerrain;
        }

        private List<int> FindRuntimeDetailLayers(TerrainData data)
        {
            List<int> result = new List<int>();
            DetailPrototype[] prototypes = data.detailPrototypes ?? Array.Empty<DetailPrototype>();

            bool useAll = runtimeDetailPrefabs == null || runtimeDetailPrefabs.Count == 0;
            for (int i = 0; i < prototypes.Length; i++)
            {
                if (useAll)
                {
                    result.Add(i);
                    continue;
                }

                GameObject prototypePrefab = prototypes[i] != null ? prototypes[i].prototype : null;
                if (prototypePrefab != null && runtimeDetailPrefabs.Contains(prototypePrefab))
                    result.Add(i);
            }

            return result;
        }

        private Dictionary<int, int> GetOrCreateLayerBackup(int layer)
        {
            if (!originalDetailValues.TryGetValue(layer, out Dictionary<int, int> backup))
            {
                backup = new Dictionary<int, int>();
                originalDetailValues.Add(layer, backup);
            }

            return backup;
        }

        private float EvaluateKeepProbability(Vector3 worldPosition, float distance)
        {
            float noiseOffset = wearSeed * 0.0137f;
            float noise = Mathf.PerlinNoise(
                worldPosition.x * wearNoiseScale + noiseOffset,
                worldPosition.z * wearNoiseScale + noiseOffset * 1.91f);

            float radiusNoise = useWearNoise
                ? Mathf.Lerp(1f - wearNoiseStrength, 1f + wearNoiseStrength, noise)
                : 1f;

            float inner = clearRadius * radiusNoise;
            float outer = inner + falloffRadius;
            if (distance <= inner) return 0f;
            if (falloffRadius <= 0f || distance >= outer) return 1f;

            float normalized = Mathf.InverseLerp(inner, outer, distance);
            return Mathf.Clamp01(falloffCurve != null
                ? falloffCurve.Evaluate(normalized)
                : normalized);
        }

        private static bool TryWorldBoundsToDetailRect(
            Terrain targetTerrain,
            TerrainData data,
            Bounds worldBounds,
            out int minX,
            out int minZ,
            out int width,
            out int height)
        {
            Vector3 terrainPosition = targetTerrain.transform.position;
            Vector3 terrainSize = data.size;
            int resolution = data.detailResolution;

            float normalizedMinX = (worldBounds.min.x - terrainPosition.x) / terrainSize.x;
            float normalizedMaxX = (worldBounds.max.x - terrainPosition.x) / terrainSize.x;
            float normalizedMinZ = (worldBounds.min.z - terrainPosition.z) / terrainSize.z;
            float normalizedMaxZ = (worldBounds.max.z - terrainPosition.z) / terrainSize.z;

            int maxXExclusive = Mathf.Clamp(Mathf.CeilToInt(normalizedMaxX * resolution), 0, resolution);
            int maxZExclusive = Mathf.Clamp(Mathf.CeilToInt(normalizedMaxZ * resolution), 0, resolution);
            minX = Mathf.Clamp(Mathf.FloorToInt(normalizedMinX * resolution), 0, Mathf.Max(0, resolution - 1));
            minZ = Mathf.Clamp(Mathf.FloorToInt(normalizedMinZ * resolution), 0, Mathf.Max(0, resolution - 1));
            width = maxXExclusive - minX;
            height = maxZExclusive - minZ;

            return width > 0 && height > 0;
        }

        private void EnsureCache()
        {
            if (cacheDirty) RebuildCache();
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float sqrLength = ab.sqrMagnitude;
            if (sqrLength <= 0.000001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sqrLength);
            return Vector2.Distance(p, a + ab * t);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawPreview || !IsValid) return;
            RebuildCache();
            if (sampledPoints.Count < 2) return;

            for (int i = 0; i < sampledPoints.Count - 1; i++)
                Gizmos.DrawLine(sampledPoints[i], sampledPoints[i + 1]);

            int stride = Mathf.Max(1, Mathf.RoundToInt(2f / sampleDistance));
            for (int i = 0; i < sampledPoints.Count; i += stride)
            {
                DrawCircle(sampledPoints[i], clearRadius, previewCircleSegments);
                DrawCircle(sampledPoints[i], clearRadius + falloffRadius, previewCircleSegments);
            }
        }

        private static void DrawCircle(Vector3 center, float radius, int segments)
        {
            if (radius <= 0f) return;
            Vector3 previous = center + Vector3.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 current = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }
    }
}
