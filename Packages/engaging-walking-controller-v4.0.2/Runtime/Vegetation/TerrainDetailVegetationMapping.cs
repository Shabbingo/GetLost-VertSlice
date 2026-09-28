using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Maps actual Terrain detail prototype assets to vegetation profiles.
    /// Reordering Terrain detail layers does not break these mappings.
    /// </summary>
    [CreateAssetMenu(menuName = "Walking Controller/Vegetation/Terrain Detail Mapping", fileName = "Terrain Detail Vegetation Mapping")]
    public sealed class TerrainDetailVegetationMapping : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Drag the prefab used by a mesh detail, or the texture used by a grass detail.")]
            public UnityEngine.Object detailPrototypeAsset;

            public VegetationProfile vegetationProfile;
        }

        [Tooltip("Mappings are matched against TerrainData.detailPrototypes by asset reference, not layer index.")]
        public List<Entry> entries = new();

        public bool TryGetEntry(DetailPrototype prototype, out Entry matchedEntry)
        {
            UnityEngine.Object asset = GetPrototypeAsset(prototype);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.detailPrototypeAsset == asset && entry.vegetationProfile != null)
                {
                    matchedEntry = entry;
                    return true;
                }
            }

            matchedEntry = null;
            return false;
        }

        public bool TryGetProfile(DetailPrototype prototype, out VegetationProfile profile)
        {
            return TryGetProfile(GetPrototypeAsset(prototype), out profile);
        }

        public bool TryGetProfile(UnityEngine.Object asset, out VegetationProfile profile)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null &&
                    entry.detailPrototypeAsset == asset &&
                    entry.vegetationProfile != null)
                {
                    profile = entry.vegetationProfile;
                    return true;
                }
            }

            profile = null;
            return false;
        }

        public static UnityEngine.Object GetPrototypeAsset(DetailPrototype prototype)
        {
            if (prototype == null)
                return null;
            return prototype.prototype != null ? prototype.prototype : prototype.prototypeTexture;
        }
    }
}
