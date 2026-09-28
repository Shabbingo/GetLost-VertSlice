using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.PathCreator
{
    public enum SceneObjectClearMode
    {
        Disable,
        Destroy
    }

    /// <summary>
    /// Clears Unity Terrain trees, Terrain details, and generated foliage
    /// GameObjects around a PathCreator spline.
    ///
    /// Expected generated foliage hierarchy:
    ///
    /// Terrain
    /// └── Generated Foliage
    ///     ├── Foliage Rule
    ///     │   ├── Generated Instance
    ///     │   ├── Generated Instance
    ///     │   └── ...
    ///     └── Foliage Rule
    ///         └── ...
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TerrainFoliageClearance : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        private PathCreator path;

        [SerializeField]
        private Terrain terrain;

        [Tooltip(
            "Automatically applies clearance to every active Terrain chunk " +
            "overlapped by the trail. The assigned Terrain is only used as a " +
            "fallback when this is disabled.")]
        [SerializeField]
        private bool autoDetectTerrainChunks = true;

        [Header("Clearance")]
        [SerializeField, Min(0.1f)]
        private float clearanceRadius = 2.5f;

        [SerializeField, Min(0.05f)]
        private float sampleSpacing = 0.5f;

        [Header("Terrain Content")]
        [SerializeField]
        private bool clearTrees = true;

        [SerializeField]
        private bool clearDetails = true;

        [SerializeField, Range(0f, 1f)]
        private float detailFalloff = 0f;

        [Header("Generated Foliage Objects")]
        [SerializeField]
        private bool clearGeneratedFoliageObjects = false;

        [Tooltip(
            "The Generated Foliage child beneath the assigned Terrain. " +
            "Its direct children are treated as foliage-rule holders, " +
            "and their direct children are treated as generated instances.")]
        [SerializeField]
        private Transform generatedFoliageRoot;

        [Tooltip(
            "Automatically searches the assigned Terrain for a child named " +
            "'Generated Foliage'. The search is case-insensitive.")]
        [SerializeField]
        private bool autoFindGeneratedFoliageRoot = true;

        [SerializeField]
        private SceneObjectClearMode generatedFoliageClearMode =
            SceneObjectClearMode.Disable;

        [Tooltip(
            "Use the bounds centre when an instance has renderers. " +
            "Otherwise the instance transform position is used.")]
        [SerializeField]
        private bool useRendererBoundsCentre = true;

        [Tooltip(
            "Only generated instances inside this vertical distance from " +
            "the path are considered.")]
        [SerializeField, Min(0.1f)]
        private float objectVerticalTolerance = 10f;

        [Tooltip(
            "Also inspect inactive foliage rules and generated instances.")]
        [SerializeField]
        private bool includeInactiveObjects = false;

        [Header("Runtime")]
        [SerializeField]
        private bool allowRuntimeClearing = false;

        [Tooltip(
            "Backs up Terrain trees, affected detail-map patches, and disabled " +
            "generated foliage objects, then restores them when Play Mode ends.")]
        [SerializeField]
        private bool restoreFoliageAfterPlayMode = true;

        [SerializeField]
        private bool clearOnStart = false;

        [Header("Preview")]
        [SerializeField]
        private bool drawClearanceGizmos = true;

        [SerializeField]
        private Color clearanceColor =
            new(1f, 0.25f, 0.05f, 0.25f);

        [SerializeField, HideInInspector]
        private bool hasBackup;

        [SerializeField, HideInInspector]
        private TreeInstance[] backedUpTrees;

        [SerializeField, HideInInspector]
        private DetailLayerBackup[] backedUpDetails;

        [SerializeField, HideInInspector]
        private List<GameObject> disabledObjects = new();

        [Serializable]
        private sealed class DetailLayerBackup
        {
            public int layerIndex;
            public int width;
            public int height;
            public int[] values;
        }

        public PathCreator Path => path;
        public Terrain Terrain => terrain;
        public float ClearanceRadius => clearanceRadius;
        public bool AutoDetectTerrainChunks => autoDetectTerrainChunks;
        public bool HasBackup => hasBackup;
        public Transform GeneratedFoliageRoot => generatedFoliageRoot;

        public int DisabledObjectCount =>
            disabledObjects != null ? disabledObjects.Count : 0;

        private void Start()
        {
            if (Application.isPlaying &&
                clearOnStart &&
                allowRuntimeClearing)
            {
                ApplyClearance();
            }
        }

        private void OnValidate()
        {
            clearanceRadius = Mathf.Max(0.1f, clearanceRadius);
            sampleSpacing = Mathf.Max(0.05f, sampleSpacing);
            objectVerticalTolerance =
                Mathf.Max(0.1f, objectVerticalTolerance);

            if (autoFindGeneratedFoliageRoot)
            {
                TryFindGeneratedFoliageRoot();
            }
        }

        public void SetPath(PathCreator newPath)
        {
            path = newPath;
        }

        public void SetTerrain(Terrain newTerrain)
        {
            terrain = newTerrain;

            if (autoFindGeneratedFoliageRoot)
            {
                TryFindGeneratedFoliageRoot();
            }
        }

        public void SetAutoDetectTerrainChunks(bool enabled)
        {
            autoDetectTerrainChunks = enabled;
        }

        public void TryFindGeneratedFoliageRoot()
        {
            if (terrain == null)
            {
                generatedFoliageRoot = null;
                return;
            }

            generatedFoliageRoot =
                FindChildRecursive(
                    terrain.transform,
                    "Generated Foliage");
        }

        public int PreviewGeneratedFoliageCount()
        {
            if (path == null ||
                path.SegmentCount == 0)
            {
                return 0;
            }

            Vector3[] samples =
                path.GetEvenlySpacedPoints(sampleSpacing, 2f);

            return FindGeneratedInstancesToClear(samples).Count;
        }

        public void ApplyClearance()
        {
            if (Application.isPlaying &&
                !allowRuntimeClearing)
            {
                Debug.LogWarning(
                    "Runtime path clearance is disabled on this component.",
                    this);
                return;
            }

            if (path == null || path.SegmentCount == 0)
            {
                Debug.LogWarning(
                    "Foliage clearance requires a valid path.",
                    this);
                return;
            }

            Vector3[] samples =
                path.GetEvenlySpacedPoints(sampleSpacing, 2f);

            if (!autoDetectTerrainChunks)
            {
                ApplyClearanceToTerrain(
                    terrain,
                    samples,
                    createBackup: true);
                return;
            }

            List<Terrain> terrains =
                TrailTerrainUtility.FindTerrainsForPath(
                    samples,
                    clearanceRadius);

            if (terrains.Count == 0)
            {
                Debug.LogWarning(
                    "No active Terrain chunks overlap this trail.",
                    this);
                return;
            }

            Terrain previousTerrain = terrain;
            Transform previousRoot = generatedFoliageRoot;

            for (int i = 0; i < terrains.Count; i++)
            {
                ApplyClearanceToTerrain(
                    terrains[i],
                    samples,
                    createBackup: false);
            }

            terrain = previousTerrain;
            generatedFoliageRoot = previousRoot;
        }

        private void ApplyClearanceToTerrain(
            Terrain targetTerrain,
            IReadOnlyList<Vector3> samples,
            bool createBackup)
        {
            if (targetTerrain == null ||
                targetTerrain.terrainData == null)
            {
                return;
            }

            terrain = targetTerrain;

            if (autoFindGeneratedFoliageRoot)
            {
                TryFindGeneratedFoliageRoot();
            }

            if (createBackup && !hasBackup)
            {
                CreateBackup();
            }

            if (clearTrees)
            {
                RemoveTrees(samples);
            }

            if (clearDetails)
            {
                RemoveDetails(samples);
            }

            if (clearGeneratedFoliageObjects)
            {
                ClearGeneratedFoliageObjects(samples);
            }

            terrain.Flush();
        }

        public void RestoreBackup()
        {
            if (!ValidateTerrainOnly())
            {
                return;
            }

            if (!hasBackup)
            {
                Debug.LogWarning(
                    "No foliage backup exists to restore.",
                    this);
                return;
            }

            TerrainData data = terrain.terrainData;

            if (backedUpTrees != null)
            {
                data.treeInstances = backedUpTrees;
            }

            if (backedUpDetails != null)
            {
                foreach (DetailLayerBackup backup in backedUpDetails)
                {
                    if (backup == null ||
                        backup.layerIndex < 0 ||
                        backup.layerIndex >=
                            data.detailPrototypes.Length ||
                        backup.values == null ||
                        backup.values.Length !=
                            backup.width * backup.height)
                    {
                        continue;
                    }

                    int[,] layer =
                        new int[backup.height, backup.width];

                    int index = 0;
                    for (int y = 0; y < backup.height; y++)
                    {
                        for (int x = 0; x < backup.width; x++)
                        {
                            layer[y, x] = backup.values[index++];
                        }
                    }

                    data.SetDetailLayer(
                        0,
                        0,
                        backup.layerIndex,
                        layer);
                }
            }

            RestoreDisabledObjects();
            terrain.Flush();
        }

        public void RefreshBackup()
        {
            if (!ValidateTerrainOnly())
            {
                return;
            }

            CreateBackup();
        }

        public void ClearBackup()
        {
            backedUpTrees = null;
            backedUpDetails = null;
            disabledObjects?.Clear();
            hasBackup = false;
        }

        public void RestoreDisabledObjects()
        {
            if (disabledObjects == null)
            {
                return;
            }

            for (int i = disabledObjects.Count - 1;
                i >= 0;
                i--)
            {
                GameObject item = disabledObjects[i];

                if (item != null)
                {
                    item.SetActive(true);
                }
            }

            disabledObjects.Clear();
        }

        private void CreateBackup()
        {
            TerrainData data = terrain.terrainData;

            backedUpTrees =
                data.treeInstances != null
                    ? (TreeInstance[])data.treeInstances.Clone()
                    : Array.Empty<TreeInstance>();

            int detailLayerCount =
                data.detailPrototypes.Length;

            backedUpDetails =
                new DetailLayerBackup[detailLayerCount];

            for (int layerIndex = 0;
                layerIndex < detailLayerCount;
                layerIndex++)
            {
                int[,] layer = data.GetDetailLayer(
                    0,
                    0,
                    data.detailWidth,
                    data.detailHeight,
                    layerIndex);

                var backup = new DetailLayerBackup
                {
                    layerIndex = layerIndex,
                    width = data.detailWidth,
                    height = data.detailHeight,
                    values = new int[
                        data.detailWidth *
                        data.detailHeight]
                };

                int index = 0;
                for (int y = 0;
                    y < data.detailHeight;
                    y++)
                {
                    for (int x = 0;
                        x < data.detailWidth;
                        x++)
                    {
                        backup.values[index++] =
                            layer[y, x];
                    }
                }

                backedUpDetails[layerIndex] = backup;
            }

            disabledObjects ??=
                new List<GameObject>();

            disabledObjects.Clear();
            hasBackup = true;
        }

        private void RemoveTrees(
            IReadOnlyList<Vector3> samples)
        {
            TerrainData data = terrain.terrainData;

            if (Application.isPlaying &&
                restoreFoliageAfterPlayMode)
            {
                TerrainRuntimeBackupRegistry.CaptureTrees(terrain);
            }

            TreeInstance[] trees = data.treeInstances;

            if (trees == null ||
                trees.Length == 0)
            {
                return;
            }

            var keptTrees =
                new List<TreeInstance>(trees.Length);

            Vector3 terrainPosition =
                terrain.transform.position;

            Vector3 terrainSize = data.size;
            float radiusSquared =
                clearanceRadius * clearanceRadius;

            foreach (TreeInstance tree in trees)
            {
                Vector3 worldPosition =
                    terrainPosition +
                    Vector3.Scale(
                        tree.position,
                        terrainSize);

                if (!IsInsideClearance(
                    worldPosition,
                    samples,
                    radiusSquared))
                {
                    keptTrees.Add(tree);
                }
            }

            data.treeInstances = keptTrees.ToArray();
        }


        private void RemoveDetails(
            IReadOnlyList<Vector3> samples)
        {
            TerrainData data = terrain.terrainData;
            int detailWidth = data.detailWidth;
            int detailHeight = data.detailHeight;

            if (detailWidth <= 0 ||
                detailHeight <= 0 ||
                data.detailPrototypes.Length == 0)
            {
                return;
            }

            CalculateDetailBounds(
                samples,
                out int minX,
                out int minZ,
                out int maxX,
                out int maxZ);

            if (maxX < minX || maxZ < minZ)
            {
                return;
            }

            int requestedWidth = maxX - minX + 1;
            int requestedHeight = maxZ - minZ + 1;

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = data.size;

            float innerRadius =
                clearanceRadius *
                Mathf.Clamp01(1f - detailFalloff);

            float innerSquared = innerRadius * innerRadius;
            float outerSquared = clearanceRadius * clearanceRadius;

            for (int layerIndex = 0;
                layerIndex < data.detailPrototypes.Length;
                layerIndex++)
            {
                int[,] layer = data.GetDetailLayer(
                    minX,
                    minZ,
                    requestedWidth,
                    requestedHeight,
                    layerIndex);

                if (layer == null)
                {
                    continue;
                }

                int actualHeight = layer.GetLength(0);
                int actualWidth = layer.GetLength(1);

                if (actualWidth <= 0 || actualHeight <= 0)
                {
                    continue;
                }

                if (Application.isPlaying &&
                    restoreFoliageAfterPlayMode)
                {
                    TerrainRuntimeBackupRegistry.CaptureDetailPatch(
                        terrain,
                        layerIndex,
                        minX,
                        minZ,
                        actualWidth,
                        actualHeight);
                }

                bool changed = false;

                for (int localZ = 0;
                    localZ < actualHeight;
                    localZ++)
                {
                    int detailZ = minZ + localZ;

                    float normalizedZ =
                        (detailZ + 0.5f) /
                        detailHeight;

                    float worldZ =
                        terrainPosition.z +
                        normalizedZ *
                        terrainSize.z;

                    for (int localX = 0;
                        localX < actualWidth;
                        localX++)
                    {
                        int value = layer[localZ, localX];

                        if (value <= 0)
                        {
                            continue;
                        }

                        int detailX = minX + localX;

                        float normalizedX =
                            (detailX + 0.5f) /
                            detailWidth;

                        float worldX =
                            terrainPosition.x +
                            normalizedX *
                            terrainSize.x;

                        Vector3 worldPosition =
                            new Vector3(worldX, 0f, worldZ);

                        float nearestSquared =
                            FindNearestHorizontalDistanceSquared(
                                worldPosition,
                                samples);

                        if (nearestSquared <= innerSquared)
                        {
                            layer[localZ, localX] = 0;
                            changed = true;
                        }
                        else if (
                            detailFalloff > 0f &&
                            nearestSquared <= outerSquared)
                        {
                            float distance =
                                Mathf.Sqrt(nearestSquared);

                            float t = Mathf.InverseLerp(
                                innerRadius,
                                clearanceRadius,
                                distance);

                            int newValue =
                                Mathf.FloorToInt(value * t);

                            if (newValue != value)
                            {
                                layer[localZ, localX] = newValue;
                                changed = true;
                            }
                        }
                    }
                }

                if (changed)
                {
                    data.SetDetailLayer(
                        minX,
                        minZ,
                        layerIndex,
                        layer);
                }
            }
        }

        private void CalculateDetailBounds(
            IReadOnlyList<Vector3> samples,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            minX = data.detailWidth;
            minZ = data.detailHeight;
            maxX = -1;
            maxZ = -1;

            if (samples == null ||
                samples.Count == 0 ||
                size.x <= 0f ||
                size.z <= 0f)
            {
                return;
            }

            float pixelsPerMetreX =
                data.detailWidth / size.x;

            float pixelsPerMetreZ =
                data.detailHeight / size.z;

            int radiusPixelsX =
                Mathf.CeilToInt(
                    clearanceRadius *
                    pixelsPerMetreX) + 1;

            int radiusPixelsZ =
                Mathf.CeilToInt(
                    clearanceRadius *
                    pixelsPerMetreZ) + 1;

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 point = samples[i];

                float localX =
                    point.x - origin.x;

                float localZ =
                    point.z - origin.z;

                int centreX =
                    Mathf.FloorToInt(
                        localX * pixelsPerMetreX);

                int centreZ =
                    Mathf.FloorToInt(
                        localZ * pixelsPerMetreZ);

                int sampleMinX =
                    centreX - radiusPixelsX;

                int sampleMinZ =
                    centreZ - radiusPixelsZ;

                int sampleMaxX =
                    centreX + radiusPixelsX;

                int sampleMaxZ =
                    centreZ + radiusPixelsZ;

                if (sampleMaxX < 0 ||
                    sampleMaxZ < 0 ||
                    sampleMinX >= data.detailWidth ||
                    sampleMinZ >= data.detailHeight)
                {
                    continue;
                }

                minX = Mathf.Min(
                    minX,
                    Mathf.Clamp(
                        sampleMinX,
                        0,
                        data.detailWidth - 1));

                minZ = Mathf.Min(
                    minZ,
                    Mathf.Clamp(
                        sampleMinZ,
                        0,
                        data.detailHeight - 1));

                maxX = Mathf.Max(
                    maxX,
                    Mathf.Clamp(
                        sampleMaxX,
                        0,
                        data.detailWidth - 1));

                maxZ = Mathf.Max(
                    maxZ,
                    Mathf.Clamp(
                        sampleMaxZ,
                        0,
                        data.detailHeight - 1));
            }
        }

        private void ClearGeneratedFoliageObjects(
            IReadOnlyList<Vector3> samples)
        {
            List<GameObject> targets =
                FindGeneratedInstancesToClear(samples);

            foreach (GameObject target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                bool mustDisableForRestore =
                    Application.isPlaying &&
                    restoreFoliageAfterPlayMode;

                if (generatedFoliageClearMode ==
                        SceneObjectClearMode.Disable ||
                    mustDisableForRestore)
                {
                    if (mustDisableForRestore)
                    {
                        TerrainRuntimeBackupRegistry
                            .CaptureDisabledObject(target);
                    }

                    disabledObjects ??=
                        new List<GameObject>();

                    if (!disabledObjects.Contains(target))
                    {
                        disabledObjects.Add(target);
                    }

                    target.SetActive(false);
                }
                else
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.Undo.DestroyObjectImmediate(
                            target);
                    }
                    else
#endif
                    {
                        Destroy(target);
                    }
                }
            }
        }

        private List<GameObject>
            FindGeneratedInstancesToClear(
                IReadOnlyList<Vector3> samples)
        {
            var results = new List<GameObject>();

            if (generatedFoliageRoot == null &&
                autoFindGeneratedFoliageRoot)
            {
                TryFindGeneratedFoliageRoot();
            }

            if (generatedFoliageRoot == null)
            {
                return results;
            }

            float radiusSquared =
                clearanceRadius * clearanceRadius;

            CollectGeneratedInstancesRecursive(
                generatedFoliageRoot,
                generatedFoliageRoot,
                samples,
                radiusSquared,
                results);

            return results;
        }

        private void CollectGeneratedInstancesRecursive(
            Transform current,
            Transform root,
            IReadOnlyList<Vector3> samples,
            float radiusSquared,
            List<GameObject> results)
        {
            if (current == null)
            {
                return;
            }

            int childCount = current.childCount;

            for (int i = 0; i < childCount; i++)
            {
                Transform child = current.GetChild(i);

                if (child == null)
                {
                    continue;
                }

                GameObject target = child.gameObject;

                if (!includeInactiveObjects &&
                    !target.activeInHierarchy)
                {
                    continue;
                }

                bool hasRenderableContent =
                    child.GetComponent<Renderer>() != null ||
                    child.GetComponentInChildren<Renderer>(
                        includeInactiveObjects) != null;

                bool isLeafInstance =
                    child.childCount == 0 ||
                    hasRenderableContent;

                if (isLeafInstance)
                {
                    Vector3 testPosition =
                        GetGeneratedInstancePosition(child);

                    Vector3 nearestSample =
                        FindNearestSamplePoint(
                            testPosition,
                            samples);

                    bool withinVerticalTolerance =
                        Mathf.Abs(
                            testPosition.y -
                            nearestSample.y) <=
                        objectVerticalTolerance;

                    bool insideClearance =
                        IsInsideClearance(
                            testPosition,
                            samples,
                            radiusSquared);

                    if (withinVerticalTolerance &&
                        insideClearance &&
                        !results.Contains(target))
                    {
                        results.Add(target);
                    }
                }

                if (child.childCount > 0)
                {
                    CollectGeneratedInstancesRecursive(
                        child,
                        root,
                        samples,
                        radiusSquared,
                        results);
                }
            }
        }

        private Vector3 GetGeneratedInstancePosition(
            Transform instance)
        {
            if (!useRendererBoundsCentre)
            {
                return instance.position;
            }

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(
                    includeInactiveObjects);

            if (renderers == null ||
                renderers.Length == 0)
            {
                return instance.position;
            }

            bool hasBounds = false;
            Bounds combined = default;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(
                        renderer.bounds);
                }
            }

            return hasBounds
                ? combined.center
                : instance.position;
        }

        private bool IsInsideClearance(
            Vector3 worldPosition,
            IReadOnlyList<Vector3> samples,
            float radiusSquared)
        {
            return
                FindNearestHorizontalDistanceSquared(
                    worldPosition,
                    samples) <= radiusSquared;
        }

        private static float
            FindNearestHorizontalDistanceSquared(
                Vector3 worldPosition,
                IReadOnlyList<Vector3> samples)
        {
            float nearestSquared =
                float.PositiveInfinity;

            for (int i = 0;
                i < samples.Count;
                i++)
            {
                Vector3 delta =
                    worldPosition - samples[i];

                delta.y = 0f;
                float squared = delta.sqrMagnitude;

                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                }
            }

            return nearestSquared;
        }

        private static Vector3
            FindNearestSamplePoint(
                Vector3 worldPosition,
                IReadOnlyList<Vector3> samples)
        {
            Vector3 nearest = samples[0];
            float nearestSquared =
                float.PositiveInfinity;

            for (int i = 0;
                i < samples.Count;
                i++)
            {
                Vector3 delta =
                    worldPosition - samples[i];

                delta.y = 0f;
                float squared = delta.sqrMagnitude;

                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = samples[i];
                }
            }

            return nearest;
        }

        private static Transform FindChildRecursive(
            Transform parent,
            string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0;
                i < parent.childCount;
                i++)
            {
                Transform child =
                    parent.GetChild(i);

                if (string.Equals(
                    child.name,
                    childName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                Transform nested =
                    FindChildRecursive(
                        child,
                        childName);

                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private bool ValidateSetup()
        {
            if (!ValidateTerrainOnly())
            {
                return false;
            }

            if (path == null)
            {
                Debug.LogError(
                    "Terrain Foliage Clearance requires a Path Creator.",
                    this);
                return false;
            }

            if (path.SegmentCount == 0)
            {
                Debug.LogWarning(
                    "The assigned path has no segments.",
                    this);
                return false;
            }

            return true;
        }

        private bool ValidateTerrainOnly()
        {
            if (terrain == null)
            {
                Debug.LogError(
                    "Terrain Foliage Clearance requires a Unity Terrain.",
                    this);
                return false;
            }

            if (terrain.terrainData == null)
            {
                Debug.LogError(
                    "The assigned Terrain has no TerrainData.",
                    this);
                return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawClearanceGizmos ||
                path == null ||
                path.SegmentCount == 0)
            {
                return;
            }

            Gizmos.color = clearanceColor;

            Vector3[] samples =
                path.GetEvenlySpacedPoints(
                    Mathf.Max(
                        sampleSpacing,
                        clearanceRadius * 0.5f),
                    1.5f);

            foreach (Vector3 sample in samples)
            {
                Gizmos.DrawWireSphere(
                    sample,
                    clearanceRadius);
            }
        }
    }
}
