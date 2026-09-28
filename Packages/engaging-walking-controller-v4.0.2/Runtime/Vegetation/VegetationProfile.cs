using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Shared gameplay definition for vegetation, regardless of whether it comes
    /// from painted Terrain details or a trigger volume.
    /// </summary>
    [CreateAssetMenu(menuName = "Walking Controller/Vegetation/Vegetation Profile", fileName = "Vegetation Profile")]
    public sealed class VegetationProfile : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("Movement resistance at full interaction strength. 0 has no effect; 1 reaches the receiver's minimum speed.")]
        [Range(0f, 1f)] public float resistance = 0.4f;

        [Tooltip("Extra exertion at full interaction strength. 1 means double the normal exertion cost.")]
        [Range(0f, 2f)] public float additionalExertion = 0.25f;

        [Header("Audio")]
        [Tooltip("Surface profile containing vegetation rustle clips.")]
        public TerrainSurfaceProfile audioProfile;

        [Header("Optional Feedback")]
        [Tooltip("Reserved for camera feedback systems. Does not move the camera by itself.")]
        [Range(0f, 1f)] public float cameraResistance = 0f;

        [Tooltip("Reserved for visibility or AI systems. Does not alter rendering by itself.")]
        [Range(0f, 1f)] public float visibilityReduction = 0f;
    }
}
