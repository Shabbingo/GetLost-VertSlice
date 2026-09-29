#if UNITY_EDITOR
using System.IO;
using System.Linq;
using GetLost.Wagon;
using GetLost.PlayerTools;
using Tom.WalkingController;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GetLost.VerticalSlice.Editor
{
    public static class VerticalSliceSetup
    {
        private const string RootFolder = "Assets/GetLost/VerticalSlice";
        private const string PrefabPath = RootFolder + "/VerticalSlicePlayer.prefab";
        private const string InputPath = "Assets/InputSystem_Actions.inputactions";
        private const string PathLayerSource = "Assets/My Assets/Textures/Pebbles.terrainlayer";
        private const string PathLayerFolder = "Assets/Resources/GetLost";
        private const string PathLayerPath = PathLayerFolder + "/GravelPath.terrainlayer";
        private const string WagonPrefabPath = PathLayerFolder + "/WagonPrototype.prefab";
        private const string NavigationRootName = "Navigation UI";
        private const string ToolIconsPath = "Assets/My Assets/UI/Sprites/ToolsUI.png";
        private const string DefaultFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [InitializeOnLoadMethod]
        private static void CreateInitialAssetsWhenReady()
        {
            EditorApplication.delayCall += EnsureInitialAssets;
        }

        private static void EnsureInitialAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureInitialAssets;
                return;
            }

            EnsurePathTerrainLayer();
            EnsureWagonPrefab(false);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                CreatePlayerPrefab();
            else
                EnsureNavigationTools(false);

            EnsurePauseMenu(false);
        }

        [MenuItem("Get Lost/Vertical Slice/Rebuild Player Prefab")]
        public static void CreatePlayerPrefab()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CreatePlayerPrefab;
                return;
            }

            EnsurePathTerrainLayer();
            EnsureFolder("Assets/GetLost");
            EnsureFolder(RootFolder);
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (actions == null)
            {
                Debug.LogError($"[Vertical Slice] Missing input actions at {InputPath}.");
                return;
            }

            GameObject root = new("Vertical Slice Player") { tag = "Player" };
            try
            {
                CharacterController character = root.AddComponent<CharacterController>();
                character.height = 1.8f;
                character.radius = 0.35f;
                character.center = new Vector3(0f, 0.9f, 0f);
                character.stepOffset = 0.35f;
                character.slopeLimit = 55f;

                WalkingInputReader input = root.AddComponent<WalkingInputReader>();
                GroundProbe groundProbe = root.AddComponent<GroundProbe>();
                TerrainSurfaceResolver surfaceResolver = root.AddComponent<TerrainSurfaceResolver>();
                ExertionController exertion = root.AddComponent<ExertionController>();
                VegetationInteractionReceiver vegetation = root.AddComponent<VegetationInteractionReceiver>();
                AudioSource audioSource = root.AddComponent<AudioSource>();
                FootstepAudioManager footstepAudio = root.AddComponent<FootstepAudioManager>();
                ManagedGrassVegetationProvider managedGrass = root.AddComponent<ManagedGrassVegetationProvider>();
                TerrainDetailVegetationDetector terrainDetails = root.AddComponent<TerrainDetailVegetationDetector>();

                GameObject pitchObject = new("Camera Pitch Pivot");
                pitchObject.transform.SetParent(root.transform, false);
                pitchObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                GameObject effectsObject = new("Camera Effects Pivot");
                effectsObject.transform.SetParent(pitchObject.transform, false);
                GameObject cameraObject = new("Player Camera");
                cameraObject.transform.SetParent(effectsObject.transform, false);
                cameraObject.tag = "MainCamera";
                Camera playerCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();

                FirstPersonLook look = pitchObject.AddComponent<FirstPersonLook>();
                CameraEffectsController cameraEffects = root.AddComponent<CameraEffectsController>();
                WalkingMotor motor = root.GetComponent<WalkingMotor>() ?? root.AddComponent<WalkingMotor>();
                WalkingCameraBob cameraBob = root.AddComponent<WalkingCameraBob>();
                FootingFeedback footing = root.AddComponent<FootingFeedback>();

                SetObject(input, "moveAction", FindReference(actions, "Move"));
                SetObject(input, "lookAction", FindReference(actions, "Look"));
                SetObject(input, "sprintAction", FindReference(actions, "Sprint"));
                SetObject(input, "jumpAction", FindReference(actions, "Jump"));
                SetObject(groundProbe, "characterController", character);
                SetObject(surfaceResolver, "terrainLayerMap", AssetDatabase.LoadAssetAtPath<TerrainLayerSurfaceMap>("Assets/TerrainSurfaces/Terrain Layer Surface Map.asset"));
                SetObject(managedGrass, "receiver", vegetation);
                SetObject(managedGrass, "mapping", AssetDatabase.LoadAssetAtPath<TerrainDetailVegetationMapping>("Assets/TerrainSurfaces/Terrain Detail Vegetation Mapping.asset"));
                SetObject(terrainDetails, "profile", AssetDatabase.LoadAssetAtPath<TerrainDetailVegetationProfile>("Assets/TerrainSurfaces/Legacy/Terrain Detail Vegetation Profile.asset"));
                SetObject(terrainDetails, "footstepAudio", footstepAudio);
                SetObject(look, "input", input);
                SetObject(look, "yawTarget", root.transform);
                SetObject(look, "pitchTarget", pitchObject.transform);
                SetObject(motor, "characterController", character);
                SetObject(motor, "input", input);
                SetObject(motor, "groundProbe", groundProbe);
                SetObject(motor, "surfaceResolver", surfaceResolver);
                SetObject(motor, "exertion", exertion);
                SetObject(motor, "vegetation", vegetation);
                SetObject(motor, "cameraTransform", playerCamera.transform);
                cameraEffects.Configure(playerCamera.transform, effectsObject.transform);
                SetObject(cameraBob, "motor", motor);
                SetObject(cameraBob, "cameraEffects", cameraEffects);
                SetObject(footing, "motor", motor);
                SetObject(footing, "cameraEffects", cameraEffects);
                SetObject(footing, "audioSource", audioSource);

                BuildNavigationUI(root);
                BuildPauseMenu(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Vertical Slice] Created clean player prefab at {PrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Get Lost/Vertical Slice/Rebuild Pause Menu On Player Prefab")]
        private static void RebuildPauseMenu()
        {
            EnsurePauseMenu(true);
        }

        private static void EnsurePauseMenu(bool overwrite)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => EnsurePauseMenu(overwrite);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                return;

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform existing = contents.transform.Find("Pause Menu Canvas");
                PauseMenuController existingController = contents.GetComponent<PauseMenuController>();
                if (existing != null && existingController != null && !overwrite)
                    return;

                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);
                if (existingController != null)
                    Object.DestroyImmediate(existingController);

                BuildPauseMenu(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Vertical Slice] Pause menu installed on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void BuildPauseMenu(GameObject player)
        {
            PauseMenuController controller = player.GetComponent<PauseMenuController>() ?? player.AddComponent<PauseMenuController>();

            GameObject canvasObject = new("Pause Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(player.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject overlay = CreatePanel(canvasObject.transform, "Pause Menu Root", Vector2.zero, new Color(0.015f, 0.02f, 0.018f, 0.84f));
            RectTransform overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().raycastTarget = true;

            GameObject panel = CreatePanel(overlay.transform, "Pause Panel", new Vector2(460f, 480f), new Color(0.075f, 0.085f, 0.07f, 0.98f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI title = CreateLabel(panel.transform, "Pause Title", "PAUSED", 48f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -48f);
            titleRect.sizeDelta = new Vector2(380f, 70f);

            Button resumeButton = CreatePauseButton(panel.transform, "Resume", new Vector2(0f, 76f));
            Button restartButton = CreatePauseButton(panel.transform, "Restart", new Vector2(0f, -8f));
            Button quitButton = CreatePauseButton(panel.transform, "Quit", new Vector2(0f, -92f));
            UnityEventTools.AddPersistentListener(resumeButton.onClick, controller.Resume);
            UnityEventTools.AddPersistentListener(restartButton.onClick, controller.Restart);
            UnityEventTools.AddPersistentListener(quitButton.onClick, controller.QuitGame);

            SetObject(controller, "pauseMenuRoot", overlay);
            SetBehaviourArray(
                controller,
                "disableWhilePaused",
                player.GetComponent<WalkingInputReader>(),
                player.GetComponentInChildren<FirstPersonLook>(true),
                player.GetComponent<WalkingMotor>(),
                player.GetComponent<ToolRadialWheelController>(),
                player.GetComponentInChildren<WorldMapTool>(true));
        }

        private static Button CreatePauseButton(Transform parent, string labelText, Vector2 position)
        {
            GameObject buttonObject = new(labelText, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(320f, 64f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.18f, 0.22f, 0.16f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(0.82f, 0.92f, 0.7f, 1f);
            colours.pressedColor = new Color(0.62f, 0.74f, 0.5f, 1f);
            colours.selectedColor = colours.highlightedColor;
            button.colors = colours;

            TextMeshProUGUI label = CreateLabel(buttonObject.transform, "Label", labelText.ToUpperInvariant(), 27f, TextAlignmentOptions.Center);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            return button;
        }

        [MenuItem("Get Lost/Vertical Slice/Rebuild Navigation UI On Player Prefab")]
        private static void RebuildNavigationTools()
        {
            EnsureNavigationTools(true);
        }

        private static void EnsureNavigationTools(bool overwrite)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => EnsureNavigationTools(overwrite);
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                return;

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform existing = contents.transform.Find(NavigationRootName);
                bool hasWorldSpaceMap = contents
                    .GetComponentsInChildren<Canvas>(true)
                    .Any(candidate => candidate.name == "World Space Map Canvas");
                bool hasHeldPresentation = contents.GetComponentInChildren<HeldMapPresentation>(true) != null;

                if (existing != null && hasWorldSpaceMap && hasHeldPresentation && !overwrite)
                    return;

                foreach (Canvas oldMapCanvas in contents.GetComponentsInChildren<Canvas>(true)
                             .Where(candidate => candidate.name == "World Space Map Canvas")
                             .ToArray())
                {
                    Object.DestroyImmediate(oldMapCanvas.gameObject);
                }

                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                RemoveIfPresent<ToolRadialWheelController>(contents);
                RemoveIfPresent<PlayerInputContextManager>(contents);
                RemoveIfPresent<PlayerToolManager>(contents);

                BuildNavigationUI(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Vertical Slice] Navigation UI installed on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void BuildNavigationUI(GameObject player)
        {
            PlayerToolManager manager = player.GetComponent<PlayerToolManager>() ?? player.AddComponent<PlayerToolManager>();
            PlayerInputContextManager context = player.GetComponent<PlayerInputContextManager>() ?? player.AddComponent<PlayerInputContextManager>();
            ToolRadialWheelController wheel = player.GetComponent<ToolRadialWheelController>() ?? player.AddComponent<ToolRadialWheelController>();
            FirstPersonLook look = player.GetComponentInChildren<FirstPersonLook>(true);
            Camera playerCamera = player.GetComponentInChildren<Camera>(true);

            // ALT belongs to the radial wheel in the vertical slice. Leaving
            // FirstPersonLook's legacy cursor-release shortcut enabled creates
            // an update-order race: it can unlock the cursor before the wheel
            // validates the press, causing the wheel to reject the same input.
            SetBool(look, "holdAltToReleaseCursor", false);
            SetEnum(manager, "startingTool", (int)PlayerToolType.Compass);
            SetBool(manager, "selectingSameToolUnequips", true);
            SetObject(wheel, "toolManager", manager);
            SetObject(wheel, "inputContextManager", context);
            SetBehaviourArray(wheel, "disableWhileOpen", look);
            ConfigureWheelSlots(wheel);

            GameObject navigation = new(NavigationRootName);
            navigation.transform.SetParent(player.transform, false);
            TopologyMapGenerator topology = navigation.AddComponent<TopologyMapGenerator>();
            navigation.AddComponent<RuntimeTerrainMapSource>();
            topology.textureWidth = 512;
            topology.textureHeight = 512;
            topology.contourInterval = 10f;

            GameObject canvasObject = new("Navigation HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(navigation.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject worldMapCanvasObject = new(
                "World Space Map Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            worldMapCanvasObject.transform.SetParent(
                playerCamera != null ? playerCamera.transform : navigation.transform,
                false);

            RectTransform worldMapCanvasRect = worldMapCanvasObject.GetComponent<RectTransform>();
            worldMapCanvasRect.sizeDelta = new Vector2(860f, 680f);
            worldMapCanvasRect.localPosition = new Vector3(0f, -0.08f, 1f);
            worldMapCanvasRect.localRotation = Quaternion.identity;
            worldMapCanvasRect.localScale = Vector3.one * 0.0012f;

            Canvas worldMapCanvas = worldMapCanvasObject.GetComponent<Canvas>();
            worldMapCanvas.renderMode = RenderMode.WorldSpace;
            worldMapCanvas.worldCamera = playerCamera;
            worldMapCanvas.overrideSorting = true;
            worldMapCanvas.sortingOrder = 25;
            CanvasScaler worldMapScaler = worldMapCanvasObject.GetComponent<CanvasScaler>();
            worldMapScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            worldMapScaler.referencePixelsPerUnit = 100f;
            worldMapScaler.dynamicPixelsPerUnit = 100f;

            GameObject scanPivotObject = new("Map Scan Pivot", typeof(RectTransform));
            scanPivotObject.transform.SetParent(worldMapCanvasObject.transform, false);
            RectTransform scanPivot = scanPivotObject.GetComponent<RectTransform>();
            scanPivot.anchorMin = scanPivot.anchorMax = scanPivot.pivot = new Vector2(0.5f, 0.5f);
            scanPivot.sizeDelta = new Vector2(860f, 680f);

            GameObject mapRoot = CreatePanel(scanPivotObject.transform, "Map Root", new Vector2(860f, 680f), new Color(0.13f, 0.10f, 0.055f, 0.97f));
            RectTransform mapRootRect = mapRoot.GetComponent<RectTransform>();
            mapRootRect.anchorMin = mapRootRect.anchorMax = mapRootRect.pivot = new Vector2(0.5f, 0.5f);
            mapRootRect.anchoredPosition = Vector2.zero;

            GameObject mapImageObject = new("Generated Topography", typeof(RectTransform), typeof(RawImage));
            mapImageObject.transform.SetParent(mapRoot.transform, false);
            RectTransform mapRect = mapImageObject.GetComponent<RectTransform>();
            mapRect.anchorMin = new Vector2(0.05f, 0.08f);
            mapRect.anchorMax = new Vector2(0.95f, 0.88f);
            mapRect.offsetMin = mapRect.offsetMax = Vector2.zero;
            RawImage mapImage = mapImageObject.GetComponent<RawImage>();
            mapImage.color = Color.white;

            RectTransform startMarker = CreateMarker(mapImageObject.transform, "Start Marker", new Color(0.15f, 0.85f, 0.25f, 1f));
            RectTransform endMarker = CreateMarker(mapImageObject.transform, "End Marker", new Color(0.9f, 0.25f, 0.18f, 1f));
            startMarker.gameObject.SetActive(false);
            endMarker.gameObject.SetActive(false);

            TextMeshProUGUI mapTitle = CreateLabel(mapRoot.transform, "Map Title", "TOPOGRAPHIC SURVEY", 26f, TextAlignmentOptions.Center);
            RectTransform titleRect = mapTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0.1f, 0.9f);
            titleRect.anchorMax = new Vector2(0.9f, 0.98f);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;

            TextMeshProUGUI fieldNote = CreateLabel(mapRoot.transform, "Field Note", "FIELD NOTE\nNo active survey.", 18f, TextAlignmentOptions.TopLeft);
            RectTransform noteRect = fieldNote.rectTransform;
            noteRect.anchorMin = new Vector2(0.06f, 0.085f);
            noteRect.anchorMax = new Vector2(0.45f, 0.22f);
            noteRect.offsetMin = noteRect.offsetMax = Vector2.zero;
            fieldNote.color = new Color(0.11f, 0.08f, 0.035f, 0.95f);

            WorldMapTool mapTool = navigation.AddComponent<WorldMapTool>();
            SetObject(mapTool, "mapRoot", mapRoot);
            SetObject(mapTool, "mapImage", mapImage);
            SetObject(mapTool, "startMarker", startMarker);
            SetObject(mapTool, "endMarker", endMarker);
            SetObject(mapTool, "missionFieldNoteText", fieldNote);
            SetObject(mapTool, "topologyMapGenerator", topology);
            SetObject(mapTool, "playerCamera", playerCamera);
            SetObject(mapTool, "toolManager", manager);
            SetObject(mapTool, "scanTransform", scanPivot);
            SetBehaviourArray(mapTool, "disableWhileFocused", look);

            HeldMapPresentation heldPresentation = worldMapCanvasObject.AddComponent<HeldMapPresentation>();
            SetObject(heldPresentation, "mapTool", mapTool);
            SetObject(heldPresentation, "movementSource", player.transform);
            SetObject(heldPresentation, "viewTransform", playerCamera != null ? playerCamera.transform : null);
            SetObject(heldPresentation, "mapVisualRoot", mapRoot);
            SetObject(mapTool, "heldPresentation", heldPresentation);

            GameObject compassRoot = CreatePanel(canvasObject.transform, "Compass Root", new Vector2(210f, 210f), new Color(0.06f, 0.07f, 0.055f, 0.88f));
            RectTransform compassRect = compassRoot.GetComponent<RectTransform>();
            compassRect.anchorMin = compassRect.anchorMax = new Vector2(0.5f, 0f);
            compassRect.pivot = new Vector2(0.5f, 0f);
            compassRect.anchoredPosition = new Vector2(0f, 42f);

            CreateCompassLabel(compassRoot.transform, "N", new Vector2(0f, 82f));
            CreateCompassLabel(compassRoot.transform, "E", new Vector2(82f, 0f));
            CreateCompassLabel(compassRoot.transform, "S", new Vector2(0f, -82f));
            CreateCompassLabel(compassRoot.transform, "W", new Vector2(-82f, 0f));

            GameObject needleObject = new("Needle", typeof(RectTransform), typeof(Image));
            needleObject.transform.SetParent(compassRoot.transform, false);
            RectTransform needle = needleObject.GetComponent<RectTransform>();
            needle.anchorMin = needle.anchorMax = needle.pivot = new Vector2(0.5f, 0.5f);
            needle.sizeDelta = new Vector2(8f, 145f);
            needle.GetComponent<Image>().color = new Color(0.88f, 0.18f, 0.12f, 1f);

            CompassNeedle compassNeedle = compassRoot.AddComponent<CompassNeedle>();
            SetObject(compassNeedle, "player", player.transform);
            SetObject(compassNeedle, "needle", needle);
            CompassTool compassTool = navigation.AddComponent<CompassTool>();
            SetObject(compassTool, "compassRoot", compassRoot);
        }

        private static void ConfigureWheelSlots(ToolRadialWheelController wheel)
        {
            SerializedObject serialized = new(wheel);
            SerializedProperty slots = serialized.FindProperty("slots");
            slots.arraySize = 2;
            ConfigureSlot(slots.GetArrayElementAtIndex(0), PlayerToolType.Map, ToolRadialWheelController.RadialDirection.Down, FindSprite("ToolsUI_1"));
            ConfigureSlot(slots.GetArrayElementAtIndex(1), PlayerToolType.Compass, ToolRadialWheelController.RadialDirection.Right, FindSprite("ToolsUI_3"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSlot(SerializedProperty slot, PlayerToolType tool, ToolRadialWheelController.RadialDirection direction, Sprite icon)
        {
            slot.FindPropertyRelative("toolType").enumValueIndex = (int)tool;
            slot.FindPropertyRelative("direction").enumValueIndex = (int)direction;
            slot.FindPropertyRelative("iconSprite").objectReferenceValue = icon;
            slot.FindPropertyRelative("iconTint").colorValue = Color.white;
            slot.FindPropertyRelative("selectedSprite").objectReferenceValue = icon;
            slot.FindPropertyRelative("selectedTint").colorValue = Color.white;
            slot.FindPropertyRelative("iconSize").vector2Value = new Vector2(92f, 92f);
            slot.FindPropertyRelative("preserveAspect").boolValue = true;
            slot.FindPropertyRelative("iconScale").floatValue = 1f;
        }

        private static Sprite FindSprite(string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(ToolIconsPath).OfType<Sprite>().FirstOrDefault(sprite => sprite.name == name);
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Color colour)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<RectTransform>().sizeDelta = size;
            Image image = panel.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return panel;
        }

        private static RectTransform CreateMarker(Transform parent, string name, Color colour)
        {
            GameObject marker = new(name, typeof(RectTransform), typeof(Image));
            marker.transform.SetParent(parent, false);
            RectTransform rect = marker.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(22f, 22f);
            marker.GetComponent<Image>().color = colour;
            return rect;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, TextAlignmentOptions alignment)
        {
            GameObject label = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            TMP_FontAsset defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontPath);
            if (defaultFont != null)
                tmp.font = defaultFont;
            tmp.fontSize = size;
            tmp.alignment = alignment;
            tmp.color = new Color(0.91f, 0.86f, 0.68f, 1f);
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void CreateCompassLabel(Transform parent, string text, Vector2 position)
        {
            TextMeshProUGUI label = CreateLabel(parent, text, text, 24f, TextAlignmentOptions.Center);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(36f, 36f);
            rect.anchoredPosition = position;
        }

        private static void RemoveIfPresent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
                Object.DestroyImmediate(component);
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string propertyName, int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBehaviourArray(Object target, string propertyName, params Behaviour[] behaviours)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            Behaviour[] valid = behaviours.Where(behaviour => behaviour != null).ToArray();
            property.arraySize = valid.Length;
            for (int i = 0; i < valid.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = valid[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsurePathTerrainLayer()
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(PathLayerPath) != null)
                return;

            TerrainLayer source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(PathLayerSource);
            if (source == null)
            {
                Debug.LogError($"[Vertical Slice] Missing path terrain layer source at {PathLayerSource}.");
                return;
            }

            EnsureFolder("Assets/Resources");
            EnsureFolder(PathLayerFolder);
            if (!AssetDatabase.CopyAsset(PathLayerSource, PathLayerPath))
            {
                Debug.LogError($"[Vertical Slice] Could not create the runtime path layer at {PathLayerPath}.");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Vertical Slice] Created the wagon gravel terrain layer at {PathLayerPath}.");
        }

        [MenuItem("Get Lost/Vertical Slice/Rebuild Default Wagon Prefab")]
        private static void RebuildWagonPrefab()
        {
            EnsureWagonPrefab(true);
        }

        private static void EnsureWagonPrefab(bool overwrite)
        {
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(WagonPrefabPath) != null)
                return;

            EnsurePathTerrainLayer();
            EnsureFolder("Assets/Resources");
            EnsureFolder(PathLayerFolder);
            GameObject root = new("Wagon Prototype");
            try
            {
                root.AddComponent<WagonController>();
                WagonWorld world = root.AddComponent<WagonWorld>();
                SetObject(world, "pathTerrainLayer",
                    AssetDatabase.LoadAssetAtPath<TerrainLayer>(PathLayerPath));
                PrefabUtility.SaveAsPrefabAsset(root, WagonPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Vertical Slice] Created editable wagon prefab at {WagonPrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static InputActionReference FindReference(InputActionAsset asset, string actionName)
        {
            InputAction action = asset.FindAction(actionName);
            if (action == null)
            {
                Debug.LogWarning($"[Vertical Slice] Input action '{actionName}' was not found.");
                return null;
            }
            InputActionReference existing = AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>().FirstOrDefault(reference => reference.action != null && reference.action.id == action.id);
            if (existing != null)
                return existing;
            string referencePath = $"{RootFolder}/Input_{actionName}.asset";
            InputActionReference created = AssetDatabase.LoadAssetAtPath<InputActionReference>(referencePath);
            if (created != null)
                return created;
            created = InputActionReference.Create(action);
            created.name = actionName;
            AssetDatabase.CreateAsset(created, referencePath);
            return created;
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized property '{propertyName}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
