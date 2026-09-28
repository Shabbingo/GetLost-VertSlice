using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class TerrainFoliageExclusionVolume : MonoBehaviour
    {
        [Min(0f)] public float edgeFalloff = 0f;
        public bool affectGameObjects = true;
        public bool affectTerrainDetails = true;
        public bool affectTerrainTrees = true;

        private Collider cachedCollider;

        public bool Affects(TerrainFoliageOutputMode mode)
        {
            return mode == TerrainFoliageOutputMode.GameObject ? affectGameObjects :
                   (mode == TerrainFoliageOutputMode.TerrainDetail ||
                    mode == TerrainFoliageOutputMode.InstancedGrass) ? affectTerrainDetails :
                   affectTerrainTrees;
        }

        public float GetKeepProbability(Vector3 worldPosition)
        {
            Collider volume = cachedCollider != null ? cachedCollider : (cachedCollider = GetComponent<Collider>());
            if (volume == null || !volume.enabled || !isActiveAndEnabled)
                return 1f;

            Vector3 closest = volume.ClosestPoint(worldPosition);
            float distance = Vector3.Distance(closest, worldPosition);

            // ClosestPoint returns the input point while it is inside the collider.
            if (distance <= 0.0001f)
                return 0f;

            if (edgeFalloff <= 0f || distance >= edgeFalloff)
                return 1f;

            return Mathf.Clamp01(distance / edgeFalloff);
        }

        private void OnDrawGizmosSelected()
        {
            Collider volume = GetComponent<Collider>();
            if (volume == null) return;
            Gizmos.DrawWireCube(volume.bounds.center, volume.bounds.size);
        }
    }
}
