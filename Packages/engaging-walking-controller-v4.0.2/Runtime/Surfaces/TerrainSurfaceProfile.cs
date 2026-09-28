using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Unified surface definition used by movement, footsteps, particles and sliding.
    /// Existing Surface Profile assets upgrade automatically because the original
    /// movement fields and their serialized names have been preserved.
    /// </summary>
    [CreateAssetMenu(menuName = "Walking Controller/Surface Profile", fileName = "Surface Profile")]
    public sealed class TerrainSurfaceProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Surface";
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        [Header("Movement")]
        [Min(0.05f)] public float speedMultiplier = 1f;
        [Min(0.05f)] public float accelerationMultiplier = 1f;
        [Range(0.05f, 2f)] public float traction = 1f;
        [Tooltip("Multiplies hidden-footing loss on this surface. Use values above 1 for loose or slippery terrain, and 0 to disable footing loss.")]
        [Range(0f, 3f)] public float footingLossMultiplier = 1f;

        [Header("Feedback")]
        [Min(0f)] public float exertionMultiplier = 1f;
        [Min(0f)] public float footstepNoiseMultiplier = 1f;
        [Range(0f, 2f)] public float cameraBobMultiplier = 1f;

        [Header("Sliding")]
        [Tooltip("Multiplies downhill slide acceleration. Set to 0 to prevent this surface from triggering an uncontrolled slide.")]
        [Min(0f)] public float downhillSlideMultiplier = 1f;
        [Tooltip("Allows this surface to begin sliding at its own angle instead of using the WalkingMotor default.")]
        public bool overrideSlideStartAngle;
        [Tooltip("The slope angle at which this surface causes an uncontrolled slide.")]
        [Range(0f, 89f)] public float slideStartAngle = 42f;

        [Header("Footstep Audio")]
        [Tooltip("Random clips used for ordinary footsteps on this surface.")]
        public AudioClip[] footstepClips;
        [Range(0f, 2f)] public float footstepVolume = 0.75f;
        [Range(0.5f, 1.5f)] public float minimumFootstepPitch = 0.96f;
        [Range(0.5f, 1.5f)] public float maximumFootstepPitch = 1.04f;
        [Tooltip("Multiplies the distance required before the next automatic footstep.")]
        [Range(0.25f, 3f)] public float stepDistanceMultiplier = 1f;

        [Header("Sliding Audio")]
        [Tooltip("Optional looping scrape/skid sound while uncontrollably sliding.")]
        public AudioClip slideLoop;
        [Range(0f, 2f)] public float slideVolume = 0.75f;
        [Range(0.5f, 1.5f)] public float slidePitch = 1f;

        [Header("Vegetation Audio")]
        [Tooltip("Short rustles used while moving inside a vegetation volume assigned to this profile.")]
        public AudioClip[] vegetationRustleClips;
        [Range(0f, 2f)] public float vegetationRustleVolume = 0.55f;
        [Min(0.05f)] public float vegetationRustleInterval = 0.65f;

        [Header("Footstep Particles (Optional)")]
        [Tooltip("Optional prefab spawned at the active foot on each step.")]
        public ParticleSystem footstepParticlePrefab;
        [Min(0f)] public float particleLifetime = 2f;

        private void OnValidate()
        {
            if (maximumFootstepPitch < minimumFootstepPitch)
                maximumFootstepPitch = minimumFootstepPitch;
        }
    }
}
