using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Tom.WalkingController;
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

        [Header("3D POI Pins")]
        [SerializeField] private GameObject pinPrefab;
        [Tooltip("Child transform at the needle insertion point. This point is aligned to the POI coordinate.")]
        [SerializeField] private string pinAttachmentPivotName = "GameObject";
        [Tooltip("Material identifying the renderer slot whose Base Color is controlled by the POI colour fields.")]
        [SerializeField] private Material pinHeadMaterial;
        [Tooltip("Uniform scale multiplier for Major POI pins.")]
        [SerializeField] private float majorPinScale = 620f;
        [Tooltip("Uniform scale multiplier for Minor POI pins.")]
        [SerializeField] private float minorPinScale = 380f;
        [SerializeField] private Vector3 pinRotationOffset;
        [Tooltip("Maximum deterministic lean applied to each pin so they look hand-placed.")]
        [SerializeField, Range(0f, 20f)] private float pinRandomTiltDegrees = 8f;
        [SerializeField] private Vector3 pinLocalOffset;
        [SerializeField] private int pinSortingOrder = 30;
        [SerializeField] private Color availableMajorColour = new(1f, .48f, .08f, 1f);
        [SerializeField] private Color availableMinorColour = new(.15f, .72f, 1f, 1f);
        [SerializeField] private Color completedColour = new(.42f, .48f, .42f, .9f);
        [SerializeField] private TMP_Text missionSummaryText;

        [Header("You Are Here Marker")]
        [SerializeField] private bool showPlayerMarker = true;
        [SerializeField] private Transform player;
        [SerializeField] private Sprite playerMarkerSprite;
        [SerializeField] private Vector2 playerMarkerSize = new(22f, 22f);
        [SerializeField] private Color playerMarkerColour = new(.9f, .12f, .08f, 1f);
        [SerializeField] private string playerMarkerLabel = "YOU ARE HERE";
        [SerializeField] private Vector2 playerMarkerLabelSize = new(130f, 24f);
        [SerializeField] private Vector2 playerMarkerLabelOffset = new(0f, 22f);

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
        private RectTransform playerMarker;
        private Image playerMarkerImage;
        private TMP_Text playerMarkerText;

        public IReadOnlyList<MissionPointOfInterest> PointsOfInterest => pointsOfInterest;
        public event Action<MissionPointOfInterest> PoiCompleted;
        public event Action MissionProgressChanged;

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

        private void LateUpdate()
        {
            if (!worldBoardPresentationConnected || !showPlayerMarker)
                return;
            if (!player)
            {
                WalkingMotor motor = FindAnyObjectByType<WalkingMotor>();
                if (motor)
                    player = motor.transform;
            }
            if (!player)
                return;
            EnsurePlayerMarker();
            if (!playerMarker)
                return;
            PositionPin(playerMarker, player.position);
            playerMarker.SetAsLastSibling();
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
            MissionProgressChanged?.Invoke();
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
                Color pinColour = poi.State == MissionPoiState.Completed
                    ? completedColour
                    : poi.Kind == MissionPoiKind.Major ? availableMajorColour : availableMinorColour;
                MapPin3DVisual visual = pin.GetComponent<MapPin3DVisual>();
                visual.Configure(
                    pinPrefab,
                    pinAttachmentPivotName,
                    pinHeadMaterial,
                    pinColour,
                    poi.Kind == MissionPoiKind.Major ? majorPinScale : minorPinScale,
                    pinRotationOffset,
                    poi.PoiId,
                    pinRandomTiltDegrees,
                    pinLocalOffset,
                    pinSortingOrder);
                pin.SetAsLastSibling();
            }
            RefreshPlayerMarker();
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
            GameObject pinObject = new($"{poi.DisplayName} Pin", typeof(RectTransform), typeof(MapPin3DVisual));
            RectTransform pin = pinObject.GetComponent<RectTransform>();
            pin.SetParent(pinRoot, false);
            pin.anchorMin = pin.anchorMax = new Vector2(.5f, .5f);
            pin.pivot = new Vector2(.5f, .5f);
            pins[poi] = pin;
            return pin;
        }

        private void RefreshPlayerMarker()
        {
            if (!showPlayerMarker)
            {
                if (playerMarker)
                    playerMarker.gameObject.SetActive(false);
                return;
            }
            EnsurePlayerMarker();
            if (!playerMarker)
                return;
            playerMarker.gameObject.SetActive(true);
            playerMarker.sizeDelta = playerMarkerSize;
            if (playerMarkerImage)
            {
                playerMarkerImage.sprite = playerMarkerSprite;
                playerMarkerImage.color = playerMarkerColour;
                playerMarkerImage.rectTransform.localRotation = playerMarkerSprite
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 0f, 45f);
            }
            if (playerMarkerText)
            {
                playerMarkerText.text = playerMarkerLabel;
                playerMarkerText.color = playerMarkerColour;
                playerMarkerText.rectTransform.sizeDelta = playerMarkerLabelSize;
                playerMarkerText.rectTransform.anchoredPosition = playerMarkerLabelOffset;
            }
            if (player)
                PositionPin(playerMarker, player.position);
            playerMarker.SetAsLastSibling();
        }

        private void EnsurePlayerMarker()
        {
            if (playerMarker || !pinRoot)
                return;
            Transform existing = pinRoot.Find("You Are Here Marker");
            GameObject markerObject = existing
                ? existing.gameObject
                : new GameObject("You Are Here Marker", typeof(RectTransform));
            markerObject.transform.SetParent(pinRoot, false);
            playerMarker = markerObject.GetComponent<RectTransform>();
            playerMarker.anchorMin = playerMarker.anchorMax = new Vector2(.5f, .5f);
            playerMarker.pivot = new Vector2(.5f, .5f);

            Transform existingIcon = playerMarker.Find("Marker");
            GameObject iconObject = existingIcon
                ? existingIcon.gameObject
                : new GameObject("Marker", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(playerMarker, false);
            playerMarkerImage = iconObject.GetComponent<Image>();
            playerMarkerImage.raycastTarget = false;
            RectTransform iconRect = playerMarkerImage.rectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;

            Transform existingLabel = playerMarker.Find("Label");
            GameObject labelObject = existingLabel
                ? existingLabel.gameObject
                : new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(playerMarker, false);
            playerMarkerText = labelObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset && !playerMarkerText.font)
                playerMarkerText.font = TMP_Settings.defaultFontAsset;
            playerMarkerText.fontSize = 14f;
            playerMarkerText.fontStyle = FontStyles.Bold;
            playerMarkerText.alignment = TextAlignmentOptions.Center;
            playerMarkerText.raycastTarget = false;
            playerMarkerText.rectTransform.anchorMin = playerMarkerText.rectTransform.anchorMax =
                new Vector2(.5f, .5f);
            playerMarkerText.rectTransform.pivot = new Vector2(.5f, .5f);
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
                    $"{(item.Kind == MissionPoiKind.Major ? "[MAJOR]" : "[MINOR]")} {item.DisplayName}"));
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

    /// <summary>
    /// Hosts a physical 3D pin on a UI/map anchor. The named attachment pivot
    /// inside the prefab is aligned to the map coordinate, while a material
    /// property block recolours only the pin head without cloning materials.
    /// </summary>
    public sealed class MapPin3DVisual : MonoBehaviour
    {
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColourId = Shader.PropertyToID("_Color");

        private GameObject sourcePrefab;
        private Transform uniformSpace;
        private GameObject instance;
        private Transform attachmentPivot;
        private Renderer[] renderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock propertyBlock;

        public void Configure(
            GameObject prefab,
            string attachmentPivotName,
            Material headMaterial,
            Color headColour,
            float scaleMultiplier,
            Vector3 rotationOffset,
            string variationKey,
            float randomTiltDegrees,
            Vector3 localOffset,
            int sortingOrder)
        {
            if (!prefab)
            {
                if (instance)
                    instance.SetActive(false);
                return;
            }

            if (!instance || sourcePrefab != prefab)
                Build(prefab, attachmentPivotName);
            if (!instance)
                return;

            instance.SetActive(true);
            UpdateUniformSpaceScale();
            Transform root = instance.transform;
            root.localPosition = Vector3.zero;
            Vector3 stableTilt = CreateStableTilt(variationKey, randomTiltDegrees);
            root.localRotation = prefab.transform.localRotation *
                                 Quaternion.Euler(rotationOffset + stableTilt);
            root.localScale = prefab.transform.localScale * Mathf.Max(0.0001f, scaleMultiplier);

            if (!attachmentPivot || attachmentPivot.name != attachmentPivotName)
                attachmentPivot = FindDescendant(root, attachmentPivotName);
            if (attachmentPivot)
            {
                // Align in world space after scale compensation. This keeps the
                // named insertion point exact even under rotated/scaled canvases.
                root.position += transform.position - attachmentPivot.position;
            }
            root.position += uniformSpace.TransformVector(localOffset);

            ApplyHeadColour(headMaterial, headColour);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i])
                    renderers[i].sortingOrder = sortingOrder;
        }

        private void Build(GameObject prefab, string attachmentPivotName)
        {
            if (uniformSpace)
            {
                uniformSpace.gameObject.SetActive(false);
                Destroy(uniformSpace.gameObject);
            }

            sourcePrefab = prefab;
            GameObject spaceObject = new("Uniform Pin Space");
            uniformSpace = spaceObject.transform;
            uniformSpace.SetParent(transform, false);
            uniformSpace.localPosition = Vector3.zero;
            uniformSpace.localRotation = Quaternion.identity;
            UpdateUniformSpaceScale();

            instance = Instantiate(prefab, uniformSpace, false);
            instance.name = "3D Pin Visual";
            attachmentPivot = FindDescendant(instance.transform, attachmentPivotName);
            renderers = instance.GetComponentsInChildren<Renderer>(true);

            foreach (Collider markerCollider in instance.GetComponentsInChildren<Collider>(true))
                markerCollider.enabled = false;

            if (!attachmentPivot)
            {
                Debug.LogWarning(
                    $"[Map Pin] Attachment pivot '{attachmentPivotName}' was not found in '{prefab.name}'. The prefab origin will be used.",
                    this);
            }
        }

        private void UpdateUniformSpaceScale()
        {
            if (!uniformSpace)
                return;

            Vector3 inheritedScale = transform.lossyScale;
            float referenceScale = Mathf.Max(0.000001f, Mathf.Abs(inheritedScale.x));
            uniformSpace.localScale = new Vector3(
                SafeScaleRatio(referenceScale, inheritedScale.x),
                SafeScaleRatio(referenceScale, inheritedScale.y),
                SafeScaleRatio(referenceScale, inheritedScale.z));
        }

        private static float SafeScaleRatio(float referenceScale, float inheritedAxisScale)
        {
            float magnitude = Mathf.Abs(inheritedAxisScale);
            if (magnitude < 0.000001f)
                return 1f;
            return referenceScale / magnitude;
        }

        private static Vector3 CreateStableTilt(string key, float maximumDegrees)
        {
            if (maximumDegrees <= 0f)
                return Vector3.zero;

            uint hash = 2166136261u;
            string stableKey = string.IsNullOrWhiteSpace(key) ? "Map Pin" : key;
            for (int i = 0; i < stableKey.Length; i++)
            {
                hash ^= stableKey[i];
                hash *= 16777619u;
            }

            float direction = (hash & 0xffffu) / 65535f * Mathf.PI * 2f;
            float magnitudeSample = ((hash >> 16) & 0xffffu) / 65535f;
            float magnitude = Mathf.Lerp(maximumDegrees * 0.35f, maximumDegrees, magnitudeSample);

            // The prefab's needle begins on its local Y axis before its base
            // rotation. Local X/Z offsets therefore produce a natural lean.
            return new Vector3(
                Mathf.Cos(direction) * magnitude,
                0f,
                Mathf.Sin(direction) * magnitude);
        }

        private void ApplyHeadColour(Material headMaterial, Color colour)
        {
            bool applied = false;

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                if (!targetRenderer)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (!material || !headMaterial ||
                        (material != headMaterial &&
                         !string.Equals(material.name, headMaterial.name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    ApplyColourToSlot(targetRenderer, materialIndex, colour);
                    applied = true;
                }
            }

            // The prefab's root mesh is the spherical pin head. Keep that
            // contract as a safe fallback if its material assignment changes.
            if (!applied && instance && instance.TryGetComponent(out Renderer headRenderer) &&
                headRenderer.sharedMaterials.Length > 0)
            {
                ApplyColourToSlot(headRenderer, 0, colour);
            }
        }

        private void ApplyColourToSlot(Renderer targetRenderer, int materialIndex, Color colour)
        {
            // MaterialPropertyBlock is a UnityEngine.Object wrapper and cannot
            // be constructed by a MonoBehaviour field initializer. Create it
            // lazily after Unity has finished constructing the component.
            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
            propertyBlock.SetColor(BaseColourId, colour);
            propertyBlock.SetColor(ColourId, colour);
            targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
            propertyBlock.Clear();
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (!root || string.IsNullOrWhiteSpace(objectName))
                return null;
            if (root.name == objectName)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform match = FindDescendant(root.GetChild(i), objectName);
                if (match)
                    return match;
            }
            return null;
        }
    }
}
