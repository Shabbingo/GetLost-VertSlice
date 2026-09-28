using UnityEngine;

[CreateAssetMenu(fileName = "TerrainHeightData", menuName = "SOs/Terrain/Height Grid Data")]
public class HeightGridData : GridDataBase
{
    [Header("Serialized Height Data")]
    [SerializeField] private float[] flattenedHeights;

    // --- Initialization ---
    public void Initialize(int w, int d)
    {
        width = w;
        depth = d;
        flattenedHeights = new float[w * d];
    }

    // --- Direct Grid Access ---
    public void SetHeight(int x, int z, float value)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
        {
            Debug.LogWarning($"SetHeight: Index out of bounds ({x},{z}) for grid size {width}x{depth}");
            return;
        }
        flattenedHeights[z * width + x] = value;
    }

    public float GetHeight(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return float.NaN;
        return flattenedHeights[z * width + x];
    }

    // Converts back to a 2D array for visualization or processing
    public float[,] GetHeights2D()
    {
        float[,] grid = new float[width, depth];
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                grid[x, z] = flattenedHeights[z * width + x];
        return grid;
    }

    // --- World-Space Lookup (Bilinear Interpolation) ---
    public float GetInterpolatedHeight(Vector3 worldPos)
    {
        if (flattenedHeights == null || flattenedHeights.Length == 0)
        {
            Debug.LogWarning("HeightGridData: Grid data is empty.");
            return float.NaN;
        }

        if (!TryWorldToGrid(worldPos, out int x0, out int z0, out float fx, out float fz))
            return float.NaN;

        int x1 = x0 + 1;
        int z1 = z0 + 1;

        // Bilinear interpolation
        float h00 = GetHeight(x0, z0);
        float h10 = GetHeight(x1, z0);
        float h01 = GetHeight(x0, z1);
        float h11 = GetHeight(x1, z1);

        float h0 = Mathf.Lerp(h00, h10, fx);
        float h1 = Mathf.Lerp(h01, h11, fx);
        return Mathf.Lerp(h0, h1, fz);
    }

    // Optional: Validate the asset in the Inspector
    private void OnValidate()
    {
        if (flattenedHeights != null && width > 0 && depth > 0)
        {
            int expectedSize = width * depth;
            if (flattenedHeights.Length != expectedSize)
                Debug.LogWarning($"HeightGridData: Data array size ({flattenedHeights.Length}) does not match grid dimensions ({width}x{depth} = {expectedSize}).");
        }
    }
}
