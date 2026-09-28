using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Samples Unity Terrain detail density around the player and converts it into
    /// smooth movement resistance, exertion and rustle audio. No per-bush colliders are required.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class TerrainDetailVegetationDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TerrainDetailVegetationProfile profile;
        [SerializeField] private Terrain terrainOverride;
        [SerializeField] private FootstepAudioManager footstepAudio;

        [Header("Sampling")]
        [Tooltip("World-space radius sampled around the player. Larger values make resistance begin before the player reaches the centre of a bush patch.")]
        [SerializeField, Min(0.05f)] private float sampleRadius = 0.75f;
        [Tooltip("How often the Terrain detail maps are sampled. 0.1–0.25 seconds is usually sufficient.")]
        [SerializeField, Min(0.02f)] private float sampleInterval = 0.15f;
        [Tooltip("Maximum detail density treated as fully dense. Unity detail densities commonly range from 0 upward depending on brush strength and scatter mode.")]
        [SerializeField, Min(1f)] private float densityForFullEffect = 8f;

        [Header("Response")]
        [Tooltip("Minimum speed multiplier at maximum vegetation strength.")]
        [SerializeField, Range(0.05f, 1f)] private float minimumSpeedMultiplier = 0.55f;
        [SerializeField, Min(0f)] private float enterSmoothing = 8f;
        [SerializeField, Min(0f)] private float exitSmoothing = 5f;

        private Terrain activeTerrain;
        private float sampleTimer;
        private float targetStrength;
        private float targetAdditionalExertion;
        private TerrainSurfaceProfile targetAudioProfile;

        public float CurrentStrength { get; private set; }
        public float SpeedMultiplier => Mathf.Lerp(1f, minimumSpeedMultiplier, CurrentStrength);
        public float ExertionMultiplier { get; private set; } = 1f;
        public TerrainSurfaceProfile CurrentAudioProfile { get; private set; }

        private void Reset()
        {
            footstepAudio = GetComponent<FootstepAudioManager>();
        }

        private void Awake()
        {
            if (footstepAudio == null)
                footstepAudio = GetComponent<FootstepAudioManager>();
        }

        private void Update()
        {
            sampleTimer -= Time.deltaTime;
            if (sampleTimer <= 0f)
            {
                sampleTimer = sampleInterval;
                SampleTerrainDetails();
            }

            float smooth = targetStrength > CurrentStrength ? enterSmoothing : exitSmoothing;
            CurrentStrength = Mathf.MoveTowards(CurrentStrength, targetStrength, smooth * Time.deltaTime);
            ExertionMultiplier = 1f + Mathf.MoveTowards(
                Mathf.Max(0f, ExertionMultiplier - 1f),
                targetAdditionalExertion,
                smooth * Time.deltaTime);

            CurrentAudioProfile = CurrentStrength > 0.01f ? targetAudioProfile : null;
            if (footstepAudio != null)
                footstepAudio.SetTerrainVegetation(CurrentAudioProfile, CurrentStrength);
        }

        private void SampleTerrainDetails()
        {
            targetStrength = 0f;
            targetAdditionalExertion = 0f;
            targetAudioProfile = null;

            if (profile == null || profile.layers == null || profile.layers.Count == 0)
                return;

            activeTerrain = ResolveTerrain(transform.position);
            if (activeTerrain == null || activeTerrain.terrainData == null)
                return;

            TerrainData data = activeTerrain.terrainData;
            Vector3 local = transform.position - activeTerrain.transform.position;
            Vector3 size = data.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                return;

            int centreX = Mathf.FloorToInt(local.x / size.x * data.detailWidth);
            int centreZ = Mathf.FloorToInt(local.z / size.z * data.detailHeight);
            int radiusX = Mathf.Max(0, Mathf.CeilToInt(sampleRadius / size.x * data.detailWidth));
            int radiusZ = Mathf.Max(0, Mathf.CeilToInt(sampleRadius / size.z * data.detailHeight));

            int xBase = Mathf.Clamp(centreX - radiusX, 0, data.detailWidth - 1);
            int zBase = Mathf.Clamp(centreZ - radiusZ, 0, data.detailHeight - 1);
            int width = Mathf.Clamp(radiusX * 2 + 1, 1, data.detailWidth - xBase);
            int height = Mathf.Clamp(radiusZ * 2 + 1, 1, data.detailHeight - zBase);

            float strongestContribution = 0f;
            foreach (TerrainDetailVegetationProfile.DetailLayerRule rule in profile.layers)
            {
                if (rule == null || rule.detailLayerIndex < 0 || rule.detailLayerIndex >= data.detailPrototypes.Length)
                    continue;

                int[,] density = data.GetDetailLayer(xBase, zBase, width, height, rule.detailLayerIndex);
                float sum = 0f;
                for (int z = 0; z < height; z++)
                    for (int x = 0; x < width; x++)
                        sum += density[z, x];

                float averageDensity = sum / Mathf.Max(1, width * height);
                float normalizedDensity = Mathf.Clamp01(averageDensity / densityForFullEffect);
                float contribution = normalizedDensity * rule.resistance;
                float exertionContribution = normalizedDensity * rule.additionalExertion;

                if (profile.combineAdditively)
                {
                    targetStrength = Mathf.Clamp01(targetStrength + contribution);
                    targetAdditionalExertion += exertionContribution;
                }
                else
                {
                    targetStrength = Mathf.Max(targetStrength, contribution);
                    targetAdditionalExertion = Mathf.Max(targetAdditionalExertion, exertionContribution);
                }

                if (contribution > strongestContribution && rule.vegetationAudioProfile != null)
                {
                    strongestContribution = contribution;
                    targetAudioProfile = rule.vegetationAudioProfile;
                }
            }

            targetAdditionalExertion = Mathf.Max(0f, targetAdditionalExertion);
        }

        private Terrain ResolveTerrain(Vector3 worldPosition)
        {
            if (terrainOverride != null && Contains(terrainOverride, worldPosition))
                return terrainOverride;

            Terrain[] terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
                if (Contains(terrains[i], worldPosition))
                    return terrains[i];

            return null;
        }

        private static bool Contains(Terrain terrain, Vector3 worldPosition)
        {
            if (terrain == null || terrain.terrainData == null)
                return false;

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return worldPosition.x >= origin.x && worldPosition.z >= origin.z
                && worldPosition.x <= origin.x + size.x && worldPosition.z <= origin.z + size.z;
        }
    }
}
