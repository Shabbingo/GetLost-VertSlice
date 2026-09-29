using System.Collections.Generic;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [CreateAssetMenu(
        fileName = "Terrain Foliage Rule",
        menuName = "Thomas/Terrain Foliage Rule")]
    public sealed class TerrainFoliageRule : ScriptableObject
    {
        [Header("Terrain Layer")]
        public TerrainLayer terrainLayer;

        [Tooltip("Optional additional terrain surfaces accepted by this rule. The strongest matching layer weight is used.")]
        public List<TerrainLayer> additionalTerrainLayers =
            new List<TerrainLayer>();

        [Range(0f, 1f)]
        public float minimumLayerWeight = 0.1f;

        [Header("Foliage")]
        public List<TerrainFoliagePrefabEntry> prefabEntries =
            new List<TerrainFoliagePrefabEntry>();

        [Range(0f, 1f)]
        public float spawnChance = 1f;

        [Header("Cliff Formations")]
        [Tooltip("Concentrates this rule into relief-aware rock bands instead of scattering it uniformly across every eligible slope.")]
        public bool useCliffFormationDistribution;

        [Tooltip("Independent placement attempts made in each spawner grid cell. This affects every output type in this rule.")]
        [Range(1, 6)]
        public int cliffSamplesPerCell = 2;

        [Tooltip("Slope where cliff suitability begins to fade in.")]
        [Range(0f, 89f)]
        public float cliffMinimumSlope = 38f;

        [Tooltip("Slope that receives full cliff suitability, provided the nearby height drop is also sufficient.")]
        [Range(0.1f, 90f)]
        public float cliffFullDensitySlope = 60f;

        [Tooltip("World-space radius used to measure the largest downhill height drop around a candidate.")]
        [Min(0.5f)]
        public float cliffReliefRadius = 10f;

        [Tooltip("Downhill height drop where cliff suitability begins to fade in.")]
        [Min(0f)]
        public float cliffMinimumHeightDrop = 4f;

        [Tooltip("Downhill height drop that receives full cliff suitability.")]
        [Min(0.01f)]
        public float cliffFullHeightDrop = 12f;

        [Tooltip("Approximate width of broad rock formations and the gaps between them in world metres.")]
        [Min(5f)]
        public float cliffPatchSize = 45f;

        [Tooltip("Placement density retained in the gaps between formations.")]
        [Range(0f, 1f)]
        public float minimumCliffPatchDensity = 0.08f;

        [Tooltip("Placement density at the centres of formations.")]
        [Range(0f, 1f)]
        public float maximumCliffPatchDensity = 1f;

        [Tooltip("Pushes the distribution toward clearer gaps and denser rock bands.")]
        [Range(0f, 1f)]
        public float cliffPatchContrast = 0.75f;

        [Tooltip("Distorts formation boundaries so they do not resemble simple noise circles.")]
        [Range(0f, 1f)]
        public float cliffPatchDomainWarp = 0.45f;

        [Header("Continuous Cliff Faces")]
        [Tooltip("Builds the visible cliff from large overlapping GameObject panels marked as cliff-face pieces instead of scattered boulders.")]
        public bool buildContinuousCliffFaces;

        [Tooltip("Approximate world-space distance between large cliff panels. Their width should exceed this value so neighbouring panels overlap.")]
        [Min(5f)]
        public float cliffFaceSpacing = 18f;

        [Min(0.1f)] public float cliffFaceMinimumWidthScale = 4f;
        [Min(0.1f)] public float cliffFaceMaximumWidthScale = 7f;
        [Min(0.1f)] public float cliffFaceMinimumHeightScale = 5f;
        [Min(0.1f)] public float cliffFaceMaximumHeightScale = 9f;
        [Min(0.1f)] public float cliffFaceMinimumDepthScale = 2f;
        [Min(0.1f)] public float cliffFaceMaximumDepthScale = 3.5f;

        [Tooltip("Pushes cliff panels into the hillside to hide their rear faces and seams.")]
        [Min(0f)]
        public float cliffFaceEmbedDepth = 2.5f;

        [Tooltip("Small rotation variation around world up. Large random rotations break the continuous cliff silhouette.")]
        [Range(0f, 30f)]
        public float cliffFaceYawJitter = 8f;

        [Header("Natural Tree Distribution")]
        [Tooltip("Uses continuous world-space density fields to form woodland stands, sparse areas and organic openings. Only Terrain Tree entries are affected.")]
        public bool useNaturalTreeDensityVariation;

        [Tooltip("Approximate size of the broad forest patches in world metres.")]
        [Min(25f)]
        public float naturalTreePatchSize = 260f;

        [Tooltip("Tree density retained in the sparsest parts of the forest.")]
        [Range(0f, 1f)]
        public float minimumNaturalTreeDensity = 0.08f;

        [Tooltip("Tree density retained in the densest parts of the forest.")]
        [Range(0f, 1f)]
        public float maximumNaturalTreeDensity = 1f;

        [Tooltip("Pushes more land toward distinctly dense or distinctly sparse woodland.")]
        [Range(0f, 1f)]
        public float naturalTreeDensityContrast = 0.75f;

        [Tooltip("Bends the density field so its edges do not resemble simple noise circles.")]
        [Range(0f, 1f)]
        public float naturalTreeDomainWarp = 0.4f;

        [Header("Forest Understory Distribution")]
        [Tooltip("Applies the woodland density field to every prefab in this rule, including streamed GameObjects such as bushes and saplings.")]
        public bool followNaturalForestDensity;

        [Tooltip("Adds smaller irregular thickets inside the broad woodland stands. Intended for mid-height sight-blocking foliage.")]
        public bool useUnderstoryThickets;

        [Tooltip("Approximate width of individual understory thickets in world metres.")]
        [Min(6f)]
        public float understoryThicketSize = 24f;

        [Tooltip("Understory density retained between thickets.")]
        [Range(0f, 1f)]
        public float minimumUnderstoryThicketDensity = 0.08f;

        [Tooltip("Understory density retained in the centres of thickets.")]
        [Range(0f, 1f)]
        public float maximumUnderstoryThicketDensity = 1f;

        [Tooltip("Pushes the understory toward clearer gaps and denser thickets.")]
        [Range(0f, 1f)]
        public float understoryThicketContrast = 0.75f;

        [Tooltip("Distorts thicket boundaries to avoid obvious circular patches.")]
        [Range(0f, 1f)]
        public float understoryThicketDomainWarp = 0.55f;

        [Header("Gentle Ground Meadows")]
        [Tooltip("Broadens Terrain Detail grass patches on flatter ground so open plains form fuller meadows.")]
        public bool useGentleGroundMeadows;

        [Tooltip("Terrain at or below this slope receives the meadow treatment.")]
        [Range(0f, 45f)]
        public float meadowMaximumSlope = 14f;

        [Tooltip("Extra detail-map cells added around accepted grass placements on gentle ground.")]
        [Range(0, 8)]
        public int meadowRadiusBonus = 3;

        [Tooltip("Density boost applied inside broad meadow patches.")]
        [Range(0.25f, 2f)]
        public float meadowDensityMultiplier = 1.15f;

        [Tooltip("Extra independent Terrain Detail samples per grid cell. These never spawn trees, rocks or GameObjects.")]
        [Range(0, 4)]
        public int additionalTerrainDetailSamplesPerCell;

        [Header("Placement Constraints")]
        public bool useSlopeLimit = true;

        [Range(0f, 90f)]
        public float minimumSlope = 0f;

        [Range(0f, 90f)]
        public float maximumSlope = 35f;

        [Header("Default Transform")]
        [Tooltip("Default value used only when migrating older rule assets.")]
        public bool alignToTerrainNormal = true;

        [Tooltip("Default value used only when migrating older rule assets.")]
        public bool randomYRotation = true;

        [Min(0.01f)]
        public float minimumScale = 0.8f;

        [Min(0.01f)]
        public float maximumScale = 1.2f;

        // Legacy v0.1.6 data retained only so existing rule assets migrate safely.
        [SerializeField, HideInInspector]
        private List<GameObject> prefabs = new List<GameObject>();

        [SerializeField, HideInInspector]
        private List<float> prefabWeights = new List<float>();

        [SerializeField, HideInInspector]
        private List<Vector3> prefabPositionOffsets = new List<Vector3>();

        [SerializeField, HideInInspector]
        private List<Vector3> prefabRotationOffsets = new List<Vector3>();

        [SerializeField, HideInInspector]
        private List<Vector3> prefabScaleMultipliers = new List<Vector3>();

        public bool EnsureMigrated()
        {
            prefabEntries ??= new List<TerrainFoliagePrefabEntry>();

            if (prefabEntries.Count > 0 || prefabs == null || prefabs.Count == 0)
            {
                ValidateEntries();
                return false;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                TerrainFoliagePrefabEntry entry =
                    new TerrainFoliagePrefabEntry
                    {
                        prefab = prefabs[i],
                        weight = GetLegacyValue(prefabWeights, i, 1f),
                        alignToTerrainNormal = alignToTerrainNormal,
                        randomYRotation = randomYRotation,
                        positionOffset = GetLegacyValue(
                            prefabPositionOffsets,
                            i,
                            Vector3.zero),
                        rotationOffset = GetLegacyValue(
                            prefabRotationOffsets,
                            i,
                            Vector3.zero),
                        scaleMultiplier = GetLegacyValue(
                            prefabScaleMultipliers,
                            i,
                            Vector3.one)
                    };

                entry.Validate();
                prefabEntries.Add(entry);
            }

            return true;
        }

        public TerrainFoliagePrefabEntry GetRandomEntry(System.Random random)
        {
            return GetRandomEntry(random, null);
        }

        public TerrainFoliagePrefabEntry GetRandomEntry(
            System.Random random,
            TerrainFoliageOutputMode? requiredOutputMode)
        {
            EnsureMigrated();

            float totalWeight = 0f;

            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];

                if (entry != null &&
                    entry.prefab != null &&
                    (!requiredOutputMode.HasValue ||
                     entry.outputMode == requiredOutputMode.Value))
                {
                    totalWeight += Mathf.Max(0f, entry.weight);
                }
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            double targetWeight = random.NextDouble() * totalWeight;
            float runningWeight = 0f;

            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];

                if (entry == null ||
                    entry.prefab == null ||
                    entry.weight <= 0f ||
                    (requiredOutputMode.HasValue &&
                     entry.outputMode != requiredOutputMode.Value))
                {
                    continue;
                }

                runningWeight += entry.weight;

                if (targetWeight <= runningWeight)
                {
                    return entry;
                }
            }

            for (int i = prefabEntries.Count - 1; i >= 0; i--)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];

                if (entry != null &&
                    entry.prefab != null &&
                    entry.weight > 0f &&
                    (!requiredOutputMode.HasValue ||
                     entry.outputMode == requiredOutputMode.Value))
                {
                    return entry;
                }
            }

            return null;
        }

        public TerrainFoliagePrefabEntry GetRandomGroundCoverEntry(
            System.Random random)
        {
            EnsureMigrated();
            float totalWeight = 0f;

            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];
                if (IsGroundCover(entry))
                    totalWeight += Mathf.Max(0f, entry.weight);
            }

            if (totalWeight <= 0f)
                return null;

            double target = random.NextDouble() * totalWeight;
            float running = 0f;
            TerrainFoliagePrefabEntry last = null;

            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];
                if (!IsGroundCover(entry))
                    continue;

                last = entry;
                running += entry.weight;
                if (target <= running)
                    return entry;
            }

            return last;
        }

        public TerrainFoliagePrefabEntry GetRandomCliffFaceEntry(
            System.Random random)
        {
            EnsureMigrated();
            float totalWeight = 0f;
            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];
                if (IsCliffFace(entry))
                    totalWeight += Mathf.Max(0f, entry.weight);
            }

            if (totalWeight <= 0f)
                return null;

            double target = random.NextDouble() * totalWeight;
            float running = 0f;
            TerrainFoliagePrefabEntry last = null;
            for (int i = 0; i < prefabEntries.Count; i++)
            {
                TerrainFoliagePrefabEntry entry = prefabEntries[i];
                if (!IsCliffFace(entry))
                    continue;

                last = entry;
                running += entry.weight;
                if (target <= running)
                    return entry;
            }

            return last;
        }

        private static bool IsCliffFace(TerrainFoliagePrefabEntry entry)
        {
            return entry != null &&
                   entry.prefab != null &&
                   entry.weight > 0f &&
                   entry.outputMode == TerrainFoliageOutputMode.GameObject &&
                   entry.useForCliffFaces;
        }

        private static bool IsGroundCover(TerrainFoliagePrefabEntry entry)
        {
            return entry != null &&
                   entry.prefab != null &&
                   entry.weight > 0f &&
                   (entry.outputMode == TerrainFoliageOutputMode.TerrainDetail ||
                    entry.outputMode == TerrainFoliageOutputMode.InstancedGrass);
        }

        public void ValidateEntries()
        {
            prefabEntries ??= new List<TerrainFoliagePrefabEntry>();

            for (int i = 0; i < prefabEntries.Count; i++)
            {
                prefabEntries[i]?.Validate();
            }
        }

        private static T GetLegacyValue<T>(
            IReadOnlyList<T> values,
            int index,
            T fallback)
        {
            return values != null && index >= 0 && index < values.Count
                ? values[index]
                : fallback;
        }

        private void OnValidate()
        {
            additionalTerrainLayers ??= new List<TerrainLayer>();
            additionalTerrainLayers.RemoveAll(layer => layer == null || layer == terrainLayer);
            for (int i = additionalTerrainLayers.Count - 1; i >= 0; i--)
                if (additionalTerrainLayers.IndexOf(additionalTerrainLayers[i]) != i)
                    additionalTerrainLayers.RemoveAt(i);
            minimumLayerWeight = Mathf.Clamp01(minimumLayerWeight);
            spawnChance = Mathf.Clamp01(spawnChance);
            cliffSamplesPerCell = Mathf.Clamp(cliffSamplesPerCell, 1, 6);
            cliffMinimumSlope = Mathf.Clamp(cliffMinimumSlope, 0f, 89f);
            cliffFullDensitySlope = Mathf.Clamp(
                cliffFullDensitySlope,
                cliffMinimumSlope + 0.1f,
                90f);
            cliffReliefRadius = Mathf.Max(0.5f, cliffReliefRadius);
            cliffMinimumHeightDrop = Mathf.Max(0f, cliffMinimumHeightDrop);
            cliffFullHeightDrop = Mathf.Max(
                cliffMinimumHeightDrop + 0.01f,
                cliffFullHeightDrop);
            cliffPatchSize = Mathf.Max(5f, cliffPatchSize);
            minimumCliffPatchDensity = Mathf.Clamp01(
                minimumCliffPatchDensity);
            maximumCliffPatchDensity = Mathf.Clamp(
                maximumCliffPatchDensity,
                minimumCliffPatchDensity,
                1f);
            cliffPatchContrast = Mathf.Clamp01(cliffPatchContrast);
            cliffPatchDomainWarp = Mathf.Clamp01(cliffPatchDomainWarp);
            cliffFaceSpacing = Mathf.Max(5f, cliffFaceSpacing);
            cliffFaceMinimumWidthScale = Mathf.Max(
                0.1f,
                cliffFaceMinimumWidthScale);
            cliffFaceMaximumWidthScale = Mathf.Max(
                cliffFaceMinimumWidthScale,
                cliffFaceMaximumWidthScale);
            cliffFaceMinimumHeightScale = Mathf.Max(
                0.1f,
                cliffFaceMinimumHeightScale);
            cliffFaceMaximumHeightScale = Mathf.Max(
                cliffFaceMinimumHeightScale,
                cliffFaceMaximumHeightScale);
            cliffFaceMinimumDepthScale = Mathf.Max(
                0.1f,
                cliffFaceMinimumDepthScale);
            cliffFaceMaximumDepthScale = Mathf.Max(
                cliffFaceMinimumDepthScale,
                cliffFaceMaximumDepthScale);
            cliffFaceEmbedDepth = Mathf.Max(0f, cliffFaceEmbedDepth);
            cliffFaceYawJitter = Mathf.Clamp(cliffFaceYawJitter, 0f, 30f);
            naturalTreePatchSize = Mathf.Max(25f, naturalTreePatchSize);
            minimumNaturalTreeDensity = Mathf.Clamp01(minimumNaturalTreeDensity);
            maximumNaturalTreeDensity = Mathf.Clamp(
                maximumNaturalTreeDensity,
                minimumNaturalTreeDensity,
                1f);
            naturalTreeDensityContrast = Mathf.Clamp01(naturalTreeDensityContrast);
            naturalTreeDomainWarp = Mathf.Clamp01(naturalTreeDomainWarp);
            understoryThicketSize = Mathf.Max(6f, understoryThicketSize);
            minimumUnderstoryThicketDensity =
                Mathf.Clamp01(minimumUnderstoryThicketDensity);
            maximumUnderstoryThicketDensity = Mathf.Clamp(
                maximumUnderstoryThicketDensity,
                minimumUnderstoryThicketDensity,
                1f);
            understoryThicketContrast =
                Mathf.Clamp01(understoryThicketContrast);
            understoryThicketDomainWarp =
                Mathf.Clamp01(understoryThicketDomainWarp);
            meadowMaximumSlope = Mathf.Clamp(meadowMaximumSlope, 0f, 45f);
            meadowRadiusBonus = Mathf.Clamp(meadowRadiusBonus, 0, 8);
            meadowDensityMultiplier = Mathf.Clamp(
                meadowDensityMultiplier,
                0.25f,
                2f);
            additionalTerrainDetailSamplesPerCell = Mathf.Clamp(
                additionalTerrainDetailSamplesPerCell,
                0,
                4);
            minimumSlope = Mathf.Clamp(minimumSlope, 0f, 90f);
            maximumSlope = Mathf.Clamp(maximumSlope, minimumSlope, 90f);
            minimumScale = Mathf.Max(0.01f, minimumScale);
            maximumScale = Mathf.Max(minimumScale, maximumScale);
            EnsureMigrated();
            ValidateEntries();
        }
    }
}
