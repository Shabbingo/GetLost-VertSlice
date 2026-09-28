using System.Collections.Generic;
using Thomas.TerrainFoliageSpawner;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Applies vegetation resistance from the exact visible placements used by
    /// TerrainFoliageGrassRenderer. This avoids Unity Terrain detail-grid offsets.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VegetationInteractionReceiver))]
    public sealed class ManagedGrassVegetationProvider : MonoBehaviour
    {
        private sealed class ProfileSourceKey { }

        [Header("References")]
        [SerializeField] private TerrainDetailVegetationMapping mapping;
        [SerializeField] private VegetationInteractionReceiver receiver;

        [Header("Exact Placement Sampling")]
        [SerializeField, Min(0.1f)] private float sampleRadius = 0.9f;
        [SerializeField, Min(0.02f)] private float sampleInterval = 0.15f;
        [Tooltip("Nearby managed instances required for the full profile effect.")]
        [SerializeField, Min(1f)] private float instancesForFullEffect = 8f;

        private readonly List<TerrainFoliageGrassRenderer> renderers = new();
        private readonly Dictionary<GameObject, int> prefabCounts = new();
        private readonly Dictionary<VegetationProfile, int> profileCounts = new();
        private readonly Dictionary<VegetationProfile, object> sourceKeys = new();
        private readonly HashSet<VegetationProfile> reportedProfiles = new();

        private float nextSampleTime;

        public int CurrentNearbyInstanceCount { get; private set; }

        private void Reset()
        {
            receiver = GetComponent<VegetationInteractionReceiver>();
        }

        private void Awake()
        {
            if (receiver == null)
                receiver = GetComponent<VegetationInteractionReceiver>();
        }

        private void OnEnable()
        {
            nextSampleTime = 0f;
        }

        private void OnDisable()
        {
            ClearAllSources();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextSampleTime)
                return;

            nextSampleTime = now + sampleInterval;
            SampleManagedGrass();
        }

        private void SampleManagedGrass()
        {
            prefabCounts.Clear();
            profileCounts.Clear();
            reportedProfiles.Clear();
            CurrentNearbyInstanceCount = 0;

            if (receiver == null || mapping == null)
            {
                ClearAllSources();
                return;
            }

            TerrainFoliageGrassRenderer.GetActiveRenderers(renderers);
            Vector3 position = transform.position;

            for (int i = 0; i < renderers.Count; i++)
            {
                TerrainFoliageGrassRenderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.AccumulateVisibleGrassCounts(
                        position,
                        sampleRadius,
                        prefabCounts);
                }
            }

            foreach (KeyValuePair<GameObject, int> pair in prefabCounts)
            {
                if (pair.Key == null || pair.Value <= 0 ||
                    !mapping.TryGetProfile(pair.Key, out VegetationProfile profile))
                {
                    continue;
                }

                profileCounts.TryGetValue(profile, out int count);
                profileCounts[profile] = count + pair.Value;
                CurrentNearbyInstanceCount += pair.Value;
            }

            foreach (KeyValuePair<VegetationProfile, int> pair in profileCounts)
            {
                float strength = Mathf.Clamp01(
                    pair.Value / instancesForFullEffect);
                object sourceKey = GetSourceKey(pair.Key);

                // Persistent between samples, then explicitly removed as soon as
                // the next exact-position query no longer finds the vegetation.
                receiver.SetPersistentSource(sourceKey, pair.Key, strength);
                reportedProfiles.Add(pair.Key);
            }

            foreach (KeyValuePair<VegetationProfile, object> pair in sourceKeys)
            {
                if (!reportedProfiles.Contains(pair.Key))
                    receiver.RemoveSource(pair.Value);
            }
        }

        private object GetSourceKey(VegetationProfile profile)
        {
            if (!sourceKeys.TryGetValue(profile, out object sourceKey))
            {
                sourceKey = new ProfileSourceKey();
                sourceKeys.Add(profile, sourceKey);
            }

            return sourceKey;
        }

        private void ClearAllSources()
        {
            if (receiver != null)
            {
                foreach (object sourceKey in sourceKeys.Values)
                    receiver.RemoveSource(sourceKey);
            }

            CurrentNearbyInstanceCount = 0;
            reportedProfiles.Clear();
            prefabCounts.Clear();
            profileCounts.Clear();
        }

        private void OnValidate()
        {
            sampleRadius = Mathf.Max(0.1f, sampleRadius);
            sampleInterval = Mathf.Max(0.02f, sampleInterval);
            instancesForFullEffect = Mathf.Max(1f, instancesForFullEffect);
        }
    }
}
