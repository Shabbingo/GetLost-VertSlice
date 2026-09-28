using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// How an icon's on-map size is determined.
/// FixedPixels: constant size on the output texture regardless of map scale (typical for UI-style markers).
/// WorldUnits: size scales with the map's world-space extent, so it represents a real-world radius/size.
/// </summary>
public enum IconSizeMode
{
    FixedPixels,
    WorldUnits
}

/// <summary>
/// A single icon to be stamped onto the topology map at a world-space position.
/// Note: v1 always centers the icon's bounding box on worldPosition — sprite.pivot
/// is not taken into account, so off-center pivots (e.g. a pin anchored at its tip)
/// will not be anchored the way they would in a SpriteRenderer.
/// </summary>
[System.Serializable]
public class MapIcon
{
    public Sprite sprite;
    public Vector3 worldPosition;
    public Color tint = Color.white;
    [Tooltip("Rotation applied to the icon, in degrees, counter-clockwise.")]
    public float rotationDegrees = 0f;

    public IconSizeMode sizeMode = IconSizeMode.FixedPixels;
    [Tooltip("Icon size in output pixels. Used when Size Mode = Fixed Pixels.")]
    public float pixelSize = 24f;
    [Tooltip("Icon size in world units. Used when Size Mode = World Units; scales with the map's world extent and resolution.")]
    public float worldSize = 20f;

    [Tooltip("Icons with a higher sorting order are drawn later (on top of) icons with a lower one. Ties keep insertion order.")]
    public int sortingOrder = 0;
}


/// <summary>
/// A single recorded point along a tracked travel path, with the world position and the
/// time it was recorded. The timestamp drives both the path's time-based color gradient
/// and any speed calculations done from the point list (e.g. by TravelPointsTracking).
/// </summary>
[System.Serializable]
public struct TravelPoint
{
    public Vector3 position;
    [Tooltip("Time.timeAsDouble at the moment this point was recorded.")]
    public double timestamp;

    public TravelPoint(Vector3 position, double timestamp)
    {
        this.position = position;
        this.timestamp = timestamp;
    }
}

/// <summary>
/// Generates a shaded contour (topology) map texture from a HeightGridData asset,
/// with an optional water overlay sourced from a WaterGridData asset.
///
/// Pipeline:
///  1. Sample the source height grid at texture resolution (bilinear) -> fine height buffer.
///     If a WaterGridData is assigned, also sample it (nearest, in world space) -> fine water mask.
///  2. Shade every pixel by height band (band = floor((h - min) / contourInterval)).
///  3. Run Marching Squares on the fine height buffer for each band boundary level,
///     producing line segments, which are rasterized on top of the shaded bands.
///  4. If water data is present, overlay water color over water pixels (masking contour
///     lines beneath the surface) and optionally draw a coastline outline.
///
/// Attach to any GameObject, assign a HeightGridData asset, and use the context menu
/// "Generate Topology Map" (or call GenerateTexture() directly from your own code).
/// </summary>
public class TopologyMapGenerator : MonoBehaviour
{
    [Header("Source Data")]
    public HeightGridData sourceData;

    [Header("Water Overlay (Optional)")]
    [Tooltip("If assigned, water cells are painted onto the map. Uses world-space lookups, so it doesn't need to share resolution with sourceData.")]
    public WaterGridData sourceWaterData;
    [Tooltip("If false, water data is ignored even if assigned.")]
    public bool showWaterOverlay = true;
    public Color waterColor = new Color(0.25f, 0.55f, 0.85f, 1f);
    [Tooltip("Draws a coastline outline along the water/land boundary.")]
    public bool drawWaterOutline = true;
    public Color waterOutlineColor = new Color(0.05f, 0.25f, 0.45f, 1f);
    [Range(1, 5)]
    public int waterOutlineThickness = 1;

    [Header("Icon Overlay")]
    [Tooltip("Icons stamped onto the map at their world positions. Add via the Inspector, or at runtime with AddIcon().")]
    public List<MapIcon> icons = new List<MapIcon>();
    [Tooltip("Log a warning when an icon's world position falls entirely outside the sampled map bounds.")]
    public bool logOutOfBoundsIcons = false;

    [Header("Travel Path Overlay")]
    [Tooltip("Recorded path points to render as a line on the map, ordered oldest-to-newest by timestamp. Populate via SetTravelPath()/AddTravelPoint() (e.g. from a TravelPointsTracking component attached to the player) or edit directly in the Inspector for testing.")]
    public List<TravelPoint> travelPath = new List<TravelPoint>();
    [Tooltip("If false, the travel path is skipped entirely even if points are present.")]
    public bool showTravelPath = true;
    [Tooltip("Color evaluated across normalized time (0 = the first point's timestamp, 1 = the last point's timestamp), giving the path a trail that shows age/recency.")]
    public Gradient travelPathGradient = DefaultPathGradient();
    [Range(1, 5)]
    public int travelPathThickness = 2;

    [Header("Output Settings")]
    [Tooltip("Width of the generated texture in pixels.")]
    public int textureWidth = 512;
    [Tooltip("Height of the generated texture in pixels.")]
    public int textureHeight = 512;

    [Header("Contour Settings")]
    [Tooltip("Vertical distance between height divisions / contour bands.")]
    public float contourInterval = 10f;
    [Tooltip("Color gradient evaluated across band index (0 = lowest band, 1 = highest band).")]
    public Gradient bandGradient = DefaultGradient();
    [Tooltip("Color of the drawn contour lines.")]
    public Color contourLineColor = Color.black;
    [Tooltip("Thickness of contour lines in pixels.")]
    [Range(1, 5)]
    public int contourLineThickness = 1;

    [Header("Export")]
    public bool saveToDisk = false;
    public string savePath = "Assets/TopologyMap.png";

    // Cached working buffers
    private float[,] fineHeights;
    private float[,] fineWaterCoverage; // 0..1 smooth field, null if no water data assigned
    private float minHeight;
    private float maxHeight;

    // Cache of the expensive base map (bands + contours + water), so icon-only
    // updates (e.g. a moving player marker) don't have to re-run marching squares.
    private Texture2D cachedBaseTexture;
    private Color[] cachedBasePixels;

    Action OnRefreshIconsComplete;

    public string fn_Get_AssetSavePath()
    {
        return ResolveSavePath();
    }

    /// <summary>
    /// Resolves the map output path differently for Editor and standalone builds.
    /// In the Editor we keep using the configured Assets/... path.
    /// In a build we write to Application.persistentDataPath instead, because the
    /// project Assets folder does not exist as a normal writable folder.
    /// </summary>
    private string ResolveSavePath()
    {
#if UNITY_EDITOR
        return savePath;
#else
        string fileName = Path.GetFileName(savePath);

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "TopologyMap.png";

        return Path.Combine(Application.persistentDataPath, fileName);
#endif
    }


    [ContextMenu("Generate Topology Map")]
    public void GenerateAndAssign()
    {
        Texture2D tex = GenerateTexture(forceRegenerateBase: true);
        if (tex == null) return;

        if (saveToDisk)
            SaveTextureAsPNG(tex, ResolveSavePath());
    }

    [ContextMenu("Refresh Icons Only (fast)")]
    public void fn_RefreshIconsOnly()
    {
        fn_RefreshIconsOnly(null);
    }

    public void fn_RefreshIconsOnly(Action OnComplete)
    {
        OnRefreshIconsComplete = OnComplete;
        Texture2D tex = GenerateTexture(forceRegenerateBase: false);
        if (tex == null) return;

        if (saveToDisk)
            SaveTextureAsPNG(tex, ResolveSavePath());

        OnRefreshIconsComplete?.Invoke();
        OnRefreshIconsComplete = null;
    }

    [ContextMenu("Refresh Travel Path Only (fast)")]
    public void fn_RefreshTravelPathOnly()
    {
        Texture2D tex = GenerateTexture(forceRegenerateBase: false);
        if (tex == null) return;

        if (saveToDisk)
            SaveTextureAsPNG(tex, ResolveSavePath());
    }

    /// <summary>
    /// Convenience entry point: returns the finished map, including the travel path and icons.
    /// Reuses the cached base map (bands + contours + water) unless forceRegenerateBase
    /// is true or no base has been generated yet — so calling this repeatedly to update
    /// the path (e.g. as the player moves) or icons (e.g. a moving player marker) is cheap,
    /// since neither one re-runs marching squares over the full texture.
    /// </summary>
    public Texture2D GenerateTexture(bool forceRegenerateBase = false)
    {
        Texture2D baseTex = GenerateBaseTexture(forceRegenerateBase);
        if (baseTex == null) return null;

        Color[] pixels = GetBaseWorkingPixels(baseTex);
        DrawTravelPathOnBuffer(pixels);
        StampAllIcons(pixels);
        return BuildTexture(pixels);
    }

    /// <summary>
    /// Builds (or returns the cached) base map: shaded height bands, contour lines, and
    /// water overlay. This is the expensive part of the pipeline (marching squares over
    /// the full texture) — call it rarely, and let GenerateTexture()/ApplyIcons() handle
    /// icon updates cheaply on top of the cached result.
    /// </summary>
    public Texture2D GenerateBaseTexture(bool forceRegenerate = false)
    {
        if (!forceRegenerate && cachedBaseTexture != null)
            return cachedBaseTexture;

        if (sourceData == null)
        {
            Debug.LogError("TopologyMapGenerator: No sourceData assigned.");
            return null;
        }
        if (textureWidth < 2 || textureHeight < 2)
        {
            Debug.LogError("TopologyMapGenerator: Texture dimensions must be >= 2.");
            return null;
        }
        if (contourInterval <= 0f)
        {
            Debug.LogError("TopologyMapGenerator: contourInterval must be > 0.");
            return null;
        }

        BuildFineHeightBuffer();

        Color[] pixels = new Color[textureWidth * textureHeight];

        // --- 1. Shade bands ---
        ShadeBands(pixels);

        // --- 2. Draw contour lines for every interior level ---
        int lowBand = Mathf.FloorToInt(minHeight / contourInterval);
        int highBand = Mathf.CeilToInt(maxHeight / contourInterval);
        for (int band = lowBand; band <= highBand; band++)
        {
            float level = band * contourInterval;
            if (level <= minHeight || level >= maxHeight) continue; // skip levels outside data range

            List<(Vector2 a, Vector2 b)> segments = MarchingSquares(fineHeights, level);
            foreach (var seg in segments)
                DrawLine(pixels, seg.a, seg.b, contourLineColor, contourLineThickness);
        }

        // --- 3. Overlay water on top (masks contour lines under the surface) ---
        if (showWaterOverlay && fineWaterCoverage != null)
        {
            ApplyWaterOverlay(pixels);
        }

        Texture2D tex = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();

        cachedBaseTexture = tex;
        cachedBasePixels = pixels;

        return tex;
    }

    /// <summary>
    /// Stamps the current icon list onto a copy of baseTexture and returns a new texture.
    /// Cheap relative to GenerateBaseTexture — safe to call whenever icons change (e.g.
    /// every time a tracked marker moves) without re-running contours/water.
    /// </summary>
    public Texture2D ApplyIcons(Texture2D baseTexture)
    {
        if (baseTexture == null)
        {
            Debug.LogError("TopologyMapGenerator: ApplyIcons called with a null base texture.");
            return null;
        }

        Color[] pixels = GetBaseWorkingPixels(baseTexture);
        StampAllIcons(pixels);
        return BuildTexture(pixels);
    }

    /// <summary>
    /// Draws the current travel path onto a copy of baseTexture and returns a new texture.
    /// Like ApplyIcons, this is a separate, cheap layer on top of the cached base map — it
    /// does not re-run marching squares, so it's safe to call every time a new tracking
    /// point is recorded.
    /// </summary>
    public Texture2D ApplyTravelPath(Texture2D baseTexture)
    {
        if (baseTexture == null)
        {
            Debug.LogError("TopologyMapGenerator: ApplyTravelPath called with a null base texture.");
            return null;
        }

        Color[] pixels = GetBaseWorkingPixels(baseTexture);
        DrawTravelPathOnBuffer(pixels);
        return BuildTexture(pixels);
    }

    /// <summary>
    /// Returns a working pixel buffer seeded from baseTexture, preferring the cached raw
    /// buffer (avoids a GPU readback via GetPixels) when baseTexture is the cached base map.
    /// </summary>
    private Color[] GetBaseWorkingPixels(Texture2D baseTexture)
    {
        return (cachedBaseTexture == baseTexture && cachedBasePixels != null)
            ? (Color[])cachedBasePixels.Clone()
            : baseTexture.GetPixels();
    }

    private Texture2D BuildTexture(Color[] pixels)
    {
        Texture2D result = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    private void StampAllIcons(Color[] pixels)
    {
        if (icons == null || icons.Count == 0) return;

        // OrderBy is a stable sort, so icons with equal sortingOrder keep list order.
        foreach (var icon in icons.OrderBy(i => i.sortingOrder))
            StampIcon(pixels, icon);
    }

    /// <summary>
    /// Clears the cached base map, forcing the next GenerateTexture()/GenerateBaseTexture()
    /// call to fully regenerate it. Call this after changing sourceData, sourceWaterData,
    /// texture resolution, contour, or water settings at runtime (editor field edits do
    /// this automatically via OnValidate).
    /// </summary>
    public void InvalidateBaseCache()
    {
        cachedBaseTexture = null;
        cachedBasePixels = null;
    }

    private void OnValidate()
    {
        InvalidateBaseCache();
    }

    // ---------------------------------------------------------------
    // Icon helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Adds and returns a new icon at the given world position. Call InvalidateBaseCache()
    /// is NOT required — icons are applied on top of the cached base map, so just call
    /// GenerateTexture() again after adding/removing icons.
    /// </summary>
    public MapIcon AddIcon(Sprite sprite, Vector3 worldPosition, Color? tint = null,
        float rotationDegrees = 0f, IconSizeMode sizeMode = IconSizeMode.FixedPixels,
        float pixelSize = 24f, float worldSize = 20f, int sortingOrder = 0)
    {
        var icon = new MapIcon
        {
            sprite = sprite,
            worldPosition = worldPosition,
            tint = tint ?? Color.white,
            rotationDegrees = rotationDegrees,
            sizeMode = sizeMode,
            pixelSize = pixelSize,
            worldSize = worldSize,
            sortingOrder = sortingOrder
        };
        icons.Add(icon);
        return icon;
    }

    /// <summary>Removes a specific icon instance (e.g. one previously returned by AddIcon).</summary>
    public bool RemoveIcon(MapIcon icon)
    {
        return icons.Remove(icon);
    }

    /// <summary>Removes all icons.</summary>
    public void ClearIcons()
    {
        icons.Clear();
    }

    // ---------------------------------------------------------------
    // Travel path helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Assigns the point list used to draw the travel path (e.g. from a TravelPointsTracking
    /// component). Stores the list reference directly rather than copying it, so if the
    /// caller keeps adding points to the same list instance, subsequent calls to
    /// GenerateTexture()/ApplyTravelPath() will pick them up automatically.
    /// </summary>
    public void SetTravelPath(List<TravelPoint> points)
    {
        travelPath = points ?? new List<TravelPoint>();
    }

    /// <summary>Appends a single point to the travel path (points should be added in increasing timestamp order).</summary>
    public void AddTravelPoint(TravelPoint point)
    {
        travelPath.Add(point);
    }

    /// <summary>Removes all travel path points.</summary>
    public void ClearTravelPath()
    {
        travelPath.Clear();
    }

    /// <summary>
    /// Converts a world-space position into output-texture pixel coordinates, using the
    /// same origin/spacing/resolution mapping as the height/water sampling. Returns false
    /// if sourceData isn't assigned or has degenerate bounds.
    /// </summary>
    public bool WorldToPixel(Vector3 worldPos, out Vector2 pixelPos)
    {
        if (sourceData == null || sourceData.width < 2 || sourceData.depth < 2)
        {
            pixelPos = Vector2.zero;
            return false;
        }

        float rangeX = (sourceData.width - 1) * sourceData.spacingX;
        float rangeZ = (sourceData.depth - 1) * sourceData.spacingZ;
        if (rangeX <= 0f || rangeZ <= 0f)
        {
            pixelPos = Vector2.zero;
            return false;
        }

        float u = (worldPos.x - sourceData.origin.x) / rangeX;
        float v = (worldPos.z - sourceData.origin.z) / rangeZ;

        pixelPos = new Vector2(u * (textureWidth - 1), v * (textureHeight - 1));
        return true;
    }

    /// <summary>
    /// Inverse of WorldToPixel: converts output-texture pixel coordinates back into a world-space
    /// position, using the same origin/spacing/resolution mapping. The Y component is sampled
    /// (bilinear) from sourceData's height grid, so the returned position sits on the terrain
    /// surface rather than at a fixed height — handy for placing planned-path nodes correctly
    /// in 3D. If you need physics-accurate height (e.g. to match an exact collider surface),
    /// raycast down from this position separately. Returns false if sourceData isn't assigned
    /// or has degenerate bounds.
    /// </summary>
    public bool PixelToWorld(Vector2 pixelPos, out Vector3 worldPos)
    {
        if (sourceData == null || sourceData.width < 2 || sourceData.depth < 2)
        {
            worldPos = default;
            return false;
        }

        float rangeX = (sourceData.width - 1) * sourceData.spacingX;
        float rangeZ = (sourceData.depth - 1) * sourceData.spacingZ;
        if (rangeX <= 0f || rangeZ <= 0f)
        {
            worldPos = default;
            return false;
        }

        float u = Mathf.Clamp01(pixelPos.x / (textureWidth - 1));
        float v = Mathf.Clamp01(pixelPos.y / (textureHeight - 1));

        float worldX = sourceData.origin.x + u * rangeX;
        float worldZ = sourceData.origin.z + v * rangeZ;
        float worldY = SampleHeightBilinear(u * (sourceData.width - 1), v * (sourceData.depth - 1));

        worldPos = new Vector3(worldX, worldY, worldZ);
        return true;
    }

    /// <summary>
    /// Bilinear height sample at a grid-space (not world-space, not pixel-space) coordinate.
    /// gx/gz are in the range [0, width-1] / [0, depth-1], matching sourceData's raw grid.
    /// Self-contained (doesn't require fineHeights to have been built), so it works even if
    /// GenerateBaseTexture() has never been called.
    /// </summary>
    private float SampleHeightBilinear(float gx, float gz)
    {
        float[,] raw = sourceData.GetHeights2D();
        int srcW = sourceData.width;
        int srcD = sourceData.depth;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, Mathf.Max(0, srcW - 2));
        int x1 = Mathf.Min(x0 + 1, srcW - 1);
        float fx = gx - x0;

        int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, Mathf.Max(0, srcD - 2));
        int z1 = Mathf.Min(z0 + 1, srcD - 1);
        float fz = gz - z0;

        float h00 = raw[x0, z0];
        float h10 = raw[x1, z0];
        float h01 = raw[x0, z1];
        float h11 = raw[x1, z1];

        float h0 = Mathf.Lerp(h00, h10, fx);
        float h1 = Mathf.Lerp(h01, h11, fx);
        return Mathf.Lerp(h0, h1, fz);
    }

    /// <summary>
    /// Draws the travel path as a sequence of line segments between consecutive points,
    /// colored by a gradient evaluated across normalized time (0 = first point's timestamp,
    /// 1 = last point's timestamp) so the trail visually shows age/recency. No-ops if
    /// showTravelPath is false or fewer than 2 points are present.
    /// </summary>
    private void DrawTravelPathOnBuffer(Color[] pixels)
    {
        if (!showTravelPath || travelPath == null || travelPath.Count < 2) return;

        double startTime = travelPath[0].timestamp;
        double endTime = travelPath[travelPath.Count - 1].timestamp;
        double timeRange = endTime - startTime;

        for (int i = 0; i < travelPath.Count - 1; i++)
        {
            TravelPoint a = travelPath[i];
            TravelPoint b = travelPath[i + 1];

            if (!WorldToPixel(a.position, out Vector2 pa)) continue;
            if (!WorldToPixel(b.position, out Vector2 pb)) continue;

            float ta = timeRange > 0.0 ? (float)((a.timestamp - startTime) / timeRange) : 0f;
            float tb = timeRange > 0.0 ? (float)((b.timestamp - startTime) / timeRange) : 0f;

            Color ca = travelPathGradient.Evaluate(ta);
            Color cb = travelPathGradient.Evaluate(tb);

            DrawGradientLine(pixels, pa, pb, ca, cb, travelPathThickness);
        }
    }

    /// <summary>
    /// Rasterizes a single icon onto the pixel buffer: rotated, scaled, alpha-blended,
    /// tinted, and clipped to the buffer bounds. The icon's bounding box is always
    /// centered on its world position (sprite.pivot is not used — see MapIcon summary).
    /// </summary>
    private void StampIcon(Color[] pixels, MapIcon icon)
    {
        if (icon == null || icon.sprite == null) return;

        if (!WorldToPixel(icon.worldPosition, out Vector2 center))
            return;

        Sprite sprite = icon.sprite;
        Texture2D spriteTex = sprite.texture;

        if (spriteTex == null) return;

        if (!spriteTex.isReadable)
        {
            Debug.LogWarning($"TopologyMapGenerator: Sprite '{sprite.name}' is not marked Read/Write Enabled in its texture import settings, so it can't be stamped onto the map. Enable Read/Write in the sprite's import settings and try again.");
            return;
        }

        Rect spriteRect = sprite.textureRect; // pixels within the source texture, bottom-left origin
        if (spriteRect.width <= 0f || spriteRect.height <= 0f) return;
        float spriteAspect = spriteRect.width / spriteRect.height;

        // Determine destination size in output-texture pixels
        float destWidth, destHeight;
        if (icon.sizeMode == IconSizeMode.FixedPixels)
        {
            destWidth = icon.pixelSize;
            destHeight = icon.pixelSize / Mathf.Max(0.0001f, spriteAspect);
        }
        else // WorldUnits: convert a world-space size into output pixels using the map's scale
        {
            if (sourceData == null || sourceData.width < 2) return;
            float worldRangeX = (sourceData.width - 1) * sourceData.spacingX;
            if (worldRangeX <= 0f) return;

            float pixelsPerWorldUnit = (textureWidth - 1) / worldRangeX;
            destWidth = icon.worldSize * pixelsPerWorldUnit;
            destHeight = destWidth / Mathf.Max(0.0001f, spriteAspect);
        }

        if (destWidth <= 0f || destHeight <= 0f) return;

        // Bounding box radius that safely contains the icon at any rotation
        float halfDiag = 0.5f * Mathf.Sqrt(destWidth * destWidth + destHeight * destHeight);
        int minX = Mathf.FloorToInt(center.x - halfDiag);
        int maxX = Mathf.CeilToInt(center.x + halfDiag);
        int minY = Mathf.FloorToInt(center.y - halfDiag);
        int maxY = Mathf.CeilToInt(center.y + halfDiag);

        if (maxX < 0 || minX >= textureWidth || maxY < 0 || minY >= textureHeight)
        {
            if (logOutOfBoundsIcons)
                Debug.LogWarning($"TopologyMapGenerator: Icon '{sprite.name}' at world position {icon.worldPosition} falls entirely outside the map bounds; skipped.");
            return;
        }

        minX = Mathf.Max(minX, 0);
        maxX = Mathf.Min(maxX, textureWidth - 1);
        minY = Mathf.Max(minY, 0);
        maxY = Mathf.Min(maxY, textureHeight - 1);

        // Inverse rotation: for each destination pixel, rotate back into the icon's
        // un-rotated local space to find which part of the sprite it corresponds to.
        float rot = -icon.rotationDegrees * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rot);
        float sinA = Mathf.Sin(rot);

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float ox = px - center.x;
                float oy = py - center.y;

                float lx = ox * cosA - oy * sinA;
                float ly = ox * sinA + oy * cosA;

                // Normalize to -0.5..0.5 across the icon's footprint
                float u = lx / destWidth;
                float v = ly / destHeight;
                if (u < -0.5f || u > 0.5f || v < -0.5f || v > 0.5f) continue;

                // Map into source-texture pixel space (both use bottom-left origin, so no flip needed)
                float su = 0.5f + u;
                float sv = 0.5f + v;
                float srcX = spriteRect.x + su * spriteRect.width;
                float srcY = spriteRect.y + sv * spriteRect.height;

                Color sample = spriteTex.GetPixelBilinear(srcX / spriteTex.width, srcY / spriteTex.height);
                if (sample.a <= 0f) continue;

                sample *= icon.tint;

                int destIndex = py * textureWidth + px;
                pixels[destIndex] = AlphaBlend(pixels[destIndex], sample);
            }
        }
    }

    // ---------------------------------------------------------------
    // Fine height / water sampling
    // ---------------------------------------------------------------

    private void BuildFineHeightBuffer()
    {
        float[,] raw = sourceData.GetHeights2D(); // [x, z], size width x depth
        int srcW = sourceData.width;
        int srcD = sourceData.depth;

        fineHeights = new float[textureWidth, textureHeight];
        fineWaterCoverage = (sourceWaterData != null) ? new float[textureWidth, textureHeight] : null;

        minHeight = float.MaxValue;
        maxHeight = float.MinValue;

        for (int py = 0; py < textureHeight; py++)
        {
            // v: 0 at bottom row of texture -> maps to z=0 of grid (matches origin = bottom-left)
            float v = (float)py / (textureHeight - 1);
            float gz = v * (srcD - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, srcD - 2 < 0 ? 0 : srcD - 2);
            int z1 = Mathf.Min(z0 + 1, srcD - 1);
            float fz = gz - z0;

            for (int px = 0; px < textureWidth; px++)
            {
                float u = (float)px / (textureWidth - 1);
                float gx = u * (srcW - 1);
                int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, srcW - 2 < 0 ? 0 : srcW - 2);
                int x1 = Mathf.Min(x0 + 1, srcW - 1);
                float fx = gx - x0;

                float h00 = raw[x0, z0];
                float h10 = raw[x1, z0];
                float h01 = raw[x0, z1];
                float h11 = raw[x1, z1];

                float h0 = Mathf.Lerp(h00, h10, fx);
                float h1 = Mathf.Lerp(h01, h11, fx);
                float h = Mathf.Lerp(h0, h1, fz);

                fineHeights[px, py] = h;
                if (h < minHeight) minHeight = h;
                if (h > maxHeight) maxHeight = h;

                // Sample water in world space so it works regardless of whether the water
                // grid shares the same resolution/alignment as the height grid. Using the
                // bilinear coverage field (rather than a hard boolean) is what lets the
                // water/land boundary render as a smooth edge instead of blocky raster steps.
                if (fineWaterCoverage != null)
                {
                    float worldX = sourceData.origin.x + gx * sourceData.spacingX;
                    float worldZ = sourceData.origin.z + gz * sourceData.spacingZ;
                    Vector3 worldPos = new Vector3(worldX, 0f, worldZ);
                    fineWaterCoverage[px, py] = sourceWaterData.GetWaterCoverage(worldPos);
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // Band shading
    // ---------------------------------------------------------------

    private void ShadeBands(Color[] pixels)
    {
        int bandCount = Mathf.Max(1, Mathf.CeilToInt((maxHeight - minHeight) / contourInterval));

        for (int py = 0; py < textureHeight; py++)
        {
            for (int px = 0; px < textureWidth; px++)
            {
                float h = fineHeights[px, py];
                int band = Mathf.FloorToInt((h - minHeight) / contourInterval);
                band = Mathf.Clamp(band, 0, bandCount - 1);
                float t = bandCount <= 1 ? 0f : (float)band / (bandCount - 1);
                pixels[py * textureWidth + px] = bandGradient.Evaluate(t);
            }
        }
    }

    // ---------------------------------------------------------------
    // Water overlay
    // ---------------------------------------------------------------

    private void ApplyWaterOverlay(Color[] pixels)
    {
        // Blend water color by the interpolated coverage fraction (0..1) rather than a hard
        // boolean — this is what gives the water/land boundary a smooth anti-aliased edge
        // instead of a staircase of raster pixels.
        for (int py = 0; py < textureHeight; py++)
        {
            for (int px = 0; px < textureWidth; px++)
            {
                float coverage = fineWaterCoverage[px, py];
                if (coverage <= 0f) continue;

                int i = py * textureWidth + px;
                Color weighted = waterColor;
                weighted.a = waterColor.a * coverage;
                pixels[i] = AlphaBlend(pixels[i], weighted);
            }
        }

        if (!drawWaterOutline) return;

        // Trace the coastline as a vector contour at the coverage == 0.5 boundary, using the
        // same marching-squares approach as the height contour lines. This follows the true
        // sub-pixel edge of the water grid rather than snapping to raster pixel boundaries.
        List<(Vector2 a, Vector2 b)> coastline = MarchingSquares(fineWaterCoverage, 0.5f);
        foreach (var seg in coastline)
            DrawLine(pixels, seg.a, seg.b, waterOutlineColor, waterOutlineThickness);
    }

    private static Color AlphaBlend(Color background, Color foreground)
    {
        Color result = Color.Lerp(background, foreground, foreground.a);
        result.a = 1f;
        return result;
    }

    // ---------------------------------------------------------------
    // Marching Squares (operates directly on fineHeights, pixel-space output)
    // ---------------------------------------------------------------

    // Corner layout per cell (px,py) -> (px+1,py+1):
    //   a---b       a = (px,   py)    (top-left)
    //   |   |       b = (px+1, py)    (top-right)
    //   d---c       c = (px+1, py+1)  (bottom-right)
    //               d = (px,   py+1)  (bottom-left)
    private List<(Vector2, Vector2)> MarchingSquares(float[,] buffer, float level)
    {
        var segments = new List<(Vector2, Vector2)>();

        for (int py = 0; py < textureHeight - 1; py++)
        {
            for (int px = 0; px < textureWidth - 1; px++)
            {
                float a = buffer[px, py];
                float b = buffer[px + 1, py];
                float c = buffer[px + 1, py + 1];
                float d = buffer[px, py + 1];

                int caseIndex = 0;
                if (a >= level) caseIndex |= 8;
                if (b >= level) caseIndex |= 4;
                if (c >= level) caseIndex |= 2;
                if (d >= level) caseIndex |= 1;

                if (caseIndex == 0 || caseIndex == 15) continue;

                // Edge interpolation points (in pixel space)
                Vector2 top = new Vector2(px + InvLerp(a, b, level), py);
                Vector2 right = new Vector2(px + 1, py + InvLerp(b, c, level));
                Vector2 bottom = new Vector2(px + InvLerp(d, c, level), py + 1);
                Vector2 left = new Vector2(px, py + InvLerp(a, d, level));

                switch (caseIndex)
                {
                    case 1: segments.Add((left, bottom)); break;
                    case 2: segments.Add((bottom, right)); break;
                    case 3: segments.Add((left, right)); break;
                    case 4: segments.Add((top, right)); break;
                    case 5: // ambiguous saddle - simple resolution (no center-average check)
                        segments.Add((top, left));
                        segments.Add((bottom, right));
                        break;
                    case 6: segments.Add((top, bottom)); break;
                    case 7: segments.Add((top, left)); break;
                    case 8: segments.Add((top, left)); break;
                    case 9: segments.Add((top, bottom)); break;
                    case 10: // ambiguous saddle
                        segments.Add((top, right));
                        segments.Add((left, bottom));
                        break;
                    case 11: segments.Add((top, right)); break;
                    case 12: segments.Add((left, right)); break;
                    case 13: segments.Add((bottom, right)); break;
                    case 14: segments.Add((left, bottom)); break;
                }
            }
        }

        return segments;
    }

    private static float InvLerp(float v0, float v1, float level)
    {
        if (Mathf.Approximately(v1, v0)) return 0.5f;
        return Mathf.Clamp01((level - v0) / (v1 - v0));
    }

    // ---------------------------------------------------------------
    // Line rasterization
    // ---------------------------------------------------------------

    private void DrawLine(Color[] pixels, Vector2 p0, Vector2 p1, Color color, int thickness)
    {
        int x0 = Mathf.RoundToInt(p0.x);
        int y0 = Mathf.RoundToInt(p0.y);
        int x1 = Mathf.RoundToInt(p1.x);
        int y1 = Mathf.RoundToInt(p1.y);

        int dx = Mathf.Abs(x1 - x0);
        int dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int half = thickness / 2;

        while (true)
        {
            for (int oy = -half; oy <= half; oy++)
            {
                for (int ox = -half; ox <= half; ox++)
                {
                    int px = x0 + ox;
                    int py = y0 + oy;
                    if (px >= 0 && px < textureWidth && py >= 0 && py < textureHeight)
                        pixels[py * textureWidth + px] = color;
                }
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// Same Bresenham rasterization as DrawLine, but interpolates color from c0 to c1 along
    /// the length of the segment instead of drawing a single flat color. Used for the travel
    /// path so consecutive segments blend smoothly into the overall time gradient.
    /// </summary>
    private void DrawGradientLine(Color[] pixels, Vector2 p0, Vector2 p1, Color c0, Color c1, int thickness)
    {
        int x0 = Mathf.RoundToInt(p0.x);
        int y0 = Mathf.RoundToInt(p0.y);
        int x1 = Mathf.RoundToInt(p1.x);
        int y1 = Mathf.RoundToInt(p1.y);

        int dx = Mathf.Abs(x1 - x0);
        int dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int half = thickness / 2;

        // Chebyshev distance approximates the number of Bresenham steps, giving a good
        // enough step/totalSteps ratio to interpolate color along the segment.
        int totalSteps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        int step = 0;

        while (true)
        {
            float t = totalSteps > 0 ? (float)step / totalSteps : 0f;
            Color color = Color.Lerp(c0, c1, t);

            for (int oy = -half; oy <= half; oy++)
            {
                for (int ox = -half; ox <= half; ox++)
                {
                    int px = x0 + ox;
                    int py = y0 + oy;
                    if (px >= 0 && px < textureWidth && py >= 0 && py < textureHeight)
                        pixels[py * textureWidth + px] = color;
                }
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            step++;
        }
    }

    // ---------------------------------------------------------------
    // Export
    // ---------------------------------------------------------------

    public static void SaveTextureAsPNG(Texture2D tex, string path)
    {
        if (tex == null)
        {
            Debug.LogError("TopologyMapGenerator: Cannot save a null texture.");
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("TopologyMapGenerator: Cannot save map because the output path is empty.");
            return;
        }

        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory) &&
            !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] pngData = tex.EncodeToPNG();
        File.WriteAllBytes(path, pngData);

        Debug.Log($"TopologyMapGenerator: Saved texture to {path}");

#if UNITY_EDITOR
        if (path.Replace("\\", "/").StartsWith("Assets/"))
            UnityEditor.AssetDatabase.Refresh();
#endif
    }

    private static Gradient DefaultGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.10f, 0.35f, 0.15f), 0.0f), // low - dark green
                new GradientColorKey(new Color(0.55f, 0.55f, 0.20f), 0.4f), // mid - olive
                new GradientColorKey(new Color(0.55f, 0.35f, 0.20f), 0.7f), // upper-mid - brown
                new GradientColorKey(new Color(0.95f, 0.95f, 0.95f), 1.0f), // high - white/snow
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            }
        );
        return g;
    }

    private static Gradient DefaultPathGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.15f, 0.35f, 0.95f), 0.0f), // oldest - blue
                new GradientColorKey(new Color(0.95f, 0.25f, 0.15f), 1.0f), // most recent - red
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            }
        );
        return g;
    }
}