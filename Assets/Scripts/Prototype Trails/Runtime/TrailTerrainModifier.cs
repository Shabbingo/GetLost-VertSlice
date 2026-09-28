using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Trails
{
    public static class TrailTerrainModifier
    {
        private static readonly HashSet<Terrain> touchedHeightTerrains = new();

        public static void ApplyTrail(
            TrailData trail,
            TrailSystemSettings settings)
        {
            if (trail == null || settings == null)
                return;

            TrailPlayerTrackData track = trail.GetOfficialTrack();

            if (track == null ||
                track.points == null ||
                track.points.Count < 2)
            {
                return;
            }

            List<Vector3> points = new(track.points.Count);

            for (int i = 0; i < track.points.Count; i++)
                points.Add(track.points[i].worldPosition);

            ApplyPathSectionDeformation(points, settings);
            ApplyPathSectionTexture(points, settings);

            if (settings.useDelayedHeightmapLOD)
                SyncTouchedHeightmaps();
        }

        public static void ApplyPathSectionDeformation(
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            if (path == null ||
                path.Count < 2 ||
                settings == null ||
                !settings.deformTerrain)
            {
                return;
            }

            float margin =
                settings.trailWidth * 0.5f +
                settings.deformationFalloffWidth;

            Bounds sectionBounds =
                CalculatePathBounds(path, margin);

            Terrain[] terrains = Terrain.activeTerrains;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];

                if (terrain == null ||
                    terrain.terrainData == null ||
                    !TerrainIntersectsBounds(terrain, sectionBounds))
                {
                    continue;
                }

                TrailTerrainEditorBackup.CaptureIfNeeded(
                    terrain,
                    settings);

                DeformTerrainBenched(
                    terrain,
                    path,
                    settings);
            }
        }

        public static void ApplyPathSectionTexture(
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            if (path == null ||
                path.Count < 2 ||
                settings == null ||
                !settings.paintPathTexture)
            {
                return;
            }

            float margin =
                settings.trailWidth * 0.5f +
                settings.textureBlendWidth;

            Bounds sectionBounds =
                CalculatePathBounds(path, margin);

            Terrain[] terrains = Terrain.activeTerrains;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];

                if (terrain == null ||
                    terrain.terrainData == null ||
                    !TerrainIntersectsBounds(terrain, sectionBounds))
                {
                    continue;
                }

                TrailTerrainEditorBackup.CaptureIfNeeded(
                    terrain,
                    settings);

                PaintTerrain(
                    terrain,
                    path,
                    settings);
            }
        }

        public static void SyncTouchedHeightmaps()
        {
            foreach (Terrain terrain in touchedHeightTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;

                terrain.terrainData.SyncHeightmap();
                terrain.Flush();
            }

            touchedHeightTerrains.Clear();
        }

        private static void DeformTerrainBenched(
            Terrain terrain,
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            TerrainData data = terrain.terrainData;
            Vector3 terrainOrigin = terrain.transform.position;
            Vector3 terrainSize = data.size;

            float halfTrail =
                settings.trailWidth * 0.5f;

            float totalRadius =
                halfTrail +
                settings.deformationFalloffWidth;

            GetHeightmapBounds(
                terrain,
                path,
                totalRadius,
                out int minX,
                out int minZ,
                out int maxX,
                out int maxZ);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            if (width <= 0 || height <= 0)
                return;

            float[,] heights =
                data.GetHeights(
                    minX,
                    minZ,
                    width,
                    height);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int hx = minX + x;
                    int hz = minZ + z;

                    Vector3 world =
                        HeightmapToWorld(
                            terrain,
                            hx,
                            hz);

                    ClosestPathResult closest =
                        GetClosestPointOnPolylineXZ(
                            world,
                            path);

                    if (closest.distance > totalRadius)
                        continue;

                    float influence;

                    if (closest.distance <= halfTrail)
                    {
                        influence = 1f;
                    }
                    else
                    {
                        float falloff =
                            Mathf.Max(
                                0.0001f,
                                settings.deformationFalloffWidth);

                        influence =
                            1f -
                            Mathf.Clamp01(
                                (closest.distance - halfTrail) /
                                falloff);

                        influence =
                            SmoothStep01(influence);
                    }

                    influence *=
                        settings.flattenStrength;

                    float normalizedCentreX =
                        Mathf.Clamp01(
                            (closest.worldPoint.x - terrainOrigin.x) /
                            terrainSize.x);

                    float normalizedCentreZ =
                        Mathf.Clamp01(
                            (closest.worldPoint.z - terrainOrigin.z) /
                            terrainSize.z);

                    float centreHeightMetres =
                        data.GetInterpolatedHeight(
                            normalizedCentreX,
                            normalizedCentreZ);

                    float desiredHeightMetres =
                        centreHeightMetres -
                        settings.lowerByMetres;

                    float currentHeightMetres =
                        heights[z, x] *
                        terrainSize.y;

                    float rawDelta =
                        desiredHeightMetres -
                        currentHeightMetres;

                    float limitedDelta =
                        Mathf.Clamp(
                            rawDelta,
                            -settings.maximumCutMetres,
                            settings.maximumFillMetres);

                    float targetHeightMetres =
                        currentHeightMetres +
                        limitedDelta;

                    float blendedHeightMetres =
                        Mathf.Lerp(
                            currentHeightMetres,
                            targetHeightMetres,
                            influence);

                    heights[z, x] =
                        Mathf.Clamp01(
                            blendedHeightMetres /
                            Mathf.Max(0.001f, terrainSize.y));
                }
            }

            if (settings.useDelayedHeightmapLOD)
            {
                data.SetHeightsDelayLOD(
                    minX,
                    minZ,
                    heights);

                touchedHeightTerrains.Add(terrain);
            }
            else
            {
                data.SetHeights(
                    minX,
                    minZ,
                    heights);
            }
        }

        private static void PaintTerrain(
            Terrain terrain,
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            TerrainData data = terrain.terrainData;

            if (data.alphamapLayers <=
                settings.pathTerrainLayerIndex)
            {
                return;
            }

            float radius =
                settings.trailWidth * 0.5f +
                settings.textureBlendWidth;

            // A generated terrain can have control-map texels several metres wide.
            // Testing only texel centres makes a narrow trail appear as isolated dots.
            // Treat each texel as its full world-space footprint instead.
            float texelSizeX = data.size.x / Mathf.Max(1, data.alphamapWidth);
            float texelSizeZ = data.size.z / Mathf.Max(1, data.alphamapHeight);
            float texelHalfDiagonal = 0.5f * Mathf.Sqrt(
                texelSizeX * texelSizeX + texelSizeZ * texelSizeZ);
            float samplingRadius = radius + texelHalfDiagonal;

            GetAlphamapBounds(
                terrain,
                path,
                samplingRadius,
                out int minX,
                out int minZ,
                out int maxX,
                out int maxZ);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            if (width <= 0 || height <= 0)
                return;

            float[,,] maps =
                data.GetAlphamaps(
                    minX,
                    minZ,
                    width,
                    height);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int ax = minX + x;
                    int az = minZ + z;

                    Vector3 world =
                        AlphamapToWorld(
                            terrain,
                            ax,
                            az);

                    float centreDistance =
                        GetClosestPointOnPolylineXZ(
                            world,
                            path).distance;

                    float distance = Mathf.Max(0f, centreDistance - texelHalfDiagonal);

                    if (distance > radius)
                        continue;

                    float halfTrail =
                        settings.trailWidth * 0.5f;

                    float influence;

                    if (distance <= halfTrail)
                    {
                        influence = 1f;
                    }
                    else
                    {
                        float blend =
                            Mathf.Max(
                                0.0001f,
                                settings.textureBlendWidth);

                        influence =
                            1f -
                            Mathf.Clamp01(
                                (distance - halfTrail) /
                                blend);

                        influence =
                            SmoothStep01(influence);
                    }

                    influence *=
                        settings.pathTextureStrength;

                    int layerCount =
                        maps.GetLength(2);

                    float oldPath =
                        maps[
                            z,
                            x,
                            settings.pathTerrainLayerIndex];

                    float newPath =
                        Mathf.Lerp(
                            oldPath,
                            1f,
                            influence);

                    float remaining =
                        Mathf.Max(
                            0f,
                            1f - newPath);

                    float otherTotal = 0f;

                    for (int layer = 0;
                         layer < layerCount;
                         layer++)
                    {
                        if (layer ==
                            settings.pathTerrainLayerIndex)
                        {
                            continue;
                        }

                        otherTotal +=
                            maps[z, x, layer];
                    }

                    if (otherTotal > 0.0001f)
                    {
                        for (int layer = 0;
                             layer < layerCount;
                             layer++)
                        {
                            if (layer ==
                                settings.pathTerrainLayerIndex)
                            {
                                continue;
                            }

                            maps[z, x, layer] =
                                (maps[z, x, layer] /
                                otherTotal) *
                                remaining;
                        }
                    }

                    maps[
                        z,
                        x,
                        settings.pathTerrainLayerIndex] =
                        newPath;
                }
            }

            data.SetAlphamaps(
                minX,
                minZ,
                maps);

            // Runtime TerrainData clones and third-party terrain renderers can otherwise
            // defer the visible splat update even though GetAlphamaps already contains it.
            terrain.Flush();
        }

        private struct ClosestPathResult
        {
            public float distance;
            public Vector3 worldPoint;
        }

        private static ClosestPathResult GetClosestPointOnPolylineXZ(
            Vector3 world,
            List<Vector3> points)
        {
            Vector2 p = new(world.x, world.z);
            float bestSquared = float.MaxValue;
            Vector3 bestWorldPoint = points[0];

            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a3 = points[i - 1];
                Vector3 b3 = points[i];

                Vector2 a = new(a3.x, a3.z);
                Vector2 b = new(b3.x, b3.z);
                Vector2 ab = b - a;

                float sqr = ab.sqrMagnitude;
                float t = 0f;

                if (sqr > 0.000001f)
                {
                    t = Mathf.Clamp01(
                        Vector2.Dot(p - a, ab) /
                        sqr);
                }

                Vector2 closest2D = a + ab * t;
                float distanceSquared =
                    (p - closest2D).sqrMagnitude;

                if (distanceSquared < bestSquared)
                {
                    bestSquared = distanceSquared;

                    bestWorldPoint =
                        Vector3.Lerp(
                            a3,
                            b3,
                            t);

                    bestWorldPoint.x = closest2D.x;
                    bestWorldPoint.z = closest2D.y;
                }
            }

            return new ClosestPathResult
            {
                distance = Mathf.Sqrt(bestSquared),
                worldPoint = bestWorldPoint
            };
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Bounds CalculatePathBounds(
            List<Vector3> path,
            float margin)
        {
            Bounds bounds =
                new Bounds(path[0], Vector3.zero);

            for (int i = 1; i < path.Count; i++)
                bounds.Encapsulate(path[i]);

            bounds.Expand(
                new Vector3(
                    margin * 2f,
                    100000f,
                    margin * 2f));

            return bounds;
        }

        private static bool TerrainIntersectsBounds(
            Terrain terrain,
            Bounds bounds)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            Bounds terrainBounds =
                new Bounds(
                    origin + size * 0.5f,
                    size);

            return terrainBounds.Intersects(bounds);
        }

        private static void GetHeightmapBounds(
            Terrain terrain,
            List<Vector3> path,
            float radius,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int resolution = data.heightmapResolution;

            GetWorldPathBounds(
                path,
                radius,
                out float worldMinX,
                out float worldMinZ,
                out float worldMaxX,
                out float worldMaxZ);

            minX = Mathf.Clamp(
                Mathf.FloorToInt(
                    ((worldMinX - origin.x) / size.x) *
                    (resolution - 1)),
                0,
                resolution - 1);

            maxX = Mathf.Clamp(
                Mathf.CeilToInt(
                    ((worldMaxX - origin.x) / size.x) *
                    (resolution - 1)),
                0,
                resolution - 1);

            minZ = Mathf.Clamp(
                Mathf.FloorToInt(
                    ((worldMinZ - origin.z) / size.z) *
                    (resolution - 1)),
                0,
                resolution - 1);

            maxZ = Mathf.Clamp(
                Mathf.CeilToInt(
                    ((worldMaxZ - origin.z) / size.z) *
                    (resolution - 1)),
                0,
                resolution - 1);
        }

        private static void GetAlphamapBounds(
            Terrain terrain,
            List<Vector3> path,
            float radius,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            GetWorldPathBounds(
                path,
                radius,
                out float worldMinX,
                out float worldMinZ,
                out float worldMaxX,
                out float worldMaxZ);

            minX = Mathf.Clamp(
                Mathf.FloorToInt(
                    ((worldMinX - origin.x) / size.x) *
                    data.alphamapWidth),
                0,
                data.alphamapWidth - 1);

            maxX = Mathf.Clamp(
                Mathf.CeilToInt(
                    ((worldMaxX - origin.x) / size.x) *
                    data.alphamapWidth),
                0,
                data.alphamapWidth - 1);

            minZ = Mathf.Clamp(
                Mathf.FloorToInt(
                    ((worldMinZ - origin.z) / size.z) *
                    data.alphamapHeight),
                0,
                data.alphamapHeight - 1);

            maxZ = Mathf.Clamp(
                Mathf.CeilToInt(
                    ((worldMaxZ - origin.z) / size.z) *
                    data.alphamapHeight),
                0,
                data.alphamapHeight - 1);
        }

        private static void GetWorldPathBounds(
            List<Vector3> path,
            float radius,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            minX = float.MaxValue;
            minZ = float.MaxValue;
            maxX = float.MinValue;
            maxZ = float.MinValue;

            for (int i = 0; i < path.Count; i++)
            {
                minX = Mathf.Min(minX, path[i].x - radius);
                maxX = Mathf.Max(maxX, path[i].x + radius);
                minZ = Mathf.Min(minZ, path[i].z - radius);
                maxZ = Mathf.Max(maxZ, path[i].z + radius);
            }
        }

        private static Vector3 HeightmapToWorld(
            Terrain terrain,
            int x,
            int z)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            float nx =
                x /
                (float)(data.heightmapResolution - 1);

            float nz =
                z /
                (float)(data.heightmapResolution - 1);

            return new Vector3(
                origin.x + nx * size.x,
                origin.y,
                origin.z + nz * size.z);
        }

        private static Vector3 AlphamapToWorld(
            Terrain terrain,
            int x,
            int z)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            float nx =
                (x + 0.5f) /
                data.alphamapWidth;

            float nz =
                (z + 0.5f) /
                data.alphamapHeight;

            return new Vector3(
                origin.x + nx * size.x,
                origin.y,
                origin.z + nz * size.z);
        }
    }
}
