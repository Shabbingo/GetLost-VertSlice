using System.Collections;
using System.Collections.Generic;
using GetLost.Trails;
using UnityEngine;

namespace GetLost.Wagon
{
    public sealed class WagonWorld : MonoBehaviour
    {
        private const string PathTerrainLayerResource = "GetLost/GravelPath";

        public WagonController Wagon { get; private set; }
        public TrailSystemSettings Settings { get; private set; }
        public bool Ready { get; private set; }
        public bool IsInitialized => Wagon != null;
        [Header("Gravel Usage")]
        public float metresPerGravelUnit = 1f;
        [Header("Path Sampling")]
        [Min(0.1f)] public float sampleSpacing = 0.4f;
        [Tooltip("Distance combined into one terrain edit. Larger values reduce terrain API calls.")]
        [Min(0.5f)] public float terrainBatchLength = 6f;
        [Tooltip("Maximum time a short final section waits before being built.")]
        [Range(0.05f, 5f)] public float maximumBatchWait = 2.5f;
        [Header("Path Shape")]
        [Min(0.1f)] public float trailWidth = 1.65f;
        [Min(0f)] public float textureBlendWidth = 0.25f;
        [Range(0f, 1f)] public float pathTextureStrength = 1f;
        [Header("Terrain Deformation")]
        public bool deformTerrain = true;
        [Min(0f)] public float deformationFalloffWidth = 0.35f;
        [Min(0f)] public float maximumCutMetres = 0.15f;
        [Min(0f)] public float maximumFillMetres = 0.2f;
        [Range(0f, 1f)] public float flattenStrength = 0.85f;
        [Tooltip("Height of deposited gravel above the sampled terrain surface.")]
        [Min(0f)] public float raiseByMetres = 0.06f;
        [Header("Grass Clearance")]
        [Tooltip("Extra cleared width on each side of the painted path.")]
        [Min(0f)] public float grassClearancePadding = 0.5f;
        [Header("Ground Validation")]
        [Tooltip("When disabled, vegetation and prop colliders cannot create unexplained gaps in the path.")]
        public bool rejectNonTerrainObstructions;
        [Min(0f)] public float maximumOutletHeight = 3f;
        [Min(0f)] public float maximumOutletPenetration = 1f;
        [Header("Terrain Layer")]
        [SerializeField] private TerrainLayer pathTerrainLayer;
        [Header("Path Triggers")]
        [Min(0f)] public float triggerWidthPadding = 0.25f;
        [Min(0.1f)] public float triggerHeight = 1.25f;
        [Min(0.1f)] public float triggerSpacing = 0.5f;
        [Min(0.02f)] public float triggerOverlap = 0.15f;
        public WagonPathNetwork PathNetwork { get; private set; }
        public IReadOnlyList<GravelStrip> Strips => strips;
        private readonly List<GravelStrip> strips = new();
        private readonly Dictionary<Vector2Int, List<GravelStrip>> coverage = new();
        private WagonGravelSurface surface;
        private readonly Dictionary<Terrain, TerrainData> terrainSources = new();
        private readonly List<TerrainData> runtimeTerrainData = new();
        private Vector3 lastPoint;
        private bool hasLastPoint;
        private float nextSampleTime;
        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private readonly List<Vector3> pendingBuildPath = new();
        private float pendingBuildLength;
        private float pendingBuildStartedAt;
        public int PendingSurfaceBuildCount => pendingBuildPath.Count >= 2 ? 1 : 0;
        private const float CoverageCell = 2f;

        public void Initialize(WagonController wagon, TrailSystemSettings source)
        {
            Wagon = wagon;
            Settings = source != null ? Instantiate(source) : ScriptableObject.CreateInstance<TrailSystemSettings>();
            Settings.name = "Runtime wagon gravel settings";
            Settings.trailWidth = trailWidth;
            Settings.textureBlendWidth = textureBlendWidth;
            Settings.pathTextureStrength = pathTextureStrength;
            Settings.deformationFalloffWidth = deformationFalloffWidth;
            Settings.maximumCutMetres = maximumCutMetres;
            Settings.maximumFillMetres = maximumFillMetres;
            Settings.flattenStrength = flattenStrength;
            // The shared deformer subtracts lowerByMetres. A negative value therefore
            // deposits material above the sampled centre line instead of cutting a rut.
            Settings.lowerByMetres = -raiseByMetres;
            Settings.deformTerrain = deformTerrain;
            Settings.paintPathTexture = false; // Enabled after the real path layer has been resolved.
            Settings.clearFoliage = true;
            Settings.clearTerrainTrees = Settings.clearSpawnedGameObjects = false;
            Settings.clearTerrainDetails = true;
            Settings.preserveMeshTerrainDetails = true;
            Settings.foliageClearancePadding = grassClearancePadding;
            Settings.terrainDetailClearancePadding = grassClearancePadding;
            Settings.restoreTerrainAfterPlayMode = false; // Edits use disposable TerrainData clones.
            if (pathTerrainLayer == null)
                pathTerrainLayer = Resources.Load<TerrainLayer>(PathTerrainLayerResource);
            if (pathTerrainLayer == null)
                Debug.LogError($"[Wagon Path] Missing Resources/{PathTerrainLayerResource}.terrainlayer. " +
                               "Run Get Lost/Vertical Slice/Rebuild Player Prefab to restore the path layer.");
            surface = new WagonGravelSurface(Settings);
            var pathObject = new GameObject("Wagon Runtime Path");
            Rigidbody pathBody = pathObject.AddComponent<Rigidbody>();
            pathBody.isKinematic = true;
            pathBody.useGravity = false;
            PathNetwork = pathObject.AddComponent<WagonPathNetwork>();
            PathNetwork.Configure(trailWidth, triggerWidthPadding, triggerHeight,
                triggerSpacing, triggerOverlap);
            StartCoroutine(Prepare());
        }

        private IEnumerator Prepare()
        {
            if (pathTerrainLayer == null)
                yield break;

            int resolvedPathLayerIndex = -1;
            // Clone terrain before deposition; scene reload and leaving Play Mode discard edits safely.
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                terrainSources.Add(terrain, terrain.terrainData);
                TerrainData copy = Instantiate(terrain.terrainData);
                copy.name = terrain.terrainData.name + " (wagon runtime)";
                int terrainPathLayerIndex = EnsurePathTerrainLayer(copy, pathTerrainLayer);
                if (resolvedPathLayerIndex < 0)
                    resolvedPathLayerIndex = terrainPathLayerIndex;
                else if (terrainPathLayerIndex != resolvedPathLayerIndex)
                {
                    Debug.LogError($"[Wagon Path] Terrain '{terrain.name}' resolved the path layer at index " +
                                   $"{terrainPathLayerIndex}, expected {resolvedPathLayerIndex}. Path creation was disabled.");
                    yield break;
                }
                runtimeTerrainData.Add(copy);
                terrain.terrainData = copy;
                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                if (collider != null) collider.terrainData = copy;
                yield return null;
            }

            if (resolvedPathLayerIndex < 0)
            {
                Debug.LogError("[Wagon Path] No active terrain was found. Path creation was disabled.");
                yield break;
            }

            Settings.pathTerrainLayerIndex = resolvedPathLayerIndex;
            Settings.paintPathTexture = true;
            Debug.Log($"[Wagon Path] Painting '{pathTerrainLayer.name}' at terrain layer index " +
                      $"{resolvedPathLayerIndex}.");
            Ready = true;
        }

        private static int EnsurePathTerrainLayer(TerrainData terrainData, TerrainLayer pathLayer)
        {
            TerrainLayer[] layers = terrainData.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] == pathLayer)
                    return i;

            TerrainLayer[] expanded = new TerrainLayer[layers.Length + 1];
            System.Array.Copy(layers, expanded, layers.Length);
            expanded[layers.Length] = pathLayer;
            terrainData.terrainLayers = expanded;
            return layers.Length;
        }

        private void Update()
        {
            if (pendingBuildPath.Count >= 2 &&
                Time.unscaledTime - pendingBuildStartedAt >= maximumBatchWait)
                FlushPendingBuild();

            if (!Ready || Wagon == null || !Wagon.CanDeposit || !Wagon.GateOpen ||
                Wagon.gravel <= 0f || Time.timeScale <= 0f)
            {
                FlushPendingBuild();
                hasLastPoint = false;
                return;
            }
            if (Time.time < nextSampleTime) return;
            nextSampleTime = Time.time + 0.06f;
            Vector3 point = Wagon.Dispenser.position;
            if (!TryGround(ref point))
            {
                hasLastPoint = false;
                return;
            }
            if (!hasLastPoint) { lastPoint = point; hasLastPoint = true; return; }
            float distance = Vector3.ProjectOnPlane(point - lastPoint, Vector3.up).magnitude;
            if (distance < sampleSpacing) return;
            if (distance > 5f) { lastPoint = point; return; } // Teleport: never bridge the jump.

            // Short strips bound local edits and follow corners. The physics wagon cannot
            // normally cover more than one or two samples between these updates.
            // Ceil previously split a just-over-0.4m movement into TWO overlapping edits.
            // Emit full samples and carry the remainder into the next update instead.
            int count = Mathf.Max(1, Mathf.FloorToInt((distance + 0.0001f) / sampleSpacing));
            Vector3 start = lastPoint;
            for (int i = 1; i <= count; i++)
            {
                Vector3 end = Vector3.Lerp(lastPoint, point, Mathf.Min(1f, i * sampleSpacing / distance));
                if (!TryGround(ref end)) { hasLastPoint = false; return; }
                float length = Vector3.Distance(start, end);
                float availableLength = Wagon.gravel * metresPerGravelUnit;
                if (availableLength <= 0.001f) break;
                if (length > availableLength)
                {
                    end = Vector3.Lerp(start, end, availableLength / length);
                    length = availableLength;
                }
                var strip = new GravelStrip { start = start, end = end };
                QueueSurfaceBuild(strip);
                Remember(strip);
                Wagon.gravel = Mathf.Max(0f, Wagon.gravel - length / metresPerGravelUnit);
                start = end;
                if (Wagon.gravel <= 0.001f) { Wagon.gravel = 0f; Wagon.CloseGate(); break; }
            }
            lastPoint = start;
        }

        private void QueueSurfaceBuild(GravelStrip strip)
        {
            if (pendingBuildPath.Count == 0)
            {
                pendingBuildPath.Add(strip.start);
                pendingBuildStartedAt = Time.unscaledTime;
            }
            else if (Vector3.Distance(pendingBuildPath[pendingBuildPath.Count - 1],
                         strip.start) > 0.05f)
            {
                FlushPendingBuild();
                pendingBuildPath.Add(strip.start);
                pendingBuildStartedAt = Time.unscaledTime;
            }

            pendingBuildPath.Add(strip.end);
            pendingBuildLength += Vector3.Distance(strip.start, strip.end);
            if (pendingBuildLength >= terrainBatchLength)
                FlushPendingBuild();
        }

        private void FlushPendingBuild()
        {
            if (pendingBuildPath.Count < 2) return;
            var completedPath = new List<Vector3>(pendingBuildPath);

            // One authoritative operation produces the physical terrain, visual texture,
            // grass clearance and queryable trigger path from the same continuous polyline.
            surface.ApplyStage(completedPath, WagonSurfaceStage.Deform);
            surface.FlushHeightmaps();
            surface.ApplyStage(completedPath, WagonSurfaceStage.Paint);
            surface.ApplyStage(completedPath, WagonSurfaceStage.ManagedGrass);
            surface.ApplyStage(completedPath, WagonSurfaceStage.TerrainDetails);
            PathNetwork.AddPolyline(completedPath);
            pendingBuildPath.Clear();
            pendingBuildLength = 0f;
        }
        private bool TryGround(ref Vector3 point)
        {
            Terrain terrain = TrailTerrainUtility.FindTerrainAt(point);
            if (terrain == null || terrain.terrainData == null ||
                Settings.pathTerrainLayerIndex >= terrain.terrainData.alphamapLayers) return false;
            // Do not deposit through rocks, platforms, or gaps between the outlet and ground.
            float groundY = terrain.SampleHeight(point) + terrain.transform.position.y;
            if (point.y - groundY > maximumOutletHeight ||
                point.y < groundY - maximumOutletPenetration) return false;
            if (rejectNonTerrainObstructions)
            {
                int hitCount = Physics.RaycastNonAlloc(point + Vector3.up * 0.1f, Vector3.down,
                    groundHits, Mathf.Max(0.2f, point.y - groundY + 0.2f), ~0, QueryTriggerInteraction.Ignore);
                if (hitCount == groundHits.Length) return false;
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = groundHits[i];
                    if (hit.collider.transform.IsChildOf(Wagon.transform) || hit.collider is TerrainCollider)
                        continue;
                    return false;
                }
            }
            point.y = surface.OriginalHeight(terrain, point);
            return true;
        }

        private Vector2Int Cell(Vector3 point) => new(
            Mathf.FloorToInt(point.x / CoverageCell), Mathf.FloorToInt(point.z / CoverageCell));

        public bool IsCovered(Vector3 point) => IsCovered(point, new Vector3(float.PositiveInfinity, 0f, 0f));

        private bool IsCovered(Vector3 point, Vector3 connectedStart)
        {
            Vector2Int cell = Cell(point);
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                    if (coverage.TryGetValue(cell + new Vector2Int(x, z), out var nearby))
                        foreach (GravelStrip strip in nearby)
                        {
                            // Adjacent samples overlap by design. Ignoring connected recent
                            // strips preserves bends instead of mistaking them for retracing.
                            if (!float.IsInfinity(connectedStart.x) &&
                                WagonMath.DistanceToSegmentXZ(connectedStart, strip.start, strip.end) <=
                                sampleSpacing * 1.1f)
                                continue;
                            Vector3 ab = Vector3.ProjectOnPlane(strip.end - strip.start, Vector3.up);
                            float t = ab.sqrMagnitude > 0.00001f
                                ? Vector3.Dot(Vector3.ProjectOnPlane(point - strip.start, Vector3.up), ab) / ab.sqrMagnitude
                                : -1f;
                            if (t >= 0f && t <= 1f &&
                                WagonMath.DistanceToSegmentXZ(point, strip.start, strip.end) < Settings.trailWidth * 0.4f)
                                return true;
                        }
            return false;
        }

        private void Remember(GravelStrip strip)
        {
            strips.Add(strip);
            Vector2Int cell = Cell((strip.start + strip.end) * 0.5f);
            if (!coverage.TryGetValue(cell, out var list))
            {
                list = new List<GravelStrip>();
                coverage.Add(cell, list);
            }
            list.Add(strip);
        }

        public WagonSaveData Capture()
        {
            FlushPendingBuild();
            var data = new WagonSaveData
            {
                hasWagon = true, hasBeenUsed = Wagon.HasBeenUsed, position = Wagon.Body.position, rotation = Wagon.Body.rotation,
                gravel = Wagon.gravel, capacity = Wagon.capacity
            };
            foreach (GravelStrip strip in strips)
                data.strips.Add(new GravelStrip { start = strip.start, end = strip.end });
            return data;
        }

        public IEnumerator Restore(WagonSaveData data)
        {
            if (data == null || !data.hasWagon || Wagon == null) yield break;
            while (!Ready) yield return null;
            Wagon.PlaceBody(data.position, data.rotation);
            foreach (Rigidbody body in Wagon.Bodies) body.isKinematic = true;
            Wagon.RestoreUsage(data.hasBeenUsed);
            Wagon.capacity = Mathf.Max(1f, data.capacity);
            Wagon.gravel = Mathf.Clamp(data.gravel, 0f, Wagon.capacity);
            hasLastPoint = false;
            strips.Clear();
            coverage.Clear();
            try
            {
                if (data.strips != null)
                    foreach (GravelStrip strip in data.strips)
                    {
                        if (strip == null || Vector3.Distance(strip.start, strip.end) > 5f) continue;
                        surface.Apply(strip, deferHeightSync: true);
                        Remember(strip);
                        PathNetwork.AddPolyline(new[] { strip.start, strip.end });
                        yield return null;
                    }
            }
            finally
            {
                surface.FlushHeightmaps();
                foreach (Rigidbody body in Wagon.Bodies)
                    if (body != null) body.isKinematic = false;
            }
        }

        private void OnDestroy()
        {
            foreach (var entry in terrainSources)
            {
                if (entry.Key == null) continue;
                entry.Key.terrainData = entry.Value;
                TerrainCollider collider = entry.Key.GetComponent<TerrainCollider>();
                if (collider != null) collider.terrainData = entry.Value;
            }
            foreach (TerrainData copy in runtimeTerrainData)
                if (copy != null) Destroy(copy);
            if (PathNetwork != null) Destroy(PathNetwork.gameObject);
            if (Settings != null) Destroy(Settings);
        }
    }
}
