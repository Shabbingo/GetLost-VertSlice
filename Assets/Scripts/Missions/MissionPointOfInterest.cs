using System;
using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Missions
{
    public enum MissionPoiKind { Major, Minor }
    public enum MissionPoiState { Locked, Available, Completed }

    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Missions/Mission Point Of Interest")]
    public sealed class MissionPointOfInterest : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string poiId;
        [SerializeField] private string displayName = "Point of Interest";
        [SerializeField, TextArea] private string description;
        [SerializeField] private MissionPoiKind kind = MissionPoiKind.Major;

        [Header("Progression")]
        [Tooltip("Makes this POI visible on the mission board when play begins.")]
        [SerializeField] private bool initiallyAvailable;
        [Tooltip("POIs revealed when this POI is reached by the path. Multiple entries create a choice of destinations.")]
        [SerializeField] private List<MissionPointOfInterest> unlockOnCompletion = new();

        [Header("Detection Area")]
        [Tooltip("The trigger defines the path-detection area. Detection logic is supplied separately and can be replaced later.")]
        [SerializeField] private Collider detectionArea;

        [SerializeField, HideInInspector] private MissionPoiState state = MissionPoiState.Locked;

        public string PoiId => string.IsNullOrWhiteSpace(poiId) ? gameObject.name : poiId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
        public string Description => description;
        public MissionPoiKind Kind => kind;
        public bool InitiallyAvailable => initiallyAvailable;
        public IReadOnlyList<MissionPointOfInterest> UnlockOnCompletion => unlockOnCompletion;
        public Collider DetectionArea => detectionArea;
        public MissionPoiState State => state;
        public Vector3 PinWorldPosition => detectionArea ? detectionArea.bounds.center : transform.position;

        public event Action<MissionPointOfInterest> StateChanged;

        private void Reset()
        {
            detectionArea = GetComponent<Collider>();
            if (detectionArea)
                detectionArea.isTrigger = true;
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = gameObject.name;
        }

        private void OnValidate()
        {
            detectionArea ??= GetComponent<Collider>();
            if (detectionArea)
                detectionArea.isTrigger = true;
            unlockOnCompletion.RemoveAll(item => item == null || item == this);
        }

        public void ConfigureKind(MissionPoiKind value)
        {
            kind = value;
            displayName = gameObject.name;
        }

        internal void SetState(MissionPoiState value)
        {
            if (state == value)
                return;
            state = value;
            StateChanged?.Invoke(this);
        }

        private void OnDrawGizmos()
        {
            Collider area = detectionArea ? detectionArea : GetComponent<Collider>();
            if (!area)
                return;
            Color colour = kind == MissionPoiKind.Major
                ? new Color(1f, .62f, .12f, .8f)
                : new Color(.25f, .75f, 1f, .7f);
            if (state == MissionPoiState.Locked)
                colour.a *= .35f;
            Gizmos.color = colour;
            Gizmos.DrawWireCube(area.bounds.center, area.bounds.size);
        }
    }
}
