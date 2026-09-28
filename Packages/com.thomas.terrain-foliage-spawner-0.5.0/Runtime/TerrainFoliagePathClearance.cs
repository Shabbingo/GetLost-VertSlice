using System.Collections.Generic;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [DisallowMultipleComponent]
    public sealed class TerrainFoliagePathClearance : MonoBehaviour
    {
        [Tooltip("Ordered transforms forming the path centre line.")]
        public List<Transform> points = new List<Transform>();

        [Min(0f)] public float clearRadius = 1.2f;
        [Min(0f)] public float falloffRadius = 2.5f;
        public bool affectGameObjects = true;
        public bool affectTerrainDetails = true;
        public bool affectTerrainTrees = true;

        public bool Affects(TerrainFoliageOutputMode mode)
        {
            return mode == TerrainFoliageOutputMode.GameObject ? affectGameObjects :
                   (mode == TerrainFoliageOutputMode.TerrainDetail ||
                    mode == TerrainFoliageOutputMode.InstancedGrass) ? affectTerrainDetails :
                   affectTerrainTrees;
        }

        public float GetKeepProbability(Vector3 worldPosition)
        {
            if (!isActiveAndEnabled || points == null || points.Count < 2)
                return 1f;

            float bestDistance = float.PositiveInfinity;
            Vector2 p = new Vector2(worldPosition.x, worldPosition.z);

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null) continue;
                Vector3 a3 = points[i].position;
                Vector3 b3 = points[i + 1].position;
                Vector2 a = new Vector2(a3.x, a3.z);
                Vector2 b = new Vector2(b3.x, b3.z);
                bestDistance = Mathf.Min(bestDistance, DistanceToSegment(p, a, b));
            }

            if (bestDistance <= clearRadius)
                return 0f;

            float outerRadius = clearRadius + falloffRadius;
            if (falloffRadius <= 0f || bestDistance >= outerRadius)
                return 1f;

            return Mathf.InverseLerp(clearRadius, outerRadius, bestDistance);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 0.000001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
            return Vector2.Distance(p, a + ab * t);
        }

        private void OnDrawGizmosSelected()
        {
            if (points == null || points.Count < 2) return;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null) continue;
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
            }
        }
    }
}
