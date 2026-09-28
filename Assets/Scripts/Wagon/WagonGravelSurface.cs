using System.Collections.Generic;
using GetLost.Trails;
using UnityEngine;

namespace GetLost.Wagon
{
    public enum WagonSurfaceStage { Deform, Paint, ManagedGrass, TerrainDetails }

    public sealed class WagonGravelSurface
    {
        private readonly TrailSystemSettings settings;
        private readonly Dictionary<TerrainData, Dictionary<int, float>> originals = new();
        private readonly List<Vector3> path = new(2);
        private readonly HashSet<TerrainData> dirtyHeightmaps = new();
        private static readonly Unity.Profiling.ProfilerMarker deformMarker = new("Wagon.Gravel.Deform");
        private static readonly Unity.Profiling.ProfilerMarker paintMarker = new("Wagon.Gravel.Paint");
        private static readonly Unity.Profiling.ProfilerMarker managedGrassMarker = new("Wagon.Gravel.ManagedGrass");
        private static readonly Unity.Profiling.ProfilerMarker terrainDetailsMarker = new("Wagon.Gravel.TerrainDetails");
        private static readonly Unity.Profiling.ProfilerMarker syncMarker = new("Wagon.Gravel.SyncHeightmap");
        public int PendingHeightmapCount => dirtyHeightmaps.Count;
        public int MaximumTerrainDetailPrototypeCount
        {
            get
            {
                int maximum = 0;
                foreach (Terrain terrain in Terrain.activeTerrains)
                    if (terrain != null && terrain.terrainData != null &&
                        terrain.terrainData.detailPrototypes != null)
                        maximum = Mathf.Max(maximum, terrain.terrainData.detailPrototypes.Length);
                return maximum;
            }
        }

        public WagonGravelSurface(TrailSystemSettings settings) => this.settings = settings;

        private float Original(TerrainData data, int x, int z)
        {
            x = Mathf.Clamp(x, 0, data.heightmapResolution - 1);
            z = Mathf.Clamp(z, 0, data.heightmapResolution - 1);
            if (!originals.TryGetValue(data, out var heights))
            {
                heights = new Dictionary<int, float>();
                originals.Add(data, heights);
            }
            int key = z * data.heightmapResolution + x;
            if (!heights.TryGetValue(key, out float value))
            {
                value = data.GetHeights(x, z, 1, 1)[0, 0];
                heights.Add(key, value);
            }
            return value;
        }

        public float OriginalHeight(Terrain terrain, Vector3 point)
        {
            TerrainData data = terrain.terrainData;
            Vector3 p = point - terrain.transform.position;
            float x = Mathf.Clamp01(p.x / data.size.x) * (data.heightmapResolution - 1);
            float z = Mathf.Clamp01(p.z / data.size.z) * (data.heightmapResolution - 1);
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float a = Mathf.Lerp(Original(data, ix, iz), Original(data, ix + 1, iz), x - ix);
            float b = Mathf.Lerp(Original(data, ix, iz + 1), Original(data, ix + 1, iz + 1), x - ix);
            return terrain.transform.position.y + Mathf.Lerp(a, b, z - iz) * data.size.y;
        }

        public void Apply(GravelStrip strip, bool deferHeightSync = false)
        {
            path.Clear();
            path.Add(strip.start);
            path.Add(strip.end);
            ApplyStage(path, WagonSurfaceStage.Deform);
            ApplyStage(path, WagonSurfaceStage.Paint);
            ApplyStage(path, WagonSurfaceStage.ManagedGrass);
            ApplyStage(path, WagonSurfaceStage.TerrainDetails);
            if (!deferHeightSync) FlushHeightmaps();
        }

        public void ApplyStage(List<Vector3> points, WagonSurfaceStage stage)
        {
            if (points == null || points.Count < 2) return;
            switch (stage)
            {
                case WagonSurfaceStage.Deform:
                    using (deformMarker.Auto())
                    {
                        foreach (Terrain terrain in Terrain.activeTerrains)
                        {
                            float radius = settings.trailWidth * 0.5f + settings.deformationFalloffWidth;
                            if (!Intersects(terrain, points, radius)) continue;
                            TrailTerrainEditorBackup.CaptureIfNeeded(terrain, settings);
                            if (settings.deformTerrain) Deform(terrain, points, radius);
                        }
                    }
                    break;
                case WagonSurfaceStage.Paint:
                    using (paintMarker.Auto())
                        TrailTerrainModifier.ApplyPathSectionTexture(points, settings);
                    break;
                case WagonSurfaceStage.ManagedGrass:
                    using (managedGrassMarker.Auto())
                        TrailFoliageClearance.ApplyManagedGrassSection(points, settings);
                    break;
                case WagonSurfaceStage.TerrainDetails:
                    using (terrainDetailsMarker.Auto())
                        TrailFoliageClearance.ApplyTerrainDetailSection(points, settings);
                    break;
            }
        }

        public void ApplyTerrainDetailPrototype(List<Vector3> points, int prototypeIndex)
        {
            if (points == null || points.Count < 2) return;
            using (terrainDetailsMarker.Auto())
                TrailFoliageClearance.ApplyTerrainDetailPrototypeSection(
                    points, settings, prototypeIndex);
        }
        public void FlushHeightmaps(bool oneTerrainOnly = false)
        {
            if (dirtyHeightmaps.Count == 0) return;
            TerrainData synced = null;
            using (syncMarker.Auto())
                foreach (TerrainData data in dirtyHeightmaps)
                {
                    if (data != null) data.SyncHeightmap();
                    synced = data;
                    if (oneTerrainOnly) break;
                }
            if (oneTerrainOnly) dirtyHeightmaps.Remove(synced);
            else dirtyHeightmaps.Clear();
        }

        private static bool Intersects(Terrain terrain, List<Vector3> points, float radius)
        {
            if (terrain == null || terrain.terrainData == null) return false;
            Bounds pathBounds = CalculateBounds(points, radius);
            Bounds terrainBounds = terrain.terrainData.bounds;
            terrainBounds.center += terrain.transform.position;
            return pathBounds.Intersects(terrainBounds);
        }

        private void Deform(Terrain terrain, List<Vector3> points, float radius)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int resolution = data.heightmapResolution;
            float sx = (resolution - 1) / data.size.x;
            float sz = (resolution - 1) / data.size.z;
            float cellSizeX = data.size.x / Mathf.Max(1, resolution - 1);
            float cellSizeZ = data.size.z / Mathf.Max(1, resolution - 1);
            float cellHalfDiagonal = 0.5f * Mathf.Sqrt(
                cellSizeX * cellSizeX + cellSizeZ * cellSizeZ);
            Bounds bounds = CalculateBounds(points, radius + cellHalfDiagonal);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) * sx), 0, resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) * sz), 0, resolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x - origin.x) * sx), 0, resolution - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z - origin.z) * sz), 0, resolution - 1);
            float[,] heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            if (!originals.TryGetValue(data, out var baseline))
            {
                baseline = new Dictionary<int, float>();
                originals.Add(data, baseline);
            }
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int key = z * resolution + x;
                    if (!baseline.ContainsKey(key)) baseline.Add(key, heights[z - z0, x - x0]);
                }

            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 world = new Vector3(origin.x + x / sx, 0f, origin.z + z / sz);
                    Vector3 closest = ClosestPointXZ(world, points);
                    float distance = Mathf.Max(0f,
                        Vector3.ProjectOnPlane(world - closest, Vector3.up).magnitude -
                        cellHalfDiagonal);
                    if (distance > radius) continue;
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                        settings.trailWidth * 0.5f, radius, distance));
                    float original = baseline[z * resolution + x] * data.size.y;
                    float target = Mathf.Clamp(closest.y - origin.y - settings.lowerByMetres,
                        original - settings.maximumCutMetres, original + settings.maximumFillMetres);
                    float desired = Mathf.Lerp(original, target, weight * settings.flattenStrength) / data.size.y;
                    float current = heights[z - z0, x - x0];
                    float baseValue = original / data.size.y;
                    if (Mathf.Abs(desired - baseValue) > Mathf.Abs(current - baseValue))
                        heights[z - z0, x - x0] = Mathf.Clamp01(desired);
                }
            data.SetHeightsDelayLOD(x0, z0, heights);
            dirtyHeightmaps.Add(data);
        }

        private static Bounds CalculateBounds(List<Vector3> points, float radius)
        {
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
            bounds.Expand(new Vector3(radius * 2f, 100000f, radius * 2f));
            return bounds;
        }

        private static Vector3 ClosestPointXZ(Vector3 point, List<Vector3> points)
        {
            float best = float.MaxValue;
            Vector3 closest = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a = points[i - 1];
                Vector3 b = points[i];
                Vector3 ab = Vector3.ProjectOnPlane(b - a, Vector3.up);
                float t = ab.sqrMagnitude > 0.00001f
                    ? Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(point - a, Vector3.up), ab) /
                        ab.sqrMagnitude)
                    : 0f;
                Vector3 candidate = Vector3.Lerp(a, b, t);
                float distance = Vector3.ProjectOnPlane(point - candidate, Vector3.up).sqrMagnitude;
                if (distance < best) { best = distance; closest = candidate; }
            }
            return closest;
        }
    }
}
