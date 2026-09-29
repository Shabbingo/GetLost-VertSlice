using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GetLost.Missions
{
    [AddComponentMenu("Get Lost/Missions/Mission Board Controller")]
    public sealed class MissionBoardController : MonoBehaviour
    {
        [Header("POI Progression")]
        [SerializeField] private bool automaticallyFindPois = true;
        [SerializeField] private List<MissionPointOfInterest> pointsOfInterest = new();
        [Tooltip("Optional. If no POI is marked Initially Available, this becomes the first destination.")]
        [SerializeField] private MissionPointOfInterest firstMajorPoi;

        [Header("Replaceable Path Detection")]
        [Tooltip("Assign a component implementing IMissionPathDetectionSource. The included wagon adapter can be replaced without changing progression.")]
        [SerializeField] private MonoBehaviour pathDetectionSource;

        [Header("Topographic Mission Board")]
        [SerializeField] private RawImage topographyImage;
        [SerializeField] private RectTransform pinRoot;
        [SerializeField] private TopologyMapGenerator topologyMapGenerator;
        [SerializeField] private Sprite majorPinSprite;
        [SerializeField] private Sprite minorPinSprite;
        [SerializeField] private Vector2 majorPinSize = new(26f, 26f);
        [SerializeField] private Vector2 minorPinSize = new(16f, 16f);
        [SerializeField] private Color availableMajorColour = new(1f, .48f, .08f, 1f);
        [SerializeField] private Color availableMinorColour = new(.15f, .72f, 1f, 1f);
        [SerializeField] private Color completedColour = new(.42f, .48f, .42f, .9f);
        [SerializeField] private TMP_Text missionSummaryText;

        [Header("Events")]
        [SerializeField] private UnityEvent<MissionPointOfInterest> poiUnlocked;
        [SerializeField] private UnityEvent<MissionPointOfInterest> poiCompleted;
        [SerializeField] private UnityEvent allMajorPoisCompleted;

        [Header("Major POI Survey")]
        [Tooltip("Automatically provides the optional fly-up survey after completing a Major POI.")]
        [SerializeField] private bool enableMajorPoiSurvey = true;
        [SerializeField] private MajorPoiSurveyController majorPoiSurvey;

        private readonly Dictionary<MissionPointOfInterest, RectTransform> pins = new();
        private IMissionPathDetectionSource detector;
        private bool worldBoardPresentationConnected;

        public IReadOnlyList<MissionPointOfInterest> PointsOfInterest => pointsOfInterest;
        public event Action<MissionPointOfInterest> PoiCompleted;

        public void ConfigurePresentation(
            RawImage boardTopographyImage,
            RectTransform boardPinRoot,
            TopologyMapGenerator boardTopologyGenerator,
            TMP_Text boardMissionSummary)
        {
            topographyImage = boardTopographyImage;
            pinRoot = boardPinRoot;
            topologyMapGenerator = boardTopologyGenerator;
            missionSummaryText = boardMissionSummary;
            worldBoardPresentationConnected = topographyImage && pinRoot;
            RefreshBoard();
        }

        private void Reset()
        {
            pathDetectionSource = GetComponent<WagonPathMissionDetectionSource>();
            topologyMapGenerator = FindAnyObjectByType<TopologyMapGenerator>();
        }

        private void Awake()
        {
            CollectPois();
            ConfigureDetector();
            ConfigureMajorPoiSurvey();
            InitialiseProgression();
            RefreshBoard();
        }

        private void OnDestroy()
        {
            if (detector != null)
                detector.PathReachedPoi -= ReportPathReached;
            foreach (MissionPointOfInterest poi in pointsOfInterest)
                if (poi)
                    poi.StateChanged -= OnPoiStateChanged;
        }

        private void CollectPois()
        {
            if (automaticallyFindPois)
                pointsOfInterest = FindObjectsByType<MissionPointOfInterest>(FindObjectsInactive.Include)
                    .OrderBy(item => item.transform.GetSiblingIndex())
                    .ToList();
            else
                pointsOfInterest = pointsOfInterest.Where(item => item).Distinct().ToList();

            foreach (IGrouping<string, MissionPointOfInterest> duplicate in pointsOfInterest
                         .GroupBy(item => item.PoiId)
                         .Where(group => group.Count() > 1))
            {
                Debug.LogWarning(
                    $"[Mission Board] Multiple POIs use the id '{duplicate.Key}'. Give each POI a unique id before save data is added.",
                    this);
            }

            foreach (MissionPointOfInterest poi in pointsOfInterest)
            {
                poi.StateChanged -= OnPoiStateChanged;
                poi.StateChanged += OnPoiStateChanged;
            }
        }

        private void ConfigureDetector()
        {
            detector = pathDetectionSource as IMissionPathDetectionSource;
            if (detector == null)
                detector = GetComponent<IMissionPathDetectionSource>();
            if (detector == null)
            {
                Debug.LogWarning("[Mission Board] No path detection source is assigned. POIs can still be completed by calling ReportPathReached.", this);
                return;
            }
            detector.Configure(pointsOfInterest);
            detector.PathReachedPoi -= ReportPathReached;
            detector.PathReachedPoi += ReportPathReached;
        }

        private void InitialiseProgression()
        {
            foreach (MissionPointOfInterest poi in pointsOfInterest)
                poi.SetState(MissionPoiState.Locked);

            List<MissionPointOfInterest> initial = pointsOfInterest
                .Where(item => item.InitiallyAvailable)
                .ToList();
            if (initial.Count == 0)
            {
                MissionPointOfInterest first = firstMajorPoi
                    ? firstMajorPoi
                    : pointsOfInterest.FirstOrDefault(item => item.Kind == MissionPoiKind.Major);
                if (first)
                    initial.Add(first);
            }
            foreach (MissionPointOfInterest poi in initial)
                Unlock(poi);
        }

        public void ReportPathReached(MissionPointOfInterest poi)
        {
            if (!poi || poi.State != MissionPoiState.Available)
                return;

            poi.SetState(MissionPoiState.Completed);
            poiCompleted?.Invoke(poi);
            PoiCompleted?.Invoke(poi);
            foreach (MissionPointOfInterest next in poi.UnlockOnCompletion)
                Unlock(next);

            if (pointsOfInterest.Where(item => item.Kind == MissionPoiKind.Major)
                .All(item => item.State == MissionPoiState.Completed))
                allMajorPoisCompleted?.Invoke();

            RefreshBoard();
        }

        public void Unlock(MissionPointOfInterest poi)
        {
            if (!poi || poi.State != MissionPoiState.Locked)
                return;
            poi.SetState(MissionPoiState.Available);
            poiUnlocked?.Invoke(poi);
        }

        [ContextMenu("Refresh Mission Board")]
        public void RefreshBoard()
        {
            if (!worldBoardPresentationConnected)
                return;
            EnsurePinRoot();
            if (!pinRoot || !topographyImage)
                return;

            foreach (MissionPointOfInterest poi in pointsOfInterest)
            {
                bool visible = poi && poi.State != MissionPoiState.Locked;
                RectTransform pin = GetOrCreatePin(poi);
                if (!pin)
                    continue;
                pin.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                PositionPin(pin, poi.PinWorldPosition);
                Image image = pin.GetComponent<Image>();
                image.sprite = poi.Kind == MissionPoiKind.Major ? majorPinSprite : minorPinSprite;
                image.color = poi.State == MissionPoiState.Completed
                    ? completedColour
                    : poi.Kind == MissionPoiKind.Major ? availableMajorColour : availableMinorColour;
                pin.sizeDelta = poi.Kind == MissionPoiKind.Major ? majorPinSize : minorPinSize;
                pin.localRotation = poi.Kind == MissionPoiKind.Major && image.sprite == null
                    ? Quaternion.Euler(0f, 0f, 45f)
                    : Quaternion.identity;
                pin.SetAsLastSibling();
            }
            RefreshSummary();
        }

        private void EnsurePinRoot()
        {
            if (pinRoot || !topographyImage)
                return;
            GameObject root = new("Mission POI Pins", typeof(RectTransform));
            pinRoot = root.GetComponent<RectTransform>();
            pinRoot.SetParent(topographyImage.rectTransform, false);
            pinRoot.anchorMin = Vector2.zero;
            pinRoot.anchorMax = Vector2.one;
            pinRoot.offsetMin = Vector2.zero;
            pinRoot.offsetMax = Vector2.zero;
        }

        private RectTransform GetOrCreatePin(MissionPointOfInterest poi)
        {
            if (!poi)
                return null;
            if (pins.TryGetValue(poi, out RectTransform existing) && existing)
                return existing;
            GameObject pinObject = new($"{poi.DisplayName} Pin", typeof(RectTransform), typeof(Image));
            RectTransform pin = pinObject.GetComponent<RectTransform>();
            pin.SetParent(pinRoot, false);
            pin.anchorMin = pin.anchorMax = new Vector2(.5f, .5f);
            pin.pivot = new Vector2(.5f, .5f);
            pinObject.GetComponent<Image>().raycastTarget = false;
            pins[poi] = pin;
            return pin;
        }

        private void PositionPin(RectTransform pin, Vector3 worldPosition)
        {
            if (topologyMapGenerator && topologyMapGenerator.WorldToPixel(worldPosition, out Vector2 pixel))
            {
                float u = pixel.x / Mathf.Max(1f, topologyMapGenerator.textureWidth - 1f);
                float v = pixel.y / Mathf.Max(1f, topologyMapGenerator.textureHeight - 1f);
                pin.anchorMin = pin.anchorMax = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
                pin.anchoredPosition = Vector2.zero;
                return;
            }

            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains.Length == 0)
                return;
            float minX = terrains.Min(item => item.transform.position.x);
            float minZ = terrains.Min(item => item.transform.position.z);
            float maxX = terrains.Max(item => item.transform.position.x + item.terrainData.size.x);
            float maxZ = terrains.Max(item => item.transform.position.z + item.terrainData.size.z);
            pin.anchorMin = pin.anchorMax = new Vector2(
                Mathf.InverseLerp(minX, maxX, worldPosition.x),
                Mathf.InverseLerp(minZ, maxZ, worldPosition.z));
            pin.anchoredPosition = Vector2.zero;
        }

        private void RefreshSummary()
        {
            if (!missionSummaryText)
                return;
            List<MissionPointOfInterest> available = pointsOfInterest
                .Where(item => item.State == MissionPoiState.Available)
                .ToList();
            missionSummaryText.text = available.Count == 0
                ? "No further destinations."
                : "AVAILABLE DESTINATIONS\n" + string.Join("\n", available.Select(item =>
                    $"{(item.Kind == MissionPoiKind.Major ? "◆" : "•")} {item.DisplayName}"));
        }

        private void OnPoiStateChanged(MissionPointOfInterest _) => RefreshBoard();

        private void ConfigureMajorPoiSurvey()
        {
            if (!enableMajorPoiSurvey)
                return;
            if (!majorPoiSurvey)
                majorPoiSurvey = GetComponent<MajorPoiSurveyController>();
            if (!majorPoiSurvey)
                majorPoiSurvey = gameObject.AddComponent<MajorPoiSurveyController>();
            majorPoiSurvey.Configure(this);
        }
    }
}
