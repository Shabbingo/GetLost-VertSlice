using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Environment
{
    public enum TerrainLandformType
    {
        Mountain,
        Basin,
        Plateau,
        Pass
    }

    /// <summary>
    /// Optional authoring instruction consumed by the Get Lost Terrain Generator.
    /// The marker remains useful after generation as documentation of design intent.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Get Lost/Terrain/Landform Stamp")]
    public sealed class GetLostTerrainLandformStamp : MonoBehaviour
    {
        public TerrainLandformType type = TerrainLandformType.Mountain;
        [Min(10f)] public float radiusX = 450f;
        [Min(10f)] public float radiusZ = 450f;
        [Tooltip("Metres added for mountains or removed for basins and passes.")]
        public float strengthMetres = 180f;
        [Tooltip("World height used by Plateau stamps.")]
        public float plateauHeight = 150f;
        [Range(0.25f, 6f)] public float blendPower = 1.6f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = type switch
            {
                TerrainLandformType.Mountain => new Color(1f, .45f, .1f, .8f),
                TerrainLandformType.Basin => new Color(.15f, .55f, 1f, .8f),
                TerrainLandformType.Plateau => new Color(1f, .85f, .15f, .8f),
                _ => new Color(.35f, 1f, .45f, .8f)
            };
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                Quaternion.Euler(0f, transform.eulerAngles.y, 0f),
                new Vector3(radiusX * 2f, 1f, radiusZ * 2f));
            Gizmos.DrawWireSphere(Vector3.zero, .5f);
            Gizmos.matrix = previous;
        }
    }

    /// <summary>
    /// A designer-authored playable lane. Add ordered child transforms to define
    /// the route. Their Y positions establish the desired corridor elevation.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Get Lost/Terrain/Route Corridor")]
    public sealed class GetLostTerrainRouteCorridor : MonoBehaviour
    {
        [Min(4f)] public float playableHalfWidth = 32f;
        [Min(0f)] public float shoulderWidth = 95f;
        [Min(0f)] public float shoulderHeight = 55f;
        [Range(0f, 1f)] public float floorBlend = .9f;
        public float floorOffset;

        public List<Transform> GetPoints()
        {
            List<Transform> points = new();
            for (int i = 0; i < transform.childCount; i++)
                points.Add(transform.GetChild(i));
            return points;
        }

        private void OnDrawGizmosSelected()
        {
            List<Transform> points = GetPoints();
            if (points.Count < 2)
                return;
            Gizmos.color = new Color(.1f, 1f, .85f, .95f);
            for (int i = 1; i < points.Count; i++)
            {
                Gizmos.DrawLine(points[i - 1].position, points[i].position);
                DrawWidth(points[i - 1].position, points[i].position, playableHalfWidth);
            }
        }

        private static void DrawWidth(Vector3 a, Vector3 b, float halfWidth)
        {
            Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized * halfWidth;
            Gizmos.DrawLine(a - side, b - side);
            Gizmos.DrawLine(a + side, b + side);
        }
    }
}
