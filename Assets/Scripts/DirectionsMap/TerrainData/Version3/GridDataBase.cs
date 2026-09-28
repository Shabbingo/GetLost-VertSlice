using UnityEngine;

/// <summary>
/// Common grid metadata + world-to-grid math shared by any ScriptableObject
/// that stores per-cell data sampled across a world-space grid (height, water, etc).
/// Keeping this shared means multiple grid assets (e.g. HeightGridData and
/// WaterGridData) can be generated with identical origin/spacing so their
/// cell indices line up 1:1.
/// </summary>
public abstract class GridDataBase : ScriptableObject
{
    [Header("Grid Metadata")]
    public Vector3 origin;   // Bottom-left corner in world space
    public int width;        // Number of samples along X
    public int depth;        // Number of samples along Z
    public float spacingX;   // World distance between samples on X
    public float spacingZ;   // World distance between samples on Z

    /// <summary>
    /// Copies grid layout (origin/width/depth/spacing) from another grid asset.
    /// Use this to guarantee two different data grids (e.g. height + water)
    /// share identical cell indexing.
    /// </summary>
    public void CopyGridLayoutFrom(GridDataBase other)
    {
        if (other == null)
        {
            Debug.LogWarning("GridDataBase: CopyGridLayoutFrom called with a null source.");
            return;
        }

        origin = other.origin;
        width = other.width;
        depth = other.depth;
        spacingX = other.spacingX;
        spacingZ = other.spacingZ;
    }

    /// <summary>
    /// Converts a world position into grid-cell coordinates for bilinear interpolation.
    /// Returns false if the position falls outside the interpolatable range of the grid.
    /// </summary>
    protected bool TryWorldToGrid(Vector3 worldPos, out int x0, out int z0, out float fx, out float fz)
    {
        float gridX = (worldPos.x - origin.x) / spacingX;
        float gridZ = (worldPos.z - origin.z) / spacingZ;

        x0 = Mathf.FloorToInt(gridX);
        z0 = Mathf.FloorToInt(gridZ);
        fx = gridX - x0;
        fz = gridZ - z0;

        int x1 = x0 + 1;
        int z1 = z0 + 1;

        return x0 >= 0 && x1 < width && z0 >= 0 && z1 < depth;
    }

    /// <summary>
    /// Converts a world position to the nearest single grid index.
    /// Useful for boolean/categorical grids (like water masks) where
    /// interpolating between cells doesn't make sense.
    /// </summary>
    protected bool TryWorldToNearestIndex(Vector3 worldPos, out int x, out int z)
    {
        float gridX = (worldPos.x - origin.x) / spacingX;
        float gridZ = (worldPos.z - origin.z) / spacingZ;

        x = Mathf.RoundToInt(gridX);
        z = Mathf.RoundToInt(gridZ);

        return x >= 0 && x < width && z >= 0 && z < depth;
    }
}
