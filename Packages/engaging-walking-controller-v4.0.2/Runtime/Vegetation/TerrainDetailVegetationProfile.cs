using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Maps Unity Terrain detail layers to movement resistance and rustle audio.
    /// Detail layer indices match the order shown under Paint Details on the Terrain.
    /// </summary>
    [CreateAssetMenu(menuName = "Walking Controller/Terrain Detail Vegetation Profile", fileName = "Terrain Detail Vegetation Profile")]
    public sealed class TerrainDetailVegetationProfile : ScriptableObject
    {
        [Serializable]
        public sealed class DetailLayerRule
        {
            [Tooltip("Index of the Terrain detail prototype. The first detail layer is 0.")]
            [Min(0)] public int detailLayerIndex;

            [Tooltip("How strongly a fully dense patch contributes to vegetation interaction.")]
            [Range(0f, 1f)] public float resistance = 0.35f;

            [Tooltip("Extra exertion at full density. 0 means no additional exertion; 1 means double exertion.")]
            [Range(0f, 2f)] public float additionalExertion = 0.25f;

            [Tooltip("Surface profile containing the rustle clips for this vegetation layer.")]
            public TerrainSurfaceProfile vegetationAudioProfile;
        }

        [Tooltip("Rules for detail layers that should affect the player. Unlisted detail layers are ignored.")]
        public List<DetailLayerRule> layers = new();

        [Tooltip("Controls how overlapping detail layers combine. Additive is usually best for mixed scrub.")]
        public bool combineAdditively = true;
    }
}
