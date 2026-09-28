using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Collects vegetation interaction from any number of providers and exposes
    /// one stable result to movement, audio and future gameplay systems.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class VegetationInteractionReceiver : MonoBehaviour
    {
        private sealed class SourceState
        {
            public VegetationProfile profile;
            public float strength;
            public int lastUpdatedFrame;
        }

        [Header("Blending")]
        [SerializeField] private bool combineAdditively = true;
        [SerializeField, Range(0.05f, 1f)] private float minimumSpeedMultiplier = 0.55f;
        [SerializeField, Min(0f)] private float enterSmoothing = 8f;
        [SerializeField, Min(0f)] private float exitSmoothing = 5f;
        [Tooltip("Automatically removes a provider if it stops reporting for this many frames. Trigger providers are persistent until exit.")]
        [SerializeField, Min(1)] private int transientSourceTimeoutFrames = 3;

        private readonly Dictionary<object, SourceState> sources = new();
        private readonly HashSet<object> persistentSources = new();
        private readonly List<object> removalBuffer = new();

        private float targetResistance;
        private float targetAdditionalExertion;
        private float targetRawStrength;
        private VegetationProfile targetDominantProfile;

        public float CurrentStrength { get; private set; }
        public float CurrentResistance { get; private set; }
        public float SpeedMultiplier => Mathf.Lerp(1f, minimumSpeedMultiplier, CurrentResistance);
        public float ExertionMultiplier { get; private set; } = 1f;
        public VegetationProfile CurrentProfile { get; private set; }
        public TerrainSurfaceProfile CurrentAudioProfile => CurrentProfile != null ? CurrentProfile.audioProfile : null;
        public float CameraResistance => CurrentProfile != null ? CurrentProfile.cameraResistance * CurrentStrength : 0f;
        public float VisibilityReduction => CurrentProfile != null ? CurrentProfile.visibilityReduction * CurrentStrength : 0f;

        /// <summary>Report a transient source. It must report repeatedly while active.</summary>
        public void SetTransientSource(object source, VegetationProfile profile, float strength)
        {
            SetSource(source, profile, strength, false);
        }

        /// <summary>Report a persistent source. It remains active until RemoveSource is called.</summary>
        public void SetPersistentSource(object source, VegetationProfile profile, float strength)
        {
            SetSource(source, profile, strength, true);
        }

        public void RemoveSource(object source)
        {
            if (source == null)
                return;
            sources.Remove(source);
            persistentSources.Remove(source);
        }

        private void SetSource(object source, VegetationProfile profile, float strength, bool persistent)
        {
            if (source == null)
                return;

            strength = Mathf.Clamp01(strength);
            if (profile == null || strength <= 0.001f)
            {
                RemoveSource(source);
                return;
            }

            if (!sources.TryGetValue(source, out SourceState state))
            {
                state = new SourceState();
                sources.Add(source, state);
            }

            state.profile = profile;
            state.strength = strength;
            state.lastUpdatedFrame = Time.frameCount;

            if (persistent)
                persistentSources.Add(source);
            else
                persistentSources.Remove(source);
        }

        private void Update()
        {
            RemoveExpiredSources();
            RecalculateTargets();

            float smoothing = targetResistance > CurrentResistance ? enterSmoothing : exitSmoothing;
            CurrentResistance = Mathf.MoveTowards(CurrentResistance, targetResistance, smoothing * Time.deltaTime);
            CurrentStrength = Mathf.MoveTowards(CurrentStrength, targetRawStrength, smoothing * Time.deltaTime);

            float currentExtra = Mathf.Max(0f, ExertionMultiplier - 1f);
            currentExtra = Mathf.MoveTowards(currentExtra, targetAdditionalExertion, smoothing * Time.deltaTime);
            ExertionMultiplier = 1f + currentExtra;

            CurrentProfile = CurrentStrength > 0.01f ? targetDominantProfile : null;
        }

        private void RemoveExpiredSources()
        {
            removalBuffer.Clear();
            foreach (KeyValuePair<object, SourceState> pair in sources)
            {
                if (persistentSources.Contains(pair.Key))
                    continue;
                if (Time.frameCount - pair.Value.lastUpdatedFrame > transientSourceTimeoutFrames)
                    removalBuffer.Add(pair.Key);
            }

            for (int i = 0; i < removalBuffer.Count; i++)
                sources.Remove(removalBuffer[i]);
        }

        private void RecalculateTargets()
        {
            targetResistance = 0f;
            targetAdditionalExertion = 0f;
            targetRawStrength = 0f;
            targetDominantProfile = null;
            float dominantContribution = 0f;

            foreach (SourceState state in sources.Values)
            {
                if (state.profile == null || state.strength <= 0f)
                    continue;

                float resistanceContribution = state.strength * state.profile.resistance;
                float exertionContribution = state.strength * state.profile.additionalExertion;

                if (combineAdditively)
                {
                    targetResistance = Mathf.Clamp01(targetResistance + resistanceContribution);
                    targetAdditionalExertion += exertionContribution;
                    targetRawStrength = Mathf.Clamp01(targetRawStrength + state.strength);
                }
                else
                {
                    targetResistance = Mathf.Max(targetResistance, resistanceContribution);
                    targetAdditionalExertion = Mathf.Max(targetAdditionalExertion, exertionContribution);
                    targetRawStrength = Mathf.Max(targetRawStrength, state.strength);
                }

                if (resistanceContribution > dominantContribution)
                {
                    dominantContribution = resistanceContribution;
                    targetDominantProfile = state.profile;
                }
            }
        }
    }
}
