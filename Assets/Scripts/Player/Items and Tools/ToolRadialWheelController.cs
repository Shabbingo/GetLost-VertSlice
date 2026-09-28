using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GetLost.PlayerTools
{
    /// <summary>
    /// Hold-ALT radial tool wheel using an elastic-band virtual selector.
    ///
    /// PlayerInputContextManager has priority:
    /// if an interactive menu is open, this wheel will not open.
    /// </summary>
    [DisallowMultipleComponent]
    public class ToolRadialWheelController : MonoBehaviour
    {
        public enum RadialDirection
        {
            Right,
            Up,
            Left,
            Down,
            UpRight,
            DownRight,
            DownLeft,
            UpLeft
        }

        [Serializable]
        public class ToolSlot
        {
            public PlayerToolType toolType = PlayerToolType.None;

            [Tooltip("Where this tool sits on the radial wheel.")]
            public RadialDirection direction = RadialDirection.Right;

            [Header("Slot UI")]
            [Tooltip("The sprite displayed for this tool. Assign your custom icon here.")]
            public Sprite iconSprite;

            public Color iconTint = Color.white;

            [Tooltip("Optional alternate sprite used while this slot is highlighted.")]
            public Sprite selectedSprite;

            public Color selectedTint = Color.white;

            [Tooltip("Base UI size before automatic fit scaling.")]
            public Vector2 iconSize = new Vector2(75f, 75f);

            [Tooltip("Keeps the sprite's original proportions.")]
            public bool preserveAspect = true;

            [Range(0.1f, 3f)]
            [Tooltip("Per-slot size adjustment after automatic fitting.")]
            public float iconScale = 1f;

            [HideInInspector]
            public RectTransform scaleTarget;

            [NonSerialized]
            public Image runtimeImage;
        }

        [Header("References")]
        [SerializeField]
        private PlayerToolManager toolManager;

        [SerializeField]
        private PlayerInputContextManager inputContextManager;

        [SerializeField]
        private GameObject wheelRoot;

        [Header("Automatic Wheel UI")]
        [Tooltip("Creates a simple screen-space wheel if no custom Wheel Root is assigned.")]
        [SerializeField]
        private bool createDefaultWheelUI = true;

        [Tooltip("Places slot UI around the wheel and scales it down when the available space is tight.")]
        [SerializeField]
        private bool autoLayoutSlotVisuals = true;

        [Min(40f)]
        [SerializeField]
        private float uiLayoutRadius = 190f;

        [Min(0f)]
        [SerializeField]
        private float uiSlotSpacing = 14f;

        [Range(0.1f, 1f)]
        [SerializeField]
        private float minimumUiSlotScale = 0.5f;

        [Header("Input")]
        [Tooltip("Button action bound to Left Alt.")]
        [SerializeField]
        private InputActionReference holdWheelAction;

        [Tooltip(
            "Safety fallback: reads Left/Right Alt directly from the Input System as well as the InputActionReference. " +
            "Useful if another PlayerInput/action-map transition disables the referenced action."
        )]
        [SerializeField]
        private bool useDirectAltFallback = true;

        [Tooltip("Logs why an ALT press was accepted or blocked.")]
        [SerializeField]
        private bool logInputDebug = true;

        [Header("Slots")]
        [SerializeField]
        private List<ToolSlot> slots = new List<ToolSlot>();

        [Header("Elastic Selector")]
        [Min(0.01f)]
        [SerializeField]
        private float mouseSensitivity = 1.5f;

        [Min(10f)]
        [SerializeField]
        private float maximumRadius = 150f;

        [Min(0f)]
        [SerializeField]
        private float selectionDeadZone = 28f;

        [Min(0f)]
        [SerializeField]
        private float springDelay = 0.06f;

        [Min(1f)]
        [SerializeField]
        private float springReturnSpeed = 420f;

        [Min(0f)]
        [SerializeField]
        private float movementThreshold = 0.25f;

        [SerializeField]
        private bool clearHighlightWhenRecentred = false;

        [SerializeField]
        private RectTransform selectorVisual;

        [SerializeField]
        private bool invertVerticalSelection = false;

        [Header("Highlight")]
        [Min(1f)]
        [SerializeField]
        private float selectedScale = 1.15f;

        [Header("Cursor")]
        [SerializeField]
        private bool keepRealCursorLocked = true;

        [Header("Disable While Open")]
        [SerializeField]
        private Behaviour[] disableWhileOpen;

        [Header("Behaviour")]
        [SerializeField]
        private bool selectOnRelease = true;

        [SerializeField]
        private bool keepCurrentToolWhenNoSelection = true;

        [Header("Runtime Debug")]
        [SerializeField]
        private bool isOpen;

        [SerializeField]
        private Vector2 selectorOffset;

        [SerializeField]
        private Vector2 currentMouseDelta;

        [SerializeField]
        private PlayerToolType highlightedTool = PlayerToolType.None;

        [SerializeField]
        private int highlightedSlotIndex = -1;

        [SerializeField]
        private float timeSinceMouseMovement;

        [SerializeField]
        private bool menuCurrentlyBlocksWheel;

        [SerializeField]
        private bool logSelection = true;

        [SerializeField]
        private bool holdActionAvailable;

        [SerializeField]
        private bool holdActionEnabled;

        [SerializeField]
        private bool directAltHeld;

        [SerializeField]
        private string lastOpenBlockReason = "";

        private bool previousDirectAltHeld;

        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private bool[] previousBehaviourStates;

        private readonly List<Vector3>
            originalSlotScales = new List<Vector3>();

        public bool IsOpen => isOpen;
        public PlayerToolType HighlightedTool => highlightedTool;

        private void Awake()
        {
            ResolveReferences();
            EnsureWheelUI();
            BuildAndLayoutSlotVisuals();
            CacheOriginalSlotScales();

            if (wheelRoot != null)
                wheelRoot.SetActive(false);

            if (selectorVisual != null)
                selectorVisual.gameObject.SetActive(false);

            ApplySlotVisuals(-1);
        }

        private void OnEnable()
        {
            ResolveReferences();

            holdActionAvailable =
                holdWheelAction?.action != null;

            if (holdActionAvailable)
            {
                holdWheelAction.action.started -= OnWheelStarted;
                holdWheelAction.action.canceled -= OnWheelCanceled;

                holdWheelAction.action.started += OnWheelStarted;
                holdWheelAction.action.canceled += OnWheelCanceled;

                holdWheelAction.action.Enable();

                holdActionEnabled =
                    holdWheelAction.action.enabled;
            }
            else
            {
                holdActionEnabled = false;

                if (logInputDebug)
                {
                    Debug.LogWarning(
                        "[Tool Wheel] Hold Wheel Action is not assigned. " +
                        "Direct ALT fallback will be used if enabled.",
                        this
                    );
                }
            }

            previousDirectAltHeld = false;
        }

        private void OnDisable()
        {
            if (holdWheelAction?.action != null)
            {
                holdWheelAction.action.started -= OnWheelStarted;
                holdWheelAction.action.canceled -= OnWheelCanceled;
            }

            CloseWheel(false);

            previousDirectAltHeld = false;
            directAltHeld = false;
        }

        private void Update()
        {
            UpdateDirectAltFallback();

            holdActionAvailable =
                holdWheelAction?.action != null;

            holdActionEnabled =
                holdActionAvailable &&
                holdWheelAction.action.enabled;

            menuCurrentlyBlocksWheel =
                inputContextManager != null &&
                inputContextManager.IsMenuOpen;

            // A prompt/menu can appear while ALT is already held.
            // If so, immediately give priority to the menu.
            if (isOpen && menuCurrentlyBlocksWheel)
            {
                CloseWheel(false);
                return;
            }

            if (!isOpen)
                return;

            if (keepRealCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            UpdateElasticSelector();
            UpdateSelectionFromOffset();
            UpdateSelectorVisual();
        }

        private void UpdateDirectAltFallback()
        {
            if (!useDirectAltFallback ||
                Keyboard.current == null)
            {
                directAltHeld = false;
                previousDirectAltHeld = false;
                return;
            }

            directAltHeld =
                Keyboard.current.leftAltKey.isPressed ||
                Keyboard.current.rightAltKey.isPressed;

            if (directAltHeld &&
                !previousDirectAltHeld)
            {
                if (logInputDebug)
                {
                    Debug.Log(
                        "[Tool Wheel] ALT detected by direct keyboard fallback.",
                        this
                    );
                }

                OpenWheel();
            }
            else if (!directAltHeld &&
                     previousDirectAltHeld)
            {
                CloseWheel(selectOnRelease);
            }

            previousDirectAltHeld = directAltHeld;
        }

        private void OnWheelStarted(
            InputAction.CallbackContext context)
        {
            if (logInputDebug)
            {
                Debug.Log(
                    "[Tool Wheel] Hold action STARTED.",
                    this
                );
            }

            OpenWheel();
        }

        private void OnWheelCanceled(
            InputAction.CallbackContext context)
        {
            if (logInputDebug)
            {
                Debug.Log(
                    "[Tool Wheel] Hold action CANCELED.",
                    this
                );
            }

            CloseWheel(selectOnRelease);
        }

        [ContextMenu("Open Wheel")]
        public void OpenWheel()
        {
            if (isOpen)
                return;

            ResolveReferences();
            EnsureWheelUI();
            BuildAndLayoutSlotVisuals();
            CacheOriginalSlotScales();

            lastOpenBlockReason = "";

            // Explicit context has priority over cursor state.
            if (inputContextManager != null &&
                inputContextManager.IsMenuOpen)
            {
                lastOpenBlockReason =
                    $"Blocked by menu context. Registered menus: {inputContextManager.RegisteredMenuCount}";

                if (logInputDebug)
                {
                    Debug.LogWarning(
                        $"[Tool Wheel] {lastOpenBlockReason}",
                        this
                    );
                }

                return;
            }

            // Normal gameplay should already have a locked/hidden cursor.
            if (Cursor.lockState != CursorLockMode.Locked ||
                Cursor.visible)
            {
                lastOpenBlockReason =
                    $"Blocked by cursor state. Lock={Cursor.lockState}, Visible={Cursor.visible}";

                if (logInputDebug)
                {
                    Debug.LogWarning(
                        $"[Tool Wheel] {lastOpenBlockReason}",
                        this
                    );
                }

                return;
            }

            if (wheelRoot == null)
            {
                lastOpenBlockReason =
                    "Wheel Root is not assigned.";

                if (logInputDebug)
                {
                    Debug.LogWarning(
                        $"[Tool Wheel] {lastOpenBlockReason}",
                        this
                    );
                }
            }

            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;

            isOpen = true;

            if (logInputDebug)
            {
                Debug.Log(
                    "[Tool Wheel] OPENED.",
                    this
                );
            }

            selectorOffset = Vector2.zero;
            currentMouseDelta = Vector2.zero;
            timeSinceMouseMovement = 0f;

            highlightedTool = PlayerToolType.None;
            highlightedSlotIndex = -1;

            DisableConflictingBehaviours();

            if (keepRealCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (wheelRoot != null)
                wheelRoot.SetActive(true);

            if (selectorVisual != null)
            {
                selectorVisual.gameObject.SetActive(true);
                selectorVisual.anchoredPosition = Vector2.zero;
            }

            ApplySlotVisuals(-1);
        }

        public void CloseWheel(bool confirmSelection)
        {
            if (!isOpen)
                return;

            if (confirmSelection)
                ConfirmHighlightedTool();

            if (logInputDebug)
            {
                Debug.Log(
                    $"[Tool Wheel] CLOSED. Confirm={confirmSelection}.",
                    this
                );
            }

            isOpen = false;

            selectorOffset = Vector2.zero;
            currentMouseDelta = Vector2.zero;
            highlightedTool = PlayerToolType.None;
            highlightedSlotIndex = -1;

            ApplySlotVisuals(-1);

            if (selectorVisual != null)
                selectorVisual.gameObject.SetActive(false);

            if (wheelRoot != null)
                wheelRoot.SetActive(false);

            RestoreConflictingBehaviours();

            // Do not overwrite a menu cursor that became active while the
            // radial was closing.
            if (inputContextManager == null ||
                !inputContextManager.IsMenuCursorActive)
            {
                Cursor.lockState = previousCursorLockMode;
                Cursor.visible = previousCursorVisible;
            }
        }

        [ContextMenu("Close Wheel Without Selection")]
        public void CloseWheelWithoutSelection()
        {
            CloseWheel(false);
        }

        public void ConfirmHighlightedTool()
        {
            ResolveReferences();

            if (toolManager == null)
            {
                Debug.LogWarning(
                    "[ToolRadialWheelController] No PlayerToolManager assigned.",
                    this
                );
                return;
            }

            if (highlightedSlotIndex < 0)
            {
                if (!keepCurrentToolWhenNoSelection)
                    toolManager.UnequipCurrentTool();

                return;
            }

            ToolSlot slot = slots[highlightedSlotIndex];

            if (slot == null)
                return;

            bool selected =
                toolManager.SelectTool(slot.toolType);

            if (selected && logSelection)
            {
                Debug.Log(
                    $"[Tool Wheel] Selected {slot.toolType}.",
                    this
                );
            }
        }

        private void UpdateElasticSelector()
        {
            if (Mouse.current == null)
                return;

            Vector2 delta =
                Mouse.current.delta.ReadValue();

            if (invertVerticalSelection)
                delta.y *= -1f;

            currentMouseDelta = delta;

            if (delta.magnitude >= movementThreshold)
            {
                timeSinceMouseMovement = 0f;

                selectorOffset +=
                    delta * mouseSensitivity;

                selectorOffset =
                    Vector2.ClampMagnitude(
                        selectorOffset,
                        maximumRadius
                    );
            }
            else
            {
                timeSinceMouseMovement +=
                    Time.unscaledDeltaTime;

                if (timeSinceMouseMovement >= springDelay)
                {
                    selectorOffset =
                        Vector2.MoveTowards(
                            selectorOffset,
                            Vector2.zero,
                            springReturnSpeed *
                            Time.unscaledDeltaTime
                        );
                }
            }
        }

        private void UpdateSelectionFromOffset()
        {
            if (slots == null || slots.Count == 0)
                return;

            if (selectorOffset.magnitude < selectionDeadZone)
            {
                if (clearHighlightWhenRecentred)
                    SetHighlightedSlot(-1);

                return;
            }

            float angle =
                Mathf.Atan2(
                    selectorOffset.y,
                    selectorOffset.x
                ) * Mathf.Rad2Deg;

            RadialDirection direction;

            if (angle >= 22.5f && angle < 67.5f)
                direction = RadialDirection.UpRight;
            else if (angle >= 67.5f && angle < 112.5f)
                direction = RadialDirection.Up;
            else if (angle >= 112.5f && angle < 157.5f)
                direction = RadialDirection.UpLeft;
            else if (angle >= -22.5f && angle < 22.5f)
                direction = RadialDirection.Right;
            else if (angle >= -67.5f && angle < -22.5f)
                direction = RadialDirection.DownRight;
            else if (angle >= -112.5f && angle < -67.5f)
                direction = RadialDirection.Down;
            else if (angle >= -157.5f && angle < -112.5f)
                direction = RadialDirection.DownLeft;
            else
                direction = RadialDirection.Left;

            HighlightDirection(direction);
        }

        private void HighlightDirection(
            RadialDirection direction)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ToolSlot slot = slots[i];

                if (slot == null ||
                    slot.toolType == PlayerToolType.None)
                {
                    continue;
                }

                if (slot.direction == direction)
                {
                    SetHighlightedSlot(i);
                    return;
                }
            }
        }

        private void SetHighlightedSlot(int index)
        {
            if (highlightedSlotIndex == index)
                return;

            highlightedSlotIndex = index;

            if (index >= 0 &&
                index < slots.Count &&
                slots[index] != null)
            {
                highlightedTool =
                    slots[index].toolType;
            }
            else
            {
                highlightedTool =
                    PlayerToolType.None;
            }

            ApplySlotVisuals(index);
        }

        private void ApplySlotVisuals(int selectedIndex)
        {
            EnsureScaleCache();

            for (int i = 0; i < slots.Count; i++)
            {
                ToolSlot slot = slots[i];

                if (slot == null)
                    continue;

                bool selected =
                    i == selectedIndex;

                if (slot.runtimeImage != null)
                {
                    bool useSelectedSprite =
                        selected && slot.selectedSprite != null;

                    slot.runtimeImage.sprite =
                        useSelectedSprite
                            ? slot.selectedSprite
                            : slot.iconSprite;

                    if (slot.iconSprite != null ||
                        useSelectedSprite)
                    {
                        slot.runtimeImage.color =
                            selected
                                ? slot.selectedTint
                                : slot.iconTint;
                    }
                }

                if (slot.scaleTarget != null &&
                    i < originalSlotScales.Count)
                {
                    slot.scaleTarget.localScale =
                        selected
                            ? originalSlotScales[i] * selectedScale
                            : originalSlotScales[i];
                }
            }
        }

        private void UpdateSelectorVisual()
        {
            if (selectorVisual == null)
                return;

            selectorVisual.anchoredPosition =
                selectorOffset;
        }

        private void DisableConflictingBehaviours()
        {
            if (disableWhileOpen == null)
                return;

            previousBehaviourStates =
                new bool[disableWhileOpen.Length];

            for (int i = 0; i < disableWhileOpen.Length; i++)
            {
                Behaviour behaviour =
                    disableWhileOpen[i];

                if (behaviour == null ||
                    behaviour == this)
                {
                    continue;
                }

                previousBehaviourStates[i] =
                    behaviour.enabled;

                behaviour.enabled = false;
            }
        }

        private void RestoreConflictingBehaviours()
        {
            if (disableWhileOpen == null ||
                previousBehaviourStates == null)
            {
                previousBehaviourStates = null;
                return;
            }

            for (int i = 0; i < disableWhileOpen.Length; i++)
            {
                Behaviour behaviour =
                    disableWhileOpen[i];

                if (behaviour == null ||
                    behaviour == this)
                {
                    continue;
                }

                if (i < previousBehaviourStates.Length)
                {
                    behaviour.enabled =
                        previousBehaviourStates[i];
                }
            }

            previousBehaviourStates = null;
        }

        private void CacheOriginalSlotScales()
        {
            originalSlotScales.Clear();

            for (int i = 0; i < slots.Count; i++)
            {
                ToolSlot slot = slots[i];

                if (slot?.scaleTarget != null)
                    originalSlotScales.Add(slot.scaleTarget.localScale);
                else
                    originalSlotScales.Add(Vector3.one);
            }
        }

        private void EnsureScaleCache()
        {
            if (originalSlotScales.Count == slots.Count)
                return;

            CacheOriginalSlotScales();
        }

        private void ResolveReferences()
        {
            if (toolManager == null)
            {
                toolManager =
                    GetComponentInParent<PlayerToolManager>();
            }

            if (toolManager == null)
            {
                toolManager =
                    FindAnyObjectByType<PlayerToolManager>();
            }

            if (inputContextManager == null)
            {
                inputContextManager =
                    GetComponentInParent<PlayerInputContextManager>();
            }

            if (inputContextManager == null)
            {
                inputContextManager =
                    FindAnyObjectByType<PlayerInputContextManager>();
            }
        }

        private void EnsureWheelUI()
        {
            if (wheelRoot == null)
            {
                GameObject canvasObject = new GameObject(
                    "Tool Wheel Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster)
                );
                canvasObject.transform.SetParent(transform, false);

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 40;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                GameObject contentObject = new GameObject(
                    "Wheel Content",
                    typeof(RectTransform)
                );
                contentObject.transform.SetParent(canvasObject.transform, false);

                RectTransform content =
                    contentObject.GetComponent<RectTransform>();
                content.anchorMin = content.anchorMax =
                    content.pivot = new Vector2(0.5f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(1f, 1f);

                wheelRoot = contentObject;
            }

        }

        private void BuildAndLayoutSlotVisuals()
        {
            if (wheelRoot == null)
                return;

            foreach (ToolSlot slot in slots)
            {
                if (slot == null ||
                    slot.toolType == PlayerToolType.None)
                    continue;

                EnsureSlotVisual(slot);
            }

            if (autoLayoutSlotVisuals)
                LayoutSlotVisuals();
        }

        private void EnsureSlotVisual(ToolSlot slot)
        {
            RectTransform parent =
                wheelRoot.GetComponent<RectTransform>();

            if (parent == null)
                return;

            RectTransform icon = slot.scaleTarget;

            if (icon == null ||
                !icon.IsChildOf(wheelRoot.transform))
            {
                if (!createDefaultWheelUI)
                    return;

                GameObject iconObject = new GameObject(
                    slot.toolType.ToString(),
                    typeof(RectTransform),
                    typeof(Image)
                );
                iconObject.transform.SetParent(parent, false);
                icon = iconObject.GetComponent<RectTransform>();
                slot.scaleTarget = icon;
            }

            icon.gameObject.name = slot.toolType.ToString();
            icon.sizeDelta = slot.iconSize;

            Image image = icon.GetComponent<Image>();
            if (image == null)
                image = icon.gameObject.AddComponent<Image>();

            image.sprite = slot.iconSprite;
            image.preserveAspect = slot.preserveAspect;
            image.raycastTarget = false;
            image.color = slot.iconSprite != null
                ? slot.iconTint
                : new Color(0.08f, 0.11f, 0.09f, 0.88f);
            slot.runtimeImage = image;

            Transform existingLabel = icon.Find("Label");

            if (slot.iconSprite != null)
            {
                if (existingLabel != null)
                    existingLabel.gameObject.SetActive(false);

                return;
            }

            if (existingLabel == null)
            {
                GameObject labelObject = new GameObject(
                    "Label",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI)
                );
                labelObject.transform.SetParent(icon, false);
                existingLabel = labelObject.transform;
            }

            existingLabel.gameObject.SetActive(true);

            RectTransform labelRect =
                existingLabel.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 8f);
            labelRect.offsetMax = new Vector2(-8f, -8f);

            TextMeshProUGUI label =
                existingLabel.GetComponent<TextMeshProUGUI>();
            label.text = slot.toolType.ToString().ToUpperInvariant();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.9f, 0.86f, 0.7f, 1f);
            label.raycastTarget = false;
        }

        private void LayoutSlotVisuals()
        {
            List<ToolSlot> visibleSlots = new List<ToolSlot>();

            foreach (ToolSlot slot in slots)
            {
                if (slot?.scaleTarget != null &&
                    slot.toolType != PlayerToolType.None)
                {
                    visibleSlots.Add(slot);
                }
            }

            int slotCount = visibleSlots.Count;
            if (slotCount == 0)
                return;

            float chordLength =
                2f * uiLayoutRadius *
                Mathf.Sin(Mathf.PI / slotCount) -
                uiSlotSpacing;

            foreach (ToolSlot slot in visibleSlots)
            {
                RectTransform visual = slot.scaleTarget;
                visual.anchorMin = visual.anchorMax =
                    visual.pivot = new Vector2(0.5f, 0.5f);
                visual.anchoredPosition =
                    DirectionToVector(slot.direction) * uiLayoutRadius;

                float largestDimension = Mathf.Max(
                    visual.rect.width,
                    visual.rect.height,
                    1f
                );
                float fitScale = Mathf.Clamp(
                    chordLength / largestDimension,
                    minimumUiSlotScale,
                    1f
                );
                visual.localScale =
                    Vector3.one * fitScale * slot.iconScale;
            }
        }

        private static Vector2 DirectionToVector(
            RadialDirection direction)
        {
            switch (direction)
            {
                case RadialDirection.Up:
                    return Vector2.up;
                case RadialDirection.Left:
                    return Vector2.left;
                case RadialDirection.Down:
                    return Vector2.down;
                case RadialDirection.UpRight:
                    return new Vector2(1f, 1f).normalized;
                case RadialDirection.DownRight:
                    return new Vector2(1f, -1f).normalized;
                case RadialDirection.DownLeft:
                    return new Vector2(-1f, -1f).normalized;
                case RadialDirection.UpLeft:
                    return new Vector2(-1f, 1f).normalized;
                default:
                    return Vector2.right;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
            maximumRadius = Mathf.Max(10f, maximumRadius);

            selectionDeadZone =
                Mathf.Clamp(
                    selectionDeadZone,
                    0f,
                    maximumRadius
                );

            springDelay = Mathf.Max(0f, springDelay);
            springReturnSpeed = Mathf.Max(1f, springReturnSpeed);
            movementThreshold = Mathf.Max(0f, movementThreshold);
            selectedScale = Mathf.Max(1f, selectedScale);
            uiLayoutRadius = Mathf.Max(40f, uiLayoutRadius);
            uiSlotSpacing = Mathf.Max(0f, uiSlotSpacing);
            minimumUiSlotScale = Mathf.Clamp01(minimumUiSlotScale);

            foreach (ToolSlot slot in slots)
            {
                if (slot == null)
                    continue;

                slot.iconSize.x = Mathf.Max(1f, slot.iconSize.x);
                slot.iconSize.y = Mathf.Max(1f, slot.iconSize.y);
                slot.iconScale = Mathf.Clamp(slot.iconScale, 0.1f, 3f);
            }
        }
#endif
    }
}
