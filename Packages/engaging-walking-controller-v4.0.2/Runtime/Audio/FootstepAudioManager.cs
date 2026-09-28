using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Plays surface-aware footsteps using animation events or a distance fallback.
    /// Add this component to the same GameObject as WalkingMotor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WalkingMotor))]
    public sealed class FootstepAudioManager : MonoBehaviour
    {
        public enum TriggerMode
        {
            DistanceFallback,
            AnimationEvents
        }

        [Header("References")]
        [SerializeField] private WalkingMotor motor;
        [SerializeField] private VegetationInteractionReceiver vegetationReceiver;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private AudioSource slideSource;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;

        [Header("Triggering")]
        [SerializeField] private TriggerMode triggerMode = TriggerMode.DistanceFallback;
        [Tooltip("Base metres travelled per walking footstep.")]
        [SerializeField, Min(0.05f)] private float walkStepDistance = 1.8f;
        [SerializeField, Min(0.05f)] private float sprintStepDistance = 1.35f;
        [SerializeField, Min(0.05f)] private float carefulStepDistance = 2.15f;
        [SerializeField, Min(0f)] private float minimumSpeed = 0.25f;

        [Header("Volume by Movement")]
        [SerializeField, Range(0f, 2f)] private float walkVolumeMultiplier = 0.75f;
        [SerializeField, Range(0f, 2f)] private float sprintVolumeMultiplier = 1f;
        [SerializeField, Range(0f, 2f)] private float carefulVolumeMultiplier = 0.35f;
        [SerializeField, Range(0f, 2f)] private float uphillWeightMultiplier = 0.2f;

        [Header("Slide")]
        [SerializeField, Min(0f)] private float slideFadeSpeed = 7f;
        [SerializeField, Min(0.01f)] private float slideSpeedForFullVolume = 7f;

        [Header("Particles")]
        [SerializeField] private bool spawnParticles = true;
        [SerializeField] private Vector3 particleOffset = new(0f, 0.03f, 0f);

        private readonly Dictionary<TerrainSurfaceProfile, ShuffleBag> shuffleBags = new();
        private float distanceSinceStep;
        private Vector3 previousPosition;
        private bool useLeftFoot = true;
        private TerrainSurfaceProfile vegetationProfile;
        private TerrainSurfaceProfile terrainVegetationProfile;
        private float terrainVegetationStrength;
        private float vegetationTimer;

        private void Reset()
        {
            motor = GetComponent<WalkingMotor>();
            vegetationReceiver = GetComponent<VegetationInteractionReceiver>();
            CreateAudioSourcesIfMissing();
        }

        private void Awake()
        {
            if (motor == null)
                motor = GetComponent<WalkingMotor>();
            if (vegetationReceiver == null)
                vegetationReceiver = GetComponent<VegetationInteractionReceiver>();
            CreateAudioSourcesIfMissing();
            previousPosition = transform.position;
        }

        private void Update()
        {
            if (motor == null)
                return;

            UpdateDistanceFootsteps();
            UpdateSlideAudio();
            UpdateVegetationAudio();
            previousPosition = transform.position;
        }

        /// <summary>Call from an Animator Animation Event at either foot plant.</summary>
        public void Footstep() => PlayFootstep(useLeftFoot ? leftFoot : rightFoot);

        /// <summary>Call from a left-foot Animation Event.</summary>
        public void LeftFootstep() => PlayFootstep(leftFoot);

        /// <summary>Call from a right-foot Animation Event.</summary>
        public void RightFootstep() => PlayFootstep(rightFoot);

        public void EnterVegetation(TerrainSurfaceProfile profile)
        {
            vegetationProfile = profile;
            vegetationTimer = 0f;
        }

        public void ExitVegetation(TerrainSurfaceProfile profile)
        {
            if (vegetationProfile == profile)
                vegetationProfile = null;
        }

        /// <summary>Called by TerrainDetailVegetationDetector. Strength is expected in the 0–1 range.</summary>
        public void SetTerrainVegetation(TerrainSurfaceProfile profile, float strength)
        {
            terrainVegetationProfile = profile;
            terrainVegetationStrength = Mathf.Clamp01(strength);
            if (terrainVegetationStrength <= 0.01f)
                terrainVegetationProfile = null;
        }

        private void UpdateDistanceFootsteps()
        {
            if (triggerMode != TriggerMode.DistanceFallback || !motor.IsGrounded || motor.IsSliding)
                return;

            Vector3 delta = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up);
            if (motor.CurrentSpeed < minimumSpeed)
            {
                distanceSinceStep = Mathf.Min(distanceSinceStep, CurrentStepDistance() * 0.5f);
                return;
            }

            distanceSinceStep += delta.magnitude;
            float requiredDistance = CurrentStepDistance();
            if (distanceSinceStep < requiredDistance)
                return;

            distanceSinceStep %= requiredDistance;
            PlayFootstep(useLeftFoot ? leftFoot : rightFoot);
        }

        private float CurrentStepDistance()
        {
            float baseDistance = motor.IsCarefulWalking ? carefulStepDistance : motor.IsSprinting ? sprintStepDistance : walkStepDistance;
            TerrainSurfaceProfile surface = motor.CurrentSurface;
            return baseDistance * (surface != null ? surface.stepDistanceMultiplier : 1f);
        }

        private void PlayFootstep(Transform foot)
        {
            TerrainSurfaceProfile surface = motor.CurrentSurface;
            if (surface == null)
                return;

            AudioClip clip = GetNextClip(surface, surface.footstepClips);
            if (clip != null && footstepSource != null)
            {
                footstepSource.pitch = Random.Range(surface.minimumFootstepPitch, surface.maximumFootstepPitch);
                float movementVolume = motor.IsCarefulWalking ? carefulVolumeMultiplier : motor.IsSprinting ? sprintVolumeMultiplier : walkVolumeMultiplier;
                float uphillWeight = 1f + motor.UphillAmount * uphillWeightMultiplier;
                footstepSource.PlayOneShot(clip, surface.footstepVolume * movementVolume * uphillWeight);
            }

            if (spawnParticles && surface.footstepParticlePrefab != null)
            {
                Vector3 position = foot != null ? foot.position : transform.position;
                ParticleSystem particles = Instantiate(surface.footstepParticlePrefab, position + particleOffset, Quaternion.identity);
                Destroy(particles.gameObject, Mathf.Max(0.1f, surface.particleLifetime));
            }

            useLeftFoot = !useLeftFoot;
        }

        private void UpdateSlideAudio()
        {
            if (slideSource == null)
                return;

            TerrainSurfaceProfile surface = motor.CurrentSurface;
            AudioClip desired = surface != null ? surface.slideLoop : null;
            bool shouldPlay = motor.IsSliding && desired != null;

            if (shouldPlay && slideSource.clip != desired)
            {
                slideSource.Stop();
                slideSource.clip = desired;
                slideSource.loop = true;
                slideSource.Play();
            }

            float targetVolume = shouldPlay
                ? surface.slideVolume * Mathf.Clamp01(motor.CurrentSpeed / slideSpeedForFullVolume)
                : 0f;
            slideSource.volume = Mathf.MoveTowards(slideSource.volume, targetVolume, slideFadeSpeed * Time.deltaTime);
            slideSource.pitch = surface != null ? surface.slidePitch : 1f;

            if (!shouldPlay && slideSource.isPlaying && slideSource.volume <= 0.001f)
            {
                slideSource.Stop();
                slideSource.clip = null;
            }
        }

        private void UpdateVegetationAudio()
        {
            TerrainSurfaceProfile receiverProfile = vegetationReceiver != null ? vegetationReceiver.CurrentAudioProfile : null;
            float receiverStrength = vegetationReceiver != null ? vegetationReceiver.CurrentStrength : 0f;
            TerrainSurfaceProfile activeProfile = receiverProfile != null
                ? receiverProfile
                : vegetationProfile != null ? vegetationProfile : terrainVegetationProfile;
            float intensity = receiverProfile != null
                ? receiverStrength
                : vegetationProfile != null ? 1f : terrainVegetationStrength;
            if (activeProfile == null || intensity <= 0.01f || motor.CurrentSpeed < minimumSpeed || footstepSource == null)
                return;

            vegetationTimer -= Time.deltaTime;
            if (vegetationTimer > 0f)
                return;

            AudioClip clip = GetRandomClip(activeProfile.vegetationRustleClips);
            if (clip != null)
                footstepSource.PlayOneShot(clip, activeProfile.vegetationRustleVolume * intensity);

            float intensityCadence = Mathf.Lerp(1.75f, 0.65f, intensity);
            vegetationTimer = Mathf.Max(0.05f, activeProfile.vegetationRustleInterval * intensityCadence);
        }

        private AudioClip GetNextClip(TerrainSurfaceProfile profile, AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return null;

            if (!shuffleBags.TryGetValue(profile, out ShuffleBag bag) || bag.Count != clips.Length)
            {
                bag = new ShuffleBag(clips.Length);
                shuffleBags[profile] = bag;
            }

            for (int attempts = 0; attempts < clips.Length; attempts++)
            {
                AudioClip clip = clips[bag.Next()];
                if (clip != null)
                    return clip;
            }
            return null;
        }

        private static AudioClip GetRandomClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return null;
            return clips[Random.Range(0, clips.Length)];
        }

        private void CreateAudioSourcesIfMissing()
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            if (footstepSource == null && sources.Length > 0)
                footstepSource = sources[0];
            if (slideSource == null && sources.Length > 1)
                slideSource = sources[1];

            if (footstepSource == null)
            {
                footstepSource = gameObject.AddComponent<AudioSource>();
                footstepSource.playOnAwake = false;
                footstepSource.spatialBlend = 0f;
                footstepSource.volume = 1f;
            }

            if (slideSource == null || slideSource == footstepSource)
            {
                slideSource = gameObject.AddComponent<AudioSource>();
                slideSource.playOnAwake = false;
                slideSource.loop = true;
                slideSource.spatialBlend = 0f;
                slideSource.volume = 0f;
            }
        }

        private sealed class ShuffleBag
        {
            private readonly int[] order;
            private int index;
            public int Count => order.Length;

            public ShuffleBag(int count)
            {
                order = new int[count];
                Refill();
            }

            public int Next()
            {
                if (index >= order.Length)
                    Refill();
                return order[index++];
            }

            private void Refill()
            {
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int swap = Random.Range(0, i + 1);
                    (order[i], order[swap]) = (order[swap], order[i]);
                }
                index = 0;
            }
        }
    }
}
