using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    [CreateAssetMenu(menuName = "Walking Controller/Terrain Layer Surface Map", fileName = "Terrain Layer Surface Map")]
    public sealed class TerrainLayerSurfaceMap : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public TerrainLayer terrainLayer;
            public TerrainSurfaceProfile surfaceProfile;
        }

        [SerializeField] private List<Entry> entries = new();

        public TerrainSurfaceProfile FindProfile(TerrainLayer layer)
        {
            if (layer == null)
                return null;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].terrainLayer == layer)
                    return entries[i].surfaceProfile;
            }

            return null;
        }
    }
}
