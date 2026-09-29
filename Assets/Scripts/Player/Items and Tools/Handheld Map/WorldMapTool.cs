using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using GetLost.Missions;
using GetLost.PlayerTools;

/// <summary>
/// Physical in-world map tool.
///
/// Features:
/// - Opens/closes the physical map.
/// - Displays the clean TopologyMapGenerator base texture.
/// - Displays the current mission START marker.
/// - RMB focus mode zooms the camera and allows a small "eye scan" around the map.
/// - Normal first-person look can be disabled while focused to prevent camera snapping.
/// - Optional focus mode zooms the player camera while the map is open.
///
/// Recommended dedicated Input Action Map:
/// MapTool/Focus : Button, <Mouse>/rightButton
/// MapTool/Look  : Value/Vector2, <Mouse>/delta
///
/// Other systems can optionally place markers with SetStartWorldPosition and
/// SetEndWorldPosition without introducing a hard mission-system dependency.
/// </summary>
[DefaultExecutionOrder(10000)]
public class WorldMapTool : MonoBehaviour, IPlayerTool
{
    [Header("Map Object")]
    [SerializeField] private GameObject mapRoot;
    [SerializeField] private RawImage mapImage;
    [SerializeField] private RectTransform startMarker;
    [SerializeField] private RectTransform endMarker;
    [SerializeField] private HeldMapPresentation heldPresentation;

    [Header("Mission Field Note")]
    [Tooltip("Show discovered field-map information supplied by the Mission Board.")]
    [SerializeField] private bool showMissionInformationOnHandheldMap;

    [Tooltip("Show the old active START/END objective markers. Leave disabled for a visited-POIs-only field map.")]
    [SerializeField] private bool showActiveMissionMarkers;

    [Tooltip("Optional map text. If empty, a quiet field note is created on the physical map at runtime.")]
    [SerializeField] private TMP_Text missionFieldNoteText;

    [SerializeField, Min(0.1f)] private float fieldNoteRefreshSeconds = 0.75f;

    [Header("Field Note Handbook Layout")]
    [Tooltip("Optional custom handbook panel. When empty, a paper notebook is created around the field-note text at runtime.")]
    [SerializeField] private RectTransform fieldNotePanel;
    [Tooltip("Places the handbook beside the left or right edge of the map so it never covers the coordinate grid.")]
    [SerializeField] private bool fieldNoteOnLeft = true;
    [SerializeField] private Vector2 fieldNotePanelSize = new(230f, 420f);
    [Tooltip("Horizontal gap between the handbook and the map edge, plus a vertical adjustment.")]
    [SerializeField] private Vector2 fieldNoteOffset = new(12f, 18f);
    [Tooltip("Small page rotation that makes the handbook look loosely attached to the map.")]
    [SerializeField, Range(-15f, 15f)] private float fieldNoteRotationDegrees = 4f;
    [SerializeField] private Color fieldNotePaperColour = new(0.91f, 0.84f, 0.65f, 1f);
    [SerializeField] private Color fieldNoteSpineColour = new(0.23f, 0.14f, 0.07f, 1f);
    [SerializeField] private Color fieldNoteInkColour = new(0.12f, 0.1f, 0.07f, 0.95f);
    [SerializeField, Min(8f)] private float fieldNoteFontSize = 17f;

    [Header("Field Map Progress")]
    [Tooltip("Mission board that owns POI discovery and completion state. Found automatically when empty.")]
    [SerializeField] private MissionBoardController missionBoard;

    [Tooltip("Draw completed POIs on the handheld field map.")]
    [SerializeField] private bool showVisitedPois = true;

    [Tooltip("Draw a chess-style letter and number coordinate grid over the field map.")]
    [SerializeField] private bool showCoordinateGrid = true;

    [SerializeField, Range(2, 26)] private int gridColumns = 8;
    [SerializeField, Range(2, 20)] private int gridRows = 8;
    [SerializeField, Min(0.5f)] private float gridLineThickness = 1.5f;
    [SerializeField] private Color gridColour = new(0.12f, 0.1f, 0.07f, 0.28f);

    [Header("3D Visited POI Pins")]
    [SerializeField] private GameObject visitedPoiPinPrefab;
    [Tooltip("Child transform at the needle insertion point. This point is aligned to the POI coordinate.")]
    [SerializeField] private string visitedPinAttachmentPivotName = "GameObject";
    [Tooltip("Material identifying the renderer slot whose Base Color is controlled by the visited POI colour fields.")]
    [SerializeField] private Material visitedPinHeadMaterial;
    [Tooltip("Handheld-map-only master scale. This does not affect pins on the physical Mission Board.")]
    [SerializeField, Min(0.001f)] private float fieldMapPinScale = 0.2f;
    [Tooltip("Uniform scale multiplier for visited Major POI pins.")]
    [SerializeField] private float visitedMajorPinScale = 470f;
    [Tooltip("Uniform scale multiplier for visited Minor POI pins.")]
    [SerializeField] private float visitedMinorPinScale = 280f;
    [SerializeField] private Vector3 visitedPinRotationOffset;
    [Tooltip("Maximum deterministic lean applied to field-map pins so they look hand-placed.")]
    [SerializeField, Range(0f, 20f)] private float visitedPinRandomTiltDegrees = 8f;
    [SerializeField] private Vector3 visitedPinLocalOffset;
    [SerializeField] private int visitedPinSortingOrder = 35;
    [SerializeField] private Color visitedMajorColour = new(0.55f, 0.19f, 0.08f, 0.95f);
    [SerializeField] private Color visitedMinorColour = new(0.08f, 0.34f, 0.52f, 0.95f);

    [Header("Generated Topology Map")]
    [Tooltip("Assign the active TopologyMapGenerator used by your normal map system.")]
    [SerializeField] private TopologyMapGenerator topologyMapGenerator;

    [Tooltip("Generate and assign the clean topo map when this component wakes up.")]
    [SerializeField] private bool generateMapOnAwake = true;

    [Tooltip("Refresh the clean topo map whenever the physical map is opened.")]
    [SerializeField] private bool refreshMapWhenOpened = true;

    [Tooltip("Force the topology generator to rebuild its clean base texture on the next refresh.")]
    [SerializeField] private bool forceRegenerateBaseMap = false;

    [Header("Player Camera")]
    [SerializeField] private Camera playerCamera;

    [Header("Tool Manager")]
    [SerializeField] private PlayerToolManager toolManager;

    [Header("Input System")]
    [Tooltip(
        "Optional legacy map toggle. When enabled it routes through " +
        "PlayerToolManager instead of opening the map directly."
    )]
    [SerializeField] private bool allowLegacyToggleInput = false;

    [SerializeField] private InputActionReference toggleMapAction;

    [Tooltip("Dedicated MapTool/Focus action. Button bound to <Mouse>/rightButton.")]
    [SerializeField] private InputActionReference focusHoldAction;

    [Tooltip("Dedicated MapTool/Look action. Value/Vector2 bound to <Mouse>/delta.")]
    [SerializeField] private InputActionReference mapLookAction;

    [Header("Focus Mode")]
    [Tooltip(
        "Behaviours disabled while RMB focus is active. " +
        "Add your normal FirstPersonLook behaviour here. " +
        "You can also add movement behaviours if the player should be stationary.")]
    [SerializeField] private Behaviour[] disableWhileFocused;

    [Tooltip("Assign the Map Scan Pivot here. During RMB inspection this stays flat and translates beneath the player's gaze.")]
    [SerializeField] private Transform scanTransform;

    [Header("Camera Zoom")]
    [SerializeField] private float zoomedFieldOfView = 35f;
    [SerializeField] private float zoomSpeed = 12f;

    [Header("Map Inspection Pan")]
    [Tooltip("Maximum UI-space travel of the page while inspecting it. Increase this if an edge cannot be brought into view.")]
    [SerializeField] private Vector2 maximumInspectionPan = new Vector2(360f, 280f);

    [Tooltip("How many UI pixels the page moves per mouse-delta unit.")]
    [SerializeField] private float scanSensitivity = 1.1f;
    [SerializeField] private float scanFollowSpeed = 12f;

    [Tooltip("UI pixels per second that the page drifts back to centre. Keep at 0 for deliberate edge-to-edge inspection.")]
    [SerializeField] private float scanRecentreWhileHeld = 0f;

    [Header("Map World Bounds - Fallback Only")]
    [Tooltip("Only used if TopologyMapGenerator.WorldToPixel cannot be used.")]
    [SerializeField] private bool autoDetectTerrainBounds = true;

    [SerializeField] private Vector2 worldMinXZ;
    [SerializeField] private Vector2 worldMaxXZ = new Vector2(1000f, 1000f);

    [Header("Mission Markers")]
    [SerializeField] private bool clampStartMarkerToMap = true;

    [Tooltip("Automatically makes START and END markers children of the RawImage before positioning them.")]
    [SerializeField] private bool ensureMarkersAreChildrenOfMapImage = true;

    [Header("Debug")]
    [SerializeField] private bool logFocusMode = false;

    private float normalFieldOfView;

    private bool isOpen;
    private bool isFocused;

    private Quaternion scanBaseLocalRotation;
    private Vector3 scanBaseLocalPosition;
    private Vector2 scanTarget;
    private Vector2 scanCurrent;

    private bool[] focusedBehaviourPreviousStates;

    private Vector3 storedStartWorldPosition;
    private bool hasStartWorldPosition;

    private Vector3 storedEndWorldPosition;
    private bool hasEndWorldPosition;
    private float nextFieldNoteRefreshTime;
    private RectTransform coordinateGridRoot;
    private RectTransform visitedPoiRoot;
    private int builtGridColumns = -1;
    private int builtGridRows = -1;
    private MissionBoardController subscribedMissionBoard;
    private readonly Dictionary<MissionPointOfInterest, RectTransform> visitedPoiMarkers = new();
    private readonly List<MissionPointOfInterest> explicitlyRecordedFieldNotePois = new();

    public PlayerToolType ToolType => PlayerToolType.Map;
    public bool IsEquipped => isOpen;

    public bool IsOpen => isOpen;
    public bool IsFocused => isFocused;

    private void Awake()
    {
        if (toolManager == null)
            toolManager = GetComponentInParent<PlayerToolManager>();

        if (toolManager == null)
            toolManager = FindAnyObjectByType<PlayerToolManager>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera != null)
            normalFieldOfView = playerCamera.fieldOfView;

        if (scanTransform != null)
        {
            scanBaseLocalRotation = scanTransform.localRotation;
            scanBaseLocalPosition = scanTransform.localPosition;
        }

        if (autoDetectTerrainBounds)
            DetectTerrainBounds();

        ResolveMissionBoard();
        EnsureMissionMarkerHierarchy();
        ApplyMissionInformationVisibility();

        if (generateMapOnAwake)
            RefreshGeneratedMap();

        SetOpen(false, force: true);
    }

    private void OnEnable()
    {
        ResolveMissionBoard();
        SubscribeToMissionBoard();

        if (allowLegacyToggleInput)
            EnableAction(toggleMapAction);

        EnableAction(focusHoldAction);
        EnableAction(mapLookAction);

        if (focusHoldAction == null || focusHoldAction.action == null)
        {
            Debug.LogWarning(
                "[WorldMapTool] Focus action is not assigned. RMB direct-input fallback will be used.",
                this);
        }

        if (allowLegacyToggleInput &&
            toggleMapAction != null &&
            toggleMapAction.action != null)
        {
            toggleMapAction.action.performed -= OnToggleMap;
            toggleMapAction.action.performed += OnToggleMap;
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromMissionBoard();

        if (toggleMapAction != null && toggleMapAction.action != null)
            toggleMapAction.action.performed -= OnToggleMap;

        if (isFocused)
            ExitFocusMode(immediate: true);

        if (allowLegacyToggleInput)
            DisableAction(toggleMapAction);

        DisableAction(focusHoldAction);
        DisableAction(mapLookAction);

        RestoreCamera();
        RestoreScanImmediately();
    }

    private void Update()
    {
        if (isOpen && Time.unscaledTime >= nextFieldNoteRefreshTime)
        {
            RefreshMissionFieldNote();
            nextFieldNoteRefreshTime =
                Time.unscaledTime + fieldNoteRefreshSeconds;
        }

        bool focusPressed = false;

        // Primary path: dedicated MapTool/Focus action.
        if (focusHoldAction != null &&
            focusHoldAction.action != null)
        {
            focusPressed = focusHoldAction.action.IsPressed();
        }

        // Fallback path: read RMB directly.
        // This keeps focus working in standalone builds even if the
        // InputActionReference/action map is not active for some reason.
        if (!focusPressed &&
            Mouse.current != null)
        {
            focusPressed = Mouse.current.rightButton.isPressed;
        }

        bool shouldFocus = isOpen && focusPressed;

        if (shouldFocus && !isFocused)
            EnterFocusMode();
        else if (!shouldFocus && isFocused)
            ExitFocusMode(immediate: false);
    }

    private void LateUpdate()
    {
        UpdateCameraZoom();
        UpdateMapScan();
    }

    private static void EnableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null)
            reference.action.Enable();
    }

    private static void DisableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null)
            reference.action.Disable();
    }

    private void OnToggleMap(InputAction.CallbackContext context)
    {
        if (!allowLegacyToggleInput)
            return;

        if (toolManager != null)
        {
            if (toolManager.IsSelected(PlayerToolType.Map))
                toolManager.UnequipCurrentTool();
            else
                toolManager.SelectMap();

            return;
        }

        // Fallback only when no manager exists.
        ToggleMap();
    }

    public void Equip()
    {
        OpenMap();
    }

    public void Unequip()
    {
        CloseMap();
    }

    [ContextMenu("Toggle Map")]
    public void ToggleMap()
    {
        SetOpen(!isOpen);
    }

    [ContextMenu("Open Map")]
    public void OpenMap()
    {
        SetOpen(true);
    }

    [ContextMenu("Close Map")]
    public void CloseMap()
    {
        SetOpen(false);
    }

    /// <summary>
    /// Manual texture override if you ever need it.
    /// </summary>
    public void SetMapTexture(Texture texture)
    {
        if (mapImage != null)
            mapImage.texture = texture;
    }

    /// <summary>
    /// Gets the clean base topology texture and assigns it directly to the RawImage.
    /// GenerateBaseTexture is used deliberately so normal map icons/POIs are not baked in.
    /// </summary>
    [ContextMenu("Refresh Generated Physical Map")]
    public void RefreshGeneratedMap()
    {
        if (mapImage == null)
        {
            Debug.LogWarning("[WorldMapTool] Map Image is not assigned.", this);
            return;
        }

        if (topologyMapGenerator == null)
        {
            Debug.LogWarning("[WorldMapTool] Topology Map Generator is not assigned.", this);
            return;
        }

        Texture2D cleanMap =
            topologyMapGenerator.GenerateBaseTexture(forceRegenerateBaseMap);

        if (cleanMap == null)
        {
            Debug.LogWarning(
                "[WorldMapTool] TopologyMapGenerator returned a null base texture.",
                this);
            return;
        }

        mapImage.texture = cleanMap;
        forceRegenerateBaseMap = false;

        UpdateMissionMarkers();
        RefreshFieldMapProgress();

        Debug.Log("[WorldMapTool] Clean topology map assigned.", this);
    }

    /// <summary>
    /// Called by WorldMapMissionBinder after mission selection.
    /// </summary>
    public void SetStartWorldPosition(Vector3 worldPosition)
    {
        storedStartWorldPosition = worldPosition;
        hasStartWorldPosition = true;

        UpdateMissionMarkers();
    }

    /// <summary>
    /// Called by WorldMapMissionBinder after mission selection.
    /// </summary>
    public void SetEndWorldPosition(Vector3 worldPosition)
    {
        storedEndWorldPosition = worldPosition;
        hasEndWorldPosition = true;

        UpdateMissionMarkers();
    }

    private void UpdateMissionMarkers()
    {
        if (!showMissionInformationOnHandheldMap || !showActiveMissionMarkers)
        {
            if (startMarker) startMarker.gameObject.SetActive(false);
            if (endMarker) endMarker.gameObject.SetActive(false);
            return;
        }

        EnsureMissionMarkerHierarchy();

        if (hasStartWorldPosition)
            PositionMarker(startMarker, storedStartWorldPosition);

        if (hasEndWorldPosition)
            PositionMarker(endMarker, storedEndWorldPosition);
    }

    private void EnsureMissionMarkerHierarchy()
    {
        if (!ensureMarkersAreChildrenOfMapImage || mapImage == null)
            return;

        RectTransform mapRect = mapImage.rectTransform;

        EnsureMarkerParent(startMarker, mapRect);
        EnsureMarkerParent(endMarker, mapRect);
    }

    private static void EnsureMarkerParent(
        RectTransform marker,
        RectTransform mapRect)
    {
        if (marker == null || mapRect == null)
            return;

        if (marker.parent != mapRect)
            marker.SetParent(mapRect, false);

        // Prevent an inherited transform offset from making one marker look
        // correct while the other is shifted.
        marker.localRotation = Quaternion.identity;
        marker.localScale = Vector3.one;
    }

    private void PositionMarker(RectTransform marker, Vector3 worldPosition)
    {
        if (marker == null || mapImage == null)
            return;

        if (!TryWorldToMapUv(worldPosition, out Vector2 uv))
            return;

        marker.anchorMin = uv;
        marker.anchorMax = uv;
        marker.anchoredPosition = Vector2.zero;
        marker.gameObject.SetActive(true);
    }

    private bool TryWorldToMapUv(Vector3 worldPosition, out Vector2 uv)
    {
        if (topologyMapGenerator != null &&
            topologyMapGenerator.WorldToPixel(worldPosition, out Vector2 pixelPosition))
        {
            uv = new Vector2(
                pixelPosition.x / Mathf.Max(1f, topologyMapGenerator.textureWidth - 1f),
                pixelPosition.y / Mathf.Max(1f, topologyMapGenerator.textureHeight - 1f));
            if (clampStartMarkerToMap)
                uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            return true;
        }

        float width = worldMaxXZ.x - worldMinXZ.x;
        float height = worldMaxXZ.y - worldMinXZ.y;
        if (Mathf.Abs(width) < 0.001f || Mathf.Abs(height) < 0.001f)
        {
            uv = Vector2.zero;
            return false;
        }

        uv = new Vector2(
            Mathf.InverseLerp(worldMinXZ.x, worldMaxXZ.x, worldPosition.x),
            Mathf.InverseLerp(worldMinXZ.y, worldMaxXZ.y, worldPosition.z));
        if (clampStartMarkerToMap)
            uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
        return true;
    }

    [ContextMenu("Auto Detect Terrain Bounds")]
    public void DetectTerrainBounds()
    {
        Terrain[] terrains = Terrain.activeTerrains;

        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogWarning(
                "[WorldMapTool] No active Terrains found. Using manually assigned fallback bounds.",
                this);
            return;
        }

        bool hasBounds = false;
        Bounds combined = new Bounds();

        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            Vector3 size = terrain.terrainData.size;

            Bounds terrainBounds = new Bounds(
                terrain.transform.position + size * 0.5f,
                size);

            if (!hasBounds)
            {
                combined = terrainBounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(terrainBounds);
            }
        }

        if (!hasBounds)
            return;

        worldMinXZ = new Vector2(combined.min.x, combined.min.z);
        worldMaxXZ = new Vector2(combined.max.x, combined.max.z);
    }

    private void SetOpen(bool open, bool force = false)
    {
        if (!force && isOpen == open)
            return;

        isOpen = open;

        if (heldPresentation != null)
            heldPresentation.SetHeld(open);
        else if (mapRoot != null)
            mapRoot.SetActive(open);

        if (open)
        {
            if (refreshMapWhenOpened)
                RefreshGeneratedMap();

            if (playerCamera != null)
                normalFieldOfView = playerCamera.fieldOfView;

            if (scanTransform != null)
            {
                scanBaseLocalRotation = scanTransform.localRotation;
                scanBaseLocalPosition = scanTransform.localPosition;
            }

            scanTarget = Vector2.zero;
            scanCurrent = Vector2.zero;
            RefreshFieldMapProgress();
            RefreshMissionFieldNote();
            nextFieldNoteRefreshTime =
                Time.unscaledTime + fieldNoteRefreshSeconds;

        }
        else
        {
            if (isFocused)
                ExitFocusMode(immediate: true);

            RestoreCamera();
            RestoreScanImmediately();
        }
    }

    private void RefreshMissionFieldNote()
    {
        if (!showMissionInformationOnHandheldMap)
        {
            SetFieldNoteVisible(false);
            return;
        }

        if (missionFieldNoteText == null)
        {
            CreateMissionFieldNote();
        }

        if (missionFieldNoteText == null)
        {
            return;
        }

        ResolveMissionBoard();
        PrepareFieldNoteLayout();

        IEnumerable<MissionPointOfInterest> missionRecordedPois = missionBoard
            ? missionBoard.PointsOfInterest.Where(poi =>
                poi &&
                (poi.State == MissionPoiState.Completed ||
                 (poi.Kind == MissionPoiKind.Minor && poi.State != MissionPoiState.Locked)))
            : Enumerable.Empty<MissionPointOfInterest>();

        List<MissionPointOfInterest> recordedPois = explicitlyRecordedFieldNotePois
            .Where(poi => poi)
            .Concat(missionRecordedPois)
            .Distinct()
            .ToList();
        if (recordedPois.Count == 0)
        {
            missionFieldNoteText.text =
                "FIELD NOTES\nSURVEYED LOCATIONS\nNo locations recorded.";
        }
        else
        {
            missionFieldNoteText.text =
                "FIELD NOTES\nSURVEYED LOCATIONS\n" +
                string.Join("\n", recordedPois.Select(poi =>
                    $"{(poi.Kind == MissionPoiKind.Major ? "MAJOR" : "MINOR")}: " +
                    $"{poi.DisplayName} - {GetGridReference(poi.PinWorldPosition)}"));
        }
        SetFieldNoteVisible(true);
        missionFieldNoteText.ForceMeshUpdate();
    }

    private void ApplyMissionInformationVisibility()
    {
        SetFieldNoteVisible(showMissionInformationOnHandheldMap);
        if (!showMissionInformationOnHandheldMap || !showActiveMissionMarkers)
        {
            if (startMarker) startMarker.gameObject.SetActive(false);
            if (endMarker) endMarker.gameObject.SetActive(false);
        }
    }

    private void CreateMissionFieldNote()
    {
        if (mapImage == null || TMP_Settings.defaultFontAsset == null)
        {
            return;
        }

        GameObject noteObject = new GameObject(
            "Mission Field Note",
            typeof(RectTransform));
        noteObject.transform.SetParent(mapImage.transform.parent, false);

        RectTransform rect =
            noteObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.04f, 0.97f);
        rect.anchorMax = new Vector2(0.04f, 0.97f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(450f, 120f);

        TextMeshProUGUI text =
            noteObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 22f;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = new Color(0.12f, 0.1f, 0.07f, 0.92f);
        text.raycastTarget = false;

        missionFieldNoteText = text;
        PrepareFieldNoteLayout();
    }

    private void PrepareFieldNoteLayout()
    {
        if (!missionFieldNoteText)
            return;

        EnsureFieldNotePanel();
        if (!fieldNotePanel)
            return;

        RectTransform mapRect = mapImage.rectTransform;
        Transform desiredParent = mapRect.parent;
        if (fieldNotePanel.parent != desiredParent)
            fieldNotePanel.SetParent(desiredParent, false);

        float side = fieldNoteOnLeft ? -1f : 1f;
        fieldNotePanel.anchorMin = fieldNotePanel.anchorMax =
            new Vector2(fieldNoteOnLeft ? mapRect.anchorMin.x : mapRect.anchorMax.x, 0.5f);
        fieldNotePanel.pivot = new Vector2(fieldNoteOnLeft ? 1f : 0f, 0.5f);
        fieldNotePanel.sizeDelta = fieldNotePanelSize;
        fieldNotePanel.anchoredPosition = new Vector2(
            side * Mathf.Abs(fieldNoteOffset.x),
            fieldNoteOffset.y);
        fieldNotePanel.localRotation = Quaternion.Euler(
            0f,
            0f,
            (fieldNoteOnLeft ? 1f : -1f) * fieldNoteRotationDegrees);
        fieldNotePanel.localScale = Vector3.one;
        fieldNotePanel.SetAsLastSibling();

        RectTransform rect = missionFieldNoteText.rectTransform;
        if (rect.parent != fieldNotePanel)
            rect.SetParent(fieldNotePanel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(28f, 22f);
        rect.offsetMax = new Vector2(-15f, -22f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
        missionFieldNoteText.fontSize = fieldNoteFontSize;
        missionFieldNoteText.enableAutoSizing = true;
        missionFieldNoteText.fontSizeMin = Mathf.Max(8f, fieldNoteFontSize - 5f);
        missionFieldNoteText.fontSizeMax = fieldNoteFontSize;
        missionFieldNoteText.color = fieldNoteInkColour;
        missionFieldNoteText.alignment = TextAlignmentOptions.TopLeft;
        missionFieldNoteText.textWrappingMode = TextWrappingModes.Normal;
        missionFieldNoteText.overflowMode = TextOverflowModes.Ellipsis;
        missionFieldNoteText.raycastTarget = false;
    }

    private void EnsureFieldNotePanel()
    {
        if (fieldNotePanel || !missionFieldNoteText || !mapImage)
            return;

        GameObject panelObject = new(
            "Field Note Handbook",
            typeof(RectTransform),
            typeof(Image),
            typeof(Shadow));
        fieldNotePanel = panelObject.GetComponent<RectTransform>();
        fieldNotePanel.SetParent(mapImage.rectTransform.parent, false);

        Image paper = panelObject.GetComponent<Image>();
        paper.color = fieldNotePaperColour;
        paper.raycastTarget = false;

        Shadow shadow = panelObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0.04f, 0.025f, 0.01f, 0.35f);
        shadow.effectDistance = new Vector2(8f, -8f);
        shadow.useGraphicAlpha = true;
        CreateHandbookDecoration(
            "Handbook Spine",
            fieldNotePanel,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            Vector2.zero,
            new Vector2(18f, 0f),
            fieldNoteSpineColour);

        for (int binding = 0; binding < 4; binding++)
        {
            float y = 0.18f + binding * 0.21f;
            CreateHandbookDecoration(
                $"Binding {binding + 1}",
                fieldNotePanel,
                new Vector2(0f, y),
                new Vector2(0f, y),
                new Vector2(-8f, 0f),
                new Vector2(28f, 5f),
                new Color(0.09f, 0.065f, 0.035f, 1f));
        }

        // The original prefab text was authored directly beneath the map page.
        // Reusing its renderer after moving it outside that page can leave TMP
        // with stale world-canvas material/culling state. Build a clean text
        // layer under the notebook, just like the working coordinate labels.
        TMP_Text oldText = missionFieldNoteText;
        GameObject textObject = new(
            "Field Note Entries",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(fieldNotePanel, false);
        TextMeshProUGUI freshText = textObject.GetComponent<TextMeshProUGUI>();
        freshText.font = oldText && oldText.font
            ? oldText.font
            : TMP_Settings.defaultFontAsset;
        freshText.raycastTarget = false;
        freshText.maskable = false;
        freshText.extraPadding = true;
        if (oldText)
            oldText.gameObject.SetActive(false);
        missionFieldNoteText = freshText;
    }

    private static void CreateHandbookDecoration(
        string objectName,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color colour)
    {
        GameObject decoration = new(objectName, typeof(RectTransform), typeof(Image));
        RectTransform rect = decoration.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        Image image = decoration.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
    }

    private void SetFieldNoteVisible(bool visible)
    {
        if (fieldNotePanel)
            fieldNotePanel.gameObject.SetActive(visible);

        if (missionFieldNoteText)
        {
            missionFieldNoteText.gameObject.SetActive(visible);
            missionFieldNoteText.enabled = visible;
        }
    }

    private void ResolveMissionBoard()
    {
        if (!missionBoard)
            missionBoard = FindAnyObjectByType<MissionBoardController>();
        SubscribeToMissionBoard();
    }

    private void SubscribeToMissionBoard()
    {
        if (subscribedMissionBoard == missionBoard)
            return;
        UnsubscribeFromMissionBoard();
        subscribedMissionBoard = missionBoard;
        if (subscribedMissionBoard)
            subscribedMissionBoard.MissionProgressChanged += OnMissionProgressChanged;
    }

    private void UnsubscribeFromMissionBoard()
    {
        if (subscribedMissionBoard)
            subscribedMissionBoard.MissionProgressChanged -= OnMissionProgressChanged;
        subscribedMissionBoard = null;
    }

    private void OnMissionProgressChanged()
    {
        RefreshFieldMapProgress();
        RefreshMissionFieldNote();
    }

    private void RefreshFieldMapProgress()
    {
        if (!showMissionInformationOnHandheldMap || mapImage == null)
        {
            if (coordinateGridRoot) coordinateGridRoot.gameObject.SetActive(false);
            if (visitedPoiRoot) visitedPoiRoot.gameObject.SetActive(false);
            return;
        }

        ResolveMissionBoard();
        EnsureCoordinateGrid();
        RefreshVisitedPoiMarkers();
    }

    private void EnsureCoordinateGrid()
    {
        if (!showCoordinateGrid)
        {
            if (coordinateGridRoot) coordinateGridRoot.gameObject.SetActive(false);
            return;
        }

        if (!coordinateGridRoot)
            coordinateGridRoot = CreateStretchRoot("Field Map Coordinate Grid");
        if (!coordinateGridRoot)
            return;

        coordinateGridRoot.gameObject.SetActive(true);
        if (builtGridColumns == gridColumns && builtGridRows == gridRows &&
            coordinateGridRoot.childCount > 0)
            return;

        for (int i = coordinateGridRoot.childCount - 1; i >= 0; i--)
            Destroy(coordinateGridRoot.GetChild(i).gameObject);

        for (int column = 0; column <= gridColumns; column++)
            CreateGridLine(true, column / (float)gridColumns);
        for (int row = 0; row <= gridRows; row++)
            CreateGridLine(false, row / (float)gridRows);

        for (int column = 0; column < gridColumns; column++)
            CreateGridLabel(ColumnName(column), new Vector2((column + 0.5f) / gridColumns, 0.985f));
        for (int row = 0; row < gridRows; row++)
            CreateGridLabel((row + 1).ToString(), new Vector2(0.02f, 1f - (row + 0.5f) / gridRows));

        builtGridColumns = gridColumns;
        builtGridRows = gridRows;
        coordinateGridRoot.SetAsFirstSibling();
    }

    private RectTransform CreateStretchRoot(string objectName)
    {
        if (!mapImage)
            return null;
        GameObject root = new(objectName, typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(mapImage.rectTransform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private void CreateGridLine(bool vertical, float position)
    {
        GameObject lineObject = new("Grid Line", typeof(RectTransform), typeof(Image));
        RectTransform rect = lineObject.GetComponent<RectTransform>();
        rect.SetParent(coordinateGridRoot, false);
        if (vertical)
        {
            rect.anchorMin = new Vector2(position, 0f);
            rect.anchorMax = new Vector2(position, 1f);
            rect.sizeDelta = new Vector2(gridLineThickness, 0f);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, position);
            rect.anchorMax = new Vector2(1f, position);
            rect.sizeDelta = new Vector2(0f, gridLineThickness);
        }
        rect.anchoredPosition = Vector2.zero;
        Image image = lineObject.GetComponent<Image>();
        image.color = gridColour;
        image.raycastTarget = false;
    }

    private void CreateGridLabel(string label, Vector2 anchor)
    {
        GameObject labelObject = new($"Grid {label}", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(coordinateGridRoot, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(30f, 22f);
        TMP_Text text = labelObject.GetComponent<TMP_Text>();
        text.text = label;
        text.fontSize = 12f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(gridColour.r, gridColour.g, gridColour.b, Mathf.Max(0.65f, gridColour.a));
        text.raycastTarget = false;
    }

    private void RefreshVisitedPoiMarkers()
    {
        if (!showVisitedPois || !missionBoard)
        {
            if (visitedPoiRoot) visitedPoiRoot.gameObject.SetActive(false);
            return;
        }

        if (!visitedPoiRoot)
            visitedPoiRoot = CreateStretchRoot("Visited POI Markers");
        if (!visitedPoiRoot)
            return;
        visitedPoiRoot.gameObject.SetActive(true);

        foreach (MissionPointOfInterest poi in missionBoard.PointsOfInterest)
        {
            if (!poi)
                continue;
            RectTransform marker = GetOrCreateVisitedPoiMarker(poi);
            bool visited = poi.State == MissionPoiState.Completed;
            marker.gameObject.SetActive(visited);
            if (!visited)
                continue;

            Color pinColour = poi.Kind == MissionPoiKind.Major
                ? visitedMajorColour
                : visitedMinorColour;
            PositionMarker(marker, poi.PinWorldPosition);
            MapPin3DVisual visual = marker.GetComponent<MapPin3DVisual>();
            visual.Configure(
                visitedPoiPinPrefab,
                visitedPinAttachmentPivotName,
                visitedPinHeadMaterial,
                pinColour,
                (poi.Kind == MissionPoiKind.Major ? visitedMajorPinScale : visitedMinorPinScale) *
                fieldMapPinScale,
                visitedPinRotationOffset,
                poi.PoiId,
                visitedPinRandomTiltDegrees,
                visitedPinLocalOffset,
                visitedPinSortingOrder);
        }
        visitedPoiRoot.SetAsLastSibling();
    }

    private RectTransform GetOrCreateVisitedPoiMarker(MissionPointOfInterest poi)
    {
        if (visitedPoiMarkers.TryGetValue(poi, out RectTransform marker) && marker)
            return marker;
        GameObject markerObject = new($"Visited {poi.DisplayName}", typeof(RectTransform), typeof(MapPin3DVisual));
        marker = markerObject.GetComponent<RectTransform>();
        marker.SetParent(visitedPoiRoot, false);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.pivot = new Vector2(0.5f, 0.5f);
        visitedPoiMarkers[poi] = marker;
        return marker;
    }

    private string GetGridReference(Vector3 worldPosition)
    {
        if (!TryWorldToMapUv(worldPosition, out Vector2 uv))
            return "--";

        int column = Mathf.Clamp(Mathf.FloorToInt(uv.x * gridColumns), 0, gridColumns - 1);
        int rowFromTop = Mathf.Clamp(Mathf.FloorToInt((1f - uv.y) * gridRows), 0, gridRows - 1);
        return $"{ColumnName(column)}{rowFromTop + 1}";
    }

    /// <summary>
    /// Lets the bird's-eye survey and other presentation systems use exactly
    /// the same chess-grid conversion as the physical field map.
    /// </summary>
    public string GetGridReferenceForWorldPosition(Vector3 worldPosition)
    {
        return GetGridReference(worldPosition);
    }

    /// <summary>
    /// Adds a location discovered by the bird's-eye survey to the physical
    /// field notebook immediately. Mission state is still used to reconstruct
    /// the same list whenever the field map is reopened.
    /// </summary>
    public void RecordPoiInFieldNotes(MissionPointOfInterest poi)
    {
        if (!poi)
            return;
        if (!explicitlyRecordedFieldNotePois.Contains(poi))
            explicitlyRecordedFieldNotePois.Add(poi);
        RefreshMissionFieldNote();
    }

    private static string ColumnName(int zeroBasedColumn)
    {
        return ((char)('A' + Mathf.Clamp(zeroBasedColumn, 0, 25))).ToString();
    }

    private void EnterFocusMode()
    {
        isFocused = true;

        // Capture the neutral flat-page pose before applying inspection pan.
        if (scanTransform != null)
        {
            scanBaseLocalRotation = scanTransform.localRotation;
            scanBaseLocalPosition = scanTransform.localPosition;
        }

        scanTarget = Vector2.zero;
        scanCurrent = Vector2.zero;

        // Apply the focus zoom immediately. LateUpdate will keep enforcing it
        // after normal camera/look scripts have updated.
        if (playerCamera != null)
            playerCamera.fieldOfView = zoomedFieldOfView;

        if (disableWhileFocused != null)
        {
            focusedBehaviourPreviousStates =
                new bool[disableWhileFocused.Length];

            for (int i = 0; i < disableWhileFocused.Length; i++)
            {
                Behaviour behaviour = disableWhileFocused[i];

                if (behaviour == null || behaviour == this)
                    continue;

                focusedBehaviourPreviousStates[i] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

        if (logFocusMode)
        {
            string cameraInfo = playerCamera != null
                ? $" Camera='{playerCamera.name}', FOV={playerCamera.fieldOfView:0.0}, TargetFOV={zoomedFieldOfView:0.0}"
                : " Player Camera is NULL.";

            Debug.Log("[WorldMapTool] FOCUS ENTERED." + cameraInfo, this);
        }
    }

    private void ExitFocusMode(bool immediate)
    {
        if (!isFocused)
            return;

        // Reset scan BEFORE normal FirstPersonLook is re-enabled.
        // This is the no-snap fix.
        scanTarget = Vector2.zero;
        scanCurrent = Vector2.zero;

        if (scanTransform != null)
        {
            scanTransform.localPosition = scanBaseLocalPosition;
            scanTransform.localRotation = scanBaseLocalRotation;
        }

        RestoreFocusedBehaviours();

        isFocused = false;

        if (immediate && playerCamera != null)
            playerCamera.fieldOfView = normalFieldOfView;

        if (logFocusMode)
            Debug.Log("[WorldMapTool] FOCUS EXITED.", this);
    }

    private void RestoreFocusedBehaviours()
    {
        if (disableWhileFocused == null ||
            focusedBehaviourPreviousStates == null)
        {
            focusedBehaviourPreviousStates = null;
            return;
        }

        for (int i = 0; i < disableWhileFocused.Length; i++)
        {
            Behaviour behaviour = disableWhileFocused[i];

            if (behaviour == null || behaviour == this)
                continue;

            if (i < focusedBehaviourPreviousStates.Length)
                behaviour.enabled = focusedBehaviourPreviousStates[i];
        }

        focusedBehaviourPreviousStates = null;
    }

    private void UpdateCameraZoom()
    {
        if (playerCamera == null)
            return;

        float targetFov =
            isFocused
                ? zoomedFieldOfView
                : normalFieldOfView;

        float t =
            1f - Mathf.Exp(
                -zoomSpeed *
                Time.unscaledDeltaTime);

        playerCamera.fieldOfView =
            Mathf.Lerp(
                playerCamera.fieldOfView,
                targetFov,
                t);
    }

    private void UpdateMapScan()
    {
        if (!isFocused || scanTransform == null)
            return;

        Vector2 delta = Vector2.zero;

        if (mapLookAction != null &&
            mapLookAction.action != null)
        {
            delta = mapLookAction.action.ReadValue<Vector2>();
        }

        // Standalone-build fallback if MapTool/Look is not currently feeding
        // mouse delta for any reason.
        if (delta.sqrMagnitude <= 0.000001f &&
            Mouse.current != null)
        {
            delta = Mouse.current.delta.ReadValue();
        }

        // Move the page opposite the gaze direction: looking right pulls the
        // right edge toward the centre; looking up pulls the top edge down.
        scanTarget.x -= delta.x * scanSensitivity;
        scanTarget.y -= delta.y * scanSensitivity;

        scanTarget.x =
            Mathf.Clamp(
                scanTarget.x,
                -maximumInspectionPan.x,
                maximumInspectionPan.x);

        scanTarget.y =
            Mathf.Clamp(
                scanTarget.y,
                -maximumInspectionPan.y,
                maximumInspectionPan.y);

        if (scanRecentreWhileHeld > 0f)
        {
            scanTarget =
                Vector2.MoveTowards(
                    scanTarget,
                    Vector2.zero,
                    scanRecentreWhileHeld *
                    Time.unscaledDeltaTime);
        }

        float t =
            1f - Mathf.Exp(
                -scanFollowSpeed *
                Time.unscaledDeltaTime);

        scanCurrent =
            Vector2.Lerp(
                scanCurrent,
                scanTarget,
                t);

        scanTransform.localPosition =
            scanBaseLocalPosition +
            new Vector3(scanCurrent.x, scanCurrent.y, 0f);

        // Inspection is a gaze pan across a rigid page, never a page bend.
        scanTransform.localRotation = scanBaseLocalRotation;
    }


    private void RestoreCamera()
    {
        if (playerCamera != null)
            playerCamera.fieldOfView = normalFieldOfView;
    }

    private void RestoreScanImmediately()
    {
        scanTarget = Vector2.zero;
        scanCurrent = Vector2.zero;

        if (scanTransform != null)
        {
            scanTransform.localPosition = scanBaseLocalPosition;
            scanTransform.localRotation = scanBaseLocalRotation;
        }
    }
}
