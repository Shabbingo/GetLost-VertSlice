using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thomas.TerrainFoliageSpawner
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Terrain Foliage/Managed Grass Renderer")]
    public sealed class TerrainFoliageGrassRenderer : MonoBehaviour
    {
        [SerializeField] private TerrainFoliagePlacementData placementData;
        [SerializeField, Min(5f)] private float renderDistance = 90f;
        [SerializeField, Min(4f)] private float chunkSize = 24f;
        [SerializeField, Min(0f)] private float preloadDistance = 250f;
        [SerializeField] private ShadowCastingMode shadowCasting = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows = true;
        [SerializeField] private int renderingLayer = 0;

        private readonly List<DrawBatch> batches = new List<DrawBatch>();
        private readonly HashSet<int> hiddenPlacements = new HashSet<int>();
        private readonly Dictionary<Material, Material> instancedMaterials =
            new Dictionary<Material, Material>();
        private readonly Dictionary<Vector2Int, List<int>> placementChunks =
            new Dictionary<Vector2Int, List<int>>();

        private static readonly HashSet<TerrainFoliageGrassRenderer> activeRenderers =
            new HashSet<TerrainFoliageGrassRenderer>();

        private bool cacheDirty = true;
        private readonly HashSet<int> clearanceCandidates = new HashSet<int>();
        private readonly List<DrawSlot> drawSlots = new List<DrawSlot>();
        private int[] placementSlotHeads = Array.Empty<int>();

        private struct DrawInstance
        {
            public Matrix4x4 matrix;
            public int placementIndex;
        }

        // Linked through a flat array: one placement can contribute several meshes/submeshes.
        // This avoids allocating a List for every blade of grass.
        private struct DrawSlot
        {
            public DrawBatch batch;
            public int matrixIndex;
            public int next;
        }

        private sealed class DrawBatch
        {
            public Mesh mesh;
            public Material material;
            public int subMesh;
            public Vector3 chunkCentre;
            public Matrix4x4[] matrices;
            public int[] slotIds;
            public int count;
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public readonly Vector2Int chunk;
            public readonly Mesh mesh;
            public readonly Material material;
            public readonly int subMesh;

            public BatchKey(Vector2Int chunk, Mesh mesh, Material material, int subMesh)
            {
                this.chunk = chunk;
                this.mesh = mesh;
                this.material = material;
                this.subMesh = subMesh;
            }

            public bool Equals(BatchKey other) =>
                chunk == other.chunk && mesh == other.mesh &&
                material == other.material && subMesh == other.subMesh;

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = chunk.GetHashCode();
                    hash = hash * 397 ^ (mesh != null ? RuntimeHelpers.GetHashCode(mesh) : 0);
                    hash = hash * 397 ^ (material != null ? RuntimeHelpers.GetHashCode(material) : 0);
                    hash = hash * 397 ^ subMesh;
                    return hash;
                }
            }
        }

        public TerrainFoliagePlacementData PlacementData
        {
            get => placementData;
            set
            {
                if (placementData == value)
                    return;

                placementData = value;
                hiddenPlacements.Clear();
                cacheDirty = true;
            }
        }

        public int VisibleGrassCount =>
            placementData != null
                ? Mathf.Max(0, placementData.GrassCount - hiddenPlacements.Count)
                : 0;

        public void Configure(
            float distance,
            float size,
            ShadowCastingMode shadows,
            bool receivesShadows,
            int layer)
        {
            renderDistance = Mathf.Max(5f, distance);
            chunkSize = Mathf.Max(4f, size);
            shadowCasting = shadows;
            receiveShadows = receivesShadows;
            renderingLayer = Mathf.Clamp(layer, 0, 31);
            cacheDirty = true;
        }

        private void OnEnable()
        {
            activeRenderers.Add(this);
            cacheDirty = true;
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            Camera.onPreCull += HandleCameraPreCull;
        }

        private void OnDisable()
        {
            activeRenderers.Remove(this);
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            Camera.onPreCull -= HandleCameraPreCull;
            ReleaseMaterials();
        }

        private void OnValidate()
        {
            renderDistance = Mathf.Max(5f, renderDistance);
            chunkSize = Mathf.Max(4f, chunkSize);
            renderingLayer = Mathf.Clamp(renderingLayer, 0, 31);
            cacheDirty = true;
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera camera)
        {
            if (Application.isPlaying && camera.cameraType == CameraType.Game)
                Render(camera);
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (Application.isPlaying &&
                GraphicsSettings.currentRenderPipeline == null &&
                camera.cameraType == CameraType.Game)
            {
                Render(camera);
            }
        }

        public void Rebuild()
        {
            cacheDirty = false;
            batches.Clear();
            drawSlots.Clear();
            placementSlotHeads = placementData != null ? new int[placementData.GrassCount] : Array.Empty<int>();
            for (int i = 0; i < placementSlotHeads.Length; i++) placementSlotHeads[i] = -1;
            placementChunks.Clear();
            ReleaseMaterials();

            if (placementData == null || placementData.GrassCount == 0)
                return;

            Dictionary<BatchKey, List<DrawInstance>> grouped =
                new Dictionary<BatchKey, List<DrawInstance>>();

            for (int placementIndex = 0;
                 placementIndex < placementData.GrassPlacements.Count;
                 placementIndex++)
            {
                if (hiddenPlacements.Contains(placementIndex))
                    continue;

                TerrainFoliagePlacementData.Placement placement =
                    placementData.GrassPlacements[placementIndex];

                if (placement.prefabIndex < 0 ||
                    placement.prefabIndex >= placementData.GrassPrefabs.Count)
                {
                    continue;
                }

                GameObject prefab = placementData.GrassPrefabs[placement.prefabIndex];
                if (prefab == null)
                    continue;

                Matrix4x4 placementMatrix = Matrix4x4.TRS(
                    placement.position,
                    placement.rotation,
                    placement.scale);

                Vector2Int chunk = new Vector2Int(
                    Mathf.FloorToInt(placement.position.x / chunkSize),
                    Mathf.FloorToInt(placement.position.z / chunkSize));

                if (!placementChunks.TryGetValue(chunk, out List<int> chunkPlacements))
                {
                    chunkPlacements = new List<int>();
                    placementChunks.Add(chunk, chunkPlacements);
                }
                chunkPlacements.Add(placementIndex);

                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;

                for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
                {
                    MeshFilter filter = filters[filterIndex];
                    MeshRenderer renderer = filter != null
                        ? filter.GetComponent<MeshRenderer>()
                        : null;
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (renderer == null || mesh == null)
                        continue;

                    Matrix4x4 childMatrix =
                        rootInverse * filter.transform.localToWorldMatrix;
                    Matrix4x4 matrix = placementMatrix * childMatrix;
                    Material[] materials = renderer.sharedMaterials;
                    int subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);

                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    {
                        Material sourceMaterial = materials[subMesh];
                        if (sourceMaterial == null)
                            continue;

                        Material material = GetInstancedMaterial(sourceMaterial);
                        BatchKey key = new BatchKey(chunk, mesh, material, subMesh);
                        if (!grouped.TryGetValue(key, out List<DrawInstance> matrices))
                        {
                            matrices = new List<DrawInstance>();
                            grouped.Add(key, matrices);
                        }

                        matrices.Add(new DrawInstance { matrix = matrix, placementIndex = placementIndex });
                    }
                }
            }

            foreach (KeyValuePair<BatchKey, List<DrawInstance>> pair in grouped)
            {
                List<DrawInstance> source = pair.Value;
                for (int start = 0; start < source.Count; start += 1023)
                {
                    int count = Mathf.Min(1023, source.Count - start);
                    Matrix4x4[] matrices = new Matrix4x4[count];
                    var batch = new DrawBatch
                    {
                        mesh = pair.Key.mesh,
                        material = pair.Key.material,
                        subMesh = pair.Key.subMesh,
                        chunkCentre = new Vector3(
                            (pair.Key.chunk.x + 0.5f) * chunkSize,
                            0f,
                            (pair.Key.chunk.y + 0.5f) * chunkSize),
                        matrices = matrices,
                        slotIds = new int[count],
                        count = count
                    };
                    for (int i = 0; i < count; i++)
                    {
                        DrawInstance instance = source[start + i];
                        matrices[i] = instance.matrix;
                        int slotId = drawSlots.Count;
                        batch.slotIds[i] = slotId;
                        drawSlots.Add(new DrawSlot
                        {
                            batch = batch,
                            matrixIndex = i,
                            next = placementSlotHeads[instance.placementIndex]
                        });
                        placementSlotHeads[instance.placementIndex] = slotId;
                    }
                    batches.Add(batch);
                }
            }
        }

        private bool IsInsidePreloadBounds(Vector3 position, float padding)
        {
            if (placementData == null || placementData.GrassCount == 0)
                return false;

            Bounds bounds = placementData.GrassWorldBounds;
            float safePadding = Mathf.Max(0f, padding);
            bounds.Expand(new Vector3(safePadding * 2f, 100000f, safePadding * 2f));
            return bounds.Contains(position);
        }

        private bool PathTouchesGrassBounds(IReadOnlyList<Vector3> path, float padding)
        {
            Bounds bounds = placementData.GrassWorldBounds;
            float safePadding = Mathf.Max(0f, padding);
            float pathMinimumX = float.PositiveInfinity;
            float pathMaximumX = float.NegativeInfinity;
            float pathMinimumZ = float.PositiveInfinity;
            float pathMaximumZ = float.NegativeInfinity;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 point = path[i];
                pathMinimumX = Mathf.Min(pathMinimumX, point.x);
                pathMaximumX = Mathf.Max(pathMaximumX, point.x);
                pathMinimumZ = Mathf.Min(pathMinimumZ, point.z);
                pathMaximumZ = Mathf.Max(pathMaximumZ, point.z);
            }

            return pathMaximumX >= bounds.min.x - safePadding &&
                pathMinimumX <= bounds.max.x + safePadding &&
                pathMaximumZ >= bounds.min.z - safePadding &&
                pathMinimumZ <= bounds.max.z + safePadding;
        }

        public void Render(Camera camera)
        {
            if (!isActiveAndEnabled || camera == null)
                return;

            Vector3 cameraPosition = camera.transform.position;

            if (cacheDirty)
            {
                if (!IsInsidePreloadBounds(cameraPosition, preloadDistance))
                    return;

                Rebuild();
            }
            float maximumDistance = renderDistance + chunkSize * 0.75f;
            float maximumDistanceSqr = maximumDistance * maximumDistance;

            for (int i = 0; i < batches.Count; i++)
            {
                DrawBatch batch = batches[i];
                Vector3 delta = batch.chunkCentre - cameraPosition;
                delta.y = 0f;
                if (batch.count == 0 || delta.sqrMagnitude > maximumDistanceSqr)
                    continue;

                Graphics.DrawMeshInstanced(
                    batch.mesh,
                    batch.subMesh,
                    batch.material,
                    batch.matrices,
                    batch.count,
                    null,
                    shadowCasting,
                    receiveShadows,
                    renderingLayer,
                    camera,
                    LightProbeUsage.Off);
            }
        }

        public static void GetActiveRenderers(
            List<TerrainFoliageGrassRenderer> results)
        {
            if (results == null)
                return;

            results.Clear();
            activeRenderers.RemoveWhere(renderer => renderer == null);
            foreach (TerrainFoliageGrassRenderer renderer in activeRenderers)
            {
                if (renderer != null && renderer.isActiveAndEnabled)
                    results.Add(renderer);
            }
        }

        public void AccumulateVisibleGrassCounts(
            Vector3 worldPosition,
            float radius,
            Dictionary<GameObject, int> results)
        {
            if (results == null || radius <= 0f)
                return;

            if (placementData == null || placementData.GrassCount == 0 ||
                !IsInsidePreloadBounds(worldPosition, radius))
            {
                return;
            }

            if (cacheDirty)
                Rebuild();

            float radiusSquared = radius * radius;
            int minimumChunkX = Mathf.FloorToInt((worldPosition.x - radius) / chunkSize);
            int maximumChunkX = Mathf.FloorToInt((worldPosition.x + radius) / chunkSize);
            int minimumChunkZ = Mathf.FloorToInt((worldPosition.z - radius) / chunkSize);
            int maximumChunkZ = Mathf.FloorToInt((worldPosition.z + radius) / chunkSize);

            for (int chunkZ = minimumChunkZ; chunkZ <= maximumChunkZ; chunkZ++)
            {
                for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
                {
                    if (!placementChunks.TryGetValue(
                            new Vector2Int(chunkX, chunkZ),
                            out List<int> indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        int placementIndex = indices[i];
                        if (hiddenPlacements.Contains(placementIndex))
                            continue;

                        TerrainFoliagePlacementData.Placement placement =
                            placementData.GrassPlacements[placementIndex];
                        Vector3 delta = placement.position - worldPosition;
                        if (delta.x * delta.x + delta.z * delta.z > radiusSquared)
                            continue;

                        if (placement.prefabIndex < 0 ||
                            placement.prefabIndex >= placementData.GrassPrefabs.Count)
                        {
                            continue;
                        }

                        GameObject prefab =
                            placementData.GrassPrefabs[placement.prefabIndex];
                        if (prefab == null)
                            continue;

                        results.TryGetValue(prefab, out int count);
                        results[prefab] = count + 1;
                    }
                }
            }
        }
        public int ClearAlongPath(IReadOnlyList<Vector3> path, float radius)
        {
            if (placementData == null || path == null || path.Count < 2 || radius < 0f)
                return 0;

            if (!PathTouchesGrassBounds(path, radius))
                return 0;

            if (cacheDirty)
                Rebuild();

            int removed = 0;
            float radiusSquared = radius * radius;
            HashSet<int> candidates = clearanceCandidates;
            candidates.Clear();

            // Restrict the exact path-distance test to spatial chunks touched by the trail.
            // This preserves the clearing shape while avoiding a full-terrain scan.
            for (int segment = 1; segment < path.Count; segment++)
            {
                Vector3 a = path[segment - 1];
                Vector3 b = path[segment];
                int minimumChunkX = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius) / chunkSize);
                int maximumChunkX = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + radius) / chunkSize);
                int minimumChunkZ = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - radius) / chunkSize);
                int maximumChunkZ = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + radius) / chunkSize);

                for (int chunkZ = minimumChunkZ; chunkZ <= maximumChunkZ; chunkZ++)
                {
                    for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
                    {
                        if (!placementChunks.TryGetValue(new Vector2Int(chunkX, chunkZ), out List<int> indices))
                            continue;

                        for (int index = 0; index < indices.Count; index++)
                            candidates.Add(indices[index]);
                    }
                }
            }

            foreach (int i in candidates)
            {
                if (hiddenPlacements.Contains(i))
                    continue;

                Vector3 position = placementData.GrassPlacements[i].position;
                Vector2 point = new Vector2(position.x, position.z);
                float nearestSquared = float.PositiveInfinity;

                for (int segment = 1; segment < path.Count; segment++)
                {
                    Vector2 a = new Vector2(path[segment - 1].x, path[segment - 1].z);
                    Vector2 b = new Vector2(path[segment].x, path[segment].z);
                    nearestSquared = Mathf.Min(
                        nearestSquared,
                        SqrDistanceToSegment(point, a, b));
                }

                if (nearestSquared > radiusSquared)
                    continue;

                hiddenPlacements.Add(i);
                RemovePlacementFromDrawBatches(i);
                removed++;
            }

            return removed;
        }

        private void RemovePlacementFromDrawBatches(int placementIndex)
        {
            for (int id = placementSlotHeads[placementIndex]; id >= 0;)
            {
                DrawSlot slot = drawSlots[id];
                DrawBatch batch = slot.batch;
                int last = --batch.count;
                if (slot.matrixIndex != last)
                {
                    // Compact only this draw batch. Update the moved instance's slot so a
                    // subsequent removal still targets its current matrix position.
                    batch.matrices[slot.matrixIndex] = batch.matrices[last];
                    int movedId = batch.slotIds[last];
                    batch.slotIds[slot.matrixIndex] = movedId;
                    DrawSlot moved = drawSlots[movedId];
                    moved.matrixIndex = slot.matrixIndex;
                    drawSlots[movedId] = moved;
                }
                id = slot.next;
            }
            placementSlotHeads[placementIndex] = -1;
        }

        public void RestoreClearedGrass()
        {
            if (hiddenPlacements.Count == 0)
                return;

            hiddenPlacements.Clear();
            Rebuild();
        }

        private Material GetInstancedMaterial(Material source)
        {
            if (instancedMaterials.TryGetValue(source, out Material material))
                return material;

            material = new Material(source)
            {
                name = source.name + " (Managed Grass Instancing)",
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave
            };
            instancedMaterials.Add(source, material);
            return material;
        }

        private void ReleaseMaterials()
        {
            foreach (Material material in instancedMaterials.Values)
            {
                if (material == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }

            instancedMaterials.Clear();
        }

        private static float SqrDistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return (point - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return (point - (a + ab * t)).sqrMagnitude;
        }
    }
}
