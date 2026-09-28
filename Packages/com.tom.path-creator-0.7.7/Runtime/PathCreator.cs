using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tom.PathCreator
{
    public enum PathHandleMode
    {
        Free,
        Aligned,
        Mirrored,
        Automatic
    }

    [Serializable]
    public sealed class PathAnchor
    {
        public Vector3 position;
        public Vector3 handleIn;
        public Vector3 handleOut;
        public PathHandleMode mode = PathHandleMode.Aligned;

        public PathAnchor(Vector3 position)
        {
            this.position = position;
            handleIn = position + Vector3.left;
            handleOut = position + Vector3.right;
        }
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PathCreator : MonoBehaviour
    {
        [SerializeField]
        private List<PathAnchor> anchors = new()
        {
            new PathAnchor(new Vector3(-4f, 0f, 0f))
            {
                handleIn = new Vector3(-6f, 0f, 0f),
                handleOut = new Vector3(-2f, 0f, 3f),
                mode = PathHandleMode.Aligned
            },
            new PathAnchor(new Vector3(4f, 0f, 0f))
            {
                handleIn = new Vector3(2f, 0f, 3f),
                handleOut = new Vector3(6f, 0f, 0f),
                mode = PathHandleMode.Aligned
            }
        };

        [SerializeField]
        private bool closed;

        [SerializeField, Min(2)]
        private int previewResolutionPerSegment = 20;

        [SerializeField, Min(4)]
        private int lengthResolutionPerSegment = 30;

        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private Color pathColor = new(1f, 0.65f, 0.1f, 1f);

        private bool cacheDirty = true;
        private float approximateLength;
        private Bounds worldBounds;

        public event Action PathChanged;

        public IReadOnlyList<PathAnchor> Anchors => anchors;
        public int AnchorCount => anchors?.Count ?? 0;
        public bool Closed => closed;
        public int SegmentCount => closed ? AnchorCount : Mathf.Max(0, AnchorCount - 1);
        public int PreviewResolutionPerSegment => previewResolutionPerSegment;
        public float ApproximateLength
        {
            get
            {
                EnsureCache();
                return approximateLength;
            }
        }

        public Bounds WorldBounds
        {
            get
            {
                EnsureCache();
                return worldBounds;
            }
        }

        public PathAnchor GetAnchor(int index)
        {
            ValidateAnchorIndex(index);
            return anchors[index];
        }

        public Vector3 GetAnchorWorldPosition(int index)
        {
            return transform.TransformPoint(GetAnchor(index).position);
        }

        public Vector3 GetHandleInWorldPosition(int index)
        {
            return transform.TransformPoint(GetAnchor(index).handleIn);
        }

        public Vector3 GetHandleOutWorldPosition(int index)
        {
            return transform.TransformPoint(GetAnchor(index).handleOut);
        }

        public void SetClosed(bool value)
        {
            if (closed == value)
            {
                return;
            }

            closed = value;
            RecalculateAutomaticHandles();
            NotifyPathChanged();
        }

        public void SetAnchorWorldPosition(int index, Vector3 worldPosition)
        {
            ValidateAnchorIndex(index);
            PathAnchor anchor = anchors[index];
            Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
            Vector3 delta = localPosition - anchor.position;

            anchor.position = localPosition;
            anchor.handleIn += delta;
            anchor.handleOut += delta;

            RecalculateAutomaticHandles();
            NotifyPathChanged();
        }

        public void SetHandleInWorldPosition(int index, Vector3 worldPosition)
        {
            ValidateAnchorIndex(index);
            PathAnchor anchor = anchors[index];

            if (anchor.mode == PathHandleMode.Automatic)
            {
                anchor.mode = PathHandleMode.Aligned;
            }

            anchor.handleIn = transform.InverseTransformPoint(worldPosition);
            EnforceHandleMode(index, editingIncoming: true);
            NotifyPathChanged();
        }

        public void SetHandleOutWorldPosition(int index, Vector3 worldPosition)
        {
            ValidateAnchorIndex(index);
            PathAnchor anchor = anchors[index];

            if (anchor.mode == PathHandleMode.Automatic)
            {
                anchor.mode = PathHandleMode.Aligned;
            }

            anchor.handleOut = transform.InverseTransformPoint(worldPosition);
            EnforceHandleMode(index, editingIncoming: false);
            NotifyPathChanged();
        }

        public void SetHandleMode(int index, PathHandleMode mode)
        {
            ValidateAnchorIndex(index);
            anchors[index].mode = mode;

            if (mode == PathHandleMode.Automatic)
            {
                RecalculateAutomaticHandles();
            }
            else
            {
                EnforceHandleMode(index, editingIncoming: false);
            }

            NotifyPathChanged();
        }

        public Vector3 EvaluatePosition(float normalizedTime)
        {
            if (SegmentCount == 0)
            {
                return transform.position;
            }

            normalizedTime = closed
                ? Mathf.Repeat(normalizedTime, 1f)
                : Mathf.Clamp01(normalizedTime);

            float scaledTime = normalizedTime * SegmentCount;
            int segmentIndex = Mathf.Min(Mathf.FloorToInt(scaledTime), SegmentCount - 1);
            float segmentTime = scaledTime - segmentIndex;

            if (!closed && normalizedTime >= 1f)
            {
                segmentIndex = SegmentCount - 1;
                segmentTime = 1f;
            }

            GetSegmentLocalPoints(
                segmentIndex,
                out Vector3 a,
                out Vector3 b,
                out Vector3 c,
                out Vector3 d);

            return transform.TransformPoint(
                BezierUtility.EvaluateCubic(a, b, c, d, segmentTime));
        }

        public Vector3 EvaluateTangent(float normalizedTime)
        {
            if (SegmentCount == 0)
            {
                return transform.forward;
            }

            normalizedTime = closed
                ? Mathf.Repeat(normalizedTime, 1f)
                : Mathf.Clamp01(normalizedTime);

            float scaledTime = normalizedTime * SegmentCount;
            int segmentIndex = Mathf.Min(Mathf.FloorToInt(scaledTime), SegmentCount - 1);
            float segmentTime = scaledTime - segmentIndex;

            if (!closed && normalizedTime >= 1f)
            {
                segmentIndex = SegmentCount - 1;
                segmentTime = 1f;
            }

            GetSegmentLocalPoints(
                segmentIndex,
                out Vector3 a,
                out Vector3 b,
                out Vector3 c,
                out Vector3 d);

            Vector3 tangent = BezierUtility.EvaluateCubicDerivative(a, b, c, d, segmentTime);
            return transform.TransformDirection(tangent).normalized;
        }

        public Vector3[] GetEvenlySpacedPoints(float spacing, float resolution = 1f)
        {
            spacing = Mathf.Max(0.01f, spacing);
            resolution = Mathf.Max(0.1f, resolution);

            if (SegmentCount == 0)
            {
                return new[] { transform.position };
            }

            var evenlySpacedPoints = new List<Vector3> { EvaluatePosition(0f) };
            Vector3 previousPoint = evenlySpacedPoints[0];
            float distanceSinceLastPoint = 0f;

            int divisions = Mathf.Max(
                SegmentCount * 2,
                Mathf.CeilToInt(SegmentCount * 30f * resolution));

            for (int i = 1; i <= divisions; i++)
            {
                float t = i / (float)divisions;
                Vector3 pointOnCurve = EvaluatePosition(t);
                distanceSinceLastPoint += Vector3.Distance(previousPoint, pointOnCurve);

                while (distanceSinceLastPoint >= spacing)
                {
                    float overshoot = distanceSinceLastPoint - spacing;
                    Vector3 direction = (previousPoint - pointOnCurve).normalized;
                    Vector3 newPoint = pointOnCurve + direction * overshoot;

                    evenlySpacedPoints.Add(newPoint);
                    distanceSinceLastPoint = overshoot;
                    previousPoint = newPoint;
                }

                previousPoint = pointOnCurve;
            }

            if (!closed)
            {
                Vector3 endPoint = EvaluatePosition(1f);
                if (Vector3.Distance(evenlySpacedPoints[^1], endPoint) > 0.001f)
                {
                    evenlySpacedPoints.Add(endPoint);
                }
            }

            return evenlySpacedPoints.ToArray();
        }

/// <summary>
/// Replaces the current spline with world-space points.
/// This is intended for runtime recording and import workflows.
/// </summary>
public void SetPathFromWorldPoints(
    IReadOnlyList<Vector3> worldPoints,
    PathHandleMode handleMode = PathHandleMode.Automatic)
{
    if (worldPoints == null)
    {
        throw new ArgumentNullException(nameof(worldPoints));
    }

    if (worldPoints.Count < 2)
    {
        throw new ArgumentException(
            "A path requires at least two points.",
            nameof(worldPoints));
    }

    var replacement = new List<PathAnchor>(worldPoints.Count);

    for (int i = 0; i < worldPoints.Count; i++)
    {
        Vector3 localPosition =
            transform.InverseTransformPoint(worldPoints[i]);

        replacement.Add(
            new PathAnchor(localPosition)
            {
                mode = handleMode
            });
    }

    anchors = replacement;
    closed = false;
    RecalculateAutomaticHandles();
    NotifyPathChanged();
}

/// <summary>
/// Updates one anchor or appends it when recording extends the path.
/// </summary>
public void SetOrAppendAnchorWorldPosition(
    int index,
    Vector3 worldPosition,
    PathHandleMode handleMode = PathHandleMode.Automatic)
{
    if (index < 0 || index > AnchorCount)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    if (index < AnchorCount)
    {
        anchors[index].mode = handleMode;
        SetAnchorWorldPosition(index, worldPosition);
        return;
    }

    Vector3 localPosition =
        transform.InverseTransformPoint(worldPosition);

    anchors.Add(
        new PathAnchor(localPosition)
        {
            mode = handleMode
        });

    closed = false;
    RecalculateAutomaticHandles();
    NotifyPathChanged();
}

        public int InsertAnchorAfter(int anchorIndex)
        {
            ValidateAnchorIndex(anchorIndex);

            if (!closed && anchorIndex >= AnchorCount - 1)
            {
                return AddAnchorAfterEnd();
            }

            int nextIndex = (anchorIndex + 1) % AnchorCount;
            Vector3 position = EvaluateSegmentLocalPosition(anchorIndex, 0.5f);
            var newAnchor = new PathAnchor(position)
            {
                mode = PathHandleMode.Automatic
            };

            anchors.Insert(nextIndex, newAnchor);
            RecalculateAutomaticHandles();
            NotifyPathChanged();
            return nextIndex;
        }

        public int AddAnchorAfterEnd()
        {
            if (AnchorCount == 0)
            {
                ResetPath();
                return AnchorCount - 1;
            }

            PathAnchor last = anchors[^1];
            Vector3 direction = last.position - anchors[Mathf.Max(0, AnchorCount - 2)].position;

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.forward * 4f;
            }

            Vector3 position = last.position + direction.normalized * Mathf.Max(4f, direction.magnitude);
            var anchor = new PathAnchor(position)
            {
                mode = PathHandleMode.Automatic
            };

            anchors.Add(anchor);
            RecalculateAutomaticHandles();
            NotifyPathChanged();
            return AnchorCount - 1;
        }

        public void DeleteAnchor(int index)
        {
            ValidateAnchorIndex(index);

            int minimum = closed ? 3 : 2;
            if (AnchorCount <= minimum)
            {
                return;
            }

            anchors.RemoveAt(index);
            RecalculateAutomaticHandles();
            NotifyPathChanged();
        }


/// <summary>
/// Removes every anchor so the path contains no visible or generated route.
/// Intended for resetting an active TrailSession after establishment or abandonment.
/// </summary>
public void ClearPath()
{
    anchors.Clear();
    closed = false;
    NotifyPathChanged();
}

        public void ResetPath()
        {
            anchors = new List<PathAnchor>
            {
                new PathAnchor(new Vector3(-4f, 0f, 0f))
                {
                    handleIn = new Vector3(-6f, 0f, 0f),
                    handleOut = new Vector3(-2f, 0f, 3f),
                    mode = PathHandleMode.Aligned
                },
                new PathAnchor(new Vector3(4f, 0f, 0f))
                {
                    handleIn = new Vector3(2f, 0f, 3f),
                    handleOut = new Vector3(6f, 0f, 0f),
                    mode = PathHandleMode.Aligned
                }
            };

            closed = false;
            NotifyPathChanged();
        }

        private Vector3 EvaluateSegmentLocalPosition(int segmentIndex, float t)
        {
            GetSegmentLocalPoints(segmentIndex, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d);
            return BezierUtility.EvaluateCubic(a, b, c, d, t);
        }

        private void GetSegmentLocalPoints(
            int segmentIndex,
            out Vector3 a,
            out Vector3 b,
            out Vector3 c,
            out Vector3 d)
        {
            if (segmentIndex < 0 || segmentIndex >= SegmentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            }

            int startIndex = segmentIndex;
            int endIndex = (segmentIndex + 1) % AnchorCount;

            PathAnchor start = anchors[startIndex];
            PathAnchor end = anchors[endIndex];

            a = start.position;
            b = start.handleOut;
            c = end.handleIn;
            d = end.position;
        }

        private void EnforceHandleMode(int index, bool editingIncoming)
        {
            PathAnchor anchor = anchors[index];

            if (anchor.mode == PathHandleMode.Free)
            {
                return;
            }

            Vector3 edited = editingIncoming ? anchor.handleIn : anchor.handleOut;
            Vector3 opposite = editingIncoming ? anchor.handleOut : anchor.handleIn;
            Vector3 direction = (anchor.position - edited).normalized;

            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            float oppositeLength = anchor.mode == PathHandleMode.Mirrored
                ? Vector3.Distance(anchor.position, edited)
                : Vector3.Distance(anchor.position, opposite);

            Vector3 adjustedOpposite = anchor.position + direction * oppositeLength;

            if (editingIncoming)
            {
                anchor.handleOut = adjustedOpposite;
            }
            else
            {
                anchor.handleIn = adjustedOpposite;
            }
        }

        private void RecalculateAutomaticHandles()
        {
            if (AnchorCount < 2)
            {
                return;
            }

            for (int i = 0; i < AnchorCount; i++)
            {
                PathAnchor anchor = anchors[i];
                if (anchor.mode != PathHandleMode.Automatic)
                {
                    continue;
                }

                bool hasPrevious = closed || i > 0;
                bool hasNext = closed || i < AnchorCount - 1;

                Vector3 previous = hasPrevious
                    ? anchors[(i - 1 + AnchorCount) % AnchorCount].position
                    : anchor.position;

                Vector3 next = hasNext
                    ? anchors[(i + 1) % AnchorCount].position
                    : anchor.position;

                Vector3 direction = (next - previous).normalized;
                float previousDistance = Vector3.Distance(anchor.position, previous);
                float nextDistance = Vector3.Distance(anchor.position, next);

                anchor.handleIn = anchor.position - direction * previousDistance * 0.35f;
                anchor.handleOut = anchor.position + direction * nextDistance * 0.35f;

                if (!hasPrevious)
                {
                    anchor.handleIn = anchor.position - (next - anchor.position).normalized * nextDistance * 0.2f;
                }

                if (!hasNext)
                {
                    anchor.handleOut = anchor.position + (anchor.position - previous).normalized * previousDistance * 0.2f;
                }
            }
        }

        private void EnsureCache()
        {
            if (!cacheDirty)
            {
                return;
            }

            cacheDirty = false;

            if (SegmentCount == 0)
            {
                approximateLength = 0f;
                worldBounds = new Bounds(transform.position, Vector3.zero);
                return;
            }

            int divisions = Mathf.Max(
                SegmentCount * 4,
                SegmentCount * lengthResolutionPerSegment);

            Vector3 previous = EvaluatePosition(0f);
            approximateLength = 0f;
            worldBounds = new Bounds(previous, Vector3.zero);

            for (int i = 1; i <= divisions; i++)
            {
                float t = i / (float)divisions;
                Vector3 current = EvaluatePosition(t);
                approximateLength += Vector3.Distance(previous, current);
                worldBounds.Encapsulate(current);
                previous = current;
            }
        }

        private void ValidateAnchorIndex(int index)
        {
            if (index < 0 || index >= AnchorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private void NotifyPathChanged()
        {
            cacheDirty = true;
            PathChanged?.Invoke();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void OnValidate()
        {
            previewResolutionPerSegment = Mathf.Max(2, previewResolutionPerSegment);
            lengthResolutionPerSegment = Mathf.Max(4, lengthResolutionPerSegment);

            if (anchors == null || anchors.Count < 2)
            {
                ResetPath();
                return;
            }

            RecalculateAutomaticHandles();
            cacheDirty = true;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || SegmentCount == 0)
            {
                return;
            }

            Gizmos.color = pathColor;
            int lineCount = SegmentCount * previewResolutionPerSegment;
            Vector3 previous = EvaluatePosition(0f);

            for (int i = 1; i <= lineCount; i++)
            {
                Vector3 current = EvaluatePosition(i / (float)lineCount);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }
    }
}
