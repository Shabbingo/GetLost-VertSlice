using UnityEngine;

[CreateAssetMenu(fileName = "WaterGridData", menuName = "SOs/Terrain/Water Grid Data")]

public class WaterGridData : GridDataBase
{
    [Header("Serialized Water Data")]
    [SerializeField] private bool[] isWater;
    [SerializeField] private float[] waterHeight; // Surface height at this cell, NaN if not water

    // --- Initialization ---
    public void Initialize(int w, int d)
    {
        width = w;
        depth = d;
        isWater = new bool[w * d];
        waterHeight = new float[w * d];
        for (int i = 0; i < waterHeight.Length; i++)
            waterHeight[i] = float.NaN;
    }

    // --- Direct Grid Access ---
    public void SetWater(int x, int z, bool value, float height)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
        {
            Debug.LogWarning($"SetWater: Index out of bounds ({x},{z}) for grid size {width}x{depth}");
            return;
        }
        int i = z * width + x;
        isWater[i] = value;
        waterHeight[i] = value ? height : float.NaN;
    }

    public bool IsWater(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return false;
        return isWater[z * width + x];
    }

    public float GetWaterHeight(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return float.NaN;
        return waterHeight[z * width + x];
    }

    // Converts back to a 2D array for visualization or processing
    public bool[,] GetWaterMask2D()
    {
        bool[,] grid = new bool[width, depth];
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                grid[x, z] = isWater[z * width + x];
        return grid;
    }

    public float[,] GetWaterHeights2D()
    {
        float[,] grid = new float[width, depth];
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                grid[x, z] = waterHeight[z * width + x];
        return grid;
    }

    // --- World-Space Lookup ---
    // Note: water presence is boolean/categorical, so we use nearest-sample lookup
    // rather than bilinear interpolation (interpolating "half water" doesn't mean anything).

    public bool IsWaterAtWorldPosition(Vector3 worldPos)
    {
        if (isWater == null || isWater.Length == 0) return false;
        if (!TryWorldToNearestIndex(worldPos, out int x, out int z)) return false;
        return IsWater(x, z);
    }

    public float GetWaterHeightAtWorldPosition(Vector3 worldPos)
    {
        if (waterHeight == null || waterHeight.Length == 0) return float.NaN;
        if (!TryWorldToNearestIndex(worldPos, out int x, out int z)) return float.NaN;
        return GetWaterHeight(x, z);
    }

    /// <summary>
    /// Returns a bilinearly-interpolated "how wet is this point" value in the range 0..1,
    /// treating each grid cell as 0 (dry) or 1 (water) and blending between neighbors.
    /// Unlike IsWaterAtWorldPosition (nearest-sample, hard edges), this produces a smooth
    /// gradient across the water/land boundary — useful for anti-aliased rendering.
    /// </summary>
    public float GetWaterCoverage(Vector3 worldPos)
    {
        if (isWater == null || isWater.Length == 0) return 0f;

        if (!TryWorldToGrid(worldPos, out int x0, out int z0, out float fx, out float fz))
        {
            // Outside the interpolatable range (edge of grid) — fall back to nearest sample.
            if (TryWorldToNearestIndex(worldPos, out int nx, out int nz))
                return IsWater(nx, nz) ? 1f : 0f;
            return 0f;
        }

        int x1 = x0 + 1;
        int z1 = z0 + 1;

        float w00 = IsWater(x0, z0) ? 1f : 0f;
        float w10 = IsWater(x1, z0) ? 1f : 0f;
        float w01 = IsWater(x0, z1) ? 1f : 0f;
        float w11 = IsWater(x1, z1) ? 1f : 0f;

        float w0 = Mathf.Lerp(w00, w10, fx);
        float w1 = Mathf.Lerp(w01, w11, fx);
        return Mathf.Lerp(w0, w1, fz);
    }

    // Optional: Validate the asset in the Inspector
    private void OnValidate()
    {
        if (isWater != null && width > 0 && depth > 0)
        {
            int expectedSize = width * depth;
            if (isWater.Length != expectedSize)
                Debug.LogWarning($"WaterGridData: Data array size ({isWater.Length}) does not match grid dimensions ({width}x{depth} = {expectedSize}).");
        }
    }
}