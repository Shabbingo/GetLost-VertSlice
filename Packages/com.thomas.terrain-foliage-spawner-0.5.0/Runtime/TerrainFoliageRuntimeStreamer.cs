using System;
using System.Collections.Generic;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [DisallowMultipleComponent]
    public sealed class TerrainFoliageRuntimeStreamer : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private TerrainFoliagePlacementData placementData;
        [SerializeField] private Transform target;

        [Header("Object Streaming")]
        [SerializeField, Min(1f)] private float activationDistance = 120f;
        [SerializeField, Min(1f)] private float deactivationDistance = 140f;
        [SerializeField, Min(4f)] private float chunkSize = 40f;
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.20f;
        [SerializeField, Min(1)] private int maximumActiveObjects = 800;
        [SerializeField, Min(1)] private int maximumChangesPerRefresh = 60;
        [SerializeField, Min(0f)] private float indexPreloadDistance = 250f;

        [Header("Renderer Culling")]
        [Tooltip("Disables renderers before the object itself is returned to the pool. This keeps nearby collision objects alive without drawing distant rocks.")]
        [SerializeField] private bool useRendererDistanceCulling = true;
        [SerializeField, Min(1f)] private float rendererDistance = 100f;

        [Tooltip("Renderer visibility is refreshed separately from spawn/despawn streaming.")]
        [SerializeField, Min(0.02f)] private float rendererRefreshInterval = 0.15f;

        [Tooltip("Maximum active objects whose renderer state can change in one renderer refresh.")]
        [SerializeField, Min(1)] private int maximumRendererChangesPerRefresh = 100;

        [Header("Frustum Culling")]
        [Tooltip("Unity already frustum-culls MeshRenderers. Leave this enabled only if you want the streamer to additionally disable renderers outside the camera frustum.")]
        [SerializeField] private bool useExplicitFrustumCulling = false;

        [Tooltip("Extra world-space padding around renderer bounds when testing the camera frustum.")]
        [SerializeField, Min(0f)] private float frustumBoundsPadding = 2f;

        [Header("Shadow Culling")]
        [SerializeField] private bool useShadowDistanceCulling = true;
        [SerializeField, Min(1f)] private float shadowDistance = 65f;

        [Header("Colliders")]
        [SerializeField] private bool disableCollidersOutsideActivation = true;

        private readonly Dictionary<Vector2Int, List<int>> chunks = new Dictionary<Vector2Int, List<int>>();
        private readonly Dictionary<int, RuntimeInstance> active = new Dictionary<int, RuntimeInstance>();
        private readonly Dictionary<int, Stack<GameObject>> pools = new Dictionary<int, Stack<GameObject>>();

        // Reused work buffers: avoids creating garbage every refresh.
        private readonly List<int> removalBuffer = new List<int>();
        private readonly List<int> activeKeyBuffer = new List<int>();

        private float nextRefresh;
        private float nextRendererRefresh;
        private bool indexBuilt;
        private Camera targetCamera;

        private sealed class RuntimeInstance
        {
            public GameObject gameObject;
            public Renderer[] renderers;
            public Collider[] colliders;
            public bool renderersEnabled;
            public bool shadowsEnabled;
        }

        public TerrainFoliagePlacementData PlacementData
        {
            get => placementData;
            set
            {
                if (placementData == value)
                    return;

                placementData = value;
                InvalidateIndex();
                TryBuildIndexForTarget();
            }
        }

        public Transform Target
        {
            get => target;
            set
            {
                target = value;
                ResolveCamera();
                TryBuildIndexForTarget();
            }
        }

        // Kept public so existing loading/progress UI can query the streamer.
        public int CurrentActiveObjects => active.Count;
        public int PendingCount => CalculatePendingCount();

        private void Awake()
        {
            if (target == null && Camera.main != null)
                target = Camera.main.transform;

            ResolveCamera();

            deactivationDistance = Mathf.Max(deactivationDistance, activationDistance + 1f);
            rendererDistance = Mathf.Min(rendererDistance, deactivationDistance);
            shadowDistance = Mathf.Min(shadowDistance, rendererDistance);

            InvalidateIndex();
            TryBuildIndexForTarget();
        }

        private void OnEnable()
        {
            nextRefresh = 0f;
            nextRendererRefresh = 0f;
        }

        private void OnDisable() => DespawnAll();

        private void Update()
        {
            if (!TryBuildIndexForTarget())
                return;

            float now = Time.unscaledTime;

            if (now >= nextRefresh)
            {
                nextRefresh = now + refreshInterval;
                RefreshStreaming();
            }

            if (now >= nextRendererRefresh)
            {
                nextRendererRefresh = now + rendererRefreshInterval;
                RefreshRendererCulling();
            }
        }

        public void RebuildIndex()
        {
            DespawnAll();
            chunks.Clear();
            indexBuilt = true;

            if (placementData == null)
                return;

            for (int i = 0; i < placementData.Placements.Count; i++)
            {
                Vector3 position = placementData.Placements[i].position;
                Vector2Int key = WorldToChunk(position);

                if (!chunks.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    chunks.Add(key, list);
                }

                list.Add(i);
            }
        }

        private void InvalidateIndex()
        {
            DespawnAll();
            chunks.Clear();
            indexBuilt = false;
        }

        private bool TryBuildIndexForTarget()
        {
            if (indexBuilt)
                return true;

            if (placementData == null)
            {
                indexBuilt = true;
                return true;
            }

            if (target == null || !IsTargetInsidePreloadBounds())
                return false;

            RebuildIndex();
            return true;
        }

        private bool IsTargetInsidePreloadBounds()
        {
            Bounds bounds = placementData.WorldBounds;
            float padding = activationDistance + Mathf.Max(0f, indexPreloadDistance);
            bounds.Expand(new Vector3(padding * 2f, 100000f, padding * 2f));
            return bounds.Contains(target.position);
        }

        public void RefreshStreaming()
        {
            if (placementData == null || target == null)
                return;

            float activateSqr = activationDistance * activationDistance;
            float deactivateSqr = deactivationDistance * deactivationDistance;

            int radius = Mathf.CeilToInt(deactivationDistance / chunkSize);
            Vector2Int centre = WorldToChunk(target.position);
            Vector3 targetPosition = target.position;
            int changes = 0;

            removalBuffer.Clear();

            foreach (KeyValuePair<int, RuntimeInstance> pair in active)
            {
                int placementIndex = pair.Key;

                if (placementIndex < 0 || placementIndex >= placementData.Placements.Count)
                {
                    removalBuffer.Add(placementIndex);
                    continue;
                }

                Vector3 delta = placementData.Placements[placementIndex].position - targetPosition;
                // Rocks care about horizontal streaming distance; height differences shouldn't
                // unnecessarily unload objects on cliffs/steep terrain.
                float horizontalSqr = delta.x * delta.x + delta.z * delta.z;

                if (horizontalSqr > deactivateSqr)
                    removalBuffer.Add(placementIndex);
            }

            for (int i = 0; i < removalBuffer.Count && changes < maximumChangesPerRefresh; i++)
            {
                Despawn(removalBuffer[i]);
                changes++;
            }

            for (int z = -radius; z <= radius && changes < maximumChangesPerRefresh; z++)
            {
                for (int x = -radius; x <= radius && changes < maximumChangesPerRefresh; x++)
                {
                    if (active.Count >= maximumActiveObjects)
                        return;

                    Vector2Int key = new Vector2Int(centre.x + x, centre.y + z);

                    if (!chunks.TryGetValue(key, out List<int> indices))
                        continue;

                    for (int i = 0; i < indices.Count && changes < maximumChangesPerRefresh; i++)
                    {
                        int placementIndex = indices[i];

                        if (active.ContainsKey(placementIndex))
                            continue;

                        TerrainFoliagePlacementData.Placement placement =
                            placementData.Placements[placementIndex];

                        Vector3 delta = placement.position - targetPosition;
                        float horizontalSqr = delta.x * delta.x + delta.z * delta.z;

                        if (horizontalSqr > activateSqr)
                            continue;

                        if (Spawn(placementIndex, placement))
                            changes++;

                        if (active.Count >= maximumActiveObjects)
                            return;
                    }
                }
            }
        }

        public void RefreshRendererCulling()
        {
            if (target == null || active.Count == 0)
                return;

            if (useExplicitFrustumCulling && targetCamera == null)
                ResolveCamera();

            float renderSqr = rendererDistance * rendererDistance;
            float shadowSqr = shadowDistance * shadowDistance;
            Vector3 targetPosition = target.position;

            Plane[] frustumPlanes = null;
            if (useExplicitFrustumCulling && targetCamera != null)
                frustumPlanes = GeometryUtility.CalculateFrustumPlanes(targetCamera);

            activeKeyBuffer.Clear();
            foreach (int key in active.Keys)
                activeKeyBuffer.Add(key);

            int changes = 0;

            for (int i = 0; i < activeKeyBuffer.Count; i++)
            {
                if (changes >= maximumRendererChangesPerRefresh)
                    break;

                int placementIndex = activeKeyBuffer[i];

                if (!active.TryGetValue(placementIndex, out RuntimeInstance runtime) ||
                    runtime == null ||
                    runtime.gameObject == null)
                {
                    continue;
                }

                Vector3 delta = runtime.gameObject.transform.position - targetPosition;
                float horizontalSqr = delta.x * delta.x + delta.z * delta.z;

                bool shouldRender =
                    !useRendererDistanceCulling ||
                    horizontalSqr <= renderSqr;

                if (shouldRender &&
                    useExplicitFrustumCulling &&
                    frustumPlanes != null &&
                    runtime.renderers != null &&
                    runtime.renderers.Length > 0)
                {
                    shouldRender = AnyRendererInFrustum(runtime.renderers, frustumPlanes);
                }

                if (runtime.renderersEnabled != shouldRender)
                {
                    SetRenderersEnabled(runtime, shouldRender);
                    runtime.renderersEnabled = shouldRender;
                    changes++;
                }

                bool shouldCastShadows =
                    shouldRender &&
                    (!useShadowDistanceCulling || horizontalSqr <= shadowSqr);

                if (runtime.shadowsEnabled != shouldCastShadows)
                {
                    SetShadowCasting(runtime, shouldCastShadows);
                    runtime.shadowsEnabled = shouldCastShadows;
                    changes++;
                }
            }
        }

        private bool Spawn(
            int placementIndex,
            TerrainFoliagePlacementData.Placement placement)
        {
            if (placement.prefabIndex < 0 ||
                placement.prefabIndex >= placementData.Prefabs.Count)
            {
                return false;
            }

            GameObject prefab = placementData.Prefabs[placement.prefabIndex];

            if (prefab == null)
                return false;

            if (!pools.TryGetValue(placement.prefabIndex, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                pools.Add(placement.prefabIndex, pool);
            }

            GameObject instance =
                pool.Count > 0
                    ? pool.Pop()
                    : Instantiate(prefab, transform);

            instance.transform.SetPositionAndRotation(
                placement.position,
                placement.rotation);

            instance.transform.localScale = placement.scale;
            instance.SetActive(true);

            RuntimeInstance runtime = new RuntimeInstance
            {
                gameObject = instance,
                renderers = instance.GetComponentsInChildren<Renderer>(true),
                colliders = instance.GetComponentsInChildren<Collider>(true),
                renderersEnabled = true,
                shadowsEnabled = true
            };

            SetRenderersEnabled(runtime, true);
            SetShadowCasting(runtime, true);

            if (disableCollidersOutsideActivation && runtime.colliders != null)
            {
                for (int i = 0; i < runtime.colliders.Length; i++)
                {
                    if (runtime.colliders[i] != null)
                        runtime.colliders[i].enabled = true;
                }
            }

            active.Add(placementIndex, runtime);

            // Immediately apply distance/frustum/shadow state so newly spawned
            // objects do not flash for one refresh.
            ApplyRendererState(runtime);

            return true;
        }

        private void ApplyRendererState(RuntimeInstance runtime)
        {
            if (runtime == null || runtime.gameObject == null || target == null)
                return;

            Vector3 delta = runtime.gameObject.transform.position - target.position;
            float horizontalSqr = delta.x * delta.x + delta.z * delta.z;

            bool shouldRender =
                !useRendererDistanceCulling ||
                horizontalSqr <= rendererDistance * rendererDistance;

            if (shouldRender && useExplicitFrustumCulling)
            {
                if (targetCamera == null)
                    ResolveCamera();

                if (targetCamera != null)
                {
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
                    shouldRender = AnyRendererInFrustum(runtime.renderers, planes);
                }
            }

            SetRenderersEnabled(runtime, shouldRender);
            runtime.renderersEnabled = shouldRender;

            bool shouldCastShadows =
                shouldRender &&
                (!useShadowDistanceCulling ||
                 horizontalSqr <= shadowDistance * shadowDistance);

            SetShadowCasting(runtime, shouldCastShadows);
            runtime.shadowsEnabled = shouldCastShadows;
        }

        private void Despawn(int placementIndex)
        {
            if (!active.TryGetValue(placementIndex, out RuntimeInstance runtime))
                return;

            active.Remove(placementIndex);

            if (runtime == null || runtime.gameObject == null)
                return;

            int prefabIndex = -1;

            if (placementData != null &&
                placementIndex >= 0 &&
                placementIndex < placementData.Placements.Count)
            {
                prefabIndex = placementData.Placements[placementIndex].prefabIndex;
            }

            runtime.gameObject.SetActive(false);

            if (prefabIndex < 0)
            {
                Destroy(runtime.gameObject);
                return;
            }

            if (!pools.TryGetValue(prefabIndex, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                pools.Add(prefabIndex, pool);
            }

            pool.Push(runtime.gameObject);
        }

        private void DespawnAll()
        {
            if (active.Count == 0)
                return;

            activeKeyBuffer.Clear();
            foreach (int key in active.Keys)
                activeKeyBuffer.Add(key);

            for (int i = 0; i < activeKeyBuffer.Count; i++)
                Despawn(activeKeyBuffer[i]);

            active.Clear();
        }

        private static void SetRenderersEnabled(RuntimeInstance runtime, bool enabled)
        {
            if (runtime?.renderers == null)
                return;

            for (int i = 0; i < runtime.renderers.Length; i++)
            {
                Renderer renderer = runtime.renderers[i];

                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        private static void SetShadowCasting(RuntimeInstance runtime, bool enabled)
        {
            if (runtime?.renderers == null)
                return;

            UnityEngine.Rendering.ShadowCastingMode mode =
                enabled
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int i = 0; i < runtime.renderers.Length; i++)
            {
                Renderer renderer = runtime.renderers[i];

                if (renderer != null)
                    renderer.shadowCastingMode = mode;
            }
        }

        private bool AnyRendererInFrustum(Renderer[] renderers, Plane[] planes)
        {
            if (renderers == null || renderers.Length == 0)
                return true;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null)
                    continue;

                Bounds bounds = renderer.bounds;

                if (frustumBoundsPadding > 0f)
                    bounds.Expand(frustumBoundsPadding * 2f);

                if (GeometryUtility.TestPlanesAABB(planes, bounds))
                    return true;
            }

            return false;
        }

        private void ResolveCamera()
        {
            targetCamera = null;

            if (target != null)
            {
                targetCamera = target.GetComponent<Camera>();

                if (targetCamera == null)
                    targetCamera = target.GetComponentInChildren<Camera>();
            }

            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private int CalculatePendingCount()
        {
            if (placementData == null || target == null)
                return 0;

            float activateSqr = activationDistance * activationDistance;
            int radius = Mathf.CeilToInt(activationDistance / chunkSize);
            Vector2Int centre = WorldToChunk(target.position);
            Vector3 targetPosition = target.position;
            int pending = 0;

            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (!chunks.TryGetValue(
                            new Vector2Int(centre.x + x, centre.y + z),
                            out List<int> indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        int placementIndex = indices[i];

                        if (active.ContainsKey(placementIndex))
                            continue;

                        Vector3 delta =
                            placementData.Placements[placementIndex].position -
                            targetPosition;

                        float horizontalSqr =
                            delta.x * delta.x + delta.z * delta.z;

                        if (horizontalSqr <= activateSqr)
                            pending++;
                    }
                }
            }

            return pending;
        }

        private Vector2Int WorldToChunk(Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / chunkSize),
                Mathf.FloorToInt(position.z / chunkSize));
        }

        private void OnValidate()
        {
            activationDistance = Mathf.Max(1f, activationDistance);
            deactivationDistance =
                Mathf.Max(activationDistance + 1f, deactivationDistance);

            chunkSize = Mathf.Max(4f, chunkSize);
            refreshInterval = Mathf.Max(0.02f, refreshInterval);
            rendererRefreshInterval = Mathf.Max(0.02f, rendererRefreshInterval);

            maximumActiveObjects = Mathf.Max(1, maximumActiveObjects);
            maximumChangesPerRefresh = Mathf.Max(1, maximumChangesPerRefresh);
            maximumRendererChangesPerRefresh =
                Mathf.Max(1, maximumRendererChangesPerRefresh);

            rendererDistance =
                Mathf.Clamp(rendererDistance, 1f, deactivationDistance);

            shadowDistance =
                Mathf.Clamp(shadowDistance, 1f, rendererDistance);

            frustumBoundsPadding = Mathf.Max(0f, frustumBoundsPadding);
        }
    }
}
