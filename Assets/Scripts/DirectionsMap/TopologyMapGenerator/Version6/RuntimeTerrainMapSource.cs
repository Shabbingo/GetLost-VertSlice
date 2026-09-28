using UnityEngine;

/// <summary>
/// Builds the topology generator's height grid directly from every active
/// Unity Terrain. This keeps the vertical-slice map in sync with generated
/// terrain and removes the need for a pre-baked HeightGridData asset.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(TopologyMapGenerator))]
public sealed class RuntimeTerrainMapSource : MonoBehaviour
{
    [Header("Runtime Sampling")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField, Range(33, 513)] private int maximumSamplesPerAxis = 257;
    [SerializeField, Min(0.25f)] private float minimumSampleSpacing = 2f;
    [SerializeField] private bool logGeneration = true;

    private TopologyMapGenerator topologyGenerator;
    private HeightGridData runtimeHeightData;

    private void Awake()
    {
        topologyGenerator = GetComponent<TopologyMapGenerator>();
        if (generateOnAwake)
            RebuildFromActiveTerrains();
    }

    [ContextMenu("Rebuild From Active Terrains")]
    public bool RebuildFromActiveTerrains()
    {
        topologyGenerator ??= GetComponent<TopologyMapGenerator>();
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogWarning("[Runtime Terrain Map] No active Terrain was found.", this);
            return false;
        }

        if (!TryGetTerrainBounds(terrains, out Bounds bounds))
            return false;

        float usableX = Mathf.Max(0.01f, bounds.size.x);
        float usableZ = Mathf.Max(0.01f, bounds.size.z);
        float spacing = Mathf.Max(
            minimumSampleSpacing,
            Mathf.Max(usableX, usableZ) / Mathf.Max(2, maximumSamplesPerAxis - 1));

        int width = Mathf.Clamp(Mathf.CeilToInt(usableX / spacing) + 1, 2, maximumSamplesPerAxis);
        int depth = Mathf.Clamp(Mathf.CeilToInt(usableZ / spacing) + 1, 2, maximumSamplesPerAxis);
        float spacingX = usableX / (width - 1);
        float spacingZ = usableZ / (depth - 1);

        if (runtimeHeightData != null)
            Destroy(runtimeHeightData);

        runtimeHeightData = ScriptableObject.CreateInstance<HeightGridData>();
        runtimeHeightData.name = "Runtime Terrain Height Grid";
        runtimeHeightData.hideFlags = HideFlags.DontSave;
        runtimeHeightData.origin = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
        runtimeHeightData.spacingX = spacingX;
        runtimeHeightData.spacingZ = spacingZ;
        runtimeHeightData.Initialize(width, depth);

        for (int z = 0; z < depth; z++)
        {
            float worldZ = bounds.min.z + z * spacingZ;
            for (int x = 0; x < width; x++)
            {
                float worldX = bounds.min.x + x * spacingX;
                runtimeHeightData.SetHeight(x, z, SampleTerrainHeight(terrains, worldX, worldZ, bounds.min.y));
            }
        }

        topologyGenerator.sourceData = runtimeHeightData;
        topologyGenerator.InvalidateBaseCache();

        if (logGeneration)
        {
            Debug.Log(
                $"[Runtime Terrain Map] Sampled {terrains.Length} terrain(s) into a {width}x{depth} grid.",
                this);
        }

        return true;
    }

    private static bool TryGetTerrainBounds(Terrain[] terrains, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            Vector3 size = terrain.terrainData.size;
            Bounds next = new Bounds(terrain.transform.position + size * 0.5f, size);
            if (!found)
            {
                bounds = next;
                found = true;
            }
            else
            {
                bounds.Encapsulate(next);
            }
        }

        return found;
    }

    private static float SampleTerrainHeight(Terrain[] terrains, float worldX, float worldZ, float fallback)
    {
        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            const float edgeTolerance = 0.01f;
            if (worldX < origin.x - edgeTolerance || worldX > origin.x + size.x + edgeTolerance ||
                worldZ < origin.z - edgeTolerance || worldZ > origin.z + size.z + edgeTolerance)
            {
                continue;
            }

            return origin.y + terrain.SampleHeight(new Vector3(worldX, origin.y, worldZ));
        }

        return fallback;
    }

    private void OnDestroy()
    {
        if (runtimeHeightData != null)
            Destroy(runtimeHeightData);
    }
}
