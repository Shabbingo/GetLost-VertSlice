using System.Collections.Generic;
using System.Linq;
using GetLost.Missions;
using GetLost.PlayerTools;
using GetLost.Wagon;
using TMPro;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GetLost.Cheats
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Debug/Cheat Menu Controller")]
    public sealed class CheatMenuController : MonoBehaviour
    {
        [Header("Access")]
        [Tooltip("Opens and closes the cheat lock. Home is the intended keypad Home key.")]
        [SerializeField] private Key openKey = Key.Home;
        [Tooltip("Many keyboards report keypad Home as Numpad 7 when Num Lock is enabled.")]
        [SerializeField] private Key alternateOpenKey = Key.Numpad7;
        [SerializeField] private string cheatCode = "1234";
        [SerializeField] private bool rememberUnlockUntilSceneChanges = true;

        [Header("Cheats")]
        [SerializeField] private bool speedRunEnabled;
        [SerializeField] private bool superStableLegsEnabled;
        [SerializeField] private bool invincibilityEnabled;
        [SerializeField, Min(1f)] private float speedRunMultiplier = 4f;
        [SerializeField, Min(0f)] private float teleportGroundOffset = 0.25f;

        [Header("Optional References")]
        [SerializeField] private MissionBoardController missionBoard;
        [SerializeField] private WalkingMotor playerMotor;
        [SerializeField] private PlayerInputContextManager inputContext;

        [Header("Appearance")]
        [SerializeField] private Color panelColour = new(0.055f, 0.065f, 0.075f, 0.97f);
        [SerializeField] private Color accentColour = new(0.93f, 0.66f, 0.18f, 1f);
        [SerializeField] private Color buttonColour = new(0.16f, 0.18f, 0.2f, 1f);

        private GameObject canvasRoot;
        private GameObject lockRoot;
        private GameObject menuRoot;
        private TMP_Text codeDisplay;
        private TMP_Text statusText;
        private TMP_Text speedButtonLabel;
        private TMP_Text stableLegsButtonLabel;
        private TMP_Text invincibilityButtonLabel;
        private RectTransform cheatListRoot;
        private readonly List<GameObject> poiButtons = new();
        private readonly Dictionary<Behaviour, bool> suspendedBehaviours = new();
        private string enteredCode = string.Empty;
        private bool unlocked;
        private bool isOpen;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private PlayerCheatState playerCheatState;

        public bool SpeedRunEnabled => speedRunEnabled;
        public bool SuperStableLegsEnabled => superStableLegsEnabled;
        public bool InvincibilityEnabled => invincibilityEnabled;
        public bool IsOpen => isOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<CheatMenuController>() == null)
                new GameObject("Cheat Menu Controller").AddComponent<CheatMenuController>();
        }

        private void Reset()
        {
            missionBoard = FindAnyObjectByType<MissionBoardController>();
            playerMotor = FindAnyObjectByType<WalkingMotor>();
            inputContext = FindAnyObjectByType<PlayerInputContextManager>();
        }

        private void Awake()
        {
            ResolveReferences();
            BuildInterface();
            ApplyCheatStates();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (!isOpen && (Pressed(keyboard, openKey) || Pressed(keyboard, alternateOpenKey)))
            {
                Open();
                return;
            }
            if (!isOpen)
                return;
            if (Pressed(keyboard, openKey) || keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            if (!unlocked)
                ReadPhysicalCodeInput(keyboard);
        }

        private void OnDisable()
        {
            if (isOpen)
                Close();
        }

        public void Open()
        {
            if (isOpen)
                return;
            ResolveReferences();
            ApplyCheatStates();
            isOpen = true;
            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            SuspendPlayerControls();
            if (inputContext)
                inputContext.RegisterCursorMenu(this);
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            canvasRoot.SetActive(true);
            ShowCurrentScreen();
        }

        public void Close()
        {
            if (!isOpen)
                return;
            canvasRoot.SetActive(false);
            if (inputContext)
                inputContext.UnregisterCursorMenu(this);
            else
            {
                Cursor.lockState = previousCursorLockMode;
                Cursor.visible = previousCursorVisible;
            }
            RestorePlayerControls();
            isOpen = false;
            enteredCode = string.Empty;
            if (!rememberUnlockUntilSceneChanges)
                unlocked = false;
        }

        public void SetSpeedRun(bool enabled)
        {
            speedRunEnabled = enabled;
            ApplySpeedRunState();
            RefreshSpeedButton();
        }

        private void ToggleSpeedRun() => SetSpeedRun(!speedRunEnabled);

        public void SetSuperStableLegs(bool enabled)
        {
            superStableLegsEnabled = enabled;
            ApplyCheatStates();
            RefreshToggleButtons();
        }

        public void SetInvincibility(bool enabled)
        {
            invincibilityEnabled = enabled;
            ApplyCheatStates();
            RefreshToggleButtons();
        }

        private void ToggleSuperStableLegs() => SetSuperStableLegs(!superStableLegsEnabled);
        private void ToggleInvincibility() => SetInvincibility(!invincibilityEnabled);

        private void ApplySpeedRunState()
        {
            if (!playerMotor)
                playerMotor = FindAnyObjectByType<WalkingMotor>();
            if (playerMotor)
                playerMotor.CheatSpeedMultiplier = speedRunEnabled ? speedRunMultiplier : 1f;
        }

        private void ApplyCheatStates()
        {
            ApplySpeedRunState();
            if (!playerMotor)
                return;
            playerMotor.CheatSuperStableLegs = superStableLegsEnabled;
            if (!playerCheatState)
                playerCheatState = playerMotor.GetComponent<PlayerCheatState>() ??
                                   playerMotor.gameObject.AddComponent<PlayerCheatState>();
            playerCheatState.SetInvincible(invincibilityEnabled);
        }

        private void TeleportTo(MissionPointOfInterest poi)
        {
            ResolveReferences();
            if (!playerMotor || !poi)
                return;

            Vector3 destination = poi.PinWorldPosition;
            Terrain terrain = Terrain.activeTerrains.FirstOrDefault(item =>
                item && destination.x >= item.transform.position.x &&
                destination.z >= item.transform.position.z &&
                destination.x <= item.transform.position.x + item.terrainData.size.x &&
                destination.z <= item.transform.position.z + item.terrainData.size.z);
            if (terrain)
                destination.y = terrain.SampleHeight(destination) + terrain.transform.position.y;
            else if (Physics.Raycast(destination + Vector3.up * 1000f, Vector3.down,
                         out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                destination.y = hit.point.y;
            destination.y += teleportGroundOffset;

            CharacterController controller = playerMotor.GetComponent<CharacterController>();
            bool controllerWasEnabled = controller && controller.enabled;
            if (controllerWasEnabled)
                controller.enabled = false;
            playerMotor.transform.position = destination;
            playerMotor.ResetMotion();
            if (controllerWasEnabled)
                controller.enabled = true;
            Physics.SyncTransforms();
            Close();
        }

        private void CallWagon()
        {
            ResolveReferences();
            if (playerMotor && WagonPrototypeBootstrap.CallWagonTo(playerMotor.transform))
            {
                Close();
                return;
            }
            statusText.text = "NO CLEAR WAGON POSITION FOUND";
            statusText.color = new Color(1f, .35f, .28f);
        }

        private void ResolveReferences()
        {
            if (!missionBoard)
                missionBoard = FindAnyObjectByType<MissionBoardController>();
            if (!playerMotor)
                playerMotor = FindAnyObjectByType<WalkingMotor>();
            if (playerMotor && !playerCheatState)
                playerCheatState = playerMotor.GetComponent<PlayerCheatState>();
            if (!inputContext)
                inputContext = FindAnyObjectByType<PlayerInputContextManager>();
        }

        private void SuspendPlayerControls()
        {
            suspendedBehaviours.Clear();
            if (!playerMotor)
                return;
            IEnumerable<Behaviour> controls = playerMotor.transform.root
                .GetComponentsInChildren<Behaviour>(true)
                .Where(item => item &&
                    (item == playerMotor || item.GetType().Name == "FirstPersonLook"));
            foreach (Behaviour control in controls.Distinct())
            {
                suspendedBehaviours[control] = control.enabled;
                control.enabled = false;
            }
        }

        private void RestorePlayerControls()
        {
            foreach (KeyValuePair<Behaviour, bool> pair in suspendedBehaviours)
                if (pair.Key)
                    pair.Key.enabled = pair.Value;
            suspendedBehaviours.Clear();
        }

        private void ReadPhysicalCodeInput(Keyboard keyboard)
        {
            if (keyboard.backspaceKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame)
                ClearCode();
            if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame) EnterDigit(0);
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) EnterDigit(1);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) EnterDigit(2);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) EnterDigit(3);
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) EnterDigit(4);
            if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) EnterDigit(5);
            if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) EnterDigit(6);
            if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) EnterDigit(7);
            if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) EnterDigit(8);
            if (keyboard.digit9Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame) EnterDigit(9);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                SubmitCode();
        }

        private static bool Pressed(Keyboard keyboard, Key key) =>
            key != Key.None && keyboard[key].wasPressedThisFrame;

        private void EnterDigit(int digit)
        {
            if (enteredCode.Length >= Mathf.Max(1, cheatCode.Length))
                enteredCode = string.Empty;
            enteredCode += digit.ToString();
            statusText.text = "ENTER ACCESS CODE";
            statusText.color = Color.white;
            RefreshCodeDisplay();
            if (enteredCode.Length >= Mathf.Max(1, cheatCode.Length))
                SubmitCode();
        }

        private void ClearCode()
        {
            enteredCode = string.Empty;
            statusText.text = "ENTER ACCESS CODE";
            statusText.color = Color.white;
            RefreshCodeDisplay();
        }

        private void SubmitCode()
        {
            if (enteredCode == cheatCode)
            {
                unlocked = true;
                enteredCode = string.Empty;
                ShowCurrentScreen();
                return;
            }
            enteredCode = string.Empty;
            statusText.text = "ACCESS DENIED";
            statusText.color = new Color(1f, .3f, .25f);
            RefreshCodeDisplay();
        }

        private void ShowCurrentScreen()
        {
            lockRoot.SetActive(!unlocked);
            menuRoot.SetActive(unlocked);
            if (unlocked)
            {
                BuildPoiButtons();
                RefreshToggleButtons();
                statusText.text = "CHEATS UNLOCKED";
                statusText.color = accentColour;
            }
            else
            {
                statusText.text = "ENTER ACCESS CODE";
                statusText.color = Color.white;
                RefreshCodeDisplay();
            }
        }

        private void RefreshCodeDisplay()
        {
            int length = Mathf.Max(4, cheatCode.Length);
            codeDisplay.text = string.Join("  ", Enumerable.Range(0, length)
                .Select(index => index < enteredCode.Length ? "●" : "—"));
        }

        private void RefreshSpeedButton()
        {
            if (speedButtonLabel)
                speedButtonLabel.text = speedRunEnabled
                    ? $"SPEED RUN  ON   ({speedRunMultiplier:0.#}×)"
                    : $"SPEED RUN  OFF  ({speedRunMultiplier:0.#}×)";
        }

        private void RefreshToggleButtons()
        {
            RefreshSpeedButton();
            if (stableLegsButtonLabel)
                stableLegsButtonLabel.text = superStableLegsEnabled
                    ? "SUPER STABLE LEGS  ON"
                    : "SUPER STABLE LEGS  OFF";
            if (invincibilityButtonLabel)
                invincibilityButtonLabel.text = invincibilityEnabled
                    ? "INVINCIBILITY  ON"
                    : "INVINCIBILITY  OFF";
        }

        private void BuildInterface()
        {
            EnsureEventSystem();
            canvasRoot = new GameObject("Cheat Menu UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            Image shade = CreateImage(canvasRoot.transform, "Backdrop", new Color(0f, 0f, 0f, .62f));
            Stretch(shade.rectTransform);
            Image panel = CreateImage(shade.transform, "Safe Panel", panelColour);
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
            panelRect.pivot = new Vector2(.5f, .5f);
            panelRect.sizeDelta = new Vector2(620f, 790f);

            TMP_Text title = CreateText(panel.transform, "Title", "CHEAT CODE LOCK", 38f, FontStyles.Bold);
            SetRect(title.rectTransform, new Vector2(30f, -84f), new Vector2(-30f, -20f), true);
            title.color = accentColour;
            statusText = CreateText(panel.transform, "Status", "ENTER ACCESS CODE", 20f, FontStyles.Bold);
            SetRect(statusText.rectTransform, new Vector2(30f, -126f), new Vector2(-30f, -88f), true);
            Button closeButton = CreateButton(panel.transform, "×", Close, 54f);
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-18f, -18f);
            closeRect.sizeDelta = new Vector2(58f, 58f);

            lockRoot = CreateVerticalRoot(panel.transform, "Code Lock", new Vector2(55f, 35f), new Vector2(-55f, -140f), 16f);
            codeDisplay = CreateText(lockRoot.transform, "Code Display", string.Empty, 42f, FontStyles.Bold);
            AddLayout(codeDisplay.gameObject, 92f);
            GameObject keypad = new("Safe Keypad", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            keypad.transform.SetParent(lockRoot.transform, false);
            GridLayoutGroup grid = keypad.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(145f, 74f);
            grid.spacing = new Vector2(14f, 14f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.MiddleCenter;
            AddLayout(keypad, 342f);
            for (int digit = 1; digit <= 9; digit++)
            {
                int captured = digit;
                CreateButton(keypad.transform, digit.ToString(), () => EnterDigit(captured));
            }
            CreateButton(keypad.transform, "CLEAR", ClearCode);
            CreateButton(keypad.transform, "0", () => EnterDigit(0));
            CreateButton(keypad.transform, "ENTER", SubmitCode);

            menuRoot = CreateVerticalRoot(panel.transform, "Cheat Menu", new Vector2(45f, 35f), new Vector2(-45f, -140f), 12f);
            cheatListRoot = CreateScrollArea(menuRoot.transform, "Cheat List", 610f);
            TMP_Text playerCheatsTitle = CreateText(cheatListRoot, "Player Cheats Title", "PLAYER CHEATS", 24f, FontStyles.Bold);
            playerCheatsTitle.color = accentColour;
            AddLayout(playerCheatsTitle.gameObject, 44f);
            Button speedButton = CreateButton(cheatListRoot, string.Empty, ToggleSpeedRun, 66f);
            speedButtonLabel = speedButton.GetComponentInChildren<TMP_Text>();
            Button stableButton = CreateButton(cheatListRoot, string.Empty, ToggleSuperStableLegs, 66f);
            stableLegsButtonLabel = stableButton.GetComponentInChildren<TMP_Text>();
            Button invincibilityButton = CreateButton(cheatListRoot, string.Empty, ToggleInvincibility, 66f);
            invincibilityButtonLabel = invincibilityButton.GetComponentInChildren<TMP_Text>();
            CreateButton(cheatListRoot, "CALL WAGON TO PLAYER", CallWagon, 66f);
            TMP_Text teleportTitle = CreateText(cheatListRoot, "Teleport Title", "TELEPORT TO POI", 24f, FontStyles.Bold);
            teleportTitle.color = accentColour;
            AddLayout(teleportTitle.gameObject, 44f);
            BuildPoiButtons();
            RefreshToggleButtons();
            canvasRoot.SetActive(false);
        }

        private void BuildPoiButtons()
        {
            if (!cheatListRoot)
                return;
            foreach (GameObject button in poiButtons)
                if (button)
                    Destroy(button);
            poiButtons.Clear();
            ResolveReferences();
            IEnumerable<MissionPointOfInterest> pois = missionBoard
                ? missionBoard.PointsOfInterest.Where(item => item)
                : FindObjectsByType<MissionPointOfInterest>(FindObjectsInactive.Include);
            foreach (MissionPointOfInterest poi in pois
                         .OrderBy(item => item.Kind).ThenBy(item => item.DisplayName))
            {
                MissionPointOfInterest captured = poi;
                string prefix = poi.Kind == MissionPoiKind.Major ? "[MAJOR]" : "[MINOR]";
                Button button = CreateButton(cheatListRoot, $"{prefix}  {poi.DisplayName}",
                    () => TeleportTo(captured), 54f);
                poiButtons.Add(button.gameObject);
            }
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action,
            float preferredHeight = 74f)
        {
            GameObject buttonObject = new(label + " Button", typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = buttonColour;
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colours = button.colors;
            colours.normalColor = buttonColour;
            colours.highlightedColor = Color.Lerp(buttonColour, accentColour, .28f);
            colours.pressedColor = Color.Lerp(buttonColour, accentColour, .55f);
            colours.selectedColor = colours.highlightedColor;
            button.colors = colours;
            button.onClick.AddListener(action);
            AddLayout(buttonObject, preferredHeight);
            TMP_Text text = CreateText(buttonObject.transform, "Label", label, 22f, FontStyles.Bold);
            Stretch(text.rectTransform, 8f);
            return button;
        }

        private RectTransform CreateScrollArea(Transform parent, string label, float preferredHeight)
        {
            GameObject scrollObject = new(label, typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            scrollObject.transform.SetParent(parent, false);
            AddLayout(scrollObject, preferredHeight);
            Image viewport = CreateImage(scrollObject.transform, "Viewport", new Color(0f, 0f, 0f, .18f));
            viewport.gameObject.AddComponent<RectMask2D>();
            Stretch(viewport.rectTransform);
            GameObject content = new(label + " Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(.5f, 1f);
            contentRect.offsetMin = new Vector2(8f, 0f);
            contentRect.offsetMax = new Vector2(-8f, 0f);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            return contentRect;
        }

        private static GameObject CreateVerticalRoot(Transform parent, string name,
            Vector2 offsetMin, Vector2 offsetMax, float spacing)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return root;
        }

        private static Image CreateImage(Transform parent, string name, Color colour)
        {
            GameObject imageObject = new(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = colour;
            return image;
        }

        private static TMP_Text CreateText(Transform parent, string name, string value,
            float fontSize, FontStyles style)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void AddLayout(GameObject target, float preferredHeight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetRect(RectTransform rect, Vector2 offsetMin,
            Vector2 offsetMax, bool topAnchored)
        {
            rect.anchorMin = topAnchored ? new Vector2(0f, 1f) : Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current || FindAnyObjectByType<EventSystem>())
                return;
            new GameObject("Cheat Menu Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
