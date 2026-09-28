using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
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
    [Tooltip("Optional map text. If empty, a quiet field note is created on the physical map at runtime.")]
    [SerializeField] private TMP_Text missionFieldNoteText;

    [SerializeField, Min(0.1f)] private float fieldNoteRefreshSeconds = 0.75f;

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

        EnsureMissionMarkerHierarchy();

        if (generateMapOnAwake)
            RefreshGeneratedMap();

        SetOpen(false, force: true);
    }

    private void OnEnable()
    {
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

        // Preferred method: use exactly the same map conversion as the topo generator.
        if (topologyMapGenerator != null &&
            topologyMapGenerator.WorldToPixel(
                worldPosition,
                out Vector2 pixelPosition))
        {
            float textureWidth =
                Mathf.Max(1f, topologyMapGenerator.textureWidth - 1f);

            float textureHeight =
                Mathf.Max(1f, topologyMapGenerator.textureHeight - 1f);

            float u = pixelPosition.x / textureWidth;
            float v = pixelPosition.y / textureHeight;

            if (clampStartMarkerToMap)
            {
                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);
            }

            // START and END markers should both be children of the RawImage.
            marker.anchorMin = new Vector2(u, v);
            marker.anchorMax = new Vector2(u, v);
            marker.anchoredPosition = Vector2.zero;
            marker.gameObject.SetActive(true);

            return;
        }

        // Fallback: terrain bounds.
        float width = worldMaxXZ.x - worldMinXZ.x;
        float height = worldMaxXZ.y - worldMinXZ.y;

        if (Mathf.Abs(width) < 0.001f || Mathf.Abs(height) < 0.001f)
            return;

        float uFallback =
            Mathf.InverseLerp(
                worldMinXZ.x,
                worldMaxXZ.x,
                worldPosition.x);

        float vFallback =
            Mathf.InverseLerp(
                worldMinXZ.y,
                worldMaxXZ.y,
                worldPosition.z);

        if (clampStartMarkerToMap)
        {
            uFallback = Mathf.Clamp01(uFallback);
            vFallback = Mathf.Clamp01(vFallback);
        }

        marker.anchorMin = new Vector2(uFallback, vFallback);
        marker.anchorMax = new Vector2(uFallback, vFallback);
        marker.anchoredPosition = Vector2.zero;
        marker.gameObject.SetActive(true);
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
        if (missionFieldNoteText == null)
        {
            CreateMissionFieldNote();
        }

        if (missionFieldNoteText == null)
        {
            return;
        }

        // Keep this tool portable. Mission systems may assign this text or call
        // the marker methods, but the vertical slice does not require them.
        if (string.IsNullOrWhiteSpace(missionFieldNoteText.text))
            missionFieldNoteText.text = "FIELD NOTE\nNo active survey.";
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
        noteObject.transform.SetParent(mapImage.transform, false);

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
        text.enableWordWrapping = true;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = new Color(0.12f, 0.1f, 0.07f, 0.92f);
        text.raycastTarget = false;

        missionFieldNoteText = text;
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
