using System;
using UnityEngine;

namespace GetLost.Wagon
{
    [Serializable]
    public sealed class GravelStrip
    {
        public Vector3 start;
        public Vector3 end;
    }

    [Serializable]
    public sealed class WagonSaveData
    {
        public bool hasWagon;
        public bool hasBeenUsed;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float gravel = 120f;
        public float capacity = 120f;
        public System.Collections.Generic.List<GravelStrip> strips = new();
    }

    /// <summary>Small, deterministic helpers shared by runtime and the physics validation scene.</summary>
    public static class WagonMath
    {
        public static Vector3 LimitHandleDisplacement(Vector3 player, Vector3 grip,
            Vector3 displacement, float slack)
        {
            Vector3 candidate = player + displacement;
            Vector3 offset = Vector3.ProjectOnPlane(candidate - grip, Vector3.up);
            if (offset.sqrMagnitude > slack * slack)
            {
                Vector3 correction = offset - offset.normalized * slack;
                displacement -= correction;
            }
            return displacement;
        }

        public static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.00001f
                ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
