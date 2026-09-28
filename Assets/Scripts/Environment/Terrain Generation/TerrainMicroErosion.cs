using System;
using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Environment
{
    /// <summary>
    /// Runs a deterministic, world-space erosion pass across a group of Terrain
    /// tiles. Drainage is solved as one connected catchment so erosion high on a
    /// mountain can carry sediment into foothills and lower ground.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Terrain Micro Erosion")]
    public sealed class TerrainMicroErosion : MonoBehaviour
    {
        [Header("Targets")]
        [Tooltip("Treat every loaded Terrain, including inactive streaming tiles, as one connected drainage world.")]
        public bool useAllActiveTerrains = true;
        public List<Terrain> targetTerrains = new();

        [Header("Simulation Detail")]
        [Tooltip("Spacing of the erosion simulation. Smaller values reveal finer channels but cost more memory and time.")]
        [Min(3f)] public float simulationCellSizeMetres = 8f;
        [Range(1, 4)] public int erosionCycles = 3;
        public int seed = 48127;
        [Tooltip("Protects only the outside edge of the complete terrain world. Internal tile seams remain connected.")]
        [Min(0f)] public float protectedWorldEdgeMetres = 24f;

        [Header("Rainfall & Catchments")]
        [Min(20f)] public float rainfallVariationScaleMetres = 210f;
        [Range(0f, 0.8f)] public float rainfallVariation = 0.38f;
        [Tooltip("Drainage area required before a visible channel begins.")]
        [Min(1f)] public float channelStartAreaSquareMetres = 1600f;
        [Tooltip("Drainage area at which a channel reaches full configured depth.")]
        [Min(1f)] public float fullChannelAreaSquareMetres = 42000f;

        [Header("Erosion")]
        [Tooltip("Maximum cumulative depth of drainage channels.")]
        [Min(0f)] public float channelDepthMetres = 5.2f;
        [Tooltip("Gentle sheet erosion on exposed sloping ground.")]
        [Range(0f, 2f)] public float hillsideWashMetres = 0.5f;
        [Range(0f, 45f)] public float erosionBeginsAtSlopeDegrees = 1.5f;
        [Range(1f, 70f)] public float fullErosionSlopeDegrees = 18f;
        [Range(0, 3)] public int channelBankWidth = 2;

        [Header("Sediment & Lower Ground")]
        [Tooltip("Fraction of removed material carried through the drainage network.")]
        [Range(0f, 1f)] public float sedimentTransport = 0.78f;
        [Tooltip("How readily carried sediment settles as water reaches gentler land.")]
        [Range(0f, 1f)] public float depositionRate = 0.52f;
        [Range(0f, 30f)] public float depositionSlopeDegrees = 11f;
        [Min(0f)] public float maximumDepositionMetres = 2.6f;
        [Range(0, 3)] public int depositionSpread = 2;

        [Header("Safety & Finish")]
        [Tooltip("Absolute cap on erosion or deposition at any point, measured from the input terrain.")]
        [Min(0.1f)] public float maximumTerrainChangeMetres = 7f;
        [Range(0, 3)] public int finalBlendPasses = 1;
        [Range(0f, 1f)] public float finalBlendStrength = 0.18f;
        private const int MaximumSimulationCells = 900000;
        private static readonly int[] NeighbourX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] NeighbourZ = { -1, -1, -1, 0, 0, 1, 1, 1 };

        private sealed class TerrainSource
        {
            public Terrain terrain;
            public TerrainData data;
            public float[,] heights;
            public Vector3 origin;
            public Vector3 size;
        }

        public List<Terrain> GetTargets()
        {
            List<Terrain> results = new();
            HashSet<TerrainData> usedData = new();
            if (useAllActiveTerrains)
            {
                Terrain[] loaded = UnityEngine.Object.FindObjectsByType<Terrain>(
                    FindObjectsInactive.Include);
                for (int i = 0; i < loaded.Length; i++)
                    AddUnique(loaded[i], results, usedData);
            }
            else
            {
                for (int i = 0; i < targetTerrains.Count; i++)
                    AddUnique(targetTerrains[i], results, usedData);
            }

            results.Sort((left, right) =>
            {
                int z = left.transform.position.z.CompareTo(right.transform.position.z);
                return z != 0 ? z : left.transform.position.x.CompareTo(right.transform.position.x);
            });
            return results;
        }

        public void Apply(Action<float, string> reportProgress = null)
        {
            List<Terrain> terrains = GetTargets();
            if (terrains.Count == 0)
                throw new InvalidOperationException("No valid Terrain targets were found.");

            List<TerrainSource> sources = ReadSources(terrains);
            GetWorldBounds(sources, out float minX, out float minZ, out float maxX, out float maxZ);
            float spacing = CalculateSafeSpacing(minX, minZ, maxX, maxZ);
            int width = Mathf.CeilToInt((maxX - minX) / spacing) + 1;
            int height = Mathf.CeilToInt((maxZ - minZ) / spacing) + 1;

            reportProgress?.Invoke(0.02f, $"Sampling connected terrain world ({width} x {height})");
            float[] original = SampleWorld(sources, minX, minZ, spacing, width, height);
            float[] working = (float[])original.Clone();
            float[] totalDelta = new float[original.Length];

            for (int cycle = 0; cycle < erosionCycles; cycle++)
            {
                reportProgress?.Invoke(
                    0.08f + cycle * 0.52f / erosionCycles,
                    $"Solving drainage cycle {cycle + 1}/{erosionCycles}");
                RunErosionCycle(
                    working, totalDelta, minX, minZ, spacing, width, height,
                    cycle, reportProgress);
            }

            for (int pass = 0; pass < finalBlendPasses; pass++)
            {
                float[] blurred = Blur(totalDelta, width, height);
                for (int i = 0; i < totalDelta.Length; i++)
                {
                    if (!float.IsNaN(totalDelta[i]))
                        totalDelta[i] = Mathf.Lerp(totalDelta[i], blurred[i], finalBlendStrength);
                }
            }

            for (int terrainIndex = 0; terrainIndex < sources.Count; terrainIndex++)
            {
                reportProgress?.Invoke(
                    0.68f + 0.30f * terrainIndex / Mathf.Max(1f, sources.Count),
                    $"Applying erosion to {sources[terrainIndex].terrain.name}");
                ApplyDeltaToTerrain(
                    sources[terrainIndex], totalDelta,
                    minX, minZ, spacing, width, height);
                sources[terrainIndex].terrain.Flush();
            }

            reportProgress?.Invoke(1f, "Micro-terrain erosion complete");
        }

        private void RunErosionCycle(
            float[] heights,
            float[] totalDelta,
            float minX,
            float minZ,
            float spacing,
            int width,
            int height,
            int cycle,
            Action<float, string> reportProgress)
        {
            int count = heights.Length;
            int[] receiver = new int[count];
            float[] receiverSlope = new float[count];
            float[] rainfall = new float[count];
            float[] accumulation = new float[count];
            int[] order = new int[count];
            Vector2 noiseOffset = BuildSeedOffset(seed + cycle * 7919);

            for (int z = 0; z < height; z++)
            {
                float worldZ = minZ + z * spacing;
                for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;
                    order[index] = index;
                    receiver[index] = -1;
                    if (float.IsNaN(heights[index]))
                        continue;

                    float worldX = minX + x * spacing;
                    float rainNoise = Mathf.PerlinNoise(
                        worldX / rainfallVariationScaleMetres + noiseOffset.x,
                        worldZ / rainfallVariationScaleMetres + noiseOffset.y);
                    rainfall[index] = Mathf.Max(
                        0.1f,
                        1f + (rainNoise * 2f - 1f) * rainfallVariation);
                    accumulation[index] = rainfall[index];

                    float bestDrop = 0.0001f;
                    int best = -1;
                    float bestDistance = spacing;
                    for (int neighbour = 0; neighbour < 8; neighbour++)
                    {
                        int nx = x + NeighbourX[neighbour];
                        int nz = z + NeighbourZ[neighbour];
                        if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                            continue;
                        int candidate = nz * width + nx;
                        if (float.IsNaN(heights[candidate]))
                            continue;
                        float drop = heights[index] - heights[candidate];
                        float distance =
                            NeighbourX[neighbour] != 0 && NeighbourZ[neighbour] != 0
                                ? spacing * 1.41421356f
                                : spacing;
                        float gradient = drop / distance;
                        if (gradient > bestDrop)
                        {
                            bestDrop = gradient;
                            best = candidate;
                            bestDistance = distance;
                        }
                    }
                    receiver[index] = best;
                    receiverSlope[index] = best >= 0
                        ? Mathf.Max(0f, (heights[index] - heights[best]) / bestDistance)
                        : 0f;
                }
            }

            Array.Sort(order, (left, right) =>
            {
                bool leftInvalid = float.IsNaN(heights[left]);
                bool rightInvalid = float.IsNaN(heights[right]);
                if (leftInvalid != rightInvalid)
                    return leftInvalid ? 1 : -1;
                return heights[right].CompareTo(heights[left]);
            });

            for (int i = 0; i < order.Length; i++)
            {
                int index = order[i];
                int downstream = receiver[index];
                if (downstream >= 0)
                    accumulation[downstream] += accumulation[index];
            }

            reportProgress?.Invoke(
                0.13f + cycle * 0.52f / erosionCycles,
                $"Carving connected runoff channels {cycle + 1}/{erosionCycles}");

            float[] channelCut = new float[count];
            float[] wash = new float[count];
            float cycleDivisor = Mathf.Max(1, erosionCycles);
            float cellArea = spacing * spacing;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;
                    if (float.IsNaN(heights[index]))
                    {
                        channelCut[index] = float.NaN;
                        wash[index] = float.NaN;
                        continue;
                    }

                    float slopeDegrees = Mathf.Atan(receiverSlope[index]) * Mathf.Rad2Deg;
                    float slopeResponse = Mathf.SmoothStep(
                        0f, 1f,
                        Mathf.InverseLerp(
                            erosionBeginsAtSlopeDegrees,
                            fullErosionSlopeDegrees,
                            slopeDegrees));
                    float drainageArea = accumulation[index] * cellArea;
                    float channelResponse = Mathf.SmoothStep(
                        0f, 1f,
                        Mathf.InverseLerp(
                            channelStartAreaSquareMetres,
                            fullChannelAreaSquareMetres,
                            drainageArea));
                    float edgeMask = CalculateWorldEdgeMask(
                        minX + x * spacing,
                        minZ + z * spacing,
                        minX,
                        minZ,
                        minX + (width - 1) * spacing,
                        minZ + (height - 1) * spacing);

                    channelCut[index] =
                        channelDepthMetres * channelResponse *
                        Mathf.Lerp(0.18f, 1f, slopeResponse) * edgeMask /
                        cycleDivisor;
                    wash[index] =
                        hillsideWashMetres * slopeResponse *
                        Mathf.Lerp(0.72f, 1.28f, rainfall[index]) * edgeMask /
                        cycleDivisor;
                }
            }

            for (int pass = 0; pass < channelBankWidth; pass++)
                channelCut = Blur(channelCut, width, height);

            float[] erosion = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (float.IsNaN(heights[i]))
                {
                    erosion[i] = float.NaN;
                    continue;
                }
                erosion[i] = Mathf.Max(0f, channelCut[i] + wash[i]);
            }

            float[] sedimentVolume = new float[count];
            float[] deposition = new float[count];
            for (int i = 0; i < order.Length; i++)
            {
                int index = order[i];
                if (float.IsNaN(heights[index]))
                    continue;

                float incomingVolume = sedimentVolume[index];
                float erodedVolume = erosion[index] * cellArea * sedimentTransport;
                float availableVolume = incomingVolume + erodedVolume;
                int downstream = receiver[index];
                float slopeDegrees = Mathf.Atan(receiverSlope[index]) * Mathf.Rad2Deg;
                float flatness = 1f - Mathf.SmoothStep(
                    0f, 1f,
                    Mathf.InverseLerp(0f, depositionSlopeDegrees, slopeDegrees));
                float settleFraction = downstream < 0
                    ? 1f
                    : depositionRate * flatness;
                float depositDepth = Mathf.Min(
                    maximumDepositionMetres / Mathf.Max(1, erosionCycles),
                    availableVolume * settleFraction / cellArea);
                deposition[index] += depositDepth;
                float remainingVolume = Mathf.Max(
                    0f,
                    availableVolume - depositDepth * cellArea);
                if (downstream >= 0)
                    sedimentVolume[downstream] += remainingVolume;
            }

            for (int pass = 0; pass < depositionSpread; pass++)
                deposition = Blur(deposition, width, height);

            for (int i = 0; i < count; i++)
            {
                if (float.IsNaN(heights[i]))
                {
                    totalDelta[i] = float.NaN;
                    continue;
                }

                float change = deposition[i] - erosion[i];
                float nextTotal = Mathf.Clamp(
                    totalDelta[i] + change,
                    -maximumTerrainChangeMetres,
                    maximumTerrainChangeMetres);
                float applied = nextTotal - totalDelta[i];
                totalDelta[i] = nextTotal;
                heights[i] += applied;
            }
        }

        private void ApplyDeltaToTerrain(
            TerrainSource source,
            float[] delta,
            float minX,
            float minZ,
            float spacing,
            int width,
            int height)
        {
            int resolution = source.data.heightmapResolution;
            float[,] result = new float[resolution, resolution];
            float sampleX = source.size.x / Mathf.Max(1, resolution - 1);
            float sampleZ = source.size.z / Mathf.Max(1, resolution - 1);
            for (int z = 0; z < resolution; z++)
            {
                float worldZ = source.origin.z + z * sampleZ;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = source.origin.x + x * sampleX;
                    float change = SampleGridBilinear(
                        delta, worldX, worldZ,
                        minX, minZ, spacing, width, height);
                    float originalWorldHeight =
                        source.origin.y + source.heights[z, x] * source.size.y;
                    result[z, x] = Mathf.Clamp01(
                        (originalWorldHeight + change - source.origin.y) /
                        Mathf.Max(0.001f, source.size.y));
                }
            }
            // SetHeights is synchronous and already updates the heightmap, LOD, and
            // vegetation state. Calling SyncHeightmap here can replay stale GPU-side
            // data over a later rollback performed by the generator.
            source.data.SetHeights(0, 0, result);
        }

        private float[] SampleWorld(
            List<TerrainSource> sources,
            float minX,
            float minZ,
            float spacing,
            int width,
            int height)
        {
            float[] result = new float[width * height];
            for (int z = 0; z < height; z++)
            {
                float worldZ = minZ + z * spacing;
                for (int x = 0; x < width; x++)
                {
                    float worldX = minX + x * spacing;
                    result[z * width + x] = TrySampleTerrainHeight(
                        sources, worldX, worldZ, out float sampled)
                        ? sampled
                        : float.NaN;
                }
            }
            return result;
        }

        private static bool TrySampleTerrainHeight(
            List<TerrainSource> sources,
            float worldX,
            float worldZ,
            out float height)
        {
            const float tolerance = 0.01f;
            for (int i = 0; i < sources.Count; i++)
            {
                TerrainSource source = sources[i];
                if (worldX < source.origin.x - tolerance ||
                    worldZ < source.origin.z - tolerance ||
                    worldX > source.origin.x + source.size.x + tolerance ||
                    worldZ > source.origin.z + source.size.z + tolerance)
                    continue;

                float normalizedX = Mathf.Clamp01((worldX - source.origin.x) / source.size.x);
                float normalizedZ = Mathf.Clamp01((worldZ - source.origin.z) / source.size.z);
                float sampleX = normalizedX * (source.data.heightmapResolution - 1);
                float sampleZ = normalizedZ * (source.data.heightmapResolution - 1);
                height = source.origin.y +
                         SampleHeightArray(source.heights, sampleX, sampleZ) * source.size.y;
                return true;
            }
            height = 0f;
            return false;
        }

        private static float SampleHeightArray(float[,] heights, float x, float z)
        {
            int width = heights.GetLength(1);
            int height = heights.GetLength(0);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, height - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, height - 1);
            float tx = Mathf.Clamp01(x - x0);
            float tz = Mathf.Clamp01(z - z0);
            return Mathf.Lerp(
                Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx),
                Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx),
                tz);
        }

        private static float SampleGridBilinear(
            float[] values,
            float worldX,
            float worldZ,
            float minX,
            float minZ,
            float spacing,
            int width,
            int height)
        {
            float gridX = Mathf.Clamp((worldX - minX) / spacing, 0f, width - 1);
            float gridZ = Mathf.Clamp((worldZ - minZ) / spacing, 0f, height - 1);
            int x0 = Mathf.FloorToInt(gridX);
            int z0 = Mathf.FloorToInt(gridZ);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, height - 1);
            float tx = gridX - x0;
            float tz = gridZ - z0;
            float a = values[z0 * width + x0];
            float b = values[z0 * width + x1];
            float c = values[z1 * width + x0];
            float d = values[z1 * width + x1];
            if (float.IsNaN(a)) a = 0f;
            if (float.IsNaN(b)) b = a;
            if (float.IsNaN(c)) c = a;
            if (float.IsNaN(d)) d = c;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        private static float[] Blur(float[] values, int width, int height)
        {
            float[] result = new float[values.Length];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;
                    if (float.IsNaN(values[index]))
                    {
                        result[index] = float.NaN;
                        continue;
                    }
                    float total = 0f;
                    float weight = 0f;
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        int nz = z + oz;
                        if (nz < 0 || nz >= height)
                            continue;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= width)
                                continue;
                            float value = values[nz * width + nx];
                            if (float.IsNaN(value))
                                continue;
                            float sampleWeight = ox == 0 && oz == 0 ? 4f : (ox == 0 || oz == 0 ? 2f : 1f);
                            total += value * sampleWeight;
                            weight += sampleWeight;
                        }
                    }
                    result[index] = weight > 0f ? total / weight : values[index];
                }
            }
            return result;
        }

        private float CalculateWorldEdgeMask(
            float worldX,
            float worldZ,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            if (protectedWorldEdgeMetres <= 0f)
                return 1f;
            float edgeDistance = Mathf.Min(
                Mathf.Min(worldX - minX, maxX - worldX),
                Mathf.Min(worldZ - minZ, maxZ - worldZ));
            return Mathf.SmoothStep(
                0f, 1f,
                Mathf.Clamp01(edgeDistance / protectedWorldEdgeMetres));
        }

        private float CalculateSafeSpacing(
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            float spacing = Mathf.Max(3f, simulationCellSizeMetres);
            float width = Mathf.Max(1f, maxX - minX);
            float height = Mathf.Max(1f, maxZ - minZ);
            long cellCount =
                (long)(Mathf.CeilToInt(width / spacing) + 1) *
                (Mathf.CeilToInt(height / spacing) + 1);
            if (cellCount > MaximumSimulationCells)
            {
                float scale = Mathf.Sqrt(cellCount / (float)MaximumSimulationCells);
                spacing *= scale * 1.01f;
                Debug.LogWarning(
                    $"Terrain erosion simulation spacing was raised to {spacing:0.0} m " +
                    "to stay within its memory safety limit.", this);
            }
            return spacing;
        }

        private static List<TerrainSource> ReadSources(List<Terrain> terrains)
        {
            List<TerrainSource> sources = new(terrains.Count);
            for (int i = 0; i < terrains.Count; i++)
            {
                Terrain terrain = terrains[i];
                TerrainData data = terrain.terrainData;
                int resolution = data.heightmapResolution;
                sources.Add(new TerrainSource
                {
                    terrain = terrain,
                    data = data,
                    heights = data.GetHeights(0, 0, resolution, resolution),
                    origin = terrain.transform.position,
                    size = data.size
                });
            }
            return sources;
        }

        private static void GetWorldBounds(
            List<TerrainSource> sources,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            minX = float.PositiveInfinity;
            minZ = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            maxZ = float.NegativeInfinity;
            for (int i = 0; i < sources.Count; i++)
            {
                TerrainSource source = sources[i];
                minX = Mathf.Min(minX, source.origin.x);
                minZ = Mathf.Min(minZ, source.origin.z);
                maxX = Mathf.Max(maxX, source.origin.x + source.size.x);
                maxZ = Mathf.Max(maxZ, source.origin.z + source.size.z);
            }
        }

        private static void AddUnique(
            Terrain terrain,
            List<Terrain> results,
            HashSet<TerrainData> usedData)
        {
            if (terrain == null || terrain.terrainData == null || !usedData.Add(terrain.terrainData))
                return;
            results.Add(terrain);
        }

        private static Vector2 BuildSeedOffset(int value)
        {
            unchecked
            {
                uint seedValue = (uint)value;
                seedValue ^= seedValue << 13;
                seedValue ^= seedValue >> 17;
                seedValue ^= seedValue << 5;
                return new Vector2(
                    (seedValue & 0xffff) * 0.0137f + 17.31f,
                    ((seedValue >> 16) & 0xffff) * 0.0179f + 43.73f);
            }
        }

        public void ApplyNaturalPreset()
        {
            simulationCellSizeMetres = 8f;
            erosionCycles = 3;
            protectedWorldEdgeMetres = 24f;
            rainfallVariationScaleMetres = 210f;
            rainfallVariation = 0.38f;
            channelStartAreaSquareMetres = 1600f;
            fullChannelAreaSquareMetres = 42000f;
            channelDepthMetres = 5.2f;
            hillsideWashMetres = 0.5f;
            erosionBeginsAtSlopeDegrees = 1.5f;
            fullErosionSlopeDegrees = 18f;
            channelBankWidth = 2;
            sedimentTransport = 0.78f;
            depositionRate = 0.52f;
            depositionSlopeDegrees = 11f;
            maximumDepositionMetres = 2.6f;
            depositionSpread = 2;
            maximumTerrainChangeMetres = 7f;
            finalBlendPasses = 1;
            finalBlendStrength = 0.18f;
        }

        public void ApplyStrongerNaturalPreset()
        {
            ApplyNaturalPreset();
            channelStartAreaSquareMetres = 1100f;
            fullChannelAreaSquareMetres = 34000f;
            channelDepthMetres = 6.4f;
            hillsideWashMetres = 0.62f;
            maximumDepositionMetres = 3f;
            maximumTerrainChangeMetres = 8.5f;
        }

        private void Reset()
        {
            ApplyNaturalPreset();
        }

        private void OnValidate()
        {
            simulationCellSizeMetres = Mathf.Max(3f, simulationCellSizeMetres);
            erosionCycles = Mathf.Clamp(erosionCycles, 1, 4);
            rainfallVariationScaleMetres = Mathf.Max(20f, rainfallVariationScaleMetres);
            channelStartAreaSquareMetres = Mathf.Max(1f, channelStartAreaSquareMetres);
            fullChannelAreaSquareMetres = Mathf.Max(
                channelStartAreaSquareMetres + 1f,
                fullChannelAreaSquareMetres);
            fullErosionSlopeDegrees = Mathf.Max(
                erosionBeginsAtSlopeDegrees + 0.1f,
                fullErosionSlopeDegrees);
            depositionSlopeDegrees = Mathf.Max(0.1f, depositionSlopeDegrees);
            maximumTerrainChangeMetres = Mathf.Max(0.1f, maximumTerrainChangeMetres);
        }
    }
}