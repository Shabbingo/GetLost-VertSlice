using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TerrainFoliageSpawner : MonoBehaviour
    {
        private static readonly Vector2[] CliffReliefDirections =
        {
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(0.7071068f, -0.7071068f),
            new Vector2(-0.7071068f, -0.7071068f)
        };

        [Header("Terrain")]
        [SerializeField] private Terrain terrain;

        [Header("Placement")]
        [SerializeField, Min(0.1f)] private float sampleDistance = 5f;
        [SerializeField, Range(0f, 1f)] private float randomOffset = 0.8f;
        [SerializeField] private int seed = 12345;
        [SerializeField, Min(1)] private int maximumInstances = 50000;

        [Header("Rules")]
        [SerializeField] private List<TerrainFoliageRule> rules =
            new List<TerrainFoliageRule>();

        [Header("Prototype Synchronisation")]
        [SerializeField] private bool synchronisePrototypesBeforeGeneration = true;
        [SerializeField] private TerrainFoliagePrototypeLibrary prototypeLibrary;
        [Tooltip("Also synchronise every currently active Terrain in the loaded scenes.")]
        [SerializeField] private bool automaticallyFindActiveTerrains = true;
        [Tooltip("Additional Terrain chunks to synchronise when automatic discovery is disabled, or to supplement it.")]
        [SerializeField] private List<Terrain> prototypeTargetTerrains = new List<Terrain>();
        [Tooltip("Collect Terrain Detail and Terrain Tree prefabs from the assigned rules, reuse existing prototype settings, and create any missing prototypes automatically.")]
        [SerializeField] private bool autoFindPrototypesFromRules = true;

        [Header("Terrain Debug")]
        [SerializeField] private bool showDebugSample = true;
        [SerializeField] private Vector2 debugSampleNormalizedPosition =
            new Vector2(0.5f, 0.5f);
        [SerializeField, Min(0.05f)] private float debugMarkerSize = 1f;

        [Header("Scene Preview")]
        [SerializeField] private bool showPlacementPreview;
        [SerializeField] private TerrainFoliageRule previewRule;
        [SerializeField, Range(4, 64)] private int previewGridResolution = 24;
        [SerializeField, Min(0.01f)] private float previewMarkerSize = 0.35f;

        [Header("Exclusion and Paths")]
        [SerializeField] private bool automaticallyFindSceneMasks = true;
        [SerializeField] private List<TerrainFoliageExclusionVolume> exclusionVolumes =
            new List<TerrainFoliageExclusionVolume>();
        [SerializeField] private List<TerrainFoliagePathClearance> pathClearances =
            new List<TerrainFoliagePathClearance>();
        [SerializeField] private List<TerrainFoliageSplineClearance> splineClearances =
            new List<TerrainFoliageSplineClearance>();

        [Header("Runtime Streamed GameObjects")]
        [SerializeField] private TerrainFoliagePlacementData runtimePlacementData;
        [SerializeField] private bool createRuntimeStreamer = true;

        [Header("Managed Grass Renderer")]
        [SerializeField] private bool createManagedGrassRenderer = true;
        [SerializeField, Min(5f)] private float grassRenderDistance = 90f;
        [SerializeField, Min(4f)] private float grassRenderChunkSize = 24f;
        [SerializeField] private UnityEngine.Rendering.ShadowCastingMode grassShadowCasting =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        [SerializeField] private bool grassReceiveShadows = true;
        [SerializeField, Range(0, 31)] private int grassRenderingLayer = 0;

        [Header("Generated Objects")]
        [SerializeField] private string generatedRootName = "Generated Foliage";

        [SerializeField, HideInInspector]
        private List<GameObject> managedDetailPrefabs =
            new List<GameObject>();

        [SerializeField, HideInInspector]
        private List<GameObject> managedTreePrefabs =
            new List<GameObject>();

        private sealed class GenerationContext
        {
            public TerrainSampler sampler;
            public TerrainData terrainData;
            public Vector3 terrainPosition;
            public System.Random random;
            public GenerationReport report;
            public List<TerrainFoliageExclusionVolume> exclusions;
            public List<TerrainFoliagePathClearance> paths;
            public List<TerrainFoliageSplineClearance> splines;
            public Dictionary<TerrainFoliageRule, int> terrainLayerIndices;
            public Dictionary<TerrainFoliagePrefabEntry, int> detailLayers;
            public Dictionary<int, int[,]> detailMaps;
            public Dictionary<TerrainFoliagePrefabEntry, int> treeLayers;
            public List<TreeInstance> generatedTrees;
            public Dictionary<TerrainFoliageRule, Transform> ruleParents;
            public Transform generatedRoot;
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
                EnsureManagedRenderers();
        }

        public Terrain Terrain => terrain;
        public float SampleDistance => sampleDistance;
        public IReadOnlyList<TerrainFoliageRule> Rules => rules;
        public bool SynchronisePrototypesBeforeGeneration => synchronisePrototypesBeforeGeneration;
        public TerrainFoliagePrototypeLibrary PrototypeLibrary => prototypeLibrary;
        public bool AutomaticallyFindActiveTerrains => automaticallyFindActiveTerrains;
        public IReadOnlyList<Terrain> PrototypeTargetTerrains => prototypeTargetTerrains;
        public bool AutoFindPrototypesFromRules => autoFindPrototypesFromRules;
        public bool ShowDebugSample => showDebugSample;
        public float DebugMarkerSize => debugMarkerSize;
        public bool ShowPlacementPreview => showPlacementPreview;
        public TerrainFoliageRule PreviewRule => previewRule;
        public int PreviewGridResolution => previewGridResolution;
        public float PreviewMarkerSize => previewMarkerSize;
        public bool AutomaticallyFindSceneMasks => automaticallyFindSceneMasks;
        public IReadOnlyList<TerrainFoliageExclusionVolume> ExclusionVolumes => exclusionVolumes;
        public IReadOnlyList<TerrainFoliagePathClearance> PathClearances => pathClearances;
        public IReadOnlyList<TerrainFoliageSplineClearance> SplineClearances => splineClearances;
        public TerrainFoliagePlacementData RuntimePlacementData => runtimePlacementData;

        public enum PreviewStatus
        {
            Valid,
            WrongTerrainLayer,
            RejectedByLayerWeight,
            RejectedBySlope,
            RejectedByCliffFormation
        }

        public readonly struct PreviewPoint
        {
            public PreviewPoint(
                Vector3 position,
                PreviewStatus status,
                float naturalTreeDensity,
                float cliffFormationDensity)
            {
                Position = position;
                Status = status;
                NaturalTreeDensity = naturalTreeDensity;
                CliffFormationDensity = cliffFormationDensity;
            }

            public Vector3 Position { get; }
            public PreviewStatus Status { get; }
            public float NaturalTreeDensity { get; }
            public float CliffFormationDensity { get; }
        }

        public List<PreviewPoint> BuildPlacementPreview()
        {
            List<PreviewPoint> points = new List<PreviewPoint>();

            if (!showPlacementPreview ||
                terrain == null ||
                terrain.terrainData == null ||
                previewRule == null ||
                previewRule.terrainLayer == null)
            {
                return points;
            }

            TerrainSampler sampler = new TerrainSampler(terrain);
            int layerIndex = sampler.FindLayerIndex(previewRule.terrainLayer);
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            int resolution = Mathf.Max(1, previewGridResolution);

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float normalizedX = (x + 0.5f) / resolution;
                    float normalizedZ = (z + 0.5f) / resolution;

                    float worldX =
                        terrainPosition.x + normalizedX * terrainSize.x;

                    float worldZ =
                        terrainPosition.z + normalizedZ * terrainSize.z;

                    TerrainSample sample = sampler.Sample(worldX, worldZ);
                    PreviewStatus status;
                    float naturalTreeDensity =
                        EvaluateNaturalTreeDensity(
                            sample.WorldPosition,
                            previewRule,
                            seed);
                    float cliffFormationDensity =
                        EvaluateCliffFormationDensity(
                            sampler,
                            sample,
                            previewRule,
                            seed);

                    if (layerIndex < 0)
                    {
                        status = PreviewStatus.WrongTerrainLayer;
                    }
                    else if (sample.GetLayerWeight(layerIndex) <
                             previewRule.minimumLayerWeight)
                    {
                        status = PreviewStatus.RejectedByLayerWeight;
                    }
                    else
                    {
                        float slope =
                            Vector3.Angle(sample.WorldNormal, Vector3.up);

                        if (previewRule.useSlopeLimit &&
                            (slope < previewRule.minimumSlope ||
                             slope > previewRule.maximumSlope))
                        {
                            status = PreviewStatus.RejectedBySlope;
                        }
                        else if (previewRule.useCliffFormationDistribution &&
                                 cliffFormationDensity <= 0f)
                        {
                            status = PreviewStatus.RejectedByCliffFormation;
                        }
                        else
                        {
                            status = PreviewStatus.Valid;
                        }
                    }

                    points.Add(new PreviewPoint(
                        sample.WorldPosition + sample.WorldNormal * 0.05f,
                        status,
                        naturalTreeDensity,
                        cliffFormationDensity));
                }
            }

            return points;
        }

        public Vector3 GetDebugSampleWorldPosition()
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return transform.position;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;

            float worldX = terrainPosition.x +
                           Mathf.Clamp01(debugSampleNormalizedPosition.x) *
                           terrainSize.x;

            float worldZ = terrainPosition.z +
                           Mathf.Clamp01(debugSampleNormalizedPosition.y) *
                           terrainSize.z;

            TerrainSampler sampler = new TerrainSampler(terrain);
            return sampler.Sample(worldX, worldZ).WorldPosition;
        }

        public string GetDebugSampleReport()
        {
            if (terrain == null)
            {
                return "Assign a Terrain to inspect its layer weights.";
            }

            if (terrain.terrainData == null)
            {
                return "The assigned Terrain has no TerrainData.";
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;

            float worldX = terrainPosition.x +
                           Mathf.Clamp01(debugSampleNormalizedPosition.x) *
                           terrainSize.x;

            float worldZ = terrainPosition.z +
                           Mathf.Clamp01(debugSampleNormalizedPosition.y) *
                           terrainSize.z;

            TerrainSampler sampler = new TerrainSampler(terrain);
            TerrainSample sample = sampler.Sample(worldX, worldZ);
            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                $"Sample world position: {sample.WorldPosition.x:0.00}, " +
                $"{sample.WorldPosition.y:0.00}, {sample.WorldPosition.z:0.00}");

            if (terrainLayers == null || terrainLayers.Length == 0)
            {
                builder.AppendLine("Terrain has no Terrain Layers.");
                return builder.ToString();
            }

            builder.AppendLine("Layer weights:");

            for (int i = 0; i < terrainLayers.Length; i++)
            {
                string layerName = terrainLayers[i] != null
                    ? terrainLayers[i].name
                    : $"Missing Layer {i}";

                builder.AppendLine(
                    $"{i}: {layerName} = " +
                    $"{sample.GetLayerWeight(i):0.000}");
            }

            return builder.ToString();
        }

        public GenerationReport Generate()
        {
            return Generate(null);
        }

        public GenerationReport Generate(Func<float, string, bool> cancelRequested)
        {
            ValidateForGeneration();

            if (synchronisePrototypesBeforeGeneration)
            {
                PrototypeSyncReport syncReport = SynchroniseRequiredPrototypes();
                if (syncReport.HasErrors)
                {
                    throw new InvalidOperationException(syncReport.ToSummary());
                }
            }

            Clear();

            TerrainSampler sampler = new TerrainSampler(terrain);
            TerrainData terrainData = sampler.TerrainData;
            Vector3 terrainPosition = terrain.transform.position;
            System.Random random = new System.Random(seed);
            GenerationReport report = new GenerationReport();

            List<TerrainFoliageExclusionVolume> activeExclusions = GetActiveExclusionVolumes();
            List<TerrainFoliagePathClearance> activePaths = GetActivePathClearances();
            List<TerrainFoliageSplineClearance> activeSplines = GetActiveSplineClearances();
            Dictionary<TerrainFoliageRule, int> terrainLayerIndices =
                BuildTerrainLayerIndexCache(sampler);

            Dictionary<TerrainFoliageRule, Transform> ruleParents =
                new Dictionary<TerrainFoliageRule, Transform>();

            Dictionary<TerrainFoliagePrefabEntry, int> detailLayers =
                FindExistingDetailLayers(terrainData, report);

            Dictionary<int, int[,]> detailMaps =
                CreateDetailMaps(terrainData, detailLayers);

            Dictionary<TerrainFoliagePrefabEntry, int> treeLayers =
                FindExistingTreeLayers(terrainData, report);

            List<TreeInstance> generatedTrees = new List<TreeInstance>();

            GenerationContext context = new GenerationContext
            {
                sampler = sampler,
                terrainData = terrainData,
                terrainPosition = terrainPosition,
                random = random,
                report = report,
                exclusions = activeExclusions,
                paths = activePaths,
                splines = activeSplines,
                terrainLayerIndices = terrainLayerIndices,
                detailLayers = detailLayers,
                detailMaps = detailMaps,
                treeLayers = treeLayers,
                generatedTrees = generatedTrees,
                ruleParents = ruleParents
            };

            int columns =
                Mathf.CeilToInt(terrainData.size.x / sampleDistance);

            int rows =
                Mathf.CeilToInt(terrainData.size.z / sampleDistance);

            for (int row = 0; row < rows; row++)
            {
                if (cancelRequested != null &&
                    cancelRequested(rows <= 0 ? 1f : row / (float)rows,
                        $"Sampling terrain row {row + 1:N0} of {rows:N0}"))
                {
                    report.Cancelled = true;
                    WriteDetailMaps(terrainData, detailMaps);
                    AppendGeneratedTrees(terrainData, generatedTrees);
                    terrainData.RefreshPrototypes();
                    terrain.Flush();
                    return FinishReport(report, context.generatedRoot);
                }

                for (int column = 0; column < columns; column++)
                {
                    report.GridCells++;

                    float cellOriginX =
                        terrainPosition.x + column * sampleDistance;

                    float cellOriginZ =
                        terrainPosition.z + row * sampleDistance;

                    for (int ruleIndex = 0;
                         ruleIndex < rules.Count;
                         ruleIndex++)
                    {
                        TerrainFoliageRule rule = rules[ruleIndex];

                        int placementCandidateCount =
                            rule != null &&
                            rule.useCliffFormationDistribution
                                ? rule.buildContinuousCliffFaces
                                    ? 1
                                    : Mathf.Clamp(rule.cliffSamplesPerCell, 1, 6)
                                : 1;

                        int candidateCount =
                            placementCandidateCount +
                            (rule != null
                                ? Mathf.Clamp(
                                    rule.additionalTerrainDetailSamplesPerCell,
                                    0,
                                    4)
                                : 0);

                        for (int candidateIndex = 0;
                             candidateIndex < candidateCount;
                             candidateIndex++)
                        {
                            bool detailOnly =
                                candidateIndex >= placementCandidateCount;
                            float jitterRange =
                                sampleDistance * 0.5f * randomOffset;

                            float worldX =
                                cellOriginX + sampleDistance * 0.5f +
                                Mathf.Lerp(
                                    -jitterRange,
                                    jitterRange,
                                    GetStableGridValue(
                                        cellOriginX,
                                        cellOriginZ,
                                        ruleIndex,
                                        candidateIndex,
                                        seed,
                                        17));

                            float worldZ =
                                cellOriginZ + sampleDistance * 0.5f +
                                Mathf.Lerp(
                                    -jitterRange,
                                    jitterRange,
                                    GetStableGridValue(
                                        cellOriginX,
                                        cellOriginZ,
                                        ruleIndex,
                                        candidateIndex,
                                        seed,
                                        43));

                            if (TryGenerateCandidate(
                                    context,
                                    rule,
                                    ruleIndex,
                                    worldX,
                                    worldZ,
                                    detailOnly))
                            {
                                WriteDetailMaps(terrainData, detailMaps);
                                AppendGeneratedTrees(terrainData, generatedTrees);
                                terrainData.RefreshPrototypes();
                                terrain.Flush();
                                return FinishReport(
                                    report,
                                    context.generatedRoot);
                            }
                        }
                    }
                }
            }

            WriteDetailMaps(terrainData, detailMaps);
            AppendGeneratedTrees(terrainData, generatedTrees);
            terrainData.RefreshPrototypes();
            terrain.Flush();
            EnsureManagedRenderers();
            return FinishReport(report, context.generatedRoot);
        }

        private bool TryGenerateCandidate(
            GenerationContext context,
            TerrainFoliageRule rule,
            int ruleIndex,
            float worldX,
            float worldZ,
            bool detailOnly)
        {
            GenerationReport report = context.report;

            if (rule == null)
            {
                report.NullRules++;
                return false;
            }

            if (rule.terrainLayer == null)
            {
                report.MissingLayerAssignments++;
                return false;
            }

            int terrainLayerIndex = context.terrainLayerIndices.TryGetValue(
                rule,
                out int cachedLayerIndex)
                ? cachedLayerIndex
                : -1;

            if (terrainLayerIndex < 0)
            {
                report.LayerNotOnTerrain++;
                return false;
            }

            if (!context.sampler.ContainsWorldXZ(worldX, worldZ))
            {
                report.OutsideTerrain++;
                return false;
            }

            report.SampledPoints++;
            TerrainSample sample = context.sampler.Sample(worldX, worldZ);
            float layerWeight = sample.GetLayerWeight(terrainLayerIndex);

            report.HighestObservedLayerWeight = Mathf.Max(
                report.HighestObservedLayerWeight,
                layerWeight);

            if (layerWeight < rule.minimumLayerWeight)
            {
                report.RejectedByLayerWeight++;
                return false;
            }

            float slope = Vector3.Angle(sample.WorldNormal, Vector3.up);
            if (rule.useSlopeLimit &&
                (slope < rule.minimumSlope || slope > rule.maximumSlope))
            {
                report.RejectedBySlope++;
                return false;
            }

            if (rule.useCliffFormationDistribution)
            {
                float cliffFormationDensity =
                    EvaluateCliffFormationDensity(
                        context.sampler,
                        sample,
                        rule,
                        seed);

                report.HighestObservedCliffFormationDensity = Mathf.Max(
                    report.HighestObservedCliffFormationDensity,
                    cliffFormationDensity);

                if (cliffFormationDensity <= 0f ||
                    GetStablePositionValue(
                        sample.WorldPosition,
                        seed,
                        ruleIndex + 12011) > cliffFormationDensity)
                {
                    report.RejectedByCliffFormation++;
                    return false;
                }

                if (rule.buildContinuousCliffFaces && !detailOnly)
                {
                    float spacing = Mathf.Max(5f, rule.cliffFaceSpacing);
                    float spacingProbability = Mathf.Clamp01(
                        sampleDistance * sampleDistance /
                        (spacing * spacing));
                    if (GetStablePositionValue(
                            sample.WorldPosition,
                            seed,
                            ruleIndex + 19001) > spacingProbability)
                    {
                        report.RejectedByCliffFaceSpacing++;
                        return false;
                    }
                }
            }

            if (context.random.NextDouble() > rule.spawnChance)
            {
                report.RejectedBySpawnChance++;
                return false;
            }

            TerrainFoliagePrefabEntry entry = detailOnly
                ? rule.GetRandomGroundCoverEntry(context.random)
                : rule.buildContinuousCliffFaces
                    ? rule.GetRandomCliffFaceEntry(context.random)
                    : rule.GetRandomEntry(context.random);

            if (entry == null || entry.prefab == null)
            {
                // Extra candidates intentionally ask only for Terrain Detail
                // entries. A mixed rule without details is not an error.
                if (!detailOnly)
                {
                    report.MissingPrefabs++;
                }

                return false;
            }

            bool followsNaturalForestDensity =
                rule.useNaturalTreeDensityVariation &&
                (entry.outputMode == TerrainFoliageOutputMode.TerrainTree ||
                 rule.followNaturalForestDensity);

            if (followsNaturalForestDensity)
            {
                float density = EvaluateNaturalTreeDensity(
                    sample.WorldPosition,
                    rule,
                    seed);

                if (rule.followNaturalForestDensity &&
                    rule.useUnderstoryThickets &&
                    entry.outputMode != TerrainFoliageOutputMode.TerrainTree)
                {
                    density *= EvaluateUnderstoryThicketDensity(
                        sample.WorldPosition,
                        rule,
                        seed);
                }

                if (GetStablePositionValue(
                        sample.WorldPosition,
                        seed,
                        ruleIndex + 7919) > density)
                {
                    report.RejectedByNaturalTreeDensity++;
                    return false;
                }
            }

            float keepProbability = GetMaskKeepProbability(
                sample.WorldPosition,
                entry.outputMode,
                context.exclusions,
                context.paths,
                context.splines,
                out bool rejectedByExclusion,
                out bool rejectedByPath);

            if (keepProbability <= 0f ||
                context.random.NextDouble() > keepProbability)
            {
                if (rejectedByExclusion)
                {
                    report.RejectedByExclusionVolume++;
                }

                if (rejectedByPath)
                {
                    report.RejectedByPathClearance++;
                }

                return false;
            }

            if (entry.outputMode == TerrainFoliageOutputMode.TerrainDetail)
            {
                if (!context.detailLayers.TryGetValue(
                        entry,
                        out int detailLayer))
                {
                    report.MissingExistingDetailSelections++;
                    return false;
                }

                report.TerrainDetails += AddTerrainDetail(
                    context.terrainData,
                    context.terrainPosition,
                    sample.WorldPosition,
                    context.detailMaps[detailLayer],
                    entry,
                    rule,
                    slope);
            }
            else if (entry.outputMode == TerrainFoliageOutputMode.InstancedGrass)
            {
                int grassAdded = AddManagedGrassPlacements(
                    context,
                    entry,
                    rule,
                    ruleIndex,
                    sample);

                report.InstancedGrass += grassAdded;
                if (grassAdded == 0)
                    return false;
            }
            else if (entry.outputMode == TerrainFoliageOutputMode.TerrainTree)
            {
                if (!context.treeLayers.TryGetValue(
                        entry,
                        out int treePrototypeIndex))
                {
                    report.MissingExistingTreeSelections++;
                    return false;
                }

                context.generatedTrees.Add(CreateTreeInstance(
                    context.terrainData,
                    context.terrainPosition,
                    sample.WorldPosition,
                    treePrototypeIndex,
                    rule,
                    entry,
                    context.random));
                report.TerrainTrees++;
            }
            else if (entry.gameObjectStorageMode ==
                     TerrainGameObjectStorageMode.RuntimeStreamed)
            {
                if (runtimePlacementData == null)
                {
                    report.MissingRuntimePlacementData++;
                    return false;
                }

                AddRuntimePlacement(
                    context.sampler,
                    entry,
                    rule,
                    sample,
                    context.random);
                report.RuntimeStreamedPlacements++;
            }
            else
            {
                context.generatedRoot ??= CreateGeneratedRoot();
                Transform ruleParent = GetOrCreateRuleParent(
                    context.generatedRoot,
                    rule,
                    context.ruleParents);
                SpawnPrefab(
                    context.sampler,
                    entry,
                    rule,
                    sample,
                    ruleParent,
                    context.random);
                report.GameObjects++;
            }

            report.Spawned++;

            // Detail painting writes density into TerrainData and can cover a
            // broad area with one sample. It should not consume the safety cap
            // intended for discrete trees and GameObjects.
            if (entry.outputMode != TerrainFoliageOutputMode.TerrainDetail &&
                entry.outputMode != TerrainFoliageOutputMode.InstancedGrass)
            {
                report.CapCountedInstances++;
                if (report.CapCountedInstances >= maximumInstances)
                {
                    report.HitMaximumInstances = true;
                    return true;
                }
            }

            return false;
        }

        private static float GetStableGridValue(
            float cellOriginX,
            float cellOriginZ,
            int ruleIndex,
            int candidateIndex,
            int generationSeed,
            int salt)
        {
            unchecked
            {
                int hash = generationSeed;
                hash = hash * 397 ^ Mathf.RoundToInt(cellOriginX * 10f);
                hash = hash * 397 ^ Mathf.RoundToInt(cellOriginZ * 10f);
                hash = hash * 397 ^ ruleIndex;
                hash = hash * 397 ^ candidateIndex;
                hash = hash * 397 ^ salt;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;

                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        public static float EvaluateNaturalTreeDensity(
            Vector3 worldPosition,
            TerrainFoliageRule rule,
            int generationSeed)
        {
            if (rule == null ||
                !rule.useNaturalTreeDensityVariation)
            {
                return 1f;
            }

            float patchSize =
                Mathf.Max(25f, rule.naturalTreePatchSize);

            float seedOffsetX =
                (generationSeed & 0xffff) * 0.137f + 173.31f;
            float seedOffsetZ =
                ((generationSeed >> 8) & 0xffff) * 0.193f + 419.73f;

            float warpScale = patchSize * 2.4f;
            float warpAmount =
                patchSize *
                Mathf.Clamp01(rule.naturalTreeDomainWarp) *
                0.55f;

            float warpX =
                (Mathf.PerlinNoise(
                    (worldPosition.x + seedOffsetX + 911.7f) /
                        warpScale,
                    (worldPosition.z + seedOffsetZ + 271.9f) /
                        warpScale) - 0.5f) *
                2f * warpAmount;

            float warpZ =
                (Mathf.PerlinNoise(
                    (worldPosition.x + seedOffsetX - 337.2f) /
                        warpScale,
                    (worldPosition.z + seedOffsetZ + 743.6f) /
                        warpScale) - 0.5f) *
                2f * warpAmount;

            float warpedX = worldPosition.x + warpX + seedOffsetX;
            float warpedZ = worldPosition.z + warpZ + seedOffsetZ;

            float broadDensity =
                Mathf.PerlinNoise(
                    warpedX / patchSize,
                    warpedZ / patchSize);

            float mediumPatchSize = patchSize * 0.43f;
            float mediumDensity =
                Mathf.PerlinNoise(
                    (warpedX + 613.4f) / mediumPatchSize,
                    (warpedZ - 289.1f) / mediumPatchSize);

            float combinedDensity =
                broadDensity * 0.74f +
                mediumDensity * 0.26f;

            float contrast =
                Mathf.Clamp01(rule.naturalTreeDensityContrast);
            float lowerEdge = Mathf.Lerp(0f, 0.46f, contrast);
            float upperEdge = Mathf.Lerp(1f, 0.54f, contrast);
            float shapedDensity =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        lowerEdge,
                        upperEdge,
                        combinedDensity));

            return Mathf.Lerp(
                Mathf.Clamp01(rule.minimumNaturalTreeDensity),
                Mathf.Clamp(
                    rule.maximumNaturalTreeDensity,
                    rule.minimumNaturalTreeDensity,
                    1f),
                shapedDensity);
        }

        public static float EvaluateCliffFormationDensity(
            TerrainSampler sampler,
            TerrainSample sample,
            TerrainFoliageRule rule,
            int generationSeed)
        {
            if (rule == null || !rule.useCliffFormationDistribution)
                return 1f;

            if (sampler == null)
                return 0f;

            float slope = Vector3.Angle(sample.WorldNormal, Vector3.up);
            float slopeDensity = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    rule.cliffMinimumSlope,
                    Mathf.Max(
                        rule.cliffMinimumSlope + 0.1f,
                        rule.cliffFullDensitySlope),
                    slope));

            if (slopeDensity <= 0f)
                return 0f;

            float reliefRadius = Mathf.Max(0.5f, rule.cliffReliefRadius);
            float maximumDownhillDrop = 0f;

            for (int i = 0; i < CliffReliefDirections.Length; i++)
            {
                Vector2 direction = CliffReliefDirections[i];
                float sampleX =
                    sample.WorldPosition.x + direction.x * reliefRadius;
                float sampleZ =
                    sample.WorldPosition.z + direction.y * reliefRadius;

                if (!sampler.ContainsWorldXZ(sampleX, sampleZ))
                    continue;

                float downhillDrop =
                    sample.WorldPosition.y -
                    sampler.SampleWorldHeight(sampleX, sampleZ);

                maximumDownhillDrop = Mathf.Max(
                    maximumDownhillDrop,
                    downhillDrop);
            }

            float reliefDensity = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    Mathf.Max(0f, rule.cliffMinimumHeightDrop),
                    Mathf.Max(
                        rule.cliffMinimumHeightDrop + 0.01f,
                        rule.cliffFullHeightDrop),
                    maximumDownhillDrop));

            if (reliefDensity <= 0f)
                return 0f;

            float patchSize = Mathf.Max(5f, rule.cliffPatchSize);
            float seedOffsetX =
                (generationSeed & 0xffff) * 0.173f + 2341.71f;
            float seedOffsetZ =
                ((generationSeed >> 8) & 0xffff) * 0.227f + 917.43f;
            float warpScale = patchSize * 2.25f;
            float warpAmount =
                patchSize *
                Mathf.Clamp01(rule.cliffPatchDomainWarp) *
                0.5f;

            float warpX =
                (Mathf.PerlinNoise(
                    (sample.WorldPosition.x + seedOffsetX + 417.3f) /
                        warpScale,
                    (sample.WorldPosition.z + seedOffsetZ - 731.9f) /
                        warpScale) - 0.5f) *
                2f * warpAmount;

            float warpZ =
                (Mathf.PerlinNoise(
                    (sample.WorldPosition.x + seedOffsetX - 619.7f) /
                        warpScale,
                    (sample.WorldPosition.z + seedOffsetZ + 283.1f) /
                        warpScale) - 0.5f) *
                2f * warpAmount;

            float warpedX = sample.WorldPosition.x + seedOffsetX + warpX;
            float warpedZ = sample.WorldPosition.z + seedOffsetZ + warpZ;
            float broad = Mathf.PerlinNoise(
                warpedX / patchSize,
                warpedZ / patchSize);
            float detail = Mathf.PerlinNoise(
                (warpedX + 541.7f) / (patchSize * 0.47f),
                (warpedZ - 193.4f) / (patchSize * 0.47f));
            float combined = broad * 0.8f + detail * 0.2f;
            float contrast = Mathf.Clamp01(rule.cliffPatchContrast);
            float lowerEdge = Mathf.Lerp(0f, 0.44f, contrast);
            float upperEdge = Mathf.Lerp(1f, 0.56f, contrast);
            float shaped = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(lowerEdge, upperEdge, combined));
            float patchDensity = Mathf.Lerp(
                Mathf.Clamp01(rule.minimumCliffPatchDensity),
                Mathf.Clamp(
                    rule.maximumCliffPatchDensity,
                    rule.minimumCliffPatchDensity,
                    1f),
                shaped);

            return Mathf.Clamp01(
                slopeDensity * reliefDensity * patchDensity);
        }

        public static float EvaluateUnderstoryThicketDensity(
            Vector3 worldPosition,
            TerrainFoliageRule rule,
            int generationSeed)
        {
            if (rule == null || !rule.useUnderstoryThickets)
                return 1f;

            float patchSize =
                Mathf.Max(6f, rule.understoryThicketSize);

            float seedOffsetX =
                (generationSeed & 0xffff) * 0.211f + 1289.17f;
            float seedOffsetZ =
                ((generationSeed >> 8) & 0xffff) * 0.157f + 2671.43f;

            float warpScale = patchSize * 2.1f;
            float warpAmount =
                patchSize *
                Mathf.Clamp01(rule.understoryThicketDomainWarp) *
                0.48f;

            float warpX =
                (Mathf.PerlinNoise(
                    (worldPosition.x + seedOffsetX + 367.2f) / warpScale,
                    (worldPosition.z + seedOffsetZ - 811.4f) / warpScale) -
                 0.5f) * 2f * warpAmount;

            float warpZ =
                (Mathf.PerlinNoise(
                    (worldPosition.x + seedOffsetX - 593.8f) / warpScale,
                    (worldPosition.z + seedOffsetZ + 229.6f) / warpScale) -
                 0.5f) * 2f * warpAmount;

            float warpedX = worldPosition.x + seedOffsetX + warpX;
            float warpedZ = worldPosition.z + seedOffsetZ + warpZ;

            float broad = Mathf.PerlinNoise(
                warpedX / patchSize,
                warpedZ / patchSize);

            float fine = Mathf.PerlinNoise(
                (warpedX + 431.9f) / (patchSize * 0.46f),
                (warpedZ - 197.3f) / (patchSize * 0.46f));

            float combined = broad * 0.78f + fine * 0.22f;
            float contrast =
                Mathf.Clamp01(rule.understoryThicketContrast);
            float lowerEdge = Mathf.Lerp(0f, 0.45f, contrast);
            float upperEdge = Mathf.Lerp(1f, 0.55f, contrast);
            float shaped = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(lowerEdge, upperEdge, combined));

            return Mathf.Lerp(
                Mathf.Clamp01(rule.minimumUnderstoryThicketDensity),
                Mathf.Clamp(
                    rule.maximumUnderstoryThicketDensity,
                    rule.minimumUnderstoryThicketDensity,
                    1f),
                shaped);
        }

        private static float GetStablePositionValue(
            Vector3 worldPosition,
            int generationSeed,
            int salt)
        {
            unchecked
            {
                int hash = generationSeed;
                hash = hash * 397 ^
                       Mathf.RoundToInt(worldPosition.x * 10f);
                hash = hash * 397 ^
                       Mathf.RoundToInt(worldPosition.z * 10f);
                hash = hash * 397 ^ salt;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;

                return
                    (hash & 0x7fffffff) /
                    (float)int.MaxValue;
            }
        }

        public GenerationEstimate GetGenerationEstimate()
        {
            GenerationEstimate estimate = new GenerationEstimate();
            if (terrain == null || terrain.terrainData == null || sampleDistance <= 0f)
                return estimate;

            TerrainData data = terrain.terrainData;
            estimate.Columns = Mathf.CeilToInt(data.size.x / sampleDistance);
            estimate.Rows = Mathf.CeilToInt(data.size.z / sampleDistance);
            estimate.GridCells = estimate.Columns * estimate.Rows;
            int evaluationsPerCell = 0;
            if (rules != null)
            {
                foreach (TerrainFoliageRule rule in rules)
                {
                    int placementCandidates =
                        rule != null && rule.useCliffFormationDistribution
                            ? Mathf.Clamp(rule.cliffSamplesPerCell, 1, 6)
                            : 1;
                    int detailCandidates = rule != null
                        ? Mathf.Clamp(
                            rule.additionalTerrainDetailSamplesPerCell,
                            0,
                            4)
                        : 0;
                    evaluationsPerCell +=
                        placementCandidates + detailCandidates;
                }
            }
            estimate.RuleEvaluations =
                estimate.GridCells * evaluationsPerCell;
            estimate.DetailResolution = data.detailResolution;

            HashSet<GameObject> details = new HashSet<GameObject>();
            int treeEntries = 0;
            int gameObjectEntries = 0;
            int grassEntries = 0;
            if (rules != null)
            {
                foreach (TerrainFoliageRule rule in rules)
                {
                    if (rule == null || rule.prefabEntries == null) continue;
                    foreach (TerrainFoliagePrefabEntry entry in rule.prefabEntries)
                    {
                        if (entry == null || entry.prefab == null || entry.weight <= 0f) continue;
                        if (entry.outputMode == TerrainFoliageOutputMode.TerrainDetail) details.Add(entry.prefab);
                        else if (entry.outputMode == TerrainFoliageOutputMode.TerrainTree) treeEntries++;
                        else if (entry.outputMode == TerrainFoliageOutputMode.InstancedGrass) grassEntries++;
                        else gameObjectEntries++;
                    }
                }
            }
            estimate.DetailLayersWritten = details.Count;
            estimate.TreeEntries = treeEntries;
            estimate.GameObjectEntries = gameObjectEntries;
            estimate.InstancedGrassEntries = grassEntries;
            return estimate;
        }

        private Dictionary<TerrainFoliageRule, int> BuildTerrainLayerIndexCache(TerrainSampler sampler)
        {
            Dictionary<TerrainFoliageRule, int> cache = new Dictionary<TerrainFoliageRule, int>();
            if (rules == null) return cache;
            foreach (TerrainFoliageRule rule in rules)
                if (rule != null && !cache.ContainsKey(rule))
                    cache.Add(rule, rule.terrainLayer != null ? sampler.FindLayerIndex(rule.terrainLayer) : -1);
            return cache;
        }

        private List<TerrainFoliageExclusionVolume> GetActiveExclusionVolumes()
        {
            if (automaticallyFindSceneMasks)
                return new List<TerrainFoliageExclusionVolume>(
                    FindObjectsByType<TerrainFoliageExclusionVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            return exclusionVolumes != null
                ? exclusionVolumes.FindAll(item => item != null && item.isActiveAndEnabled)
                : new List<TerrainFoliageExclusionVolume>();
        }

        private List<TerrainFoliagePathClearance> GetActivePathClearances()
        {
            if (automaticallyFindSceneMasks)
                return new List<TerrainFoliagePathClearance>(
                    FindObjectsByType<TerrainFoliagePathClearance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            return pathClearances != null
                ? pathClearances.FindAll(item => item != null && item.isActiveAndEnabled)
                : new List<TerrainFoliagePathClearance>();
        }

        private List<TerrainFoliageSplineClearance> GetActiveSplineClearances()
        {
            List<TerrainFoliageSplineClearance> result = automaticallyFindSceneMasks
                ? new List<TerrainFoliageSplineClearance>(
                    FindObjectsByType<TerrainFoliageSplineClearance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                : (splineClearances != null
                    ? splineClearances.FindAll(item => item != null && item.isActiveAndEnabled)
                    : new List<TerrainFoliageSplineClearance>());

            for (int i = 0; i < result.Count; i++)
                result[i]?.RebuildCache();
            return result;
        }

        private static float GetMaskKeepProbability(
            Vector3 worldPosition,
            TerrainFoliageOutputMode mode,
            IReadOnlyList<TerrainFoliageExclusionVolume> exclusions,
            IReadOnlyList<TerrainFoliagePathClearance> paths,
            IReadOnlyList<TerrainFoliageSplineClearance> splines,
            out bool affectedByExclusion,
            out bool affectedByPath)
        {
            float keep = 1f;
            affectedByExclusion = false;
            affectedByPath = false;

            for (int i = 0; i < exclusions.Count; i++)
            {
                TerrainFoliageExclusionVolume volume = exclusions[i];
                if (volume == null || !volume.Affects(mode)) continue;
                float value = volume.GetKeepProbability(worldPosition);
                if (value < 0.9999f) affectedByExclusion = true;
                keep = Mathf.Min(keep, value);
            }

            for (int i = 0; i < paths.Count; i++)
            {
                TerrainFoliagePathClearance path = paths[i];
                if (path == null || !path.Affects(mode)) continue;
                float value = path.GetKeepProbability(worldPosition);
                if (value < 0.9999f) affectedByPath = true;
                keep = Mathf.Min(keep, value);
            }

            for (int i = 0; i < splines.Count; i++)
            {
                TerrainFoliageSplineClearance spline = splines[i];
                if (spline == null || !spline.Affects(mode)) continue;
                float value = spline.GetKeepProbability(worldPosition);
                if (value < 0.9999f) affectedByPath = true;
                keep = Mathf.Min(keep, value);
            }

            return keep;
        }

        public PrototypeSyncReport SynchroniseRequiredPrototypes(bool autoFindFromRules = false)
        {
            bool discoverFromRules = autoFindFromRules || autoFindPrototypesFromRules;
            PrototypeSyncReport report = new PrototypeSyncReport();
            List<Terrain> targets = GetPrototypeTargetTerrains();
            HashSet<GameObject> requiredDetails = new HashSet<GameObject>();
            HashSet<GameObject> requiredTrees = new HashSet<GameObject>();

            CollectRequiredPrototypePrefabs(requiredDetails, requiredTrees);
            report.TargetTerrains = targets.Count;
            report.RequiredDetailPrefabs = requiredDetails.Count;
            report.RequiredTreePrefabs = requiredTrees.Count;

            TerrainData libraryData = prototypeLibrary != null
                ? prototypeLibrary.TemplateTerrainData
                : null;

            TerrainData fallbackData = terrain != null ? terrain.terrainData : null;

            for (int i = 0; i < targets.Count; i++)
            {
                Terrain target = targets[i];
                if (target == null || target.terrainData == null)
                {
                    report.InvalidTerrains++;
                    continue;
                }

                TerrainData data = target.terrainData;
                bool changed = false;

                foreach (GameObject prefab in requiredDetails)
                {
                    if (FindDetailPrototypeIndex(data.detailPrototypes, prefab) >= 0)
                        continue;

                    DetailPrototype source = FindDetailPrototype(
                        libraryData, fallbackData, prefab,
                        prototypeLibrary == null || prototypeLibrary.AllowSpawnerTerrainFallback);

                    if (source == null && discoverFromRules)
                    {
                        for (int t = 0; t < targets.Count && source == null; t++)
                            if (targets[t] != null) source = FindDetailPrototype(targets[t].terrainData, prefab);
                        if (source == null)
                        {
                            source = CreateDetailPrototype(prefab);
                            if (!source.Validate(out string error))
                            {
                                report.MissingDetailSources++;
                                report.AddMissing($"Detail '{prefab.name}' cannot be registered: {error}");
                                continue;
                            }
                            report.DetailDefaultsCreated++;
                        }
                    }

                    if (source == null)
                    {
                        report.MissingDetailSources++;
                        report.AddMissing($"Detail '{prefab.name}' has no source prototype in the library/template TerrainData.");
                        continue;
                    }

                    List<DetailPrototype> list = new List<DetailPrototype>(
                        data.detailPrototypes ?? Array.Empty<DetailPrototype>());
                    list.Add(ClonePrototype(source));
                    data.detailPrototypes = list.ToArray();
                    report.DetailPrototypesAdded++;
                    changed = true;
                }

                foreach (GameObject prefab in requiredTrees)
                {
                    if (FindTreePrototypeIndex(data.treePrototypes, prefab) >= 0)
                        continue;

                    TreePrototype source = FindTreePrototype(
                        libraryData, fallbackData, prefab,
                        prototypeLibrary == null || prototypeLibrary.AllowSpawnerTerrainFallback);

                    if (source == null && discoverFromRules)
                    {
                        for (int t = 0; t < targets.Count && source == null; t++)
                            if (targets[t] != null) source = FindTreePrototype(targets[t].terrainData, prefab);
                        if (source == null)
                        {
                            if (prefab.GetComponentInChildren<Renderer>(true) == null)
                            {
                                report.MissingTreeSources++;
                                report.AddMissing($"Tree '{prefab.name}' has no Renderer. Assign a renderable tree prefab in the rule.");
                                continue;
                            }
                            source = new TreePrototype { prefab = prefab, bendFactor = 0f };
                            report.TreeDefaultsCreated++;
                        }
                    }

                    if (source == null)
                    {
                        report.MissingTreeSources++;
                        report.AddMissing($"Tree '{prefab.name}' has no source prototype in the library/template TerrainData.");
                        continue;
                    }

                    List<TreePrototype> list = new List<TreePrototype>(
                        data.treePrototypes ?? Array.Empty<TreePrototype>());
                    list.Add(ClonePrototype(source));
                    data.treePrototypes = list.ToArray();
                    report.TreePrototypesAdded++;
                    changed = true;
                }

                if (changed)
                {
                    data.RefreshPrototypes();
                    target.Flush();
                    report.TerrainsChanged++;
                }
            }

            return report;
        }

        public List<Terrain> GetPrototypeTargetTerrains()
        {
            List<Terrain> result = new List<Terrain>();
            HashSet<Terrain> seen = new HashSet<Terrain>();

            void Add(Terrain item)
            {
                if (item != null && seen.Add(item)) result.Add(item);
            }

            Add(terrain);

            if (automaticallyFindActiveTerrains)
            {
                Terrain[] active = Terrain.activeTerrains;
                if (active != null)
                    for (int i = 0; i < active.Length; i++) Add(active[i]);
            }

            if (prototypeTargetTerrains != null)
                for (int i = 0; i < prototypeTargetTerrains.Count; i++)
                    Add(prototypeTargetTerrains[i]);

            return result;
        }

        private void CollectRequiredPrototypePrefabs(
            HashSet<GameObject> details,
            HashSet<GameObject> trees)
        {
            if (rules == null) return;

            for (int r = 0; r < rules.Count; r++)
            {
                TerrainFoliageRule rule = rules[r];
                if (rule == null) continue;
                rule.EnsureMigrated();

                if (rule.prefabEntries == null) continue;
                for (int e = 0; e < rule.prefabEntries.Count; e++)
                {
                    TerrainFoliagePrefabEntry entry = rule.prefabEntries[e];
                    if (entry == null || entry.prefab == null || entry.weight <= 0f) continue;

                    if (entry.outputMode == TerrainFoliageOutputMode.TerrainDetail)
                        details.Add(entry.prefab);
                    else if (entry.outputMode == TerrainFoliageOutputMode.TerrainTree)
                        trees.Add(entry.prefab);
                }
            }
        }

        private static DetailPrototype CreateDetailPrototype(GameObject prefab)
        {
            return new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = .8f,
                maxWidth = 1.2f,
                minHeight = .8f,
                maxHeight = 1.2f,
                noiseSpread = .2f,
                healthyColor = Color.white,
                dryColor = Color.white
            };
        }

        private static DetailPrototype FindDetailPrototype(
            TerrainData primary,
            TerrainData fallback,
            GameObject prefab,
            bool allowFallback)
        {
            DetailPrototype found = FindDetailPrototype(primary, prefab);
            return found ?? (allowFallback ? FindDetailPrototype(fallback, prefab) : null);
        }

        private static DetailPrototype FindDetailPrototype(TerrainData data, GameObject prefab)
        {
            if (data == null) return null;
            DetailPrototype[] prototypes = data.detailPrototypes;
            int index = FindDetailPrototypeIndex(prototypes, prefab);
            return index >= 0 ? prototypes[index] : null;
        }

        private static TreePrototype FindTreePrototype(
            TerrainData primary,
            TerrainData fallback,
            GameObject prefab,
            bool allowFallback)
        {
            TreePrototype found = FindTreePrototype(primary, prefab);
            return found ?? (allowFallback ? FindTreePrototype(fallback, prefab) : null);
        }

        private static TreePrototype FindTreePrototype(TerrainData data, GameObject prefab)
        {
            if (data == null) return null;
            TreePrototype[] prototypes = data.treePrototypes;
            int index = FindTreePrototypeIndex(prototypes, prefab);
            return index >= 0 ? prototypes[index] : null;
        }

        private static T ClonePrototype<T>(T source) where T : class, new()
        {
            if (source == null) return null;
            T clone = new T();
            Type type = typeof(T);

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsInitOnly) field.SetValue(clone, field.GetValue(source));
            }

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                    continue;
                try { property.SetValue(clone, property.GetValue(source)); }
                catch { }
            }

            return clone;
        }

        [Serializable]
        public sealed class PrototypeSyncReport
        {
            public int TargetTerrains;
            public int TerrainsChanged;
            public int InvalidTerrains;
            public int RequiredDetailPrefabs;
            public int RequiredTreePrefabs;
            public int DetailPrototypesAdded;
            public int TreePrototypesAdded;
            public int DetailDefaultsCreated;
            public int TreeDefaultsCreated;
            public int MissingDetailSources;
            public int MissingTreeSources;
            [SerializeField] private List<string> missingSources = new List<string>();

            public bool HasErrors => MissingDetailSources > 0 || MissingTreeSources > 0;
            public IReadOnlyList<string> MissingSources => missingSources;

            public void AddMissing(string message)
            {
                if (!missingSources.Contains(message)) missingSources.Add(message);
            }

            public string ToSummary()
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine($"Prototype target terrains: {TargetTerrains:N0}");
                builder.AppendLine($"Terrains changed: {TerrainsChanged:N0}");
                builder.AppendLine($"Unique prefabs found in rules: {RequiredDetailPrefabs:N0} details, {RequiredTreePrefabs:N0} trees");
                builder.AppendLine($"Detail prototypes added: {DetailPrototypesAdded:N0}");
                builder.AppendLine($"Tree prototypes added: {TreePrototypesAdded:N0}");
                builder.AppendLine($"New default settings created: {DetailDefaultsCreated:N0} details, {TreeDefaultsCreated:N0} trees");
                if (RequiredDetailPrefabs == 0 && RequiredTreePrefabs == 0)
                    builder.AppendLine("No Terrain Detail or Terrain Tree entries found. GameObject and Managed Instanced Grass entries do not need Terrain prototypes.");
                if (InvalidTerrains > 0) builder.AppendLine($"Invalid terrains: {InvalidTerrains:N0}");
                if (missingSources.Count > 0)
                {
                    builder.AppendLine("Missing prototype sources:");
                    for (int i = 0; i < missingSources.Count; i++)
                        builder.AppendLine($"- {missingSources[i]}");
                }
                return builder.ToString().TrimEnd();
            }
        }

        public void Clear()
        {
            ClearGeneratedObjects();
            ClearManagedTerrainDetails();
            ClearManagedTerrainTrees();

            if (runtimePlacementData != null)
                runtimePlacementData.ClearData();

            TerrainFoliageRuntimeStreamer streamer =
                GetComponent<TerrainFoliageRuntimeStreamer>();
            if (streamer != null)
                streamer.PlacementData = runtimePlacementData;

            TerrainFoliageGrassRenderer grassRenderer =
                GetComponent<TerrainFoliageGrassRenderer>();
            if (grassRenderer != null)
                grassRenderer.PlacementData = runtimePlacementData;
        }

        private void ClearGeneratedObjects()
        {
            Transform existingRoot = transform.Find(generatedRootName);

            if (existingRoot == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existingRoot.gameObject);
            }
            else
            {
                DestroyImmediate(existingRoot.gameObject);
            }
        }

        private void ClearManagedTerrainDetails()
        {
            if (terrain == null ||
                terrain.terrainData == null ||
                managedDetailPrefabs == null ||
                managedDetailPrefabs.Count == 0)
            {
                return;
            }

            TerrainData terrainData = terrain.terrainData;
            DetailPrototype[] prototypes = terrainData.detailPrototypes;
            int resolution = terrainData.detailResolution;

            if (resolution <= 0 || prototypes == null)
            {
                return;
            }

            int[,] emptyMap = new int[resolution, resolution];

            for (int managedIndex = 0;
                 managedIndex < managedDetailPrefabs.Count;
                 managedIndex++)
            {
                GameObject managedPrefab =
                    managedDetailPrefabs[managedIndex];

                int prototypeIndex =
                    FindDetailPrototypeIndex(prototypes, managedPrefab);

                if (prototypeIndex >= 0)
                {
                    terrainData.SetDetailLayer(
                        0,
                        0,
                        prototypeIndex,
                        emptyMap);
                }
            }

            managedDetailPrefabs.Clear();
        }


        private void ClearManagedTerrainTrees()
        {
            if (terrain == null || terrain.terrainData == null ||
                managedTreePrefabs == null || managedTreePrefabs.Count == 0)
                return;

            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            TreeInstance[] instances = data.treeInstances;
            HashSet<int> managed = new HashSet<int>();

            for (int i = 0; i < managedTreePrefabs.Count; i++)
            {
                int index = FindTreePrototypeIndex(prototypes, managedTreePrefabs[i]);
                if (index >= 0) managed.Add(index);
            }

            List<TreeInstance> remaining = new List<TreeInstance>();
            for (int i = 0; i < instances.Length; i++)
                if (!managed.Contains(instances[i].prototypeIndex)) remaining.Add(instances[i]);

            data.treeInstances = remaining.ToArray();
            managedTreePrefabs.Clear();
        }

        private Dictionary<TerrainFoliagePrefabEntry, int> FindExistingTreeLayers(
            TerrainData terrainData, GenerationReport report)
        {
            Dictionary<TerrainFoliagePrefabEntry, int> result =
                new Dictionary<TerrainFoliagePrefabEntry, int>();
            TreePrototype[] prototypes = terrainData.treePrototypes ?? Array.Empty<TreePrototype>();
            managedTreePrefabs ??= new List<GameObject>();

            for (int r = 0; r < rules.Count; r++)
            {
                TerrainFoliageRule rule = rules[r];
                if (rule == null || rule.prefabEntries == null) continue;
                rule.EnsureMigrated();
                for (int e = 0; e < rule.prefabEntries.Count; e++)
                {
                    TerrainFoliagePrefabEntry entry = rule.prefabEntries[e];
                    if (entry == null || entry.prefab == null || entry.weight <= 0f ||
                        entry.outputMode != TerrainFoliageOutputMode.TerrainTree) continue;

                    int index = FindTreePrototypeIndex(prototypes, entry.prefab);
                    if (index < 0)
                    {
                        report.UnregisteredTreePrefabs++;
                        if (!report.UnregisteredTreePrefabNames.Contains(entry.prefab.name))
                            report.UnregisteredTreePrefabNames.Add(entry.prefab.name);
                        continue;
                    }
                    result[entry] = index;
                    if (!managedTreePrefabs.Contains(entry.prefab)) managedTreePrefabs.Add(entry.prefab);
                }
            }
            return result;
        }

        private Dictionary<TerrainFoliagePrefabEntry, int>
            FindExistingDetailLayers(
                TerrainData terrainData,
                GenerationReport report)
        {
            Dictionary<TerrainFoliagePrefabEntry, int> result =
                new Dictionary<TerrainFoliagePrefabEntry, int>();

            DetailPrototype[] prototypes =
                terrainData.detailPrototypes ??
                Array.Empty<DetailPrototype>();

            managedDetailPrefabs ??= new List<GameObject>();

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                TerrainFoliageRule rule = rules[ruleIndex];

                if (rule == null || rule.prefabEntries == null)
                {
                    continue;
                }

                rule.EnsureMigrated();

                for (int entryIndex = 0;
                     entryIndex < rule.prefabEntries.Count;
                     entryIndex++)
                {
                    TerrainFoliagePrefabEntry entry =
                        rule.prefabEntries[entryIndex];

                    if (entry == null ||
                        entry.prefab == null ||
                        entry.weight <= 0f ||
                        entry.outputMode !=
                        TerrainFoliageOutputMode.TerrainDetail)
                    {
                        continue;
                    }

                    int prototypeIndex =
                        FindDetailPrototypeIndex(
                            prototypes,
                            entry.prefab);

                    if (prototypeIndex < 0)
                    {
                        report.UnregisteredDetailPrefabs++;

                        if (!report.UnregisteredDetailPrefabNames.Contains(
                                entry.prefab.name))
                        {
                            report.UnregisteredDetailPrefabNames.Add(
                                entry.prefab.name);
                        }

                        continue;
                    }

                    result[entry] = prototypeIndex;

                    if (!managedDetailPrefabs.Contains(entry.prefab))
                    {
                        managedDetailPrefabs.Add(entry.prefab);
                    }
                }
            }

            return result;
        }

        private static Dictionary<int, int[,]> CreateDetailMaps(
            TerrainData terrainData,
            IReadOnlyDictionary<TerrainFoliagePrefabEntry, int> layers)
        {
            Dictionary<int, int[,]> maps =
                new Dictionary<int, int[,]>();

            int resolution = terrainData.detailResolution;

            foreach (KeyValuePair<TerrainFoliagePrefabEntry, int> pair
                     in layers)
            {
                if (!maps.ContainsKey(pair.Value))
                {
                    maps.Add(
                        pair.Value,
                        new int[resolution, resolution]);
                }
            }

            return maps;
        }

        private static int AddTerrainDetail(
            TerrainData terrainData,
            Vector3 terrainPosition,
            Vector3 worldPosition,
            int[,] map,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            float slope)
        {
            int resolution = terrainData.detailResolution;

            float normalizedX = Mathf.InverseLerp(
                terrainPosition.x,
                terrainPosition.x + terrainData.size.x,
                worldPosition.x);

            float normalizedZ = Mathf.InverseLerp(
                terrainPosition.z,
                terrainPosition.z + terrainData.size.z,
                worldPosition.z);

            int centerX = Mathf.Clamp(
                Mathf.FloorToInt(normalizedX * resolution),
                0,
                resolution - 1);

            int centerZ = Mathf.Clamp(
                Mathf.FloorToInt(normalizedZ * resolution),
                0,
                resolution - 1);

            GetDetailConcentrationSettings(
                entry,
                out int radius,
                out float falloff);

            bool useMeadowExpansion =
                rule != null &&
                rule.useGentleGroundMeadows &&
                entry.allowMeadowExpansion &&
                slope <= rule.meadowMaximumSlope;

            if (useMeadowExpansion)
            {
                radius = Mathf.Clamp(
                    radius + rule.meadowRadiusBonus,
                    0,
                    16);

                // A slightly softer edge allows adjacent stamps to merge into
                // a field without producing obvious round clumps.
                falloff = Mathf.Max(falloff, 0.58f);
            }

            int requestedDensity = Mathf.Clamp(
                Mathf.RoundToInt(
                    entry.detailDensity *
                    (useMeadowExpansion
                        ? rule.meadowDensityMultiplier
                        : 1f)),
                1,
                16);
            int totalLogicalDensity = 0;

            for (int offsetZ = -radius; offsetZ <= radius; offsetZ++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int x = centerX + offsetX;
                    int z = centerZ + offsetZ;

                    if (x < 0 || x >= resolution ||
                        z < 0 || z >= resolution)
                    {
                        continue;
                    }

                    float normalizedDistance = radius <= 0
                        ? 0f
                        : Mathf.Clamp01(
                            Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ) /
                            (radius + 0.001f));

                    if (normalizedDistance > 1f)
                    {
                        continue;
                    }

                    float densityScale = Mathf.Lerp(
                        1f,
                        1f - normalizedDistance,
                        falloff);

                    int logicalContribution = Mathf.Clamp(
                        Mathf.RoundToInt(requestedDensity * densityScale),
                        1,
                        16);

                    int mapContribution;
                    int mapMaximum;

                    if (terrainData.detailScatterMode ==
                        DetailScatterMode.CoverageMode)
                    {
                        mapContribution = Mathf.RoundToInt(
                            logicalContribution / 16f * 255f);
                        mapMaximum = 255;
                    }
                    else
                    {
                        mapContribution = logicalContribution;
                        mapMaximum = 16;
                    }

                    int oldValue = map[z, x];
                    int newValue = Mathf.Clamp(
                        oldValue + mapContribution,
                        0,
                        mapMaximum);

                    map[z, x] = newValue;

                    if (newValue > oldValue)
                    {
                        totalLogicalDensity += logicalContribution;
                    }
                }
            }

            return totalLogicalDensity;
        }

        private static void GetDetailConcentrationSettings(
            TerrainFoliagePrefabEntry entry,
            out int radius,
            out float falloff)
        {
            switch (entry.detailConcentration)
            {
                case TerrainDetailConcentration.Dense:
                    radius = 1;
                    falloff = 0.2f;
                    break;

                case TerrainDetailConcentration.VeryDense:
                    radius = 2;
                    falloff = 0.15f;
                    break;

                case TerrainDetailConcentration.Custom:
                    radius = Mathf.Clamp(entry.customDetailRadius, 0, 8);
                    falloff = Mathf.Clamp01(entry.customDetailFalloff);
                    break;

                default:
                    radius = 0;
                    falloff = 0f;
                    break;
            }
        }

        private static void WriteDetailMaps(
            TerrainData terrainData,
            IReadOnlyDictionary<int, int[,]> detailMaps)
        {
            foreach (KeyValuePair<int, int[,]> pair in detailMaps)
            {
                terrainData.SetDetailLayer(
                    0,
                    0,
                    pair.Key,
                    pair.Value);
            }
        }

        public static int FindDetailPrototypeIndex(
            IReadOnlyList<DetailPrototype> prototypes,
            GameObject prefab)
        {
            if (prototypes == null || prefab == null)
            {
                return -1;
            }

            for (int i = 0; i < prototypes.Count; i++)
            {
                DetailPrototype prototype = prototypes[i];

                if (prototype != null &&
                    prototype.usePrototypeMesh &&
                    prototype.prototype == prefab)
                {
                    return i;
                }
            }

            return -1;
        }


        private static TreeInstance CreateTreeInstance(
            TerrainData data, Vector3 terrainPosition, Vector3 worldPosition,
            int prototypeIndex, TerrainFoliageRule rule,
            TerrainFoliagePrefabEntry entry, System.Random random)
        {
            Vector3 local = worldPosition - terrainPosition;
            float scale = RandomRange(random, rule.minimumScale, rule.maximumScale) *
                          entry.treeScaleMultiplier;
            return new TreeInstance
            {
                position = new Vector3(
                    Mathf.Clamp01(local.x / data.size.x),
                    Mathf.Clamp01(local.y / data.size.y),
                    Mathf.Clamp01(local.z / data.size.z)),
                prototypeIndex = prototypeIndex,
                widthScale = scale,
                heightScale = scale,
                rotation = entry.treeRandomRotation ?
                    RandomRange(random, 0f, Mathf.PI * 2f) : 0f,
                color = entry.treeColor,
                lightmapColor = entry.treeLightmapColor
            };
        }

        private static void AppendGeneratedTrees(
            TerrainData data, IReadOnlyList<TreeInstance> generated)
        {
            if (generated == null || generated.Count == 0) return;
            TreeInstance[] existing = data.treeInstances ?? Array.Empty<TreeInstance>();
            TreeInstance[] combined = new TreeInstance[existing.Length + generated.Count];
            Array.Copy(existing, combined, existing.Length);
            for (int i = 0; i < generated.Count; i++) combined[existing.Length + i] = generated[i];
            data.treeInstances = combined;
        }

        public static int FindTreePrototypeIndex(
            IReadOnlyList<TreePrototype> prototypes, GameObject prefab)
        {
            if (prototypes == null || prefab == null) return -1;
            for (int i = 0; i < prototypes.Count; i++)
                if (prototypes[i] != null && prototypes[i].prefab == prefab) return i;
            return -1;
        }

        private GenerationReport FinishReport(
            GenerationReport report,
            Transform generatedRoot)
        {
            if (report.GameObjects == 0 && generatedRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(generatedRoot.gameObject);
                }
            }

            return report;
        }

        private void ValidateForGeneration()
        {
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    "Assign a Terrain before generating foliage.");
            }

            if (terrain.terrainData == null)
            {
                throw new InvalidOperationException(
                    "The assigned Terrain does not have TerrainData.");
            }

            if (rules == null || rules.Count == 0)
            {
                throw new InvalidOperationException(
                    "Add at least one Terrain Foliage Rule.");
            }

            if (sampleDistance <= 0f)
            {
                throw new InvalidOperationException(
                    "Sample Distance must be greater than zero.");
            }

            if (HasTerrainDetailEntries() &&
                terrain.terrainData.detailResolution <= 0)
            {
                throw new InvalidOperationException(
                    "The Terrain Detail Resolution is zero. Configure Terrain " +
                    "details before using Terrain Detail output.");
            }
        }

        private bool HasTerrainDetailEntries()
        {
            if (rules == null)
            {
                return false;
            }

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                TerrainFoliageRule rule = rules[ruleIndex];

                if (rule == null || rule.prefabEntries == null)
                {
                    continue;
                }

                for (int entryIndex = 0;
                     entryIndex < rule.prefabEntries.Count;
                     entryIndex++)
                {
                    TerrainFoliagePrefabEntry entry =
                        rule.prefabEntries[entryIndex];

                    if (entry != null &&
                        entry.outputMode ==
                        TerrainFoliageOutputMode.TerrainDetail)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Transform CreateGeneratedRoot()
        {
            GameObject root = new GameObject(generatedRootName);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        private static Transform GetOrCreateRuleParent(
            Transform generatedRoot,
            TerrainFoliageRule rule,
            IDictionary<TerrainFoliageRule, Transform> ruleParents)
        {
            if (ruleParents.TryGetValue(
                    rule,
                    out Transform existingParent))
            {
                return existingParent;
            }

            GameObject parentObject = new GameObject(rule.name);
            parentObject.transform.SetParent(generatedRoot, false);
            ruleParents.Add(rule, parentObject.transform);
            return parentObject.transform;
        }

        private void AddRuntimePlacement(
            TerrainSampler sampler,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            TerrainSample sample,
            System.Random random)
        {
            CalculateGameObjectTransform(
                sampler,
                entry,
                rule,
                sample,
                random,
                out Vector3 position,
                out Quaternion rotation,
                out Vector3 scale);
            runtimePlacementData.Add(entry.prefab, position, rotation, scale);
        }

        private int AddManagedGrassPlacements(
            GenerationContext context,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            int ruleIndex,
            TerrainSample sourceSample)
        {
            if (runtimePlacementData == null)
            {
                context.report.MissingRuntimePlacementData++;
                return 0;
            }

            int terrainLayerIndex = context.terrainLayerIndices.TryGetValue(
                rule,
                out int cachedLayerIndex)
                ? cachedLayerIndex
                : -1;

            int added = 0;
            int count = Mathf.Clamp(entry.grassInstancesPerSample, 1, 16);
            float spread = Mathf.Max(0f, entry.grassSpread);

            for (int i = 0; i < count; i++)
            {
                float angle = RandomRange(context.random, 0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt((float)context.random.NextDouble()) * spread;
                float worldX = sourceSample.WorldPosition.x + Mathf.Cos(angle) * radius;
                float worldZ = sourceSample.WorldPosition.z + Mathf.Sin(angle) * radius;

                if (!context.sampler.ContainsWorldXZ(worldX, worldZ))
                    continue;

                TerrainSample sample = context.sampler.Sample(worldX, worldZ);
                if (terrainLayerIndex < 0 ||
                    sample.GetLayerWeight(terrainLayerIndex) < rule.minimumLayerWeight)
                {
                    continue;
                }

                float slope = Vector3.Angle(sample.WorldNormal, Vector3.up);
                if (rule.useSlopeLimit &&
                    (slope < rule.minimumSlope || slope > rule.maximumSlope))
                {
                    continue;
                }

                float keep = GetMaskKeepProbability(
                    sample.WorldPosition,
                    TerrainFoliageOutputMode.InstancedGrass,
                    context.exclusions,
                    context.paths,
                    context.splines,
                    out _,
                    out _);

                if (keep <= 0f || context.random.NextDouble() > keep)
                    continue;

                Quaternion rotation = entry.grassAlignToTerrainNormal
                    ? Quaternion.FromToRotation(Vector3.up, sample.WorldNormal)
                    : Quaternion.identity;

                if (entry.grassRandomYRotation)
                {
                    rotation *= Quaternion.AngleAxis(
                        RandomRange(context.random, 0f, 360f),
                        Vector3.up);
                }

                float ruleScale = RandomRange(
                    context.random,
                    rule.minimumScale,
                    rule.maximumScale);
                float width = RandomRange(
                    context.random,
                    entry.grassMinimumWidthScale,
                    entry.grassMaximumWidthScale) * ruleScale;
                float height = RandomRange(
                    context.random,
                    entry.grassMinimumHeightScale,
                    entry.grassMaximumHeightScale) * ruleScale;
                Vector3 scale = Vector3.Scale(
                    entry.prefab.transform.localScale,
                    new Vector3(width, height, width));
                Vector3 position = sample.WorldPosition +
                                   sample.WorldNormal * entry.grassGroundOffset;

                runtimePlacementData.AddGrass(
                    entry.prefab,
                    position,
                    rotation,
                    scale);
                added++;
            }

            return added;
        }

        private void EnsureManagedRenderers()
        {
            if (runtimePlacementData == null)
                return;

            TerrainFoliageRuntimeStreamer streamer =
                GetComponent<TerrainFoliageRuntimeStreamer>();
            if (createRuntimeStreamer && runtimePlacementData.Count > 0)
            {
                if (streamer == null)
                    streamer = gameObject.AddComponent<TerrainFoliageRuntimeStreamer>();
                streamer.PlacementData = runtimePlacementData;
            }
            else if (streamer != null)
            {
                streamer.PlacementData = runtimePlacementData;
            }

            TerrainFoliageGrassRenderer grassRenderer =
                GetComponent<TerrainFoliageGrassRenderer>();
            if (createManagedGrassRenderer && runtimePlacementData.GrassCount > 0)
            {
                if (grassRenderer == null)
                    grassRenderer = gameObject.AddComponent<TerrainFoliageGrassRenderer>();

                grassRenderer.Configure(
                    grassRenderDistance,
                    grassRenderChunkSize,
                    grassShadowCasting,
                    grassReceiveShadows,
                    grassRenderingLayer);
                grassRenderer.PlacementData = runtimePlacementData;
            }
            else if (grassRenderer != null)
            {
                grassRenderer.PlacementData = runtimePlacementData;
            }
        }

        private static void CalculateGameObjectTransform(
            TerrainSampler sampler,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            TerrainSample sample,
            System.Random random,
            out Vector3 worldPosition,
            out Quaternion rotation,
            out Vector3 scale)
        {
            if (rule.buildContinuousCliffFaces && entry.useForCliffFaces)
            {
                CalculateCliffFaceTransform(
                    sampler,
                    entry,
                    rule,
                    sample,
                    random,
                    out worldPosition,
                    out rotation,
                    out scale);
                return;
            }

            rotation = Quaternion.identity;
            if (entry.alignToTerrainNormal) rotation = Quaternion.FromToRotation(Vector3.up, sample.WorldNormal);
            if (entry.randomYRotation) rotation *= Quaternion.AngleAxis(RandomRange(random, 0f, 360f), Vector3.up);
            rotation *= Quaternion.Euler(entry.rotationOffset);
            worldPosition = sample.WorldPosition + rotation * entry.positionOffset;
            float randomScale = RandomRange(random, rule.minimumScale, rule.maximumScale);
            scale = Vector3.Scale(entry.prefab.transform.localScale, entry.scaleMultiplier * randomScale);
        }

        private static void CalculateCliffFaceTransform(
            TerrainSampler sampler,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            TerrainSample sample,
            System.Random random,
            out Vector3 worldPosition,
            out Quaternion rotation,
            out Vector3 scale)
        {
            Vector3 downhill = GetSteepestDownhillDirection(
                sampler,
                sample,
                Mathf.Max(0.5f, rule.cliffReliefRadius));
            if (downhill.sqrMagnitude < 0.001f)
            {
                downhill = Vector3.ProjectOnPlane(
                    -sample.WorldNormal,
                    Vector3.up).normalized;
            }
            if (downhill.sqrMagnitude < 0.001f)
                downhill = Vector3.forward;

            float yaw = RandomRange(
                random,
                -rule.cliffFaceYawJitter,
                rule.cliffFaceYawJitter);
            rotation = Quaternion.LookRotation(downhill, Vector3.up) *
                       Quaternion.AngleAxis(yaw, Vector3.up) *
                       Quaternion.Euler(entry.rotationOffset);
            worldPosition = sample.WorldPosition -
                            downhill * Mathf.Max(0f, rule.cliffFaceEmbedDepth) +
                            rotation * entry.positionOffset;

            Vector3 faceScale = new Vector3(
                RandomRange(
                    random,
                    rule.cliffFaceMinimumWidthScale,
                    rule.cliffFaceMaximumWidthScale),
                RandomRange(
                    random,
                    rule.cliffFaceMinimumHeightScale,
                    rule.cliffFaceMaximumHeightScale),
                RandomRange(
                    random,
                    rule.cliffFaceMinimumDepthScale,
                    rule.cliffFaceMaximumDepthScale));
            scale = Vector3.Scale(
                entry.prefab.transform.localScale,
                faceScale);
        }

        private static Vector3 GetSteepestDownhillDirection(
            TerrainSampler sampler,
            TerrainSample sample,
            float radius)
        {
            Vector3 bestDirection = Vector3.zero;
            float largestDrop = 0f;
            for (int i = 0; i < CliffReliefDirections.Length; i++)
            {
                Vector2 direction = CliffReliefDirections[i];
                float worldX = sample.WorldPosition.x + direction.x * radius;
                float worldZ = sample.WorldPosition.z + direction.y * radius;
                if (!sampler.ContainsWorldXZ(worldX, worldZ))
                    continue;

                float drop = sample.WorldPosition.y -
                             sampler.SampleWorldHeight(worldX, worldZ);
                if (drop <= largestDrop)
                    continue;

                largestDrop = drop;
                bestDirection = new Vector3(direction.x, 0f, direction.y);
            }

            return bestDirection.normalized;
        }

        private static void SpawnPrefab(
            TerrainSampler sampler,
            TerrainFoliagePrefabEntry entry,
            TerrainFoliageRule rule,
            TerrainSample sample,
            Transform parent,
            System.Random random)
        {
            CalculateGameObjectTransform(
                sampler,
                entry,
                rule,
                sample,
                random,
                out Vector3 worldPosition,
                out Quaternion rotation,
                out Vector3 scale);
            GameObject instance = Instantiate(entry.prefab, worldPosition, rotation, parent);
            instance.transform.localScale = scale;
        }

        private static float RandomRange(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private void Reset()
        {
            terrain = GetComponent<Terrain>();

            if (terrain == null)
            {
                terrain = FindAnyObjectByType<Terrain>();
            }
        }

        private void OnValidate()
        {
            sampleDistance = Mathf.Max(0.1f, sampleDistance);
            maximumInstances = Mathf.Max(1, maximumInstances);

            debugSampleNormalizedPosition.x =
                Mathf.Clamp01(debugSampleNormalizedPosition.x);

            debugSampleNormalizedPosition.y =
                Mathf.Clamp01(debugSampleNormalizedPosition.y);

            debugMarkerSize = Mathf.Max(0.05f, debugMarkerSize);

            previewGridResolution =
                Mathf.Clamp(previewGridResolution, 4, 64);

            previewMarkerSize = Mathf.Max(0.01f, previewMarkerSize);
            grassRenderDistance = Mathf.Max(5f, grassRenderDistance);
            grassRenderChunkSize = Mathf.Max(4f, grassRenderChunkSize);

            if (string.IsNullOrWhiteSpace(generatedRootName))
            {
                generatedRootName = "Generated Foliage";
            }
        }

        [Serializable]
        public sealed class GenerationEstimate
        {
            public int Columns;
            public int Rows;
            public int GridCells;
            public int RuleEvaluations;
            public int DetailResolution;
            public int DetailLayersWritten;
            public int TreeEntries;
            public int GameObjectEntries;
            public int InstancedGrassEntries;
        }

        [Serializable]
        public sealed class GenerationReport
        {
            public int GridCells;
            public int SampledPoints;
            public int OutsideTerrain;
            public int NullRules;
            public int MissingLayerAssignments;
            public int LayerNotOnTerrain;
            public int RejectedByLayerWeight;
            public int RejectedBySlope;
            public int RejectedByCliffFormation;
            public int RejectedByCliffFaceSpacing;
            public int RejectedBySpawnChance;
            public int RejectedByNaturalTreeDensity;
            public int RejectedByExclusionVolume;
            public int RejectedByPathClearance;
            public int MissingPrefabs;
            public int MissingExistingDetailSelections;
            public int MissingExistingTreeSelections;
            public int UnregisteredDetailPrefabs;
            public int UnregisteredTreePrefabs;
            public int Spawned;
            public int CapCountedInstances;
            public int GameObjects;
            public int RuntimeStreamedPlacements;
            public int MissingRuntimePlacementData;
            public int TerrainDetails;
            public int TerrainTrees;
            public int InstancedGrass;
            public float HighestObservedLayerWeight;
            public float HighestObservedCliffFormationDensity;
            public bool HitMaximumInstances;
            public bool Cancelled;

            public List<string> UnregisteredDetailPrefabNames =
                new List<string>();
            public List<string> UnregisteredTreePrefabNames =
                new List<string>();

            public string ToSummary()
            {
                StringBuilder builder = new StringBuilder();

                builder.AppendLine($"Accepted placements: {Spawned:N0}");
                builder.AppendLine(
                    $"Cap-counted trees and objects: " +
                    $"{CapCountedInstances:N0}");
                builder.AppendLine($"GameObjects: {GameObjects:N0}");
                builder.AppendLine($"Runtime streamed placements: {RuntimeStreamedPlacements:N0}");
                builder.AppendLine($"Managed grass mesh instances: {InstancedGrass:N0}");
                if (MissingRuntimePlacementData > 0) builder.AppendLine($"Streamed placements skipped (no Placement Data asset): {MissingRuntimePlacementData:N0}");
                builder.AppendLine(
                    $"Terrain detail density written: {TerrainDetails:N0}");
                builder.AppendLine($"Terrain trees written: {TerrainTrees:N0}");
                builder.AppendLine($"Grid cells: {GridCells:N0}");
                builder.AppendLine($"Sampled points: {SampledPoints:N0}");
                builder.AppendLine(
                    $"Rejected by layer weight: " +
                    $"{RejectedByLayerWeight:N0}");
                builder.AppendLine(
                    $"Rejected by slope: {RejectedBySlope:N0}");
                builder.AppendLine(
                    $"Rejected by cliff formation: " +
                    $"{RejectedByCliffFormation:N0}");
                builder.AppendLine(
                    $"Rejected by cliff panel spacing: " +
                    $"{RejectedByCliffFaceSpacing:N0}");
                builder.AppendLine(
                    $"Rejected by spawn chance: " +
                    $"{RejectedBySpawnChance:N0}");
                builder.AppendLine(
                    $"Rejected by natural tree density: " +
                    $"{RejectedByNaturalTreeDensity:N0}");
                builder.AppendLine($"Rejected by exclusion volumes: {RejectedByExclusionVolume:N0}");
                builder.AppendLine($"Rejected by path clearance: {RejectedByPathClearance:N0}");
                builder.AppendLine(
                    $"Highest observed target-layer weight: " +
                    $"{HighestObservedLayerWeight:0.000}");
                if (RejectedByCliffFormation > 0 ||
                    HighestObservedCliffFormationDensity > 0f)
                {
                    builder.AppendLine(
                        $"Highest observed cliff formation density: " +
                        $"{HighestObservedCliffFormationDensity:0.000}");
                }

                if (LayerNotOnTerrain > 0)
                {
                    builder.AppendLine(
                        $"Rules whose layer was not on terrain: " +
                        $"{LayerNotOnTerrain:N0}");
                }

                if (MissingPrefabs > 0)
                {
                    builder.AppendLine(
                        $"Missing prefab selections: {MissingPrefabs:N0}");
                }

                if (UnregisteredDetailPrefabs > 0)
                {
                    builder.AppendLine(
                        "Terrain Detail prefabs not registered on Terrain: " +
                        string.Join(", ", UnregisteredDetailPrefabNames));
                }

                if (MissingExistingDetailSelections > 0)
                {
                    builder.AppendLine(
                        $"Placements skipped because the existing detail " +
                        $"prototype was unavailable: " +
                        $"{MissingExistingDetailSelections:N0}");
                }

                if (UnregisteredTreePrefabs > 0)
                {
                    builder.AppendLine(
                        "Terrain Tree prefabs not registered on Terrain: " +
                        string.Join(", ", UnregisteredTreePrefabNames));
                }

                if (MissingExistingTreeSelections > 0)
                {
                    builder.AppendLine(
                        $"Placements skipped because the existing tree prototype " +
                        $"was unavailable: {MissingExistingTreeSelections:N0}");
                }

                if (HitMaximumInstances)
                {
                    builder.AppendLine(
                        "Stopped because Maximum Instances was reached.");
                }

                return builder.ToString();
            }
        }
    }
}
