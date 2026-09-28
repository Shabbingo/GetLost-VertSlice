using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GetLost.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using FoliageRule = Thomas.TerrainFoliageSpawner.TerrainFoliageRule;
using FoliageSpawner = Thomas.TerrainFoliageSpawner.TerrainFoliageSpawner;

/// <summary>
/// Non-destructive terrain-world generator for Get Lost. Broad terrain is a
/// continuous world-space function; design markers provide authored structure;
/// the existing connected erosion solver supplies drainage detail.
/// </summary>
public sealed class GetLostTerrainGeneratorWindow : EditorWindow
{
    const string DefaultOutput = "Assets/GetLost/Generated Terrain";

    enum TerrainPreset
    {
        RollingHills,
        Mountains,
        Alps,
        Grassland,
        Swamps
    }

    [SerializeField] TerrainPreset selectedPreset = TerrainPreset.Mountains;
    [SerializeField] string worldName = "Get Lost World";
    [SerializeField] int seed = 48127;
    [SerializeField, Range(1, 8)] int tilesX = 4;
    [SerializeField, Range(1, 8)] int tilesZ = 4;
    [SerializeField] float tileSize = 1000f;
    [SerializeField] float terrainHeight = 640f;
    [SerializeField] int heightmapResolution = 513;
    [SerializeField] int alphamapResolution = 256;
    [SerializeField] Vector3 worldOrigin = new Vector3(-2000f, 0f, -2000f);

    [SerializeField] float baseHeight = 92f;
    [SerializeField] float continentScale = 1750f;
    [SerializeField, Range(1, 8)] int octaves = 5;
    [SerializeField, Range(.1f, .9f)] float persistence = .48f;
    [SerializeField, Range(1.2f, 3.5f)] float lacunarity = 2.02f;
    [SerializeField] float rollingHeight = 75f;
    [SerializeField] float mountainHeight = 330f;
    [SerializeField, Range(0f, 1f)] float mountainCoverage = .42f;
    [SerializeField, Range(.5f, 3f)] float mountainRidgeSharpness = 1.15f;
    [SerializeField, Range(.1f, .7f)] float mountainTaperWidth = .45f;
    [SerializeField, Range(0f, 20f)] float mountainDetailHeight = 7f;
    [SerializeField, Range(0f, 4f)] float derivativeDamping = 1.15f;
    [SerializeField] float domainWarpMetres = 210f;
    [SerializeField] float domainWarpScale = 1050f;
    [SerializeField] float basinDepth = 55f;
    [SerializeField] float waterLevel = 68f;
    [SerializeField] bool applySurfaceFinish = true;
    [SerializeField, Range(1, 4)] int surfaceFinishPasses = 2;
    [SerializeField, Range(0f, 1f)] float surfaceFinishStrength = .72f;
    [SerializeField, Min(0f)] float preservedMicroReliefMetres = 2.5f;

    [SerializeField] bool useSceneDesignMarkers = true;
    [SerializeField] bool applyConnectedErosion = true;
    [SerializeField] float erosionCellSize = 10f;
    [SerializeField, Range(1, 4)] int erosionCycles = 2;
    [SerializeField] float channelDepth = 4.5f;
    [SerializeField] bool applyHillsideErosion = true;
    [SerializeField, Min(0f)] float hillsideErosionRelief = 14f;
    [SerializeField, Min(20f)] float hillsideGullySpacing = 140f;
    [SerializeField, Range(1, 5)] int hillsideErosionOctaves = 4;

    [SerializeField] TerrainLayer grassLayer;
    [SerializeField] TerrainLayer soilLayer;
    [SerializeField] TerrainLayer rockLayer;
    [SerializeField] TerrainLayer highlandLayer;
    [SerializeField] Material terrainMaterial;
    [SerializeField] int terrainPhysicsLayer;
    [SerializeField] string outputRoot = DefaultOutput;
    [SerializeField] FoliageSpawner foliageTemplate;
    [SerializeField] List<FoliageRule> foliageRules = new();
    [SerializeField] bool foliageRuleSelectionInitialised;
    [SerializeField] GameObject foliageWorldRoot;
    [SerializeField] bool automaticallyPrepareFoliage = true;
    [SerializeField] List<GetLostTerrainFoliageWorkflow.LayerMapping> foliageLayerMappings = new();
    [SerializeField] string foliageStatus = "Generate terrain, then generate foliage here.";

    Vector2 scroll;
    Texture2D preview;
    string previewSummary = "Generate a preview to inspect the broad landforms.";

    readonly struct NoiseSample
    {
        public readonly float value;
        public readonly Vector2 derivative;
        public NoiseSample(float value, Vector2 derivative)
        {
            this.value = value;
            this.derivative = derivative;
        }
    }

    readonly struct NoisePoint
    {
        public readonly double x, z;
        public NoisePoint(double x, double z) { this.x = x; this.z = z; }
        public static NoisePoint operator +(NoisePoint a, NoisePoint b) => new(a.x + b.x, a.z + b.z);
        public static NoisePoint operator +(NoisePoint a, Vector2 b) => new(a.x + b.x, a.z + b.y);
        public static NoisePoint operator +(Vector2 a, NoisePoint b) => new(a.x + b.x, a.y + b.z);
        public static NoisePoint operator *(NoisePoint a, double scale) => new(a.x * scale, a.z * scale);
    }
    sealed class CorridorSnapshot
    {
        public float width, shoulderWidth, shoulderHeight, floorBlend, floorOffset;
        public Vector3[] points;
    }

    sealed class StampSnapshot
    {
        public TerrainLandformType type;
        public Vector2 center;
        public float radiusX, radiusZ, yaw, strength, plateauHeight, blendPower;
    }

    sealed class GenerationContext
    {
        public readonly List<StampSnapshot> stamps = new();
        public readonly List<CorridorSnapshot> corridors = new();
        public float width, depth;
        public int seed;
    }

    [MenuItem("Tools/Get Lost/Terrain/Terrain World Generator")]
    static void Open() => GetWindow<GetLostTerrainGeneratorWindow>("Get Lost Terrain");

    [MenuItem("GameObject/Get Lost/Terrain Design/Landform Stamp", false, 10)]
    static void CreateLandformStamp()
    {
        GameObject go = new("Landform Stamp");
        Undo.RegisterCreatedObjectUndo(go, "Create Landform Stamp");
        go.AddComponent<GetLostTerrainLandformStamp>();
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Get Lost/Terrain Design/Route Corridor", false, 11)]
    static void CreateRouteCorridor()
    {
        GameObject root = new("Route Corridor");
        Undo.RegisterCreatedObjectUndo(root, "Create Route Corridor");
        root.AddComponent<GetLostTerrainRouteCorridor>();
        for (int i = 0; i < 3; i++)
        {
            GameObject point = new($"Point {i + 1}");
            point.transform.SetParent(root.transform);
            point.transform.position = new Vector3(i * 150f - 150f, 110f, 0f);
        }
        Selection.activeGameObject = root;
    }

    void OnEnable()
    {
        if (!foliageTemplate) foliageTemplate = GetLostTerrainFoliageWorkflow.FindTemplate();
        foliageRules ??= new List<FoliageRule>();
        if (!foliageRuleSelectionInitialised)
        {
            foliageRules.Clear();
            foliageRules.AddRange(GetLostTerrainFoliageWorkflow.FindDefaultRules());
            foliageRuleSelectionInitialised = true;
        }
        grassLayer ??= AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Proxy Games/Stylized Nature Kit Lite/Terrain/Grass.terrainlayer");
        soilLayer ??= AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Proxy Games/Stylized Nature Kit Lite/Terrain/Dirt.terrainlayer");
        rockLayer ??= AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Proxy Games/Stylized Nature Kit Lite/Terrain/Rock.terrainlayer");
        highlandLayer ??= AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Proxy Games/Stylized Nature Kit Lite/Terrain/Sand.terrainlayer");
        terrainPhysicsLayer = Mathf.Clamp(terrainPhysicsLayer, 0, 31);
        if (mountainRidgeSharpness < .5f) mountainRidgeSharpness = 1.15f;
        if (mountainTaperWidth < .1f) mountainTaperWidth = .45f;
        if (surfaceFinishPasses < 1)
        {
            applySurfaceFinish = true;
            surfaceFinishPasses = 2;
            surfaceFinishStrength = .72f;
            preservedMicroReliefMetres = 2.5f;
        }
    }

    void OnDisable()
    {
        if (preview)
            DestroyImmediate(preview);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(
            "Generates a new, seamless Unity Terrain world. It never overwrites an existing output folder. " +
            "Use Landform Stamps for major geography and Route Corridors for designed playable lanes.",
            MessageType.Info);

        DrawWorldSettings();
        DrawPresetSettings();
        DrawLandformSettings();
        DrawDesignSettings();
        DrawSurfaceSettings();
        DrawPreview();

        EditorGUILayout.Space(10);
        using (new EditorGUI.DisabledScope(!CanGenerate(out _)))
        {
            if (GUILayout.Button("Generate New Terrain World", GUILayout.Height(42)))
                GenerateWorld();
        }
        if (!CanGenerate(out string reason))
            EditorGUILayout.HelpBox(reason, MessageType.Warning);
        DrawFoliageWorkflow();
        EditorGUILayout.EndScrollView();
    }

    void DrawFoliageWorkflow()
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Terrain Foliage", EditorStyles.boldLabel);
        automaticallyPrepareFoliage = EditorGUILayout.Toggle(
            new GUIContent(
                "Set Up After Terrain Generation",
                "Creates and synchronizes foliage spawners as soon as a new terrain world is generated."),
            automaticallyPrepareFoliage);
        foliageTemplate = (FoliageSpawner)EditorGUILayout.ObjectField(
            new GUIContent("Optional Settings Source", "An existing spawner whose sampling and rendering settings should be reused. If empty, setup creates one and assigns the rules selected below."),
            foliageTemplate, typeof(FoliageSpawner), true);
        DrawFoliageRuleSelection();
        foliageWorldRoot = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Terrain World Root", "Automatically filled after terrain generation. You can also assign an existing generated world's parent object."),
            foliageWorldRoot, typeof(GameObject), true);
        if (GUILayout.Button("Use Selected Terrain World") && Selection.activeGameObject)
            foliageWorldRoot = Selection.activeGameObject;

        Terrain[] targets = foliageWorldRoot ? foliageWorldRoot.GetComponentsInChildren<Terrain>(true) : Array.Empty<Terrain>();
        TerrainLayer[] targetLayers = targets.Length > 0
            ? targets.Where(t => t.terrainData).SelectMany(t => t.terrainData.terrainLayers).Where(l => l).Distinct().ToArray()
            : UniqueLayers();
        GetLostTerrainFoliageWorkflow.UpdateMappings(
            foliageRules,
            targetLayers,
            foliageLayerMappings);
        if (foliageLayerMappings.Count > 0)
        {
            EditorGUILayout.LabelField("Foliage Rule → Generated Surface", EditorStyles.miniBoldLabel);
            string[] choices = new[] { "Choose surface…" }.Concat(targetLayers.Select(l => l.name)).ToArray();
            foreach (var mapping in foliageLayerMappings)
            {
                int index = Array.IndexOf(targetLayers, mapping.target) + 1;
                int selected = EditorGUILayout.Popup(mapping.source ? mapping.source.name : "Missing rule layer", index, choices);
                mapping.target = selected > 0 ? targetLayers[selected - 1] : null;
            }
        }
        EditorGUILayout.HelpBox(
            $"Target chunks: {targets.Length}. Setup creates a spawner and placement asset for every chunk, assigns readable world-local copies of the {foliageRules.Count(rule => rule)} selected foliage rules, and synchronizes their required prototypes across the whole world.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(targets.Length == 0 || Application.isPlaying))
        {
            if (GUILayout.Button("Create / Sync Foliage Spawners", GUILayout.Height(30)))
            {
                try
                {
                    foliageStatus = CreateOrSyncFoliageSpawners();
                }
                catch (Exception exception)
                {
                    foliageStatus = "Foliage setup stopped: " + exception.Message;
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("Terrain Foliage", foliageStatus, "OK");
                }
                finally { EditorUtility.ClearProgressBar(); }
            }
        }

        bool ready = foliageTemplate && foliageRules.Any(rule => rule) && targets.Length > 0 &&
            foliageLayerMappings.Count > 0 && foliageLayerMappings.All(m => m.target);
        using (new EditorGUI.DisabledScope(!ready || Application.isPlaying))
        {
            if (GUILayout.Button("Generate Foliage on This World", GUILayout.Height(36)))
            {
                try
                {
                    foliageStatus = GetLostTerrainFoliageWorkflow.Generate(
                        foliageWorldRoot,
                        foliageTemplate,
                        foliageRules,
                        foliageLayerMappings);
                }
                catch (Exception exception)
                {
                    foliageStatus = "Foliage generation stopped: " + exception.Message;
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("Terrain Foliage", foliageStatus, "OK");
                }
                finally { EditorUtility.ClearProgressBar(); }
            }
        }
        EditorGUILayout.HelpBox(foliageStatus, MessageType.None);
    }

    void DrawFoliageRuleSelection()
    {
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("Foliage Rules To Use", EditorStyles.miniBoldLabel);

        int removeIndex = -1;
        for (int i = 0; i < foliageRules.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            foliageRules[i] = (FoliageRule)EditorGUILayout.ObjectField(
                $"Rule {i + 1}",
                foliageRules[i],
                typeof(FoliageRule),
                false);
            if (GUILayout.Button("−", GUILayout.Width(24f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();
        }
        if (removeIndex >= 0)
            foliageRules.RemoveAt(removeIndex);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Rule"))
            foliageRules.Add(null);
        if (GUILayout.Button("Use Source Rules"))
        {
            foliageRules.Clear();
            if (foliageTemplate)
                foliageRules.AddRange(foliageTemplate.Rules.Where(rule => rule));
        }
        if (GUILayout.Button("Production Defaults"))
        {
            foliageRules.Clear();
            foliageRules.AddRange(GetLostTerrainFoliageWorkflow.FindDefaultRules());
        }
        if (GUILayout.Button("Clear"))
            foliageRules.Clear();
        EditorGUILayout.EndHorizontal();

        if (!foliageRules.Any(rule => rule))
        {
            EditorGUILayout.HelpBox(
                "Choose at least one foliage rule. Only rules in this list will be copied and used for the generated terrain.",
                MessageType.Warning);
        }
    }

    string CreateOrSyncFoliageSpawners()
    {
        foliageTemplate = GetLostTerrainFoliageWorkflow.EnsureSettingsSource(
            foliageWorldRoot,
            foliageTemplate,
            foliageRules);
        Terrain[] targets = foliageWorldRoot
            .GetComponentsInChildren<Terrain>(true);
        TerrainLayer[] targetLayers = targets
            .Where(t => t.terrainData)
            .SelectMany(t => t.terrainData.terrainLayers)
            .Where(layer => layer)
            .Distinct()
            .ToArray();
        GetLostTerrainFoliageWorkflow.UpdateMappings(
            foliageRules,
            targetLayers,
            foliageLayerMappings);

        if (foliageLayerMappings.Count == 0 ||
            foliageLayerMappings.Any(mapping => !mapping.target))
        {
            throw new InvalidOperationException(
                "One or more foliage rule surfaces could not be matched automatically. " +
                "Choose the unassigned generated surfaces above, then click setup again.");
        }

        return GetLostTerrainFoliageWorkflow.CreateAndSynchronise(
            foliageWorldRoot,
            foliageTemplate,
            foliageRules,
            foliageLayerMappings);
    }

    void DrawWorldSettings()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
        worldName = EditorGUILayout.TextField("World Name", worldName);
        seed = EditorGUILayout.IntField("Seed", seed);
        tilesX = EditorGUILayout.IntSlider("Tiles X", tilesX, 1, 8);
        tilesZ = EditorGUILayout.IntSlider("Tiles Z", tilesZ, 1, 8);
        tileSize = Mathf.Max(100f, EditorGUILayout.FloatField("Tile Size (m)", tileSize));
        terrainHeight = Mathf.Max(100f, EditorGUILayout.FloatField("Height Range (m)", terrainHeight));
        heightmapResolution = EditorGUILayout.IntPopup("Heightmap Resolution", heightmapResolution,
            new[] { "257", "513", "1025" }, new[] { 257, 513, 1025 });
        alphamapResolution = EditorGUILayout.IntPopup("Paint Resolution", alphamapResolution,
            new[] { "128", "256", "512" }, new[] { 128, 256, 512 });
        worldOrigin = EditorGUILayout.Vector3Field("World Origin", worldOrigin);
        outputRoot = EditorGUILayout.TextField("Asset Folder", outputRoot);
        EditorGUILayout.LabelField("Total Size", $"{tilesX * tileSize:0} × {tilesZ * tileSize:0} metres");
    }

    void DrawPresetSettings()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Starting Preset", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        selectedPreset = (TerrainPreset)EditorGUILayout.EnumPopup("Terrain Style", selectedPreset);
        if (GUILayout.Button("Apply", GUILayout.Width(72f)))
        {
            ApplyPreset(selectedPreset);
            GeneratePreview();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox(PresetDescription(selectedPreset), MessageType.None);
    }

    void ApplyPreset(TerrainPreset preset)
    {
        applySurfaceFinish = true;
        useSceneDesignMarkers = true;
        applyConnectedErosion = true;
        applyHillsideErosion = true;
        hillsideErosionOctaves = 4;
        hillsideErosionRelief = preset == TerrainPreset.Alps ? 24f : preset == TerrainPreset.Mountains ? 14f : preset == TerrainPreset.RollingHills ? 8f : preset == TerrainPreset.Grassland ? 2f : .6f;
        hillsideGullySpacing = preset == TerrainPreset.Alps ? 180f : preset == TerrainPreset.RollingHills ? 110f : 140f;
        switch (preset)
        {
            case TerrainPreset.RollingHills:
                baseHeight = 96f; continentScale = 1950f; octaves = 5; persistence = .44f; lacunarity = 1.94f;
                rollingHeight = 92f; mountainHeight = 95f; mountainCoverage = .18f;
                mountainRidgeSharpness = .78f; mountainTaperWidth = .62f; mountainDetailHeight = 5f; derivativeDamping = 1.55f;
                domainWarpMetres = 145f; domainWarpScale = 1250f; basinDepth = 32f; waterLevel = 68f;
                erosionCellSize = 10f; erosionCycles = 2; channelDepth = 2.8f;
                surfaceFinishPasses = 2; surfaceFinishStrength = .68f; preservedMicroReliefMetres = 2.2f;
                break;
            case TerrainPreset.Mountains:
                baseHeight = 92f; continentScale = 1850f; octaves = 5; persistence = .48f; lacunarity = 2.02f;
                rollingHeight = 68f; mountainHeight = 315f; mountainCoverage = .44f;
                mountainRidgeSharpness = .95f; mountainTaperWidth = .52f; mountainDetailHeight = 7f; derivativeDamping = 1.2f;
                domainWarpMetres = 225f; domainWarpScale = 1100f; basinDepth = 55f; waterLevel = 68f;
                erosionCellSize = 10f; erosionCycles = 2; channelDepth = 4.5f;
                surfaceFinishPasses = 2; surfaceFinishStrength = .72f; preservedMicroReliefMetres = 2.5f;
                break;
            case TerrainPreset.Alps:
                baseHeight = 100f; continentScale = 2250f; octaves = 6; persistence = .5f; lacunarity = 2.06f;
                rollingHeight = 58f; mountainHeight = 475f; mountainCoverage = .58f;
                mountainRidgeSharpness = 1.2f; mountainTaperWidth = .46f; mountainDetailHeight = 12f; derivativeDamping = .95f;
                domainWarpMetres = 285f; domainWarpScale = 1325f; basinDepth = 82f; waterLevel = 72f;
                terrainHeight = Mathf.Max(terrainHeight, 800f);
                erosionCellSize = 8f; erosionCycles = 3; channelDepth = 6.2f;
                surfaceFinishPasses = 2; surfaceFinishStrength = .66f; preservedMicroReliefMetres = 3.2f;
                break;
            case TerrainPreset.Grassland:
                baseHeight = 88f; continentScale = 2450f; octaves = 4; persistence = .4f; lacunarity = 1.9f;
                rollingHeight = 38f; mountainHeight = 32f; mountainCoverage = .07f;
                mountainRidgeSharpness = .68f; mountainTaperWidth = .68f; mountainDetailHeight = 2.5f; derivativeDamping = 1.9f;
                domainWarpMetres = 75f; domainWarpScale = 1650f; basinDepth = 16f; waterLevel = 64f;
                erosionCellSize = 12f; erosionCycles = 1; channelDepth = 1.4f;
                surfaceFinishPasses = 2; surfaceFinishStrength = .62f; preservedMicroReliefMetres = 1.5f;
                break;
            case TerrainPreset.Swamps:
                baseHeight = 78f; continentScale = 2350f; octaves = 4; persistence = .43f; lacunarity = 1.88f;
                rollingHeight = 18f; mountainHeight = 10f; mountainCoverage = 0f;
                mountainRidgeSharpness = .65f; mountainTaperWidth = .7f; mountainDetailHeight = .8f; derivativeDamping = 2.3f;
                domainWarpMetres = 110f; domainWarpScale = 1450f; basinDepth = 30f; waterLevel = 68f;
                erosionCellSize = 10f; erosionCycles = 2; channelDepth = 1.8f;
                surfaceFinishPasses = 3; surfaceFinishStrength = .78f; preservedMicroReliefMetres = .9f;
                break;
        }
        Repaint();
    }

    static string PresetDescription(TerrainPreset preset) => preset switch
    {
        TerrainPreset.RollingHills => "Broad rounded hills, gentle valleys, and occasional low ridges suitable for mixed exploration.",
        TerrainPreset.Mountains => "Wide mountain chains with readable passes and foothills; the balanced Get Lost starting style.",
        TerrainPreset.Alps => "Tall connected ranges, deeper valleys, stronger drainage, and more dramatic elevation changes.",
        TerrainPreset.Grassland => "Large walkable plains with low rolling relief and only rare upland interruptions.",
        TerrainPreset.Swamps => "Very low relief with broad wet basins, subtle drainage channels, and extensive land near water level.",
        _ => string.Empty
    };
    void DrawLandformSettings()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Natural Landforms", EditorStyles.boldLabel);
        baseHeight = EditorGUILayout.FloatField("Base Height", baseHeight);
        continentScale = Mathf.Max(100f, EditorGUILayout.FloatField("Broad Scale", continentScale));
        rollingHeight = Mathf.Max(0f, EditorGUILayout.FloatField("Rolling Height", rollingHeight));
        mountainHeight = Mathf.Max(0f, EditorGUILayout.FloatField("Mountain Height", mountainHeight));
        mountainCoverage = EditorGUILayout.Slider("Mountain Coverage", mountainCoverage, 0f, 1f);
        mountainRidgeSharpness = EditorGUILayout.Slider(
            new GUIContent("Ridge Sharpness", "Lower values produce wider ridge tops and gentler shoulders; higher values produce narrow crests."),
            mountainRidgeSharpness, .5f, 3f);
        mountainTaperWidth = EditorGUILayout.Slider(
            new GUIContent("Mountain Taper Width", "Higher values spread the mountain influence farther into surrounding foothills."),
            mountainTaperWidth, .1f, .7f);
        mountainDetailHeight = EditorGUILayout.Slider(
            new GUIContent("Mountain Micro Relief", "Small-scale height variation added after the broad mountain mass. This is capped so it cannot create full-height spikes."),
            mountainDetailHeight, 0f, 20f);
        octaves = EditorGUILayout.IntSlider("Noise Octaves", octaves, 1, 8);
        persistence = EditorGUILayout.Slider("Persistence", persistence, .1f, .9f);
        lacunarity = EditorGUILayout.Slider("Lacunarity", lacunarity, 1.2f, 3.5f);
        derivativeDamping = EditorGUILayout.Slider(
            new GUIContent("Slope Feedback", "Quilez-style derivative feedback suppresses noisy high-frequency detail on already complex slopes."),
            derivativeDamping, 0f, 4f);
        domainWarpMetres = Mathf.Max(0f, EditorGUILayout.FloatField("Domain Warp (m)", domainWarpMetres));
        domainWarpScale = Mathf.Max(50f, EditorGUILayout.FloatField("Warp Scale", domainWarpScale));
        basinDepth = Mathf.Max(0f, EditorGUILayout.FloatField("Natural Basin Depth", basinDepth));
        waterLevel = EditorGUILayout.FloatField("Water Reference Height", waterLevel);
        EditorGUILayout.Space(3);
        applySurfaceFinish = EditorGUILayout.Toggle(
            new GUIContent("Shape-Preserving Finish", "Removes large, short-range height steps while retaining small terrain relief."),
            applySurfaceFinish);
        if (applySurfaceFinish)
        {
            surfaceFinishPasses = EditorGUILayout.IntSlider("Finish Passes", surfaceFinishPasses, 1, 4);
            surfaceFinishStrength = EditorGUILayout.Slider("Finish Strength", surfaceFinishStrength, 0f, 1f);
            preservedMicroReliefMetres = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Preserve Relief Under (m)", "Local height detail at or below this amplitude is retained by the finish pass."),
                preservedMicroReliefMetres));
        }
    }

    void DrawDesignSettings()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Level Design", EditorStyles.boldLabel);
        useSceneDesignMarkers = EditorGUILayout.Toggle("Use Scene Markers", useSceneDesignMarkers);
        if (useSceneDesignMarkers)
        {
            int stamps = FindObjectsByType<GetLostTerrainLandformStamp>(FindObjectsInactive.Include).Length;
            int corridors = FindObjectsByType<GetLostTerrainRouteCorridor>(FindObjectsInactive.Include).Length;
            EditorGUILayout.LabelField("Markers Found", $"{stamps} landforms, {corridors} corridors");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Landform Stamp")) CreateLandformStamp();
            if (GUILayout.Button("Add Route Corridor")) CreateRouteCorridor();
            EditorGUILayout.EndHorizontal();
        }
        applyHillsideErosion = EditorGUILayout.Toggle("Apply Hillside Gullies", applyHillsideErosion);
        if (applyHillsideErosion)
        {
            hillsideErosionRelief = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Hillside Relief (m)", "Maximum combined ridge/gully displacement. Fades out on flat ground."), hillsideErosionRelief));
            hillsideGullySpacing = Mathf.Max(20f, EditorGUILayout.FloatField("Gully Spacing (m)", hillsideGullySpacing));
            hillsideErosionOctaves = EditorGUILayout.IntSlider("Gully Detail Octaves", hillsideErosionOctaves, 1, 5);
        }
        applyConnectedErosion = EditorGUILayout.Toggle("Apply Drainage Erosion", applyConnectedErosion);
        if (applyConnectedErosion)
        {
            erosionCellSize = Mathf.Max(4f, EditorGUILayout.FloatField("Erosion Cell Size", erosionCellSize));
            erosionCycles = EditorGUILayout.IntSlider("Erosion Cycles", erosionCycles, 1, 4);
            channelDepth = Mathf.Max(0f, EditorGUILayout.FloatField("Channel Depth", channelDepth));
        }
    }

    void DrawSurfaceSettings()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain Surface", EditorStyles.boldLabel);
        grassLayer = (TerrainLayer)EditorGUILayout.ObjectField("Grass", grassLayer, typeof(TerrainLayer), false);
        soilLayer = (TerrainLayer)EditorGUILayout.ObjectField("Soil / Low Ground", soilLayer, typeof(TerrainLayer), false);
        rockLayer = (TerrainLayer)EditorGUILayout.ObjectField("Rock", rockLayer, typeof(TerrainLayer), false);
        highlandLayer = (TerrainLayer)EditorGUILayout.ObjectField("Highland", highlandLayer, typeof(TerrainLayer), false);
        terrainMaterial = (Material)EditorGUILayout.ObjectField("Terrain Material", terrainMaterial, typeof(Material), false);
        terrainPhysicsLayer = EditorGUILayout.LayerField("Terrain Physics Layer", terrainPhysicsLayer);
    }

    void DrawPreview()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        if (GUILayout.Button("Regenerate Topographic Preview", GUILayout.Height(30)))
            GeneratePreview();
        if (preview)
        {
            float width = Mathf.Min(position.width - 28f, 520f);
            Rect rect = GUILayoutUtility.GetRect(width, width, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
        }
        EditorGUILayout.HelpBox(previewSummary, MessageType.None);
    }

    bool CanGenerate(out string reason)
    {
        if (string.IsNullOrWhiteSpace(worldName)) { reason = "Enter a world name."; return false; }
        if (!outputRoot.StartsWith("Assets/", StringComparison.Ordinal)) { reason = "Output folder must be inside Assets/."; return false; }
        if (new[] { grassLayer, soilLayer, rockLayer, highlandLayer }.All(layer => layer == null)) { reason = "Assign at least one Terrain Layer."; return false; }
        long samples = (long)(tilesX * (heightmapResolution - 1) + 1) * (tilesZ * (heightmapResolution - 1) + 1);
        if (samples > 17000000) { reason = "This resolution exceeds the tool's 17 million sample safety limit."; return false; }
        reason = string.Empty;
        return true;
    }

    void GeneratePreview()
    {
        const int resolution = 256;
        GenerationContext context = CaptureContext();
        float[,] heights = new float[resolution, resolution];
        float min = float.MaxValue, max = float.MinValue;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float wx = Mathf.Lerp(worldOrigin.x, worldOrigin.x + context.width, x / (resolution - 1f));
            float wz = Mathf.Lerp(worldOrigin.z, worldOrigin.z + context.depth, z / (resolution - 1f));
            float h = EvaluateHeight(wx, wz, context);
            heights[z, x] = h; min = Mathf.Min(min, h); max = Mathf.Max(max, h);
        }
        heights = FinishHeightfield(heights, context);
        MeasureRange(heights, out min, out max);
        if (preview) DestroyImmediate(preview);
        preview = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { name = "Get Lost terrain preview", hideFlags = HideFlags.HideAndDontSave };
        Color[] pixels = new Color[resolution * resolution];
        float cellX = context.width / (resolution - 1f), cellZ = context.depth / (resolution - 1f);
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float h = heights[z, x];
            float dx = heights[z, Mathf.Min(resolution - 1, x + 1)] - heights[z, Mathf.Max(0, x - 1)];
            float dz = heights[Mathf.Min(resolution - 1, z + 1), x] - heights[Mathf.Max(0, z - 1), x];
            float slope = Mathf.Atan(Mathf.Sqrt((dx / (2 * cellX)) * (dx / (2 * cellX)) + (dz / (2 * cellZ)) * (dz / (2 * cellZ)))) * Mathf.Rad2Deg;
            float t = Mathf.InverseLerp(min, max, h);
            Color color = h < waterLevel
                ? Color.Lerp(new Color(.03f, .16f, .24f), new Color(.12f, .48f, .62f), Mathf.InverseLerp(min, waterLevel, h))
                : Color.Lerp(new Color(.20f, .38f, .14f), new Color(.65f, .55f, .37f), t);
            color = Color.Lerp(color, new Color(.42f, .42f, .43f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(28f, 48f, slope)));
            if (x % Mathf.Max(1, (resolution - 1) / tilesX) == 0 || z % Mathf.Max(1, (resolution - 1) / tilesZ) == 0)
                color = Color.Lerp(color, Color.black, .25f);
            pixels[z * resolution + x] = color;
        }
        preview.SetPixels(pixels); preview.Apply();
        previewSummary = $"Height {min:0}–{max:0} m. Hillside gullies {(applyHillsideErosion ? "shown at preview resolution" : "disabled")}; connected drainage runs during generation if enabled. Blue is below the {waterLevel:0} m reference. Dark lines show tile boundaries.";
        Repaint();
    }

    void GenerateWorld()
    {
        if (!CanGenerate(out string reason))
            throw new InvalidOperationException(reason);
        string safeName = MakeSafeName(worldName);
        EnsureAssetFolder(outputRoot);
        string folder = $"{outputRoot}/{safeName}";
        if (AssetDatabase.IsValidFolder(folder) || Directory.Exists(folder))
        {
            EditorUtility.DisplayDialog("Terrain World Already Exists", $"'{folder}' already exists. Choose another World Name; existing terrain will not be overwritten.", "OK");
            return;
        }
        AssetDatabase.CreateFolder(outputRoot, safeName);
        GenerationContext context = CaptureContext();
        int cellsX = tilesX * (heightmapResolution - 1);
        int cellsZ = tilesZ * (heightmapResolution - 1);
        int samplesX = cellsX + 1, samplesZ = cellsZ + 1;
        float[,] worldHeights = new float[samplesZ, samplesX];
        float min = float.MaxValue, max = float.MinValue;
        GameObject root = null;
        List<Terrain> terrains = new();
        try
        {
            for (int z = 0; z < samplesZ; z++)
            {
                if ((z & 31) == 0)
                    EditorUtility.DisplayProgressBar("Get Lost Terrain", $"Sampling continuous world {z}/{samplesZ}", .42f * z / samplesZ);
                float worldZ = worldOrigin.z + context.depth * z / cellsZ;
                for (int x = 0; x < samplesX; x++)
                {
                    float worldX = worldOrigin.x + context.width * x / cellsX;
                    float height = Mathf.Clamp(EvaluateHeight(worldX, worldZ, context), worldOrigin.y, worldOrigin.y + terrainHeight);
                    worldHeights[z, x] = height;
                    min = Mathf.Min(min, height); max = Mathf.Max(max, height);
                }
            }

            EditorUtility.DisplayProgressBar("Get Lost Terrain", "Finishing landforms and carving hillside gullies", .43f);
            worldHeights = FinishHeightfield(worldHeights, context,
                progress => EditorUtility.DisplayProgressBar("Get Lost Terrain", "Carving slope-aligned hillside gullies", .43f + progress * .04f));
            MeasureRange(worldHeights, out min, out max);

            root = new GameObject($"{worldName} - Generated Terrain");
            Undo.RegisterCreatedObjectUndo(root, "Generate Terrain World");
            TerrainLayer[] surfaceLayers = UniqueLayers();
            for (int tileZ = 0; tileZ < tilesZ; tileZ++)
            for (int tileX = 0; tileX < tilesX; tileX++)
            {
                int tileIndex = tileZ * tilesX + tileX;
                EditorUtility.DisplayProgressBar("Get Lost Terrain", $"Building tile {tileIndex + 1}/{tilesX * tilesZ}", .42f + .42f * tileIndex / (tilesX * tilesZ));
                TerrainData data = new()
                {
                    name = $"{safeName} Terrain {tileX}-{tileZ}",
                    heightmapResolution = heightmapResolution,
                    alphamapResolution = alphamapResolution,
                    baseMapResolution = 512,
                    size = new Vector3(tileSize, terrainHeight, tileSize),
                    terrainLayers = surfaceLayers
                };
                string assetPath = $"{folder}/Terrain {tileX}-{tileZ}.asset";
                AssetDatabase.CreateAsset(data, assetPath);
                float[,] tileHeights = new float[heightmapResolution, heightmapResolution];
                int startX = tileX * (heightmapResolution - 1), startZ = tileZ * (heightmapResolution - 1);
                for (int z = 0; z < heightmapResolution; z++)
                for (int x = 0; x < heightmapResolution; x++)
                    tileHeights[z, x] = Mathf.Clamp01((worldHeights[startZ + z, startX + x] - worldOrigin.y) / terrainHeight);
                data.SetHeights(0, 0, tileHeights);

                // Build the components explicitly. Terrain.CreateTerrainGameObject
                // assigns TerrainData while auto-connect is still enabled, allowing a
                // newly-created tile to join unrelated scene terrains before we can
                // configure its grouping and corrupt its edge state.
                GameObject terrainObject = new($"Terrain {tileX}-{tileZ}");
                Undo.RegisterCreatedObjectUndo(terrainObject, "Generate Terrain Tile");
                terrainObject.layer = terrainPhysicsLayer;
                terrainObject.transform.SetParent(root.transform, false);
                terrainObject.transform.position =
                    worldOrigin + new Vector3(tileX * tileSize, 0f, tileZ * tileSize);

                Terrain terrain = terrainObject.AddComponent<Terrain>();
                terrain.allowAutoConnect = false;
                terrain.groupingID = seed;
                terrain.terrainData = data;
                TerrainCollider terrainCollider = terrainObject.AddComponent<TerrainCollider>();
                terrainCollider.terrainData = data;
                terrain.materialTemplate = terrainMaterial;
                terrain.drawInstanced = true;
                terrain.heightmapPixelError = 5f;
                terrain.basemapDistance = 1800f;
                terrains.Add(terrain);
            }

            // Reapply the one continuous source field after every Terrain component
            // exists. This guarantees that no component-creation callback can leave
            // a generated edge different from its matching neighbour.
            RestoreGeneratedHeights(terrains, worldHeights);

            string erosionStatus = "disabled";
            if (applyConnectedErosion)
            {
                float sourceSeamError = MeasureMaximumSeamError(terrains, out string sourceWorstSeam);
                if (sourceSeamError > .02f)
                    throw new InvalidOperationException(
                        $"Generated terrain was discontinuous before erosion at {sourceWorstSeam}: " +
                        $"{sourceSeamError:0.000} m.");

                List<float[,]> beforeErosion = terrains.Select(t =>
                    t.terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution)).ToList();
                ApplyErosion(root, terrains);
                float erosionSeamError = MeasureMaximumSeamError(terrains, out string erosionWorstSeam);
                if (erosionSeamError > .5f)
                {
                    TerrainMicroErosion failedErosion = root.GetComponent<TerrainMicroErosion>();
                    if (failedErosion)
                        DestroyImmediate(failedErosion);
                    RestoreGeneratedHeights(terrains, worldHeights);
                    float restoredSeamError = MeasureMaximumSeamError(terrains, out string restoredWorstSeam);
                    if (restoredSeamError > .02f)
                        throw new InvalidOperationException(
                            $"Terrain rollback failed at {restoredWorstSeam}: {restoredSeamError:0.000} m.");
                    erosionStatus = $"reverted after unsafe seam at {erosionWorstSeam} ({erosionSeamError:0.000} m)";
                    Debug.LogWarning($"[Get Lost Terrain] Connected erosion was reverted because it produced {erosionSeamError:0.000} m of seam error at {erosionWorstSeam}. The generated landforms and hillside gullies remain intact without connected drainage.");
                }
                else
                {

                    erosionStatus = $"{erosionCycles} cycles at requested {erosionCellSize:0.#} m; {MeasureErosionChange(terrains, beforeErosion)}";
                }
            }

            for (int tileZ = 0; tileZ < tilesZ; tileZ++)
            for (int tileX = 0; tileX < tilesX; tileX++)
            {
                int tileIndex = tileZ * tilesX + tileX;
                EditorUtility.DisplayProgressBar("Get Lost Terrain", $"Painting final terrain {tileIndex + 1}/{terrains.Count}", .97f + .02f * tileIndex / terrains.Count);
                PaintTerrain(terrains[tileIndex].terrainData, tileX, tileZ, context, surfaceLayers);
            }

            ReconcileMinorSeams(terrains);
            float reconciledSeamError = MeasureMaximumSeamError(terrains, out string reconciledWorstSeam);
            if (reconciledSeamError > .02f)
                throw new InvalidOperationException(
                    $"Terrain seam validation failed before saving at {reconciledWorstSeam}: " +
                    $"{reconciledSeamError:0.000} m.");

            // Keep a verified CPU-side copy across persistence. SetHeights and
            // SetAlphamaps have already committed their native data; calling
            // Terrain.Flush here can replay an older render-side heightmap.
            List<float[,]> finalHeightSnapshots = terrains
                .Select(terrain => terrain.terrainData.GetHeights(
                    0, 0, heightmapResolution, heightmapResolution))
                .ToList();
            foreach (Terrain terrain in terrains)
                EditorUtility.SetDirty(terrain.terrainData);
            AssetDatabase.SaveAssets();

            float savedSeamError = MeasureMaximumSeamError(terrains, out string savedWorstSeam);
            if (savedSeamError > .02f)
            {
                for (int i = 0; i < terrains.Count; i++)
                {
                    terrains[i].terrainData.SetHeights(0, 0, finalHeightSnapshots[i]);
                    EditorUtility.SetDirty(terrains[i].terrainData);
                }
                AssetDatabase.SaveAssets();
                float repairedSaveError = MeasureMaximumSeamError(
                    terrains, out string repairedSaveWorstSeam);
                if (repairedSaveError > .02f)
                    throw new InvalidOperationException(
                        $"Terrain persistence changed a verified seam at {repairedSaveWorstSeam}: " +
                        $"{repairedSaveError:0.000} m.");
                Debug.LogWarning(
                    $"[Get Lost Terrain] Unity changed a verified seam while saving at " +
                    $"{savedWorstSeam} ({savedSeamError:0.000} m). The verified final " +
                    "heightmaps were restored and saved successfully.");
            }

            ValidateAndReport(folder, terrains, context, min, max, erosionStatus);
            ConnectNeighbours(terrains);
            foliageWorldRoot = root;
            if (automaticallyPrepareFoliage)
            {
                try
                {
                    foliageStatus = CreateOrSyncFoliageSpawners();
                }
                catch (Exception foliageException)
                {
                    foliageStatus =
                        "Terrain ready, but automatic foliage setup needs attention: " +
                        foliageException.Message;
                    Debug.LogWarning(
                        "[Get Lost Terrain] " + foliageStatus,
                        root);
                }
            }
            else
            {
                foliageStatus =
                    $"Terrain ready: {terrains.Count} tiles. " +
                    "Click Create / Sync Foliage Spawners next.";
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.LookAt(
                worldOrigin + new Vector3(context.width * .5f, Mathf.Max(150f, max), context.depth * .5f),
                Quaternion.Euler(58f, 0f, 0f),
                Mathf.Max(context.width, context.depth) * .7f);
            Debug.Log($"[Get Lost Terrain] Generated {terrains.Count} tiles in {folder}. Height range {min:0.0}–{max:0.0} m.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (root)
                Undo.DestroyObjectImmediate(root);
            if (AssetDatabase.IsValidFolder(folder))
                AssetDatabase.DeleteAsset(folder);
            EditorUtility.DisplayDialog(
                "Terrain Generation Failed",
                exception.Message + "\n\nThe incomplete terrain root and newly created output folder were rolled back.",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    GenerationContext CaptureContext()
    {
        GenerationContext context = new() { width = tilesX * tileSize, depth = tilesZ * tileSize, seed = seed };
        if (!useSceneDesignMarkers)
            return context;
        foreach (GetLostTerrainLandformStamp source in FindObjectsByType<GetLostTerrainLandformStamp>(FindObjectsInactive.Include))
        {
            context.stamps.Add(new StampSnapshot
            {
                type = source.type,
                center = new Vector2(source.transform.position.x, source.transform.position.z),
                radiusX = source.radiusX,
                radiusZ = source.radiusZ,
                yaw = source.transform.eulerAngles.y * Mathf.Deg2Rad,
                strength = source.strengthMetres,
                plateauHeight = source.plateauHeight,
                blendPower = source.blendPower
            });
        }
        foreach (GetLostTerrainRouteCorridor source in FindObjectsByType<GetLostTerrainRouteCorridor>(FindObjectsInactive.Include))
        {
            Vector3[] points = source.GetPoints().Where(point => point != null).Select(point => point.position).ToArray();
            if (points.Length < 2) continue;
            context.corridors.Add(new CorridorSnapshot
            {
                width = source.playableHalfWidth,
                shoulderWidth = source.shoulderWidth,
                shoulderHeight = source.shoulderHeight,
                floorBlend = source.floorBlend,
                floorOffset = source.floorOffset,
                points = points
            });
        }
        return context;
    }

    float EvaluateHeight(float worldX, float worldZ, GenerationContext context)
    {
        Vector2 p = new(worldX, worldZ);
        NoisePoint seedOffset = SeedOffset(context.seed);
        float warpFrequency = 1f / domainWarpScale;
        float warpX = FbmValue(p * warpFrequency + seedOffset + new Vector2(17.1f, 41.7f), 3, .52f, 2.03f);
        float warpZ = FbmValue(p * warpFrequency + seedOffset + new Vector2(83.4f, 12.8f), 3, .52f, 2.03f);
        Vector2 warped = p + new Vector2(warpX, warpZ) * domainWarpMetres;

        float broadFrequency = 1f / continentScale;
        NoiseSample rolling = DerivativeFbm(warped * broadFrequency + seedOffset, octaves, persistence, lacunarity);
        // Damping belongs inside the octave sum. Applying the final high-frequency
        // gradient to the entire hill creates pits and rises as gradients vary/cancel.
        float damped = rolling.value;
        float region = .5f + .5f * FbmValue(warped / (continentScale * 1.8f) + seedOffset + new Vector2(29f, -37f), 4, .55f, 2.01f);
        float coverageThreshold = Mathf.Lerp(.72f, .22f, mountainCoverage);
        // Preserve the existing broad mountain envelope. These are blend endpoints,
        // not GLSL smoothstep thresholds; coverage zero must still disable mountains.
        float mountainMask = mountainCoverage <= 0f ? 0f : Mathf.Lerp(
            coverageThreshold, Mathf.Min(.98f, coverageThreshold + mountainTaperWidth),
            Mathf.SmoothStep(0f, 1f, region));
        float ridgeNoise = 1f - Mathf.Abs(FbmValue(
            warped / (continentScale * .62f) + seedOffset + new Vector2(-71f, 19f),
            3, .5f, 1.92f));
        float roundedRidge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ridgeNoise));
        float ridgeProfile = Mathf.Pow(roundedRidge, mountainRidgeSharpness);
        float mountains = ridgeProfile * mountainMask * mountainHeight;
        float detailNoise = FbmValue(
            warped / Mathf.Max(180f, continentScale * .16f) + seedOffset + new Vector2(113f, -59f),
            3, .45f, 2.01f);
        float mountainDetail = detailNoise * mountainDetailHeight * mountainMask * ridgeProfile;
        float basin = Mathf.Pow(Mathf.Clamp01(1f - region), 2f) * basinDepth;
        float height = baseHeight + damped * rollingHeight + mountains + mountainDetail - basin;
        return height;
    }

    float[,] FinishHeightfield(float[,] heights, GenerationContext context, Action<float> reportProgress = null)
    {
        if (applySurfaceFinish && surfaceFinishPasses > 0 && surfaceFinishStrength > 0f)
            heights = ApplySurfaceFinish(heights);
        float cellX = context.width / (heights.GetLength(1) - 1f);
        float cellZ = context.depth / (heights.GetLength(0) - 1f);
        if (applyHillsideErosion && hillsideErosionRelief > 0f)
            heights = TerrainHillsideErosion.Apply(heights, new Vector2(worldOrigin.x, worldOrigin.z),
                new Vector2(cellX, cellZ), seed, hillsideErosionRelief, hillsideGullySpacing, hillsideErosionOctaves, reportProgress);
        // Authored plateaus and route floors take precedence over procedural detail.
        for (int z = 0; z < heights.GetLength(0); z++)
        for (int x = 0; x < heights.GetLength(1); x++)
        {
            float wx = worldOrigin.x + x * cellX, wz = worldOrigin.z + z * cellZ;
            float h = ApplyStamps(heights[z, x], wx, wz, context.stamps);
            h = ApplyCorridors(h, wx, wz, context.corridors);
            heights[z, x] = Mathf.Clamp(h, worldOrigin.y, worldOrigin.y + terrainHeight);
        }
        return heights;
    }

    static float ApplyStamps(float height, float x, float z, List<StampSnapshot> stamps)
    {
        foreach (StampSnapshot stamp in stamps)
        {
            float dx = x - stamp.center.x, dz = z - stamp.center.y;
            float c = Mathf.Cos(-stamp.yaw), s = Mathf.Sin(-stamp.yaw);
            float rx = (dx * c - dz * s) / Mathf.Max(1f, stamp.radiusX);
            float rz = (dx * s + dz * c) / Mathf.Max(1f, stamp.radiusZ);
            float influence = Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(rx * rx + rz * rz)), stamp.blendPower);
            if (influence <= 0f) continue;
            switch (stamp.type)
            {
                case TerrainLandformType.Mountain: height += stamp.strength * influence; break;
                case TerrainLandformType.Basin:
                case TerrainLandformType.Pass: height -= Mathf.Abs(stamp.strength) * influence; break;
                case TerrainLandformType.Plateau: height = Mathf.Lerp(height, stamp.plateauHeight, influence); break;
            }
        }
        return height;
    }

    static float ApplyCorridors(float height, float x, float z, List<CorridorSnapshot> corridors)
    {
        Vector2 p = new(x, z);
        foreach (CorridorSnapshot corridor in corridors)
        {
            float bestDistance = float.MaxValue, target = height;
            for (int i = 1; i < corridor.points.Length; i++)
            {
                Vector3 a3 = corridor.points[i - 1], b3 = corridor.points[i];
                Vector2 a = new(a3.x, a3.z), b = new(b3.x, b3.z), segment = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
                float distance = Vector2.Distance(p, a + segment * t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = Mathf.Lerp(a3.y, b3.y, t) + corridor.floorOffset;
                }
            }
            float floor = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corridor.width * .55f, corridor.width, bestDistance));
            height = Mathf.Lerp(height, target, floor * corridor.floorBlend);
            if (corridor.shoulderHeight > 0f && corridor.shoulderWidth > 0f)
            {
                float outer = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corridor.width, corridor.width + corridor.shoulderWidth, bestDistance));
                float inner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corridor.width * .75f, corridor.width * 1.2f, bestDistance));
                height += outer * inner * corridor.shoulderHeight;
            }
        }
        return height;
    }

    NoiseSample DerivativeFbm(NoisePoint point, int count, float gain, float frequencyMultiplier)
    {
        float amplitude = .55f, sum = 0f;
        Vector2 accumulatedDerivative = Vector2.zero;
        Matrix2 transform = new(.80f, -.60f, .60f, .80f);
        NoisePoint p = point;
        float frequency = 1f;
        float rotationCos = 1f, rotationSin = 0f;
        for (int octave = 0; octave < count; octave++)
        {
            NoiseSample sample = ValueNoiseWithDerivative(p, seed + octave * 1013);
            // Chain rule: rotate local derivatives back into the original domain,
            // and include amplitude so tiny octaves cannot dominate the damping.
            Vector2 derivative = new Vector2(
                rotationCos * sample.derivative.x + rotationSin * sample.derivative.y,
                -rotationSin * sample.derivative.x + rotationCos * sample.derivative.y) * (frequency * amplitude);
            sum += amplitude * sample.value / (1f + derivativeDamping * accumulatedDerivative.sqrMagnitude);
            // Only coarser octaves modulate the current octave, never themselves.
            accumulatedDerivative += derivative;
            p = transform.Multiply(p) * frequencyMultiplier + new Vector2(13.7f, 9.2f);
            float nextCos = .8f * rotationCos - .6f * rotationSin;
            rotationSin = .6f * rotationCos + .8f * rotationSin;
            rotationCos = nextCos;
            frequency *= frequencyMultiplier;
            amplitude *= gain;
        }
        return new NoiseSample(sum, accumulatedDerivative);
    }

    float FbmValue(NoisePoint p, int count, float gain, float frequencyMultiplier)
    {
        float sum = 0f, amplitude = .55f;
        Matrix2 transform = new(.80f, -.60f, .60f, .80f);
        for (int octave = 0; octave < count; octave++)
        {
            sum += ValueNoiseWithDerivative(p, seed + octave * 1619).value * amplitude;
            p = transform.Multiply(p) * frequencyMultiplier + new Vector2(7.1f, 15.9f);
            amplitude *= gain;
        }
        return sum;
    }

    static NoiseSample ValueNoiseWithDerivative(NoisePoint p, int noiseSeed)
    {
        int ix = (int)Math.Floor(p.x), iz = (int)Math.Floor(p.z);
        double fx = p.x - ix, fz = p.z - iz;
        double ux = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
        double uz = fz * fz * fz * (fz * (fz * 6.0 - 15.0) + 10.0);
        double dux = 30.0 * fx * fx * (fx * (fx - 2.0) + 1.0);
        double duz = 30.0 * fz * fz * (fz * (fz - 2.0) + 1.0);
        float a = Hash(ix, iz, noiseSeed), b = Hash(ix + 1, iz, noiseSeed);
        float c = Hash(ix, iz + 1, noiseSeed), d = Hash(ix + 1, iz + 1, noiseSeed);
        double k0 = a, k1 = b - a, k2 = c - a, k3 = a - b - c + d;
        return new NoiseSample(
            (float)(k0 + k1 * ux + k2 * uz + k3 * ux * uz),
            new Vector2((float)(dux * (k1 + k3 * uz)), (float)(duz * (k2 + k3 * ux))));
    }

    static float Hash(int x, int z, int noiseSeed)
    {
        unchecked
        {
            uint h = (uint)noiseSeed;
            h ^= (uint)x * 0x9E3779B9u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)z * 0x85EBCA6Bu;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    static NoisePoint SeedOffset(int value)
    {
        System.Random random = new(value);
        return new NoisePoint(random.Next(-100000, 100000), random.Next(-100000, 100000));
    }

    float[,] ApplySurfaceFinish(float[,] source)
    {
        int height = source.GetLength(0);
        int width = source.GetLength(1);
        float[,] current = source;
        float edgeScale = Mathf.Max(4f, preservedMicroReliefMetres * 8f);
        for (int pass = 0; pass < surfaceFinishPasses; pass++)
        {
            float[,] next = (float[,])current.Clone();
            for (int z = 1; z < height - 1; z++)
            for (int x = 1; x < width - 1; x++)
            {
                float center = current[z, x];
                float neighbourSum = 0f;
                float lowestNeighbour = float.MaxValue;
                float highestNeighbour = float.MinValue;
                for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetZ == 0)
                        continue;
                    float neighbour = current[z + offsetZ, x + offsetX];
                    neighbourSum += neighbour;
                    lowestNeighbour = Mathf.Min(lowestNeighbour, neighbour);
                    highestNeighbour = Mathf.Max(highestNeighbour, neighbour);
                }
                float localReference = (neighbourSum - lowestNeighbour - highestNeighbour) / 6f;
                float isolatedFeatureLimit = Mathf.Max(.75f, preservedMicroReliefMetres * 1.5f);
                float filteredCenter = localReference + Mathf.Clamp(
                    center - localReference,
                    -isolatedFeatureLimit,
                    isolatedFeatureLimit);

                float weightedHeight = filteredCenter * 4f;
                float totalWeight = 4f;
                for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetZ == 0)
                        continue;
                    float neighbour = current[z + offsetZ, x + offsetX];
                    float spatialWeight = offsetX == 0 || offsetZ == 0 ? 2f : 1f;
                    float difference = Mathf.Abs(neighbour - filteredCenter) / edgeScale;
                    float featureWeight = 1f / (1f + difference * difference * difference * difference);
                    float weight = spatialWeight * featureWeight;
                    weightedHeight += neighbour * weight;
                    totalWeight += weight;
                }
                float smoothed = weightedHeight / totalWeight;
                float residual = filteredCenter - smoothed;
                float detail = Mathf.Clamp(residual, -preservedMicroReliefMetres, preservedMicroReliefMetres);
                next[z, x] = Mathf.Lerp(center, smoothed + detail, surfaceFinishStrength);
            }
            current = next;
        }
        return current;
    }

    static void MeasureRange(float[,] heights, out float minimum, out float maximum)
    {
        minimum = float.MaxValue;
        maximum = float.MinValue;
        int height = heights.GetLength(0);
        int width = heights.GetLength(1);
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            minimum = Mathf.Min(minimum, heights[z, x]);
            maximum = Mathf.Max(maximum, heights[z, x]);
        }
    }
    readonly struct Matrix2
    {
        readonly float a, b, c, d;
        public Matrix2(float a, float b, float c, float d) { this.a = a; this.b = b; this.c = c; this.d = d; }
        public NoisePoint Multiply(NoisePoint v) => new(a * v.x + b * v.z, c * v.x + d * v.z);
    }

    TerrainLayer[] UniqueLayers() => new[] { grassLayer, soilLayer, rockLayer, highlandLayer }.Where(layer => layer != null).Distinct().ToArray();

    void PaintTerrain(TerrainData data, int tileX, int tileZ, GenerationContext context, TerrainLayer[] layers)
    {
        int resolution = data.alphamapResolution;
        float[,,] map = new float[resolution, resolution, layers.Length];
        int grass = Array.IndexOf(layers, grassLayer), soil = Array.IndexOf(layers, soilLayer);
        int rock = Array.IndexOf(layers, rockLayer), high = Array.IndexOf(layers, highlandLayer);
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float u = x / (resolution - 1f), v = z / (resolution - 1f);
            float h = data.GetInterpolatedHeight(u, v) + worldOrigin.y;
            float slope = data.GetSteepness(u, v);
            float wx = worldOrigin.x + tileX * tileSize + u * tileSize;
            float wz = worldOrigin.z + tileZ * tileSize + v * tileSize;
            float moisture = .5f + .5f * FbmValue(new Vector2(wx, wz) / 650f + SeedOffset(seed + 7001), 3, .55f, 2.03f);
            float rockWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 43f, slope));
            float highWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(baseHeight + mountainHeight * .45f, baseHeight + mountainHeight * .82f, h)) * (1f - rockWeight);
            float soilWeight = Mathf.Clamp01(Mathf.InverseLerp(waterLevel + 35f, waterLevel - 8f, h) * .75f + (1f - moisture) * .28f) * (1f - rockWeight);
            float grassWeight = Mathf.Max(.02f, 1f - rockWeight - highWeight - soilWeight);
            if (grass >= 0) map[z, x, grass] += grassWeight;
            if (soil >= 0) map[z, x, soil] += soilWeight;
            if (rock >= 0) map[z, x, rock] += rockWeight;
            if (high >= 0) map[z, x, high] += highWeight;
            float total = 0f;
            for (int layer = 0; layer < layers.Length; layer++) total += map[z, x, layer];
            if (total <= .0001f) map[z, x, 0] = 1f;
            else for (int layer = 0; layer < layers.Length; layer++) map[z, x, layer] /= total;
        }
        data.SetAlphamaps(0, 0, map);
    }

    void ApplyErosion(GameObject root, List<Terrain> terrains)
    {
        EditorUtility.DisplayProgressBar("Get Lost Terrain", "Applying connected drainage erosion", .87f);
        TerrainMicroErosion erosion = root.AddComponent<TerrainMicroErosion>();
        erosion.useAllActiveTerrains = false;
        erosion.targetTerrains = terrains;
        erosion.simulationCellSizeMetres = erosionCellSize;
        erosion.erosionCycles = erosionCycles;
        erosion.seed = seed;
        erosion.channelDepthMetres = channelDepth;
        erosion.maximumTerrainChangeMetres = Mathf.Max(2f, channelDepth * 1.5f);
        erosion.channelBankWidth = 1;
        erosion.Apply((progress, message) => EditorUtility.DisplayProgressBar("Get Lost Terrain", message, .87f + progress * .10f));
    }

    static string MeasureErosionChange(List<Terrain> terrains, List<float[,]> before)
    {
        float maximumCut = 0f, maximumDeposit = 0f;
        double sumSquares = 0;
        long samples = 0;
        for (int i = 0; i < terrains.Count; i++)
        {
            TerrainData data = terrains[i].terrainData;
            int resolution = data.heightmapResolution;
            float[,] after = data.GetHeights(0, 0, resolution, resolution);
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float delta = (after[z, x] - before[i][z, x]) * data.size.y;
                if (float.IsNaN(delta) || float.IsInfinity(delta))
                    throw new InvalidOperationException("Drainage erosion produced a non-finite terrain height.");
                maximumCut = Mathf.Max(maximumCut, -delta);
                maximumDeposit = Mathf.Max(maximumDeposit, delta);
                sumSquares += delta * delta;
                samples++;
            }
        }
        return $"maximum cut {maximumCut:0.000} m, maximum deposit {maximumDeposit:0.000} m, RMS change {Math.Sqrt(sumSquares / Math.Max(1L, samples)):0.000} m";
    }

    void RestoreGeneratedHeights(List<Terrain> terrains, float[,] worldHeights)
    {
        int resolution = heightmapResolution;
        for (int tileZ = 0; tileZ < tilesZ; tileZ++)
        for (int tileX = 0; tileX < tilesX; tileX++)
        {
            int index = tileZ * tilesX + tileX;
            int startX = tileX * (resolution - 1);
            int startZ = tileZ * (resolution - 1);
            float[,] restored = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
                restored[z, x] = Mathf.Clamp01(
                    (worldHeights[startZ + z, startX + x] - worldOrigin.y) / terrainHeight);
            terrains[index].terrainData.SetHeights(0, 0, restored);
            terrains[index].Flush();
        }
    }

    float MeasureMaximumSeamError(List<Terrain> terrains, out string worstSeam)
    {
        int resolution = heightmapResolution;
        float maximum = 0f;
        worstSeam = "none";
        for (int z = 0; z < tilesZ; z++)
        for (int x = 0; x < tilesX; x++)
        {
            int index = z * tilesX + x;
            Terrain terrain = terrains[index];
            TerrainData data = terrain.terrainData;
            for (int i = 0; i < resolution; i++)
            {
                if (x < tilesX - 1)
                {
                    Terrain neighbour = terrains[index + 1];
                    float error = Mathf.Abs(
                        terrain.transform.position.y + data.GetHeight(resolution - 1, i) -
                        (neighbour.transform.position.y + neighbour.terrainData.GetHeight(0, i)));
                    if (error > maximum)
                    {
                        maximum = error;
                        worstSeam = $"right edge of tile {x}-{z}, sample {i}, against tile {x + 1}-{z}";
                    }
                }
                if (z < tilesZ - 1)
                {
                    Terrain neighbour = terrains[(z + 1) * tilesX + x];
                    float error = Mathf.Abs(
                        terrain.transform.position.y + data.GetHeight(i, resolution - 1) -
                        (neighbour.transform.position.y + neighbour.terrainData.GetHeight(i, 0)));
                    if (error > maximum)
                    {
                        maximum = error;
                        worstSeam = $"top edge of tile {x}-{z}, sample {i}, against tile {x}-{z + 1}";
                    }
                }
            }
        }
        return maximum;
    }
    void ReconcileMinorSeams(List<Terrain> terrains)
    {
        const float maximumRepairMetres = .5f;
        int resolution = heightmapResolution;
        List<float[,]> samples = terrains
            .Select(terrain => terrain.terrainData.GetHeights(0, 0, resolution, resolution))
            .ToList();
        bool[] changed = new bool[terrains.Count];

        for (int z = 0; z < tilesZ; z++)
        for (int x = 0; x < tilesX; x++)
        {
            int index = z * tilesX + x;
            if (x < tilesX - 1)
            {
                int neighbourIndex = index + 1;
                for (int i = 0; i < resolution; i++)
                    AverageSeamSample(terrains, samples, changed, index, i, resolution - 1, neighbourIndex, i, 0, maximumRepairMetres);
            }
            if (z < tilesZ - 1)
            {
                int neighbourIndex = (z + 1) * tilesX + x;
                for (int i = 0; i < resolution; i++)
                    AverageSeamSample(terrains, samples, changed, index, resolution - 1, i, neighbourIndex, 0, i, maximumRepairMetres);
            }
        }

        for (int z = 1; z < tilesZ; z++)
        for (int x = 1; x < tilesX; x++)
        {
            int lowerLeft = (z - 1) * tilesX + x - 1;
            int lowerRight = lowerLeft + 1;
            int upperLeft = z * tilesX + x - 1;
            int upperRight = upperLeft + 1;
            float average = (
                WorldHeight(terrains[lowerLeft], samples[lowerLeft][resolution - 1, resolution - 1]) +
                WorldHeight(terrains[lowerRight], samples[lowerRight][resolution - 1, 0]) +
                WorldHeight(terrains[upperLeft], samples[upperLeft][0, resolution - 1]) +
                WorldHeight(terrains[upperRight], samples[upperRight][0, 0])) * .25f;
            SetWorldHeight(terrains[lowerLeft], samples[lowerLeft], resolution - 1, resolution - 1, average);
            SetWorldHeight(terrains[lowerRight], samples[lowerRight], resolution - 1, 0, average);
            SetWorldHeight(terrains[upperLeft], samples[upperLeft], 0, resolution - 1, average);
            SetWorldHeight(terrains[upperRight], samples[upperRight], 0, 0, average);
            changed[lowerLeft] = changed[lowerRight] = changed[upperLeft] = changed[upperRight] = true;
        }

        for (int i = 0; i < terrains.Count; i++)
        {
            if (!changed[i])
                continue;
            terrains[i].terrainData.SetHeights(0, 0, samples[i]);
            terrains[i].Flush();
        }
    }

    static void AverageSeamSample(
        List<Terrain> terrains,
        List<float[,]> samples,
        bool[] changed,
        int firstIndex,
        int firstZ,
        int firstX,
        int secondIndex,
        int secondZ,
        int secondX,
        float maximumRepairMetres)
    {
        float first = WorldHeight(terrains[firstIndex], samples[firstIndex][firstZ, firstX]);
        float second = WorldHeight(terrains[secondIndex], samples[secondIndex][secondZ, secondX]);
        float error = Mathf.Abs(first - second);
        if (error > maximumRepairMetres)
            throw new InvalidOperationException(
                $"Terrain seam differs by {error:0.000} m, exceeding the safe {maximumRepairMetres:0.0} m micro-stitch limit.");
        if (error <= .0001f)
            return;
        float average = (first + second) * .5f;
        SetWorldHeight(terrains[firstIndex], samples[firstIndex], firstZ, firstX, average);
        SetWorldHeight(terrains[secondIndex], samples[secondIndex], secondZ, secondX, average);
        changed[firstIndex] = changed[secondIndex] = true;
    }

    static float WorldHeight(Terrain terrain, float normalizedHeight) =>
        terrain.transform.position.y + normalizedHeight * terrain.terrainData.size.y;

    static void SetWorldHeight(Terrain terrain, float[,] samples, int z, int x, float worldHeight)
    {
        samples[z, x] = Mathf.Clamp01(
            (worldHeight - terrain.transform.position.y) / Mathf.Max(.001f, terrain.terrainData.size.y));
    }
    void ConnectNeighbours(List<Terrain> terrains)
    {
        for (int z = 0; z < tilesZ; z++)
        for (int x = 0; x < tilesX; x++)
            terrains[z * tilesX + x].SetNeighbors(
                x > 0 ? terrains[z * tilesX + x - 1] : null,
                z < tilesZ - 1 ? terrains[(z + 1) * tilesX + x] : null,
                x < tilesX - 1 ? terrains[z * tilesX + x + 1] : null,
                z > 0 ? terrains[(z - 1) * tilesX + x] : null);
    }

    void ValidateAndReport(string folder, List<Terrain> terrains, GenerationContext context, float generatedMin, float generatedMax, string erosionStatus)
    {
        float maximumSeamError = MeasureMaximumSeamError(terrains, out string worstSeam);
        if (maximumSeamError > .02f)
            throw new InvalidOperationException($"Terrain seam validation failed at {worstSeam}: {maximumSeamError:0.000} m.");
        StringBuilder report = new();
        report.AppendLine("GET LOST TERRAIN GENERATION REPORT");
        report.AppendLine($"World: {worldName}");
        report.AppendLine($"Seed: {seed}");
        report.AppendLine($"Tiles: {tilesX} x {tilesZ}; each {tileSize:0} m; total {context.width:0} x {context.depth:0} m");
        report.AppendLine($"Heightmap: {heightmapResolution}; paint: {alphamapResolution}; generated range {generatedMin:0.0}–{generatedMax:0.0} m");
        report.AppendLine($"Maximum seam error after all passes: {maximumSeamError:0.000} m");
        report.AppendLine("Minor seam reconciliation: edge-only averaging, guarded at 0.5 m maximum");

        report.AppendLine($"Design inputs: {context.stamps.Count} landform stamps, {context.corridors.Count} route corridors");
        report.AppendLine($"Connected drainage erosion: {erosionStatus}");
        report.AppendLine($"Hillside gullies: {(applyHillsideErosion ? $"{hillsideErosionRelief:0.0} m maximum relief, {hillsideGullySpacing:0} m spacing, {hillsideErosionOctaves} octaves (resolution limited)" : "disabled")}");
        report.AppendLine($"Mountain shape: ridge sharpness {mountainRidgeSharpness:0.00}, taper width {mountainTaperWidth:0.00}, micro relief {mountainDetailHeight:0.0} m");
        report.AppendLine($"Shape-preserving finish: {(applySurfaceFinish ? $"{surfaceFinishPasses} passes, {surfaceFinishStrength:0.00} strength, preserving relief under {preservedMicroReliefMetres:0.0} m" : "disabled")}");
        report.AppendLine("\nThe water height is a design reference only; this tool does not create water gameplay or colliders.");
        report.AppendLine("TerrainData assets are independent from all existing scenes and can be sculpted normally after generation.");
        File.WriteAllText(folder + "/Generation Report.txt", report.ToString());
        AssetDatabase.ImportAsset(folder + "/Generation Report.txt");
    }

    static string MakeSafeName(string value)
    {
        string result = value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(result) ? "Generated World" : result;
    }

    static void EnsureAssetFolder(string path)
    {
        string normalized = path.Replace('\\', '/').TrimEnd('/');
        if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            throw new InvalidOperationException("Output path must be inside Assets.");
        string[] parts = normalized.Split('/');
        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
