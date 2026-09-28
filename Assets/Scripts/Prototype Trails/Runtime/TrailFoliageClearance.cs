using System.Collections.Generic;
using Thomas.TerrainFoliageSpawner;
using UnityEngine;

namespace GetLost.Trails
{
    public static class TrailFoliageClearance
    {
        private static readonly List<TerrainFoliageGrassRenderer> managedGrassRenderers = new();

        public static void ApplyTrail(
            TrailData trail,
            TrailSystemSettings settings)
        {
            if (trail == null || settings == null)
                return;

            TrailPlayerTrackData track =
                trail.GetOfficialTrack();

            if (track == null ||
                track.points == null ||
                track.points.Count < 2)
            {
                return;
            }

            List<Vector3> points =
                new(track.points.Count);

            for (int i = 0; i < track.points.Count; i++)
                points.Add(track.points[i].worldPosition);

            ApplyPathSection(points, settings);
        }

        /// <summary>
        /// Clears only a short local trail section.
        /// </summary>
        public static void ApplyPathSection(
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            if (path == null ||
                path.Count < 2 ||
                settings == null ||
                !settings.clearFoliage)
            {
                return;
            }

            float normalRadius =
                settings.trailWidth * 0.5f +
                settings.foliageClearancePadding;

            float detailRadius =
                settings.trailWidth * 0.5f +
                settings.terrainDetailClearancePadding;

            float maxRadius =
                Mathf.Max(
                    normalRadius,
                    detailRadius);

            Bounds sectionBounds =
                CalculatePathBounds(
                    path,
                    maxRadius);

            if (settings.clearTerrainDetails)
            {
                ClearManagedGrass(
                    path,
                    detailRadius,
                    sectionBounds);
            }

            Terrain[] terrains =
                Terrain.activeTerrains;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain =
                    terrains[i];

                if (terrain == null ||
                    terrain.terrainData == null)
                {
                    continue;
                }

                if (!TerrainIntersectsBounds(
                    terrain,
                    sectionBounds))
                {
                    continue;
                }

                TrailTerrainEditorBackup.CaptureIfNeeded(
                    terrain,
                    settings);

                if (settings.clearTerrainTrees)
                {
                    ClearTerrainTrees(
                        terrain,
                        path,
                        normalRadius,
                        sectionBounds);
                }

                if (settings.clearTerrainDetails)
                {
                    ClearTerrainDetailsBatched(
                        terrain,
                        path,
                        detailRadius,
                        settings.preserveMeshTerrainDetails);
                }

                if (settings.clearSpawnedGameObjects)
                {
                    ClearGeneratedGameObjects(
                        terrain,
                        path,
                        normalRadius,
                        sectionBounds);
                }
            }
        }

        public static void ApplyManagedGrassSection(
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            if (path == null || path.Count < 2 || settings == null ||
                !settings.clearFoliage || !settings.clearTerrainDetails)
                return;

            float radius = settings.trailWidth * 0.5f +
                settings.terrainDetailClearancePadding;
            ClearManagedGrass(path, radius, CalculatePathBounds(path, radius));
        }

        public static void ApplyTerrainDetailSection(
            List<Vector3> path,
            TrailSystemSettings settings)
        {
            if (path == null || path.Count < 2 || settings == null ||
                !settings.clearFoliage || !settings.clearTerrainDetails)
                return;

            float radius = settings.trailWidth * 0.5f +
                settings.terrainDetailClearancePadding;
            Bounds bounds = CalculatePathBounds(path, radius);
            Terrain[] terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null ||
                    !TerrainIntersectsBounds(terrain, bounds))
                    continue;
                TrailTerrainEditorBackup.CaptureIfNeeded(terrain, settings);
                ClearTerrainDetailsBatched(terrain, path, radius,
                    settings.preserveMeshTerrainDetails);
            }
        }
        public static void ApplyTerrainDetailPrototypeSection(
            List<Vector3> path,
            TrailSystemSettings settings,
            int prototypeIndex)
        {
            if (path == null || path.Count < 2 || settings == null ||
                !settings.clearFoliage || !settings.clearTerrainDetails ||
                prototypeIndex < 0)
                return;

            float radius = settings.trailWidth * 0.5f +
                settings.terrainDetailClearancePadding;
            Bounds bounds = CalculatePathBounds(path, radius);
            Terrain[] terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null ||
                    prototypeIndex >= terrain.terrainData.detailPrototypes.Length ||
                    !TerrainIntersectsBounds(terrain, bounds))
                    continue;
                TrailTerrainEditorBackup.CaptureIfNeeded(terrain, settings);
                ClearTerrainDetailsBatched(terrain, path, radius,
                    settings.preserveMeshTerrainDetails, prototypeIndex);
            }
        }
        private static void ClearManagedGrass(
            List<Vector3> path,
            float radius,
            Bounds sectionBounds)
        {
            TerrainFoliageGrassRenderer.GetActiveRenderers(managedGrassRenderers);

            for (int i = 0; i < managedGrassRenderers.Count; i++)
            {
                TerrainFoliageGrassRenderer renderer = managedGrassRenderers[i];
                if (renderer == null ||
                    renderer.PlacementData == null ||
                    renderer.PlacementData.GrassCount == 0)
                {
                    continue;
                }

                Bounds grassBounds = renderer.PlacementData.GrassWorldBounds;
                grassBounds.Expand(new Vector3(radius * 2f, 100000f, radius * 2f));
                if (!grassBounds.Intersects(sectionBounds))
                    continue;

                renderer.ClearAlongPath(path, radius);
            }
        }

        private static void ClearTerrainTrees(
            Terrain terrain,
            List<Vector3> path,
            float radius,
            Bounds sectionBounds)
        {
            TerrainData data =
                terrain.terrainData;

            TreeInstance[] trees =
                data.treeInstances;

            if (trees == null ||
                trees.Length == 0)
            {
                return;
            }

            Vector3 origin =
                terrain.transform.position;

            Vector3 size =
                data.size;

            List<TreeInstance> kept =
                new(trees.Length);

            bool changed = false;

            for (int i = 0; i < trees.Length; i++)
            {
                TreeInstance tree =
                    trees[i];

                Vector3 worldPosition =
                    new(
                        origin.x +
                        tree.position.x * size.x,

                        origin.y +
                        tree.position.y * size.y,

                        origin.z +
                        tree.position.z * size.z);

                if (!sectionBounds.Contains(
                    new Vector3(
                        worldPosition.x,
                        sectionBounds.center.y,
                        worldPosition.z)))
                {
                    kept.Add(tree);
                    continue;
                }

                if (DistanceToPolylineXZ(
                    worldPosition,
                    path) <= radius)
                {
                    changed = true;
                    continue;
                }

                kept.Add(tree);
            }

            if (changed)
            {
                data.treeInstances =
                    kept.ToArray();
            }
        }

        private static void ClearTerrainDetailsBatched(
            Terrain terrain,
            List<Vector3> worldPath,
            float worldRadius,
            bool preserveMeshDetails,
            int onlyPrototypeIndex = -1)
        {
            TerrainData data =
                terrain.terrainData;

            if (data.detailPrototypes == null ||
                data.detailPrototypes.Length == 0)
            {
                return;
            }

            int detailWidth =
                data.detailWidth;

            int detailHeight =
                data.detailHeight;

            if (detailWidth <= 0 ||
                detailHeight <= 0)
            {
                return;
            }

            Vector3 terrainOrigin =
                terrain.transform.position;

            Vector3 terrainSize =
                data.size;

            List<Vector2> detailPath =
                new(worldPath.Count);

            for (int i = 0; i < worldPath.Count; i++)
            {
                Vector3 world =
                    worldPath[i];

                float normalizedX =
                    (world.x - terrainOrigin.x) /
                    terrainSize.x;

                float normalizedZ =
                    (world.z - terrainOrigin.z) /
                    terrainSize.z;

                detailPath.Add(
                    new Vector2(
                        normalizedX * detailWidth,
                        normalizedZ * detailHeight));
            }

            float cellsPerWorldX =
                detailWidth /
                terrainSize.x;

            float cellsPerWorldZ =
                detailHeight /
                terrainSize.z;

            float detailRadius =
                worldRadius *
                Mathf.Max(
                    cellsPerWorldX,
                    cellsPerWorldZ);

            detailRadius += 1f;

            GetDetailSpaceBounds(
                detailPath,
                detailRadius,
                detailWidth,
                detailHeight,
                out int minX,
                out int minZ,
                out int maxX,
                out int maxZ);

            int regionWidth =
                maxX - minX + 1;

            int regionHeight =
                maxZ - minZ + 1;

            if (regionWidth <= 0 ||
                regionHeight <= 0)
            {
                return;
            }

            bool[,] eraseMask =
                new bool[
                    regionWidth,
                    regionHeight];

            RasterizeTrailIntoMask(
                detailPath,
                detailRadius,
                minX,
                minZ,
                regionWidth,
                regionHeight,
                eraseMask);

            int prototypeCount =
                data.detailPrototypes.Length;

            int firstPrototype = onlyPrototypeIndex >= 0 ? onlyPrototypeIndex : 0;
            int lastPrototype = onlyPrototypeIndex >= 0
                ? Mathf.Min(onlyPrototypeIndex + 1, prototypeCount)
                : prototypeCount;

            for (int prototypeIndex = firstPrototype;
                 prototypeIndex < lastPrototype;
                 prototypeIndex++)
            {
                if (preserveMeshDetails && data.detailPrototypes[prototypeIndex].usePrototypeMesh)
                    continue;

                int[,] layer =
                    data.GetDetailLayer(
                        minX,
                        minZ,
                        regionWidth,
                        regionHeight,
                        prototypeIndex);

                if (layer == null)
                    continue;

                int widthToProcess =
                    Mathf.Min(
                        layer.GetLength(1),
                        regionWidth);

                int heightToProcess =
                    Mathf.Min(
                        layer.GetLength(0),
                        regionHeight);

                bool changed = false;

                for (int z = 0;
                     z < heightToProcess;
                     z++)
                {
                    for (int x = 0;
                         x < widthToProcess;
                         x++)
                    {
                        if (!eraseMask[x, z] ||
                            layer[z, x] == 0)
                        {
                            continue;
                        }

                        layer[z, x] = 0;
                        changed = true;
                    }
                }

                if (changed)
                {
                    data.SetDetailLayer(
                        minX,
                        minZ,
                        prototypeIndex,
                        layer);
                }
            }
        }

        private static void ClearGeneratedGameObjects(
    Terrain terrain,
    List<Vector3> path,
    float radius,
    Bounds sectionBounds)
        {
            GeneratedFoliageMarker[] markers =
                terrain.GetComponentsInChildren<GeneratedFoliageMarker>(
                    true);

            if (markers == null ||
                markers.Length == 0)
            {
                return;
            }

            List<GameObject> remove =
                new();

            for (int i = 0; i < markers.Length; i++)
            {
                GeneratedFoliageMarker marker =
                    markers[i];

                if (marker == null)
                    continue;

                Vector3 position =
                    marker.transform.position;

                // Cheap check first:
                // Ignore everything outside this small trail section.
                if (!sectionBounds.Contains(
                    new Vector3(
                        position.x,
                        sectionBounds.center.y,
                        position.z)))
                {
                    continue;
                }

                // Cheap-ish path distance check next.
                if (DistanceToPolylineXZ(
                        position,
                        path) > radius)
                {
                    continue;
                }

                // Only NOW do the hierarchy/tag check.
                // At this point the object would otherwise be destroyed.
                if (IsPartOfPOI(marker.transform))
                {
                    continue;
                }

                remove.Add(
                    marker.gameObject);
            }
        

            for (int i = 0; i < remove.Count; i++)
            {
                GameObject go =
                    remove[i];

                if (go == null)
                    continue;

                Object.Destroy(go);
            }
        }


        private static bool IsPartOfPOI(
     Transform target)
        {
            Transform current =
                target;

            while (current != null)
            {
                if (current.CompareTag("POI"))
                    return true;

                current =
                    current.parent;
            }

            return false;
        }

        private static Bounds CalculatePathBounds(
            List<Vector3> path,
            float radius)
        {
            Bounds bounds =
                new Bounds(
                    path[0],
                    Vector3.zero);

            for (int i = 1; i < path.Count; i++)
                bounds.Encapsulate(path[i]);

            bounds.Expand(
                new Vector3(
                    radius * 2f,
                    100000f,
                    radius * 2f));

            return bounds;
        }

        private static bool TerrainIntersectsBounds(
            Terrain terrain,
            Bounds bounds)
        {
            Vector3 origin =
                terrain.transform.position;

            Vector3 size =
                terrain.terrainData.size;

            Bounds terrainBounds =
                new Bounds(
                    origin + size * 0.5f,
                    size);

            return terrainBounds.Intersects(bounds);
        }

        private static void RasterizeTrailIntoMask(
            List<Vector2> detailPath,
            float radius,
            int minX,
            int minZ,
            int regionWidth,
            int regionHeight,
            bool[,] eraseMask)
        {
            float radiusSquared =
                radius * radius;

            for (int segmentIndex = 1;
                 segmentIndex < detailPath.Count;
                 segmentIndex++)
            {
                Vector2 a =
                    detailPath[segmentIndex - 1];

                Vector2 b =
                    detailPath[segmentIndex];

                int localMinX =
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            Mathf.Min(a.x, b.x) -
                            radius) -
                        minX,
                        0,
                        regionWidth - 1);

                int localMaxX =
                    Mathf.Clamp(
                        Mathf.CeilToInt(
                            Mathf.Max(a.x, b.x) +
                            radius) -
                        minX,
                        0,
                        regionWidth - 1);

                int localMinZ =
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            Mathf.Min(a.y, b.y) -
                            radius) -
                        minZ,
                        0,
                        regionHeight - 1);

                int localMaxZ =
                    Mathf.Clamp(
                        Mathf.CeilToInt(
                            Mathf.Max(a.y, b.y) +
                            radius) -
                        minZ,
                        0,
                        regionHeight - 1);

                for (int z = localMinZ;
                     z <= localMaxZ;
                     z++)
                {
                    float detailZ =
                        minZ + z + 0.5f;

                    for (int x = localMinX;
                         x <= localMaxX;
                         x++)
                    {
                        if (eraseMask[x, z])
                            continue;

                        float detailX =
                            minX + x + 0.5f;

                        if (SqrDistancePointToSegment(
                            new Vector2(
                                detailX,
                                detailZ),
                            a,
                            b) <= radiusSquared)
                        {
                            eraseMask[x, z] = true;
                        }
                    }
                }
            }
        }

        private static void GetDetailSpaceBounds(
            List<Vector2> path,
            float radius,
            int detailWidth,
            int detailHeight,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            float rawMinX =
                float.MaxValue;

            float rawMaxX =
                float.MinValue;

            float rawMinZ =
                float.MaxValue;

            float rawMaxZ =
                float.MinValue;

            for (int i = 0; i < path.Count; i++)
            {
                Vector2 p = path[i];

                rawMinX =
                    Mathf.Min(
                        rawMinX,
                        p.x - radius);

                rawMaxX =
                    Mathf.Max(
                        rawMaxX,
                        p.x + radius);

                rawMinZ =
                    Mathf.Min(
                        rawMinZ,
                        p.y - radius);

                rawMaxZ =
                    Mathf.Max(
                        rawMaxZ,
                        p.y + radius);
            }

            minX =
                Mathf.Clamp(
                    Mathf.FloorToInt(rawMinX),
                    0,
                    detailWidth - 1);

            maxX =
                Mathf.Clamp(
                    Mathf.CeilToInt(rawMaxX),
                    0,
                    detailWidth - 1);

            minZ =
                Mathf.Clamp(
                    Mathf.FloorToInt(rawMinZ),
                    0,
                    detailHeight - 1);

            maxZ =
                Mathf.Clamp(
                    Mathf.CeilToInt(rawMaxZ),
                    0,
                    detailHeight - 1);
        }

        private static float DistanceToPolylineXZ(
            Vector3 world,
            List<Vector3> points)
        {
            Vector2 p =
                new(world.x, world.z);

            float bestSquared =
                float.MaxValue;

            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a =
                    new(
                        points[i - 1].x,
                        points[i - 1].z);

                Vector2 b =
                    new(
                        points[i].x,
                        points[i].z);

                float squared =
                    SqrDistancePointToSegment(
                        p,
                        a,
                        b);

                if (squared < bestSquared)
                    bestSquared = squared;
            }

            return Mathf.Sqrt(bestSquared);
        }

        private static float SqrDistancePointToSegment(
            Vector2 p,
            Vector2 a,
            Vector2 b)
        {
            Vector2 ab = b - a;
            float sqr = ab.sqrMagnitude;

            if (sqr < 0.000001f)
                return (p - a).sqrMagnitude;

            float t =
                Mathf.Clamp01(
                    Vector2.Dot(p - a, ab) /
                    sqr);

            Vector2 closest =
                a + ab * t;

            return
                (p - closest).sqrMagnitude;
        }
    }
}
