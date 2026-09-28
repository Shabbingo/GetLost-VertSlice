using System;
using UnityEngine;

namespace GetLost.EnvironmentSystem
{
    [CreateAssetMenu(
        fileName = "Distant Forest Profile",
        menuName = "Get Lost/Environment/Distant Forest Profile")]
    public class GetLostDistantForestProfile : ScriptableObject
    {
        public static event Action<GetLostDistantForestProfile> ProfileValidated;
        [Header("Distance Bands")]
        [Min(0f)] public float fadeInStart = 450f;
        [Min(0f)] public float fullyVisibleDistance = 650f;
        [Min(0f)] public float fadeOutStart = 2200f;
        [Min(1f)] public float maximumDistance = 3000f;

        [Header("Forest Density")]
        [Range(0.05f, 1f)]
        [Tooltip("Percentage of Terrain tree instances represented by the distant layer.")]
        public float density = 0.75f;

        [Min(32f)]
        [Tooltip("Spatial cell size used for CPU distance culling.")]
        public float cellSize = 300f;

        [Header("Source Tree Models")]
        [Tooltip("Use the lowest mesh-based LOD found in each Terrain tree prefab. This preserves the real silhouette of each tree type.")]
        public bool useSourceTreeMeshes = true;

        [Tooltip("If a source prefab has no usable mesh LOD, use the procedural proxy as a fallback.")]
        public bool fallbackToProceduralProxy = true;

        [Range(0, 3)]
        [Tooltip("0 uses the lowest usable mesh LOD. 1 uses one mesh LOD higher, etc. Increase only if the far trees are too simple.")]
        public int lodStepsFromLowest = 0;

        [Header("Source Tree Scale")]
        [Range(0.25f, 3f)] public float widthMultiplier = 1f;
        [Range(0.25f, 3f)] public float heightMultiplier = 1f;

        [Range(0f, 0.3f)]
        [Tooltip("Small deterministic per-tree size variation.")]
        public float sizeVariation = 0.06f;

        [Range(-5f, 5f)]
        [Tooltip("Moves the distant tree layer vertically. A small negative value can help seat tree roots into distant terrain.")]
        public float verticalOffset = 0f;

        [Header("Fallback Proxy")]
        [Min(0.5f)] public float fallbackTreeWidth = 6f;
        [Min(0.5f)] public float fallbackTreeHeight = 14f;
        [Min(0.5f)] public float minimumTreeHeight = 4f;
        [Min(1f)] public float maximumTreeHeight = 40f;

        [Range(0f, 1f)]
        [Tooltip("Fallback only. 0 = rounded broadleaf, 1 = pointed conifer.")]
        public float fallbackCanopyShape = 0.45f;

        [Header("Colour / Atmospheric Blend")]
        [ColorUsage(false, true)]
        [Tooltip("Multiplies the source tree colour. Usually leave close to white.")]
        public Color sourceColourMultiplier = Color.white;

        [ColorUsage(false, true)]
        [Tooltip("Colour the far forest gently blends toward. A pale blue-green usually works better than black/grey.")]
        public Color atmosphereTint = new Color(0.48f, 0.62f, 0.56f, 1f);

        [Range(0f, 1f)]
        [Tooltip("How much the source tree colour is shifted toward Atmosphere Tint at full distant visibility.")]
        public float atmosphereTintStrength = 0.32f;

        [Range(0.25f, 2f)]
        [Tooltip("Overall brightness of the distant trees.")]
        public float brightness = 1.05f;

        [Range(0f, 0.35f)]
        public float colourVariation = 0.08f;

        [Header("Distance Softening")]
        [Range(0f, 3f)]
        [Tooltip("Samples a softer texture mip for distant foliage. Around 0.75-1.5 removes the razor-sharp leaf-card look.")]
        public float textureMipBias = 1.0f;

        [Range(0.25f, 6f)]
        [Tooltip("Softens alpha-cutout edges in screen space before dithering them. Higher values create a gentler distant canopy edge.")]
        public float edgeSoftness = 2.0f;

        [Range(-0.25f, 0.25f)]
        [Tooltip("Offsets the source material alpha cutoff. A small negative value fills tiny holes in distant foliage.")]
        public float alphaCutoffOffset = -0.04f;

        [Header("Scene Integration")]
        [Range(0f, 1f)]
        [Tooltip("How strongly distant trees respond to the scene's ambient and main directional light. 0 keeps source colours unlit; 1 uses full simple lighting.")]
        public float sceneLightingStrength = 0.55f;

        [Range(0.05f, 1.5f)]
        [Tooltip("Forest fog strength. In v0.3+ the fullscreen fog intentionally skips distant-forest pixels, so the forest shader applies the same fog itself. 1 = normal match.")]
        public float customFogIntegration = 1.0f;


        [Header("Height Fog Blending")]
        [Range(1f, 4f)]
        [Tooltip("Softens the height-fog boundary specifically across distant tree canopies. Higher values make the fog ceiling less razor sharp.")]
        public float heightFogSoftness = 1.8f;

        [Range(0f, 80f)]
        [Tooltip("Allows a small amount of forest fog to feather above the global Fog Height instead of ending at a perfectly flat ceiling.")]
        public float heightFogBlendAboveTop = 18f;

        [Range(0f, 1f)]
        [Tooltip("0 fogs each leaf by its own height. 1 biases canopy fog toward the tree's local base height so tree tops do not poke sharply through valley mist.")]
        public float treeBaseFogging = 0.72f;

        [Range(-30f, 30f)]
        [Tooltip("Fine-tunes the estimated tree base used by height fog. Use a negative value if a source tree mesh pivot sits too high in the canopy.")]
        public float treeBaseFogHeightBias = -2f;

        [Range(0f, 1.5f)]
        [Tooltip("Strength of the extra canopy-only atmospheric compensation used to close the visual gap between terrain height fog and distant tree tops.")]
        public float heightFogCompensation = 0.55f;

        [Range(0f, 1f)]
        [Tooltip("Maximum extra canopy compensation. This prevents high valley fog from bleaching distant trees almost white.")]
        public float heightFogCompensationLimit = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("How much the EXTRA tree-base/soft-height fog is allowed to influence fog colour. Lower values preserve canopy colour while still softening the fog boundary.")]
        public float heightFogColourInfluence = 0.25f;

        [Header("Horizon Blend")]
        [Range(0f, 1f)]
        [Tooltip("Where the extra far-horizon atmospheric blend starts across the distant forest range. 0 = near fade-in, 1 = maximum distance.")]
        public float horizonFadeStart = 0.48f;

        [Range(0f, 1f)]
        [Tooltip("How strongly very distant trees lose local contrast. This no longer blends directly to the fog colour.")]
        public float horizonFadeStrength = 0.22f;

        [Header("Alpha Cutout")]
        [Range(0.01f, 0.95f)]
        [Tooltip("Fallback alpha cutoff if the source material does not expose one.")]
        public float fallbackAlphaCutoff = 0.35f;

        [Header("Rendering")]
        [Tooltip("Distant trees normally should not cast shadows.")]
        public bool castShadows = false;

        [Tooltip("Usually leave off for the simplified distant material.")]
        public bool receiveShadows = false;

        private void OnValidate()
        {
            fullyVisibleDistance = Mathf.Max(fullyVisibleDistance, fadeInStart + 1f);
            fadeOutStart = Mathf.Max(fadeOutStart, fullyVisibleDistance + 1f);
            maximumDistance = Mathf.Max(maximumDistance, fadeOutStart + 1f);
            cellSize = Mathf.Max(32f, cellSize);
            maximumTreeHeight = Mathf.Max(maximumTreeHeight, minimumTreeHeight);
            heightFogSoftness = Mathf.Max(1f, heightFogSoftness);
            heightFogBlendAboveTop = Mathf.Max(0f, heightFogBlendAboveTop);
            heightFogCompensationLimit = Mathf.Clamp01(heightFogCompensationLimit);
            heightFogColourInfluence = Mathf.Clamp01(heightFogColourInfluence);

            // Migration from the old architecture, where 0 meant "let the fullscreen pass do it".
            // v0.3+ deliberately excludes the distant forest from the fullscreen pass.
            if (customFogIntegration <= 0.001f)
                customFogIntegration = 1f;

            customFogIntegration = Mathf.Clamp(customFogIntegration, 0.05f, 1.5f);

            ProfileValidated?.Invoke(this);
        }
    }
}
