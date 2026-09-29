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

        public int RemovePlacementsInRadius(
            Vector3 worldPosition,
            float radius,
            GameObject prefabFilter = null)
        {
            if (placements == null || placements.Count == 0 || radius < 0f)
                return 0;

            float radiusSquared = radius * radius;
            int removed = 0;
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                Placement placement = placements[i];
                if (prefabFilter != null &&
                    (placement.prefabIndex < 0 || placement.prefabIndex >= prefabs.Count ||
                     prefabs[placement.prefabIndex] != prefabFilter))
                {
                    continue;
                }

                Vector3 delta = placement.position - worldPosition;
                if (delta.x * delta.x + delta.z * delta.z > radiusSquared)
                    continue;

                int last = placements.Count - 1;
                placements[i] = placements[last];
                placements.RemoveAt(last);
                removed++;
            }

            if (removed > 0)
                RecalculateWorldBounds();
            return removed;
        }

        public bool HasPlacementWithin(
            Vector3 worldPosition,
            float radius,
            GameObject prefabFilter = null)
        {
            if (placements == null || radius < 0f)
                return false;

            float radiusSquared = radius * radius;
            for (int i = 0; i < placements.Count; i++)
            {
                Placement placement = placements[i];
                if (prefabFilter != null &&
                    (placement.prefabIndex < 0 || placement.prefabIndex >= prefabs.Count ||
                     prefabs[placement.prefabIndex] != prefabFilter))
                {
                    continue;
                }

                Vector3 delta = placement.position - worldPosition;
                if (delta.x * delta.x + delta.z * delta.z <= radiusSquared)
                    return true;
            }
            return false;
        }

        private void RecalculateWorldBounds()
        {
            if (placements.Count == 0)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }

            worldBounds = new Bounds(placements[0].position, Vector3.zero);
            for (int i = 1; i < placements.Count; i++)
                worldBounds.Encapsulate(placements[i].position);
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

        public int RemoveGrassPlacementsInRadius(
            Vector3 worldPosition,
            float radius,
            GameObject prefabFilter = null)
        {
            if (grassPlacements == null || grassPlacements.Count == 0 || radius < 0f)
                return 0;

            float radiusSquared = radius * radius;
            int removed = 0;
            for (int i = grassPlacements.Count - 1; i >= 0; i--)
            {
                Placement placement = grassPlacements[i];
                if (prefabFilter != null &&
                    (placement.prefabIndex < 0 || placement.prefabIndex >= grassPrefabs.Count ||
                     grassPrefabs[placement.prefabIndex] != prefabFilter))
                {
                    continue;
                }

                Vector3 delta = placement.position - worldPosition;
                if (delta.x * delta.x + delta.z * delta.z > radiusSquared)
                    continue;

                int last = grassPlacements.Count - 1;
                grassPlacements[i] = grassPlacements[last];
                grassPlacements.RemoveAt(last);
                removed++;
            }

            if (removed > 0)
                RecalculateGrassWorldBounds();
            return removed;
        }

        public bool HasGrassPlacementWithin(
            Vector3 worldPosition,
            float radius,
            GameObject prefabFilter = null)
        {
            if (grassPlacements == null || radius < 0f)
                return false;

            float radiusSquared = radius * radius;
            for (int i = 0; i < grassPlacements.Count; i++)
            {
                Placement placement = grassPlacements[i];
                if (prefabFilter != null &&
                    (placement.prefabIndex < 0 || placement.prefabIndex >= grassPrefabs.Count ||
                     grassPrefabs[placement.prefabIndex] != prefabFilter))
                {
                    continue;
                }

                Vector3 delta = placement.position - worldPosition;
                if (delta.x * delta.x + delta.z * delta.z <= radiusSquared)
                    return true;
            }
            return false;
        }

        private void RecalculateGrassWorldBounds()
        {
            if (grassPlacements.Count == 0)
            {
                grassWorldBounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }

            grassWorldBounds = new Bounds(grassPlacements[0].position, Vector3.zero);
            for (int i = 1; i < grassPlacements.Count; i++)
                grassWorldBounds.Encapsulate(grassPlacements[i].position);
        }
    }
}
