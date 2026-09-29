using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GetLost.Missions
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Get Lost/Missions/Mission Board World Display")]
    public sealed class MissionBoardWorldDisplay : MonoBehaviour
    {
        [Header("Mission System")]
        [SerializeField] private MissionBoardController missionBoardController;
        [SerializeField] private TopologyMapGenerator topologyMapGenerator;

        [Header("Placement On Board")]
        [Tooltip("Local position of the UI surface. Move it slightly in front of the board mesh to prevent flickering.")]
        [SerializeField] private Vector3 localPosition = new(0f, 0f, -.015f);
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField, Min(.0001f)] private float worldScale = .002f;
        [SerializeField] private Vector2 canvasPixelSize = new(900f, 560f);

        [Header("Layout")]
        [SerializeField] private string boardTitle = "RANGER MISSIONS";
        [SerializeField] private Color paperColour = new(.82f, .77f, .61f, 1f);
        [SerializeField] private Color inkColour = new(.12f, .09f, .045f, 1f);
        [SerializeField] private Color mapTint = Color.white;

        [Header("Generated Display References")]
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private RawImage topographyImage;
        [SerializeField] private RectTransform pinRoot;
        [SerializeField] private TMP_Text missionSummaryText;

        private void Reset()
        {
            missionBoardController = FindAnyObjectByType<MissionBoardController>();
            topologyMapGenerator = FindAnyObjectByType<TopologyMapGenerator>();
            BuildOrRefreshDisplay();
        }

        private void Awake()
        {
            BuildOrRefreshDisplay();
            ConnectController();
        }

        [ContextMenu("Build / Refresh Mission Board Display")]
        public void BuildOrRefreshDisplay()
        {
            if (!worldCanvas)
                CreateDisplayHierarchy();
            if (!worldCanvas)
                return;

            RectTransform canvasRect = worldCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = canvasPixelSize;
            canvasRect.localPosition = localPosition;
            canvasRect.localEulerAngles = localEulerAngles;
            canvasRect.localScale = Vector3.one * worldScale;

            if (!topologyMapGenerator)
                topologyMapGenerator = FindAnyObjectByType<TopologyMapGenerator>();
            if (topographyImage && topologyMapGenerator)
            {
                Texture2D texture = topologyMapGenerator.GenerateBaseTexture(false);
                if (texture)
                    topographyImage.texture = texture;
            }
            ConnectController();
        }

        private void ConnectController()
        {
            if (!missionBoardController)
                missionBoardController = FindAnyObjectByType<MissionBoardController>();
            if (missionBoardController)
                missionBoardController.ConfigurePresentation(
                    topographyImage,
                    pinRoot,
                    topologyMapGenerator,
                    missionSummaryText);
        }

        private void CreateDisplayHierarchy()
        {
            Transform existing = transform.Find("Mission Board Display");
            GameObject canvasObject = existing
                ? existing.gameObject
                : new GameObject("Mission Board Display", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            worldCanvas = canvasObject.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.overrideSorting = true;
            worldCanvas.sortingOrder = 5;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100f;

            Image background = GetOrCreateImage(canvasObject.transform, "Board Paper");
            Stretch(background.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.color = paperColour;

            TMP_Text title = GetOrCreateText(canvasObject.transform, "Board Title");
            title.text = boardTitle;
            title.fontSize = 34f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = inkColour;
            Stretch(title.rectTransform, new Vector2(.04f, .88f), new Vector2(.96f, .98f), Vector2.zero, Vector2.zero);

            topographyImage = GetOrCreateRawImage(canvasObject.transform, "Mission Topography");
            topographyImage.color = mapTint;
            Stretch(topographyImage.rectTransform, new Vector2(.04f, .08f), new Vector2(.68f, .87f), Vector2.zero, Vector2.zero);

            Transform oldPins = topographyImage.transform.Find("Mission POI Pins");
            GameObject pinsObject = oldPins
                ? oldPins.gameObject
                : new GameObject("Mission POI Pins", typeof(RectTransform));
            pinsObject.transform.SetParent(topographyImage.transform, false);
            pinRoot = pinsObject.GetComponent<RectTransform>();
            Stretch(pinRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            missionSummaryText = GetOrCreateText(canvasObject.transform, "Mission Information");
            missionSummaryText.text = "AVAILABLE DESTINATIONS\nAwaiting survey data.";
            missionSummaryText.fontSize = 22f;
            missionSummaryText.alignment = TextAlignmentOptions.TopLeft;
            missionSummaryText.color = inkColour;
            missionSummaryText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(missionSummaryText.rectTransform, new Vector2(.71f, .08f), new Vector2(.96f, .87f), Vector2.zero, Vector2.zero);
        }

        private static Image GetOrCreateImage(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing && existing.TryGetComponent(out Image image))
                return image;
            GameObject item = new(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            Image created = item.GetComponent<Image>();
            created.raycastTarget = false;
            return created;
        }

        private static RawImage GetOrCreateRawImage(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing && existing.TryGetComponent(out RawImage image))
                return image;
            GameObject item = new(name, typeof(RectTransform), typeof(RawImage));
            item.transform.SetParent(parent, false);
            RawImage created = item.GetComponent<RawImage>();
            created.raycastTarget = false;
            return created;
        }

        private static TMP_Text GetOrCreateText(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing && existing.TryGetComponent(out TMP_Text text))
                return text;
            GameObject item = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(parent, false);
            TextMeshProUGUI created = item.GetComponent<TextMeshProUGUI>();
            created.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset)
                created.font = TMP_Settings.defaultFontAsset;
            return created;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }
    }
}
