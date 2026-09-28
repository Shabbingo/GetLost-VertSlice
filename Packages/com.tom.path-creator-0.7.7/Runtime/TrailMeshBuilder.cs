using UnityEngine;

namespace Tom.PathCreator
{
    public enum TrailProjectionMode
    {
        None,
        AssignedTerrain,
        AutoTerrainChunks,
        PhysicsLayers
    }

    /// <summary>
    /// Generates a simple ribbon trail mesh along a PathCreator spline.
    /// Terrain projection samples the assigned Unity Terrain directly, avoiding
    /// trees, rocks, props, and other colliders above the ground.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TrailMeshBuilder : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        private PathCreator path;

        [Header("Shape")]
        [SerializeField, Min(0.05f)]
        private float width = 2f;

        [SerializeField, Min(0.05f)]
        private float pointSpacing = 0.75f;

        [SerializeField]
        private float verticalOffset = 0.03f;

        [Header("UVs")]
        [SerializeField, Min(0.01f)]
        private float uvTilesPerMetre = 0.25f;

        [Header("Ground Projection")]
        [SerializeField]
        private TrailProjectionMode projectionMode =
            TrailProjectionMode.AutoTerrainChunks;

        [SerializeField]
        private Terrain terrain;

        [SerializeField]
        private LayerMask groundLayers = ~0;

        [SerializeField, Min(0.1f)]
        private float projectionHeight = 20f;

        [SerializeField, Min(0.1f)]
        private float projectionDistance = 100f;

        [Header("Updating")]
        [SerializeField]
        private bool rebuildAutomatically = true;

        [SerializeField]
        private bool rebuildInPlayMode = true;

        private MeshFilter meshFilter;
        private Mesh generatedMesh;
        private bool rebuildQueued;

        public PathCreator Path => path;
        public Terrain Terrain => terrain;
        public float Width => width;
        public float PointSpacing => pointSpacing;
        public TrailProjectionMode ProjectionMode => projectionMode;

        private void OnEnable()
        {
            EnsureComponents();
            Subscribe();
            QueueRebuild();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DestroyGeneratedMesh();
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.05f, width);
            pointSpacing = Mathf.Max(0.05f, pointSpacing);
            uvTilesPerMetre = Mathf.Max(0.01f, uvTilesPerMetre);
            projectionHeight = Mathf.Max(0.1f, projectionHeight);
            projectionDistance = Mathf.Max(0.1f, projectionDistance);

            EnsureComponents();
            Subscribe();
            QueueRebuild();
        }

        private void Update()
        {
            if (!rebuildQueued)
            {
                return;
            }

            if (Application.isPlaying && !rebuildInPlayMode)
            {
                rebuildQueued = false;
                return;
            }

            rebuildQueued = false;
            Rebuild();
        }

        public void SetPath(PathCreator newPath)
        {
            if (path == newPath)
            {
                return;
            }

            Unsubscribe();
            path = newPath;
            Subscribe();
            QueueRebuild();
        }

        public void SetTerrain(Terrain newTerrain)
        {
            terrain = newTerrain;
            QueueRebuild();
        }

        public void SetProjectionMode(
            TrailProjectionMode newProjectionMode)
        {
            projectionMode = newProjectionMode;
            QueueRebuild();
        }

        public void QueueRebuild()
        {
            if (!rebuildAutomatically && !Application.isPlaying)
            {
                return;
            }

            rebuildQueued = true;
        }

        public void Rebuild()
        {
            EnsureComponents();

            if (path == null || path.SegmentCount == 0)
            {
                ClearMesh();
                return;
            }

            Vector3[] worldPoints =
                path.GetEvenlySpacedPoints(pointSpacing, 1.5f);

            if (worldPoints == null || worldPoints.Length < 2)
            {
                ClearMesh();
                return;
            }

            int rowCount = worldPoints.Length;
            var vertices = new Vector3[rowCount * 2];
            var normals = new Vector3[rowCount * 2];
            var uvs = new Vector2[rowCount * 2];
            var triangles = new int[(rowCount - 1) * 6];

            float accumulatedDistance = 0f;

            for (int i = 0; i < rowCount; i++)
            {
                Vector3 current = worldPoints[i];
                Vector3 previous = worldPoints[Mathf.Max(0, i - 1)];
                Vector3 next = worldPoints[Mathf.Min(rowCount - 1, i + 1)];

                Vector3 forward = (next - previous).normalized;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = path.transform.forward;
                }

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                if (right.sqrMagnitude < 0.0001f)
                {
                    right = path.transform.right;
                }

                Vector3 leftWorld =
                    current - right * (width * 0.5f);

                Vector3 rightWorld =
                    current + right * (width * 0.5f);

                leftWorld = ProjectPoint(leftWorld);
                rightWorld = ProjectPoint(rightWorld);

                leftWorld += Vector3.up * verticalOffset;
                rightWorld += Vector3.up * verticalOffset;

                vertices[i * 2] =
                    transform.InverseTransformPoint(leftWorld);

                vertices[i * 2 + 1] =
                    transform.InverseTransformPoint(rightWorld);

                Vector3 localUp =
                    transform.InverseTransformDirection(Vector3.up).normalized;

                normals[i * 2] = localUp;
                normals[i * 2 + 1] = localUp;

                if (i > 0)
                {
                    accumulatedDistance += Vector3.Distance(
                        worldPoints[i - 1],
                        worldPoints[i]);
                }

                float v = accumulatedDistance * uvTilesPerMetre;
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            int triangleIndex = 0;

            for (int i = 0; i < rowCount - 1; i++)
            {
                int rootIndex = i * 2;

                triangles[triangleIndex++] = rootIndex;
                triangles[triangleIndex++] = rootIndex + 2;
                triangles[triangleIndex++] = rootIndex + 1;

                triangles[triangleIndex++] = rootIndex + 1;
                triangles[triangleIndex++] = rootIndex + 2;
                triangles[triangleIndex++] = rootIndex + 3;
            }

            EnsureGeneratedMesh();
            generatedMesh.Clear();
            generatedMesh.name = "Generated Trail Mesh";
            generatedMesh.vertices = vertices;
            generatedMesh.normals = normals;
            generatedMesh.uv = uvs;
            generatedMesh.triangles = triangles;
            generatedMesh.RecalculateBounds();
            generatedMesh.RecalculateTangents();

            meshFilter.sharedMesh = generatedMesh;
        }

        public void ClearMesh()
        {
            EnsureComponents();

            if (generatedMesh != null)
            {
                generatedMesh.Clear();
            }

            if (meshFilter != null &&
                meshFilter.sharedMesh == generatedMesh)
            {
                meshFilter.sharedMesh = null;
            }
        }

        private Vector3 ProjectPoint(Vector3 point)
        {
            switch (projectionMode)
            {
                case TrailProjectionMode.None:
                    return point;

                case TrailProjectionMode.AssignedTerrain:
                    return ProjectToTerrain(point);

                case TrailProjectionMode.AutoTerrainChunks:
                    return TrailTerrainUtility.ProjectToTerrain(
                        point,
                        terrain);

                case TrailProjectionMode.PhysicsLayers:
                    return ProjectWithRaycast(point);

                default:
                    return point;
            }
        }

        private Vector3 ProjectToTerrain(Vector3 point)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return point;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 local = point - terrainPosition;

            if (local.x < 0f ||
                local.z < 0f ||
                local.x > terrainSize.x ||
                local.z > terrainSize.z)
            {
                return point;
            }

            point.y = terrain.SampleHeight(point) + terrainPosition.y;
            return point;
        }

        private Vector3 ProjectWithRaycast(Vector3 point)
        {
            Vector3 origin = point + Vector3.up * projectionHeight;
            float maxDistance = projectionHeight + projectionDistance;

            if (Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                maxDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return point;
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
        }

        private void EnsureGeneratedMesh()
        {
            if (generatedMesh != null)
            {
                return;
            }

            generatedMesh = new Mesh
            {
                name = "Generated Trail Mesh",
                hideFlags =
                    HideFlags.DontSaveInBuild |
                    HideFlags.DontSaveInEditor
            };

            generatedMesh.MarkDynamic();
        }

        private void Subscribe()
        {
            if (path == null)
            {
                return;
            }

            path.PathChanged -= HandlePathChanged;
            path.PathChanged += HandlePathChanged;
        }

        private void Unsubscribe()
        {
            if (path != null)
            {
                path.PathChanged -= HandlePathChanged;
            }
        }

        private void HandlePathChanged()
        {
            if (rebuildAutomatically)
            {
                rebuildQueued = true;
            }
        }

        private void DestroyGeneratedMesh()
        {
            if (generatedMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedMesh);
            }
            else
            {
                DestroyImmediate(generatedMesh);
            }

            generatedMesh = null;
        }
    }
}
