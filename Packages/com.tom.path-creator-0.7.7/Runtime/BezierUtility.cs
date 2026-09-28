using UnityEngine;

namespace Tom.PathCreator
{
    /// <summary>
    /// Utility methods for evaluating cubic Bézier curves.
    /// </summary>
    public static class BezierUtility
    {
        public static Vector3 EvaluateCubic(
            Vector3 anchorA,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 anchorB,
            float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;

            return
                oneMinusT * oneMinusT * oneMinusT * anchorA +
                3f * oneMinusT * oneMinusT * t * controlA +
                3f * oneMinusT * t * t * controlB +
                t * t * t * anchorB;
        }

        public static Vector3 EvaluateCubicDerivative(
            Vector3 anchorA,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 anchorB,
            float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;

            return
                3f * oneMinusT * oneMinusT * (controlA - anchorA) +
                6f * oneMinusT * t * (controlB - controlA) +
                3f * t * t * (anchorB - controlB);
        }
    }
}
