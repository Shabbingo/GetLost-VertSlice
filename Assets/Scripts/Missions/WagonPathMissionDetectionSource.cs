using System;
using System.Collections.Generic;
using GetLost.Wagon;
using UnityEngine;

namespace GetLost.Missions
{
    [AddComponentMenu("Get Lost/Missions/Wagon Path Detection Source")]
    public sealed class WagonPathMissionDetectionSource : MonoBehaviour, IMissionPathDetectionSource
    {
        [Tooltip("Extra horizontal tolerance outside each POI trigger area.")]
        [SerializeField, Min(0f)] private float detectionPadding = .25f;

        [Tooltip("Treat POI areas as map-plane footprints. Recommended for terrain paths because POI collider height should not prevent a visible crossing from registering.")]
        [SerializeField] private bool ignoreVerticalSeparation = true;

        private IReadOnlyList<MissionPointOfInterest> points = Array.Empty<MissionPointOfInterest>();

        public event Action<MissionPointOfInterest> PathReachedPoi;

        public void Configure(IReadOnlyList<MissionPointOfInterest> pointsOfInterest)
        {
            points = pointsOfInterest ?? Array.Empty<MissionPointOfInterest>();
        }

        private void OnEnable() => WagonPathNetwork.AnyPathSegmentAdded += OnPathSegmentAdded;
        private void OnDisable() => WagonPathNetwork.AnyPathSegmentAdded -= OnPathSegmentAdded;

        private void OnPathSegmentAdded(Vector3 start, Vector3 end, float pathWidth)
        {
            // A hit can unlock another POI which appears earlier in the list.
            // Repeat only when progression actually changed so that the same
            // path segment can register every newly available overlapping POI.
            bool progressionChanged;
            int remainingPasses = Mathf.Max(1, points.Count);
            do
            {
                progressionChanged = false;
                for (int i = 0; i < points.Count; i++)
                {
                    MissionPointOfInterest poi = points[i];
                    if (!poi || poi.State != MissionPoiState.Available || !poi.DetectionArea)
                        continue;
                    if (!SegmentTouchesArea(start, end, pathWidth, poi.DetectionArea.bounds))
                        continue;

                    MissionPoiState previousState = poi.State;
                    PathReachedPoi?.Invoke(poi);
                    progressionChanged |= poi.State != previousState;
                }
            }
            while (progressionChanged && --remainingPasses > 0);
        }

        private bool SegmentTouchesArea(Vector3 start, Vector3 end, float pathWidth, Bounds bounds)
        {
            float expansion = Mathf.Max(0f, pathWidth * .5f + detectionPadding) * 2f;
            bounds.Expand(new Vector3(expansion, 0f, expansion));

            if (ignoreVerticalSeparation)
            {
                // POI detection represents a footprint on the world map. Put
                // both path endpoints on the bounds' centre plane so terrain
                // height differences cannot invalidate an X/Z crossing.
                start.y = bounds.center.y;
                end.y = bounds.center.y;
            }

            if (bounds.Contains(start) || bounds.Contains(end))
                return true;
            Vector3 delta = end - start;
            float length = delta.magnitude;
            return length > .0001f &&
                   bounds.IntersectRay(new Ray(start, delta / length), out float distance) &&
                   distance <= length;
        }
    }
}
