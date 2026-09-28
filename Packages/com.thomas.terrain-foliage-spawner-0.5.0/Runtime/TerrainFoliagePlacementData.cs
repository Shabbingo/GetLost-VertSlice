using System;
using System.Collections.Generic;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [CreateAssetMenu(fileName = "Terrain Foliage Placements", menuName = "Thomas/Terrain Foliage Placement Data")]
    public sealed class TerrainFoliagePlacementData : ScriptableObject
    {
        [Serializable]
        public struct Placement
        {
            public int prefabIndex;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [SerializeField] private List<GameObject> prefabs = new List<GameObject>();
        [SerializeField] private List<Placement> placements = new List<Placement>();
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private List<GameObject> grassPrefabs = new List<GameObject>();
        [SerializeField] private List<Placement> grassPlacements = new List<Placement>();
        [SerializeField] private Bounds grassWorldBounds;

        public IReadOnlyList<GameObject> Prefabs => prefabs;
        public IReadOnlyList<Placement> Placements => placements;
        public Bounds WorldBounds => worldBounds;
        public int Count => placements != null ? placements.Count : 0;
        public IReadOnlyList<GameObject> GrassPrefabs => grassPrefabs;
        public IReadOnlyList<Placement> GrassPlacements => grassPlacements;
        public Bounds GrassWorldBounds => grassWorldBounds;
        public int GrassCount => grassPlacements != null ? grassPlacements.Count : 0;

        public void ClearData()
        {
            prefabs.Clear();
            placements.Clear();
            worldBounds = new Bounds(Vector3.zero, Vector3.zero);
            grassPrefabs.Clear();
            grassPlacements.Clear();
            grassWorldBounds = new Bounds(Vector3.zero, Vector3.zero);
        }

        public int GetOrAddPrefab(GameObject prefab)
        {
            int index = prefabs.IndexOf(prefab);
            if (index >= 0) return index;
            prefabs.Add(prefab);
            return prefabs.Count - 1;
        }

        public void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            int prefabIndex = GetOrAddPrefab(prefab);
            placements.Add(new Placement
            {
                prefabIndex = prefabIndex,
                position = position,
                rotation = rotation,
                scale = scale
            });

            if (placements.Count == 1) worldBounds = new Bounds(position, Vector3.zero);
            else worldBounds.Encapsulate(position);
        }

        public void AddGrass(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            int prefabIndex = grassPrefabs.IndexOf(prefab);
            if (prefabIndex < 0)
            {
                grassPrefabs.Add(prefab);
                prefabIndex = grassPrefabs.Count - 1;
            }

            grassPlacements.Add(new Placement
            {
                prefabIndex = prefabIndex,
                position = position,
                rotation = rotation,
                scale = scale
            });

            if (grassPlacements.Count == 1)
                grassWorldBounds = new Bounds(position, Vector3.zero);
            else
                grassWorldBounds.Encapsulate(position);
        }
    }
}
