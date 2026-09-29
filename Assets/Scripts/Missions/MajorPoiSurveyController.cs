using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GetLost.Missions
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Missions/Major POI Survey Controller")]
    public sealed class MajorPoiSurveyController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MissionBoardController missionBoard;
        [SerializeField] private Camera playerCamera;
        [Tooltip("Optional additional player behaviours to suspend during the camera flight.")]
        [SerializeField] private Behaviour[] additionalBehavioursToDisable;

        [Header("Offer")]
        [SerializeField] private Key surveyKey = Key.F;
        [SerializeField, Min(1f)] private float offerDuration = 15f;
        [SerializeField] private string promptFormat = "{0} reached\nPress [{1}] to survey the surrounding land";

        [Header("Flight")]
        [SerializeField, Min(5f)] private float surveyHeight = 90f;
        [SerializeField, Min(0f)] private float orbitRadius = 28f;
        [SerializeField, Min(.1f)] private float ascentDuration = 3f;
        [SerializeField, Min(.1f)] private float overviewDuration = 7f;
        [SerializeField, Range(0f, 720f)] private float orbitDegrees = 150f;
        [SerializeField, Min(.1f)] private float returnDuration = 2.5f;
        [SerializeField] private Vector3 lookTargetOffset = new(0f, 3f, 0f);

        [Header("Camera Blend Curves")]
        [Tooltip("Controls the easing used while the camera rises to the overview and returns to the player.")]
        [SerializeField] private AnimationCurve flightBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Controls the easing of each camera pan from one revealed POI to the next.")]
        [SerializeField] private AnimationCurve poiPanBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Controls progress around the overview orbit. A linear curve gives a constant orbit speed.")]
        [SerializeField] private AnimationCurve orbitBlendCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Minor POI Reveal")]
        [Tooltip("After reaching survey height, pan to every Minor POI revealed by the completed Major POI.")]
        [SerializeField] private bool panToRevealedMinorPois = true;
        [SerializeField, Min(.1f)] private float minorPoiPanDuration = 1.35f;
        [SerializeField, Min(.1f)] private float minorPoiFocusDuration = 2.25f;
        [SerializeField] private Vector3 minorPoiLookOffset = new(0f, 2f, 0f);
        [SerializeField] private string minorPoiLabelFormat = "MINOR POI DISCOVERED\n{0}\nPress [{1}] to return";

        [Header("Prompt Appearance")]
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private Color promptColour = new(1f, .88f, .5f, 1f);

        [Header("Survey Notebook")]
        [Tooltip("Show a field notebook on the left of the screen while the bird's-eye survey is running.")]
        [SerializeField] private bool showSurveyNotebook = true;
        [Tooltip("Field map used to convert POI positions into the same chess-grid coordinates. Found automatically when empty.")]
        [SerializeField] private WorldMapTool fieldMapTool;
        [SerializeField] private RectTransform surveyNotebookPanel;
        [SerializeField] private TMP_Text surveyNotebookText;
        [SerializeField] private Vector2 surveyNotebookSize = new(310f, 500f);
        [SerializeField] private Vector2 surveyNotebookScreenOffset = new(28f, 0f);
        [SerializeField, Range(-12f, 12f)] private float surveyNotebookRotation = -2f;
        [SerializeField] private Color surveyNotebookPaperColour = new(.91f, .84f, .65f, .98f);
        [SerializeField] private Color surveyNotebookSpineColour = new(.23f, .14f, .07f, 1f);
        [SerializeField] private Color surveyNotebookInkColour = new(.12f, .1f, .07f, .95f);
        [SerializeField, Min(10f)] private float surveyNotebookFontSize = 21f;

        private MissionPointOfInterest pendingPoi;
        private float offerExpiresAt;
        private Coroutine flightRoutine;
        private readonly Dictionary<Behaviour, bool> suspendedBehaviours = new();
        private Transform originalParent;
        private int originalSiblingIndex;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private bool cameraDetached;
        private Canvas surveyCanvas;
        private string surveyNotebookHeading;
        private readonly List<string> surveyedPoiEntries = new();

        public bool IsSurveying => flightRoutine != null;

        private void Reset()
        {
            missionBoard = GetComponent<MissionBoardController>();
            playerCamera = Camera.main;
        }

        public void Configure(MissionBoardController board)
        {
            if (missionBoard == board)
            {
                Subscribe();
                return;
            }
            Unsubscribe();
            missionBoard = board;
            Subscribe();
        }

        private void OnEnable()
        {
            if (!missionBoard)
                missionBoard = GetComponent<MissionBoardController>();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (flightRoutine != null)
                StopCoroutine(flightRoutine);
            flightRoutine = null;
            RestoreCameraAndControls();
            HidePrompt();
            HideSurveyNotebook();
        }

        private void Update()
        {
            if (pendingPoi && flightRoutine == null)
            {
                if (Time.unscaledTime >= offerExpiresAt)
                {
                    pendingPoi = null;
                    HidePrompt();
                }
                else if (WasSurveyKeyPressed())
                {
                    MissionPointOfInterest target = pendingPoi;
                    pendingPoi = null;
                    HidePrompt();
                    flightRoutine = StartCoroutine(SurveyRoutine(target));
                }
            }
        }

        private void Subscribe()
        {
            if (!missionBoard)
                return;
            missionBoard.PoiCompleted -= OnPoiCompleted;
            missionBoard.PoiCompleted += OnPoiCompleted;
        }

        private void Unsubscribe()
        {
            if (missionBoard)
                missionBoard.PoiCompleted -= OnPoiCompleted;
        }

        private void OnPoiCompleted(MissionPointOfInterest poi)
        {
            if (!poi || poi.Kind != MissionPoiKind.Major || flightRoutine != null)
                return;
            pendingPoi = poi;
            offerExpiresAt = Time.unscaledTime + offerDuration;
            EnsurePrompt();
            promptText.text = string.Format(promptFormat, poi.DisplayName, surveyKey);
            promptText.gameObject.SetActive(true);
        }

        private IEnumerator SurveyRoutine(MissionPointOfInterest poi)
        {
            if (!playerCamera)
                playerCamera = Camera.main;
            if (!playerCamera)
            {
                Debug.LogWarning("[Major POI Survey] No player camera was found.", this);
                flightRoutine = null;
                yield break;
            }

            BeginSurveyNotebook(poi);
            SuspendPlayerControls();
            Transform cameraTransform = playerCamera.transform;
            originalParent = cameraTransform.parent;
            originalSiblingIndex = cameraTransform.GetSiblingIndex();
            originalLocalPosition = cameraTransform.localPosition;
            originalLocalRotation = cameraTransform.localRotation;
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            cameraTransform.SetParent(null, true);
            cameraDetached = true;

            Vector3 centre = poi.PinWorldPosition + lookTargetOffset;
            Vector3 planar = Vector3.ProjectOnPlane(startPosition - centre, Vector3.up);
            if (planar.sqrMagnitude < .01f)
                planar = -cameraTransform.forward;
            planar.Normalize();
            Vector3 overviewStart = centre + Vector3.up * surveyHeight + planar * orbitRadius;
            Quaternion overviewRotation = LookAt(overviewStart, centre, startRotation);

            yield return MoveCamera(cameraTransform, startPosition, startRotation,
                overviewStart, overviewRotation, ascentDuration, false);
            RecordSurveyedPoi(poi);

            bool returnRequested = false;
            if (panToRevealedMinorPois)
            {
                foreach (MissionPointOfInterest minorPoi in poi.UnlockOnCompletion
                             .Where(item => item && item.Kind == MissionPoiKind.Minor))
                {
                    Quaternion focusRotation = LookAt(
                        cameraTransform.position,
                        minorPoi.PinWorldPosition + minorPoiLookOffset,
                        cameraTransform.rotation);
                    yield return RotateCamera(
                        cameraTransform,
                        cameraTransform.rotation,
                        focusRotation,
                        minorPoiPanDuration);
                    RecordSurveyedPoi(minorPoi);

                    EnsurePrompt();
                    promptText.text = string.Format(minorPoiLabelFormat, minorPoi.DisplayName, surveyKey);
                    promptText.gameObject.SetActive(true);
                    float focusElapsed = 0f;
                    while (focusElapsed < minorPoiFocusDuration)
                    {
                        if (WasSurveyKeyPressed())
                        {
                            returnRequested = true;
                            break;
                        }
                        focusElapsed += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    HidePrompt();
                    if (returnRequested)
                        break;
                }
            }

            EnsurePrompt();
            promptText.text = $"SURVEYING {poi.DisplayName.ToUpperInvariant()}\nPress [{surveyKey}] to return";
            promptText.gameObject.SetActive(true);
            float elapsed = 0f;
            float initialAngle = Mathf.Atan2(planar.z, planar.x) * Mathf.Rad2Deg;
            while (!returnRequested && elapsed < overviewDuration && !WasSurveyKeyPressed())
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / overviewDuration);
                float blend = EvaluateCurve(orbitBlendCurve, t);
                float angle = (initialAngle + orbitDegrees * blend) * Mathf.Deg2Rad;
                Vector3 orbitOffset = new(Mathf.Cos(angle) * orbitRadius, surveyHeight,
                    Mathf.Sin(angle) * orbitRadius);
                cameraTransform.position = centre + orbitOffset;
                cameraTransform.rotation = LookAt(cameraTransform.position, centre, cameraTransform.rotation);
                yield return null;
            }
            HidePrompt();

            Vector3 returnPosition = originalParent
                ? originalParent.TransformPoint(originalLocalPosition)
                : startPosition;
            Quaternion returnRotation = originalParent
                ? originalParent.rotation * originalLocalRotation
                : startRotation;
            yield return MoveCamera(cameraTransform, cameraTransform.position, cameraTransform.rotation,
                returnPosition, returnRotation, returnDuration, false);

            RestoreCameraAndControls();
            HideSurveyNotebook();
            flightRoutine = null;
        }

        private IEnumerator RotateCamera(
            Transform cameraTransform,
            Quaternion fromRotation,
            Quaternion toRotation,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EvaluateCurve(poiPanBlendCurve, elapsed / duration);
                cameraTransform.rotation = Quaternion.SlerpUnclamped(fromRotation, toRotation, t);
                yield return null;
            }
            cameraTransform.rotation = toRotation;
        }

        private IEnumerator MoveCamera(
            Transform cameraTransform,
            Vector3 fromPosition,
            Quaternion fromRotation,
            Vector3 toPosition,
            Quaternion toRotation,
            float duration,
            bool allowEarlyFinish)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EvaluateCurve(flightBlendCurve, elapsed / duration);
                cameraTransform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(fromPosition, toPosition, t),
                    Quaternion.SlerpUnclamped(fromRotation, toRotation, t));
                if (allowEarlyFinish && WasSurveyKeyPressed())
                    break;
                yield return null;
            }
            cameraTransform.SetPositionAndRotation(toPosition, toRotation);
        }

        private void SuspendPlayerControls()
        {
            suspendedBehaviours.Clear();
            IEnumerable<Behaviour> automatic = playerCamera
                ? playerCamera.transform.root.GetComponentsInChildren<Behaviour>(true)
                    .Where(item => item &&
                        (item.GetType().Name == "FirstPersonLook" ||
                         item.GetType().Name == "WalkingMotor" ||
                         item.GetType().Name == "CameraEffectsController"))
                : Enumerable.Empty<Behaviour>();
            IEnumerable<Behaviour> configured = additionalBehavioursToDisable ?? System.Array.Empty<Behaviour>();
            foreach (Behaviour behaviour in automatic.Concat(configured).Where(item => item).Distinct())
            {
                suspendedBehaviours[behaviour] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

        private void RestoreCameraAndControls()
        {
            if (cameraDetached && playerCamera)
            {
                Transform cameraTransform = playerCamera.transform;
                cameraTransform.SetParent(originalParent, false);
                if (originalParent)
                    cameraTransform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount - 1));
                cameraTransform.localPosition = originalLocalPosition;
                cameraTransform.localRotation = originalLocalRotation;
            }
            cameraDetached = false;
            foreach (KeyValuePair<Behaviour, bool> pair in suspendedBehaviours)
                if (pair.Key)
                    pair.Key.enabled = pair.Value;
            suspendedBehaviours.Clear();
        }

        private void EnsurePrompt()
        {
            if (promptText)
                return;
            Canvas canvas = EnsureSurveyCanvas();
            GameObject promptObject = new("Survey Offer", typeof(RectTransform), typeof(TextMeshProUGUI));
            promptObject.transform.SetParent(canvas.transform, false);
            RectTransform rect = promptObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.2f, .08f);
            rect.anchorMax = new Vector2(.8f, .22f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = promptObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = promptColour;
            text.raycastTarget = false;
            promptText = text;
        }

        private Canvas EnsureSurveyCanvas()
        {
            if (surveyCanvas)
                return surveyCanvas;
            if (promptText && promptText.canvas)
            {
                surveyCanvas = promptText.canvas;
                return surveyCanvas;
            }

            surveyCanvas = FindObjectsByType<Canvas>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item && item.name == "Major POI Survey UI");
            if (!surveyCanvas)
            {
                GameObject canvasObject = new(
                    "Major POI Survey UI",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
                surveyCanvas = canvasObject.GetComponent<Canvas>();
                surveyCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                surveyCanvas.sortingOrder = 200;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = .5f;
            }
            surveyCanvas.gameObject.SetActive(true);
            return surveyCanvas;
        }

        private void BeginSurveyNotebook(MissionPointOfInterest origin)
        {
            if (!showSurveyNotebook)
                return;
            EnsureSurveyNotebook();
            if (!surveyNotebookPanel || !surveyNotebookText)
                return;

            surveyedPoiEntries.Clear();
            surveyNotebookHeading = origin
                ? $"FIELD SURVEY NOTES\n{origin.DisplayName.ToUpperInvariant()}"
                : "FIELD SURVEY NOTES";
            RefreshSurveyNotebookText("Survey in progress...");
            surveyNotebookPanel.gameObject.SetActive(true);
        }

        private void RecordSurveyedPoi(MissionPointOfInterest poi)
        {
            if (!showSurveyNotebook || !poi)
                return;
            EnsureSurveyNotebook();
            if (!surveyNotebookText)
                return;

            if (!fieldMapTool)
                fieldMapTool = FindObjectsByType<WorldMapTool>(FindObjectsInactive.Include)
                    .FirstOrDefault();
            string gridReference = fieldMapTool
                ? fieldMapTool.GetGridReferenceForWorldPosition(poi.PinWorldPosition)
                : "--";
            if (fieldMapTool)
                fieldMapTool.RecordPoiInFieldNotes(poi);
            string entry = $"{(poi.Kind == MissionPoiKind.Major ? "MAJOR" : "MINOR")}: " +
                $"{poi.DisplayName} - {gridReference}";
            if (!surveyedPoiEntries.Contains(entry))
                surveyedPoiEntries.Add(entry);
            RefreshSurveyNotebookText();
        }

        private void RefreshSurveyNotebookText(string status = null)
        {
            if (!surveyNotebookText)
                return;
            string entries = surveyedPoiEntries.Count > 0
                ? string.Join("\n", surveyedPoiEntries)
                : status ?? "Surveying...";
            surveyNotebookText.text = $"{surveyNotebookHeading}\n\n{entries}";
            surveyNotebookText.gameObject.SetActive(true);
            surveyNotebookText.enabled = true;
            surveyNotebookText.ForceMeshUpdate();
        }

        private void EnsureSurveyNotebook()
        {
            if (surveyNotebookPanel && surveyNotebookText)
                return;

            Canvas canvas = EnsureSurveyCanvas();
            if (!canvas)
                return;

            if (!surveyNotebookPanel)
            {
                GameObject panelObject = new(
                    "Survey Field Notebook",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Shadow));
                surveyNotebookPanel = panelObject.GetComponent<RectTransform>();
                surveyNotebookPanel.SetParent(canvas.transform, false);
                Image paper = panelObject.GetComponent<Image>();
                paper.color = surveyNotebookPaperColour;
                paper.raycastTarget = false;
                Shadow shadow = panelObject.GetComponent<Shadow>();
                shadow.effectColor = new Color(.04f, .025f, .01f, .45f);
                shadow.effectDistance = new Vector2(8f, -8f);
                shadow.useGraphicAlpha = true;

                CreateNotebookDecoration(
                    "Notebook Spine",
                    surveyNotebookPanel,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    Vector2.zero,
                    new Vector2(20f, 0f),
                    surveyNotebookSpineColour);
                for (int binding = 0; binding < 5; binding++)
                {
                    float y = .12f + binding * .19f;
                    CreateNotebookDecoration(
                        $"Binding {binding + 1}",
                        surveyNotebookPanel,
                        new Vector2(0f, y),
                        new Vector2(0f, y),
                        new Vector2(-8f, 0f),
                        new Vector2(30f, 6f),
                        new Color(.09f, .065f, .035f, 1f));
                }
            }

            surveyNotebookPanel.anchorMin = surveyNotebookPanel.anchorMax = new Vector2(0f, .5f);
            surveyNotebookPanel.pivot = new Vector2(0f, .5f);
            surveyNotebookPanel.sizeDelta = surveyNotebookSize;
            surveyNotebookPanel.anchoredPosition = surveyNotebookScreenOffset;
            surveyNotebookPanel.localRotation = Quaternion.Euler(0f, 0f, surveyNotebookRotation);
            surveyNotebookPanel.localScale = Vector3.one;
            surveyNotebookPanel.SetAsLastSibling();

            if (!surveyNotebookText)
            {
                GameObject textObject = new(
                    "Survey Entries",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                textObject.transform.SetParent(surveyNotebookPanel, false);
                surveyNotebookText = textObject.GetComponent<TextMeshProUGUI>();
                if (TMP_Settings.defaultFontAsset)
                    surveyNotebookText.font = TMP_Settings.defaultFontAsset;
            }
            RectTransform textRect = surveyNotebookText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(34f, 24f);
            textRect.offsetMax = new Vector2(-18f, -24f);
            surveyNotebookText.fontSize = surveyNotebookFontSize;
            surveyNotebookText.alignment = TextAlignmentOptions.TopLeft;
            surveyNotebookText.textWrappingMode = TextWrappingModes.Normal;
            surveyNotebookText.overflowMode = TextOverflowModes.Ellipsis;
            surveyNotebookText.color = surveyNotebookInkColour;
            surveyNotebookText.raycastTarget = false;
            surveyNotebookText.SetAllDirty();
            textRect.SetAsLastSibling();
        }

        private static void CreateNotebookDecoration(
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
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            Image image = decoration.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
        }

        private void HideSurveyNotebook()
        {
            if (surveyNotebookPanel)
                surveyNotebookPanel.gameObject.SetActive(false);
        }

        private void HidePrompt()
        {
            if (promptText)
                promptText.gameObject.SetActive(false);
        }

        private static Quaternion LookAt(Vector3 position, Vector3 target, Quaternion fallback)
        {
            Vector3 direction = target - position;
            return direction.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : fallback;
        }

        private static float EvaluateCurve(AnimationCurve curve, float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return curve == null || curve.length == 0
                ? Mathf.SmoothStep(0f, 1f, t)
                : Mathf.Clamp01(curve.Evaluate(t));
        }

        private bool WasSurveyKeyPressed()
        {
            return Keyboard.current != null && Keyboard.current[surveyKey].wasPressedThisFrame;
        }
    }
}
