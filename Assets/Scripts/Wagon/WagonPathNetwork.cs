using System;
using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Wagon
{
    /// <summary>
    /// Runtime, world-space representation of every path deposited by the wagon.
    /// Systems can query Points/Contains, or detect WagonPathSegment trigger volumes.
    /// </summary>
    public sealed class WagonPathNetwork : MonoBehaviour
    {
        /// <summary>
        /// Raised for each newly committed centre-line segment. Mission detection
        /// subscribes through an adapter so progression is not tied to path colliders.
        /// </summary>
        public static event Action<Vector3, Vector3, float> AnyPathSegmentAdded;

        public IReadOnlyList<Vector3> Points => points;
        public float PathWidth { get; private set; }

        private readonly List<Vector3> points = new();
        private float triggerWidth;
        private float triggerHeight;
        private float triggerSpacing;
        private float triggerOverlap;
        private Vector3 lastTriggerPoint;
        private bool hasTriggerPoint;

        public void Configure(float pathWidth, float widthPadding, float height,
            float spacing, float overlap)
        {
            PathWidth = Mathf.Max(0.1f, pathWidth);
            triggerWidth = PathWidth + Mathf.Max(0f, widthPadding) * 2f;
            triggerHeight = Mathf.Max(0.1f, height);
            triggerSpacing = Mathf.Max(0.1f, spacing);
            triggerOverlap = Mathf.Max(0.02f, overlap);
        }

        public void AddPolyline(IReadOnlyList<Vector3> newPoints)
        {
            if (newPoints == null || newPoints.Count < 2) return;

            for (int i = 1; i < newPoints.Count; i++)
            {
                Vector3 segmentStart = newPoints[i - 1];
                Vector3 segmentEnd = newPoints[i];
                if (Vector3.ProjectOnPlane(segmentEnd - segmentStart, Vector3.up).sqrMagnitude > 0.0001f)
                    AnyPathSegmentAdded?.Invoke(segmentStart, segmentEnd, PathWidth);
            }

            for (int i = 0; i < newPoints.Count; i++)
            {
                Vector3 point = newPoints[i];
                if (points.Count == 0 ||
                    Vector3.ProjectOnPlane(point - points[points.Count - 1], Vector3.up).sqrMagnitude > 0.0001f)
                    points.Add(point);

                if (!hasTriggerPoint)
                {
                    lastTriggerPoint = point;
                    hasTriggerPoint = true;
                    continue;
                }

                Vector3 cursor = lastTriggerPoint;
                float remaining = Vector3.ProjectOnPlane(point - cursor, Vector3.up).magnitude;
                while (remaining >= triggerSpacing)
                {
                    float t = triggerSpacing / remaining;
                    Vector3 next = Vector3.Lerp(cursor, point, t);
                    CreateTrigger(cursor, next);
                    cursor = next;
                    lastTriggerPoint = next;
                    remaining = Vector3.ProjectOnPlane(point - cursor, Vector3.up).magnitude;
                }
            }

            // Close the current batch so its final bend is represented immediately.
            Vector3 end = newPoints[newPoints.Count - 1];
            if (hasTriggerPoint &&
                Vector3.ProjectOnPlane(end - lastTriggerPoint, Vector3.up).sqrMagnitude > 0.0001f)
            {
                CreateTrigger(lastTriggerPoint, end);
                lastTriggerPoint = end;
            }
        }

        public bool Contains(Vector3 worldPosition, float extraRadius = 0f)
        {
            float radius = PathWidth * 0.5f + Mathf.Max(0f, extraRadius);
            float radiusSquared = radius * radius;
            for (int i = 1; i < points.Count; i++)
                if (WagonMath.DistanceToSegmentXZ(worldPosition, points[i - 1], points[i]) <= radius)
                    return true;
            return false;
        }

        private void CreateTrigger(Vector3 start, Vector3 end)
        {
            Vector3 planar = Vector3.ProjectOnPlane(end - start, Vector3.up);
            float length = planar.magnitude;
            if (length < 0.01f) return;

            var segmentObject = new GameObject($"Path Trigger {transform.childCount:0000}");
            segmentObject.transform.SetParent(transform, true);
            segmentObject.transform.SetPositionAndRotation(
                (start + end) * 0.5f + Vector3.up * (triggerHeight * 0.5f - 0.05f),
                Quaternion.LookRotation(planar.normalized, Vector3.up));
            BoxCollider trigger = segmentObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(triggerWidth, triggerHeight, length + triggerOverlap);
            WagonPathSegment segment = segmentObject.AddComponent<WagonPathSegment>();
            segment.Initialize(this, start, end);
        }
    }

    /// <summary>Marker found on every generated path trigger.</summary>
    public sealed class WagonPathSegment : MonoBehaviour
    {
        public WagonPathNetwork Network { get; private set; }
        public Vector3 Start { get; private set; }
        public Vector3 End { get; private set; }

        public void Initialize(WagonPathNetwork network, Vector3 start, Vector3 end)
        {
            Network = network;
            Start = start;
            End = end;
        }
    }
}
